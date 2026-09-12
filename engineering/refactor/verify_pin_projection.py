#!/usr/bin/env python3
"""
Prove that a rename sweep renamed and nothing else.

The codec-identity pin records the SOURCE EXPRESSION that named the era codec at each bind site, so
renaming a codec moves every line that names it. A genuine mis-rebinding hidden inside such a sweep
produces the same "big diff, all expected" shape, and the registration pin cannot separate the two:
it is blind to a rebinding by construction.

This script separates them. It applies a rename map to the PRE-change fixtures and compares the
result with the regenerated ones. A pure rename projects exactly; anything else survives as a diff,
and the diff names the file, the packet and both identities.

The map is emitted by the tool that performs the rename, never hand-written, so a rename the tool made
and the map does not know about fails here rather than passing review.

Map format (tab separated, "#" comments, blank lines ignored):

    old identity expression <TAB> new identity expression [<TAB> free-text note]

An entry whose old side is a whole identity is applied to whole identities; otherwise it is applied to
every occurrence inside an identity that stands on an identifier boundary, longest key first, which is
what a member rename inside a factory call such as MakeContainerSetSlot(Table776) needs.

Usage:
    python3 engineering/refactor/verify_pin_projection.py \\
        --rename-map engineering/refactor/rename-map.tsv \\
        --before-rev HEAD~1 --after fixtures/codec-identity/

    python3 engineering/refactor/verify_pin_projection.py \\
        --rename-map engineering/refactor/rename-map.tsv \\
        --before /tmp/pins-before --after fixtures/codec-identity/
"""

from __future__ import annotations

import argparse
import difflib
import pathlib
import re
import subprocess
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[2]
PIN_PATH = "fixtures/codec-identity"

# Columns rendered AFTER the identity. Read right to left, everything before them is the identity, so a column added to the pin later does not silently become part of a codec name.
TRAILING = ("via:", "read:", "fault:", "shape:", "witness:")
TRAILING_EXACT = ("n/a",)

# fixtures/timelines/bands.txt: phase, flow, identifier, then a first protocol range. Its lines carry no wire id, so the binding-line reader above skips them and this is what recognises one.
BAND_LINE = re.compile(r"^\S+\s+\S+\s+\S+\s+\d+(?:-\d*)?\s+\S")


def parse_map(path: pathlib.Path) -> list[tuple[str, str]]:
    entries: list[tuple[str, str]] = []
    seen: set[str] = set()
    for number, raw in enumerate(path.read_text().splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = raw.split("\t")
        if len(parts) < 2 or not parts[0].strip() or not parts[1].strip():
            sys.exit(f"{path}:{number}: expected 'old<TAB>new', got {raw!r}")
        old, new = parts[0].strip(), parts[1].strip()
        if old in seen:
            sys.exit(f"{path}:{number}: '{old}' is mapped twice")
        seen.add(old)
        entries.append((old, new))

    # Longest first, so a rename of Foo.BarV1_8 is not eaten by a rename of Foo.Bar.
    entries.sort(key=lambda e: len(e[0]), reverse=True)
    return entries


def split_line(line: str) -> tuple[str, str, str] | None:
    """A pin line as (head, identity, tail), or None when it is not a binding line."""
    fields = line.split()
    if len(fields) < 6 or not fields[2].startswith("0x"):
        return None

    end = len(fields)
    while end > 5 and (fields[end - 1].startswith(TRAILING) or fields[end - 1] in TRAILING_EXACT):
        end -= 1

    head = " ".join(fields[:4])
    identity = " ".join(fields[4:end])
    tail = " ".join(fields[end:])
    return head, identity, tail


def project(identity: str, entries: list[tuple[str, str]], used: set[str]) -> str:
    for old, new in entries:
        if identity == old:
            used.add(old)
            return new

    projected = identity
    for old, new in entries:
        pattern = rf"(?<![A-Za-z0-9_.]){re.escape(old)}(?![A-Za-z0-9_])"
        projected, hits = re.subn(pattern, lambda _, value=new: value, projected)
        if hits:
            used.add(old)

    return projected


def project_text(text: str, entries: list[tuple[str, str]], used: set[str]) -> str:
    out: list[str] = []
    for line in text.splitlines():
        parts = split_line(line)
        if parts is None:
            # A band line carries several identities behind column padding the pin's own renderer chose, so it is projected where it stands rather than reassembled from its fields.
            out.append(project(line, entries, used) if BAND_LINE.match(line) else line)
            continue
        head, identity, tail = parts
        out.append(f"{head} {project(identity, entries, used)}{' ' + tail if tail else ''}")

    return "\n".join(out) + ("\n" if text.endswith("\n") else "")


def materialise(rev: str) -> pathlib.Path:
    directory = pathlib.Path(tempfile.mkdtemp(prefix="umpk-pins-"))
    listing = subprocess.run(
        ["git", "-C", str(REPO), "ls-tree", "--name-only", f"{rev}:{PIN_PATH}"],
        capture_output=True, text=True, check=False)
    if listing.returncode != 0:
        sys.exit(f"cannot read {rev}:{PIN_PATH}: {listing.stderr.strip()}")

    for name in listing.stdout.split():
        blob = subprocess.run(
            ["git", "-C", str(REPO), "show", f"{rev}:{PIN_PATH}/{name}"],
            capture_output=True, text=True, check=True)
        (directory / name).write_text(blob.stdout)

    return directory


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rename-map", required=True, type=pathlib.Path)
    parser.add_argument("--before", type=pathlib.Path, help="directory holding the pre-change fixtures")
    parser.add_argument("--before-rev", help="git revision to read the pre-change fixtures from, e.g. HEAD~1")
    parser.add_argument("--after", required=True, type=pathlib.Path, help="directory holding the regenerated fixtures")
    parser.add_argument("--strict-map", action="store_true", help="fail when a map entry matched nothing")
    args = parser.parse_args()

    if (args.before is None) == (args.before_rev is None):
        sys.exit("pass exactly one of --before or --before-rev")

    entries = parse_map(args.rename_map)
    before = args.before if args.before is not None else materialise(args.before_rev)
    after = args.after
    if not before.is_dir():
        sys.exit(f"{before}: not a directory")
    if not after.is_dir():
        sys.exit(f"{after}: not a directory")

    names = sorted({p.name for p in before.glob("*.txt")} | {p.name for p in after.glob("*.txt")})
    if not names:
        sys.exit(f"no *.txt fixtures under {before} or {after}")

    used: set[str] = set()
    failures = 0
    for name in names:
        left, right = before / name, after / name
        if not left.exists():
            print(f"{name}: only in {after}")
            failures += 1
            continue
        if not right.exists():
            print(f"{name}: only in {before}")
            failures += 1
            continue

        projected = project_text(left.read_text(), entries, used)
        actual = right.read_text()
        if projected == actual:
            continue

        failures += 1
        diff = difflib.unified_diff(
            projected.splitlines(), actual.splitlines(),
            fromfile=f"{name} (projected)", tofile=f"{name} (regenerated)", lineterm="", n=0)
        print("\n".join(diff))

    unused = sorted(old for old, _ in entries if old not in used)
    if unused:
        print(f"{len(unused)} map entr{'y' if len(unused) == 1 else 'ies'} matched nothing: {', '.join(unused[:10])}")
        if args.strict_map:
            failures += 1

    if failures:
        print(f"projection: {failures} file(s) differ after applying {len(entries)} rename(s)")
        return 1

    print(f"projection: OK ({len(names)} fixtures, {len(entries)} renames, {len(used)} applied)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
