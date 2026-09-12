#!/usr/bin/env python3
"""
Prove that a new pin column was ADDED and nothing else moved.

A pin gains a column by appending a token to every line. That is a whole-file diff, and a whole-file
diff is where a real change hides: one line whose identity or byte count also moved reads exactly like
the 8,275 lines whose token is simply new. The reviewer cannot see it and neither can the pin.

This script separates them. It strips the last whitespace-separated field from every line of the AFTER
fixtures and compares the result with the BEFORE fixtures. A purely additive column reduces to nothing;
a line that changed for any other reason survives, and the diff names the file and both forms of the
line.

Comment lines (leading "#") and the "codecs:" header carry no column and are compared verbatim.

Usage:

    git show HEAD:fixtures/codec-identity > /dev/null   # (use a worktree or `git show` per file)
    python3 engineering/refactor/verify_additive_column.py \\
        --before <dir of pre-change fixtures> \\
        --after fixtures/codec-identity/ \\
        --prefix shape:

Exit status is 0 when every line reduces, 1 otherwise.
"""

import argparse
import pathlib
import sys


def strip_last_field(line: str) -> str:
    head, sep, _ = line.rstrip("\n").rpartition(" ")
    return head if sep else line.rstrip("\n")


def compare(before_path: pathlib.Path, after_path: pathlib.Path, prefix: str) -> list[str]:
    problems: list[str] = []
    before = before_path.read_text().splitlines()
    after = after_path.read_text().splitlines()
    if len(before) != len(after):
        problems.append(
            f"{after_path.name}: {len(before)} lines before, {len(after)} after; a column addition adds no lines"
        )
        return problems

    for index, (old, new) in enumerate(zip(before, after), start=1):
        if old.startswith("#") or old == "codecs:" or not old.strip():
            if old != new:
                problems.append(f"{after_path.name}:{index}: header line changed\n  before: {old}\n  after:  {new}")
            continue

        last = new.rpartition(" ")[2]
        if not last.startswith(prefix):
            problems.append(f"{after_path.name}:{index}: last field is '{last}', expected a '{prefix}' token")
            continue

        reduced = strip_last_field(new)
        if reduced != old:
            problems.append(f"{after_path.name}:{index}: line moved for a reason other than the new column\n  before: {old}\n  after:  {reduced}")

    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--before", required=True, type=pathlib.Path, help="directory holding the pre-change fixtures")
    parser.add_argument("--after", required=True, type=pathlib.Path, help="directory holding the regenerated fixtures")
    parser.add_argument("--prefix", default="shape:", help="the token prefix the new column must carry")
    args = parser.parse_args()

    after_files = sorted(args.after.glob("*.txt"))
    if not after_files:
        print(f"additive: no fixtures under {args.after}", file=sys.stderr)
        return 1

    problems: list[str] = []
    for after_path in after_files:
        before_path = args.before / after_path.name
        if not before_path.exists():
            problems.append(f"{after_path.name}: no pre-change fixture in {args.before}")
            continue
        problems.extend(compare(before_path, after_path, args.prefix))

    if problems:
        print(f"additive: FAILED ({len(problems)} finding(s))")
        for problem in problems:
            print(problem)
        return 1

    print(f"additive: OK ({len(after_files)} fixtures, every line reduces to its pre-change form)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
