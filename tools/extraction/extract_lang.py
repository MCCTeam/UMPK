#!/usr/bin/env python3
"""Extract vanilla `en_us` translation tables into `data/java/<protocol>/lang.json`.

Why per protocol and not one shared table
------------------------------------------
Every protocol negotiates against ITS OWN era's translation table, because a key can
exist in more than one era with a different argument arity. 1.8's `gameMode.changed`
is "Your game mode has been updated" (zero arguments); 1.9's is "Your game mode has
been updated to %s" (one argument). Resolving a 1.8 chat line against the modern
table would print a trailing "to " with nothing after it. This is not a legacy-only
problem either: 1.13's `commands.spawnpoint.success.single` takes four positional
arguments, 26.2's takes six, and `menu.preparingSpawn` gained a `%s%%` progress
suffix at 1.14.4 (protocol 498) that 1.13 (protocol 393) never had. So the dataset
carries one full table per protocol rather than a base-plus-overlay scheme: the
tables are human-auditable text, and 49 of them still compress far better than the
arity bugs a partial table would reintroduce.

Representative version per protocol
------------------------------------
Several Minecraft releases share one protocol number (the catalog in
`data/java/versions.json` maps names N:1 onto protocols). This script extracts from
the LAST catalog entry for each protocol (e.g. protocol 47 -> 1.8.9, 110 -> 1.9.4),
then verifies every OTHER catalog name mapping to that protocol agrees with the
representative on argument ARITY for every key both tables define. A mismatch means
one protocol number cannot be represented by a single table and is a hard failure,
not a warning: silently picking one release's table over another's would reproduce
exactly the bug this script exists to prevent.

Lang table formats, oldest to newest
-------------------------------------
1.8 - 1.10.x ship `assets/minecraft/lang/en_US.lang` (capital US); 1.11 - 1.12.2 ship
`assets/minecraft/lang/en_us.lang` (lowercase, still `key=value` with CRLF line
endings on 1.8.9 specifically); 1.13+ ship `assets/minecraft/lang/en_us.json`. Every
modern jar also carries a sibling `deprecated.json` mapping OLD keys to NEW ones,
which is deliberately not read: it is a rename table, not a translation table, and
reading it would inject stale keys that never resolve to a real template.

1.18+ server jars are BUNDLER jars: the real game jar with the lang file lives at
`META-INF/versions/<name>/<file>`, named by `META-INF/versions.list`
(`<sha256>\t<name>\t<path>`), so a jar that has no top-level lang file is retried one
level down before giving up.

Usage
-----
    python3 tools/extraction/extract_lang.py --data ../../data/java
    python3 tools/extraction/extract_lang.py --data ../../data/java --check

`--check` re-extracts every protocol into memory and diffs it against the committed
file, without writing anything; nonzero exit on any difference. This is the
staleness pin: server jars are gitignored and not redistributable, so `--check` plus
each file's `_provenance.jarSha256` is what stands in for committing the jars
themselves.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import sys
import zipfile
from pathlib import Path

import umpk_extract as ux

# Mirrors vanilla ChatComponentTranslation's FORMAT_PATTERN and applies it to every era because modern keys can drift on arity too (see the module docstring).
FORMAT_PATTERN = re.compile(r"%(?:(\d+)\$)?([A-Za-z%]|$)")

# Candidate paths inside a jar (or a bundler jar's inner game jar), oldest first.
LANG_PATHS = (
    "assets/minecraft/lang/en_us.json",
    "assets/minecraft/lang/en_us.lang",
    "assets/minecraft/lang/en_US.lang",
)


def arity(template: str) -> int:
    """Number of positional arguments `template` consumes (0 when it takes none)."""
    auto = 0
    highest = 0
    for match in FORMAT_PATTERN.finditer(template):
        index, conversion = match.group(1), match.group(2)
        if conversion == "%":
            continue
        if conversion != "s":
            # Vanilla throws on anything but s/%; these keys are client-side String.format users and never arrive as chat components.
            continue
        if index is None:
            auto += 1
        else:
            highest = max(highest, int(index))
    return max(auto, highest)


def _parse_lang_text(raw: str) -> dict[str, str]:
    # 1.8.9's en_US.lang is CRLF; splitlines() handles both line endings uniformly. 1.12.2 has ten values that themselves contain "=" (colour-code-laden death messages and the like), so split("=", 1) keeps everything after the first one.
    table: dict[str, str] = {}
    for line in raw.splitlines():
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        table[key] = value
    return table


def _parse_lang_json(raw: bytes) -> dict[str, str]:
    document = json.loads(raw.decode("utf-8"))
    # Keep only string values. deprecated.json is never read at all (see module docstring), but a defensive filter here costs nothing if a future jar ever merges non-string metadata into en_us.json itself.
    return {k: v for k, v in document.items() if isinstance(v, str)}


def _find_lang(names: set[str]) -> str | None:
    for path in LANG_PATHS:
        if path in names:
            return path
    return None


def read_lang(jar_path: Path) -> tuple[dict[str, str], str]:
    """Reads a vanilla `en_us` lang table out of a server jar.

    Returns (entries, path-within-jar). Tries the three known top-level paths first;
    for a bundler jar (1.18+, no top-level lang file but a META-INF/versions.list)
    walks into the inner game jar and retries there.
    """
    with zipfile.ZipFile(jar_path) as zf:
        names = set(zf.namelist())
        found = _find_lang(names)
        if found is not None:
            data = zf.read(found)
            return (_parse_lang_json(data) if found.endswith(".json") else _parse_lang_text(data.decode("utf-8"))), found

        if "META-INF/versions.list" in names:
            listing = zf.read("META-INF/versions.list").decode("utf-8")
            for line in listing.splitlines():
                fields = line.strip().split("\t")
                if len(fields) < 3:
                    continue
                inner_name = f"META-INF/versions/{fields[2]}"
                if inner_name not in names:
                    continue
                with zipfile.ZipFile(io.BytesIO(zf.read(inner_name))) as izf:
                    inner_names = set(izf.namelist())
                    inner_found = _find_lang(inner_names)
                    if inner_found is not None:
                        data = izf.read(inner_found)
                        parsed = _parse_lang_json(data) if inner_found.endswith(".json") else _parse_lang_text(data.decode("utf-8"))
                        return parsed, f"{inner_name}!{inner_found}"

    raise SystemExit(f"{jar_path}: no lang table found (checked {LANG_PATHS}, including bundled inner jars)")


def jar_sha256(jar_path: Path) -> str:
    return hashlib.sha256(jar_path.read_bytes()).hexdigest()


def load_catalog(versions_path: Path) -> tuple[dict[int, str], dict[int, list[str]]]:
    """protocol -> representative name (the LAST catalog entry for that protocol),
    protocol -> every catalog name mapping to it, in catalog order."""
    catalog = json.loads(versions_path.read_text(encoding="utf-8"))["versions"]
    representative: dict[int, str] = {}
    members: dict[int, list[str]] = {}
    for entry in catalog:
        protocol = entry["protocol"]
        representative[protocol] = entry["name"]  # last write wins: catalog order
        members.setdefault(protocol, []).append(entry["name"])
    return representative, members


def extract_protocol(protocol: int, rep_name: str, member_names: list[str], downloads: Path) -> dict:
    rep_jar = downloads / rep_name / "server.jar"
    if not rep_jar.exists():
        raise SystemExit(f"protocol {protocol}: missing representative server jar {rep_jar}")
    rep_entries, source_path = read_lang(rep_jar)

    for name in member_names:
        if name == rep_name:
            continue
        jar_path = downloads / name / "server.jar"
        if not jar_path.exists():
            print(f"  note: {name} server.jar absent, skipping arity guard", file=sys.stderr)
            continue
        entries, _ = read_lang(jar_path)
        drift = [k for k in entries if k in rep_entries and arity(entries[k]) != arity(rep_entries[k])]
        if drift:
            raise SystemExit(
                f"protocol {protocol}: {name} disagrees with representative {rep_name} on the "
                f"arity of {len(drift)} key(s) ({drift[:5]}); a single per-protocol table cannot "
                f"represent this band")

    return {
        "_provenance": {
            "kind": "extracted",
            "tool": "tools/extraction/extract_lang.py",
            "source": source_path,
            "version": rep_name,
            "jarSha256": jar_sha256(rep_jar),
        },
        "entries": dict(sorted(rep_entries.items())),
    }


def write_json(path: Path, payload: dict) -> None:
    # Deliberately no em-dash guard here, unlike umpk_extract.write_json: this file's "entries" are Mojang's own shipped client strings, not authored prose, and the dataset's whole premise is byte-for-byte fidelity to what the game actually sends (protocol 776's gui.friends.error.generic uses one). Rewriting a vanilla string to dodge a house rule aimed at OUR writing would make the table wrong.
    text = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data", required=True, type=Path, help="the data/java root")
    parser.add_argument("--downloads", type=Path, default=None, help="server jar root (default: the umpk_extract convention)")
    parser.add_argument("--check", action="store_true", help="verify committed lang.json files instead of writing them")
    args = parser.parse_args()

    downloads = args.downloads if args.downloads is not None else ux.DOWNLOADS
    representative, members = load_catalog(args.data / "versions.json")

    problems: list[str] = []
    written = 0
    for protocol in sorted(representative):
        document = extract_protocol(protocol, representative[protocol], members[protocol], downloads)
        out_path = args.data / str(protocol) / "lang.json"

        if args.check:
            if not out_path.exists():
                problems.append(f"protocol {protocol}: {out_path} does not exist")
                continue
            committed = json.loads(out_path.read_text(encoding="utf-8"))
            if committed != document:
                problems.append(f"protocol {protocol}: {out_path} is stale (does not match re-extraction)")
            continue

        write_json(out_path, document)
        written += 1
        print(f"wrote {out_path} ({len(document['entries'])} entries, {representative[protocol]!r})")

    if args.check:
        if problems:
            for problem in problems:
                print(f"error: {problem}", file=sys.stderr)
            return 1
        print(f"check: OK ({len(representative)} protocols)")
        return 0

    print(f"wrote {written} lang.json file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
