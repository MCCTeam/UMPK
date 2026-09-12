#!/usr/bin/env python3
"""
Locate (and where it can, fill in) the hand-written per-protocol rows a new Minecraft version needs.

Twelve test files carry a literal table with one row per supported protocol. The values in those
tables stay hand-written on purpose: a conformance test that derives its expectation from the data it
verifies cannot fail on wrong data, and that exact mistake has shipped here. Only the ABSENCE of a row
is mechanical, which is what this script and AllProtocolTableCoverageTests between them make loud.

Nine of the twelve carry a bare list of protocol numbers, so a row is just the number and --apply can
insert it. Three carry a value beside the number (a release name, a vanilla feature string, an
expected count); those are reported, never written, because only a human reading vanilla knows what
goes in the second column.

Usage:
    python3 engineering/refactor/add-protocol-rows.py --check 777
    python3 engineering/refactor/add-protocol-rows.py --apply 777
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]

# (path, the member whose literal list holds the protocol column, whether a row is just the number)
TABLES: list[tuple[str, str, bool, str]] = [
    ("tests/Umpk.Protocol.Java.Tests/Registration/AliasBandBindingTests.cs",
     "private static readonly int[] All =", True, ""),
    ("tests/Umpk.Protocol.Java.Tests/Codecs/ConnectionObligationBindingTests.cs",
     "private static readonly int[] All =", True, ""),
    ("tests/Umpk.Protocol.Java.Tests/Login/LoginEraBindingTests.cs",
     "private static readonly int[] All =", True, ""),
    ("tests/Umpk.Protocol.Java.Tests/Codecs/MisboundCodecFramingTests.cs",
     "private static readonly int[] AllProtocols =", True, ""),
    ("tests/Umpk.Protocol.Java.Tests/World/RespawnEraTests.cs",
     "public static readonly int[] AllProtocols =", True, ""),
    ("tests/Umpk.Protocol.Java.Tests/Codecs/UseItemEraFramingTests.cs",
     "public static readonly int[] RotationBand =", True,
     "goes in the band whose wire form the new version keeps; RotationBand is the newest"),
    ("tests/Umpk.Client.Tests/RespawnSendTests.cs",
     "private static readonly int[] All =", True, ""),
    ("tests/Umpk.Client.Tests/SlabRestAcrossProtocolsTests.cs",
     "private static readonly int[] All =", True, ""),
    ("tests/Umpk.Data.Lang.Tests/VanillaTranslationsTests.cs",
     "int[] expected =", True, ""),
    ("tests/Umpk.Client.Tests/PhysicsProfileConformanceTests.cs",
     "public static TheoryData<int, string> WaterTravelExpectations() => new()", False,
     "each row carries the vanilla fluid-movement name; WaterClimbBumpExpectations needs one too"),
    ("tests/Umpk.Data.Java.Tests/Generated/PublishedVersionNameTests.cs",
     "private static readonly (string Name, int Protocol)[] Names =", False,
     "each row carries the published release name, or several"),
    ("tests/Umpk.IntegrationTests/LiveMatrix.cs",
     "public static readonly IReadOnlyList<(string Name, int Protocol)> Representatives =", False,
     "each row carries the representative release name whose jar the leg boots"),
]


def block(text: str, anchor: str, path: str) -> tuple[int, int]:
    """The half-open character range inside the collection initialiser that follows an anchor."""
    start = text.find(anchor)
    if start < 0:
        sys.exit(f"{path}: anchor not found: {anchor!r}")
    if text.find(anchor, start + 1) >= 0:
        sys.exit(f"{path}: anchor is ambiguous: {anchor!r}")

    i = start + len(anchor)
    while i < len(text) and text[i] not in "[{":
        if text[i] not in " \r\n\t" and text[i:i + 5] != "new()":
            if not text.startswith("new()", i):
                sys.exit(f"{path}: unexpected {text[i]!r} between {anchor!r} and its initialiser")
        i += 1
    if i >= len(text):
        sys.exit(f"{path}: no collection initialiser after {anchor!r}")

    pairs = {"[": "]", "{": "}"}
    depth = 0
    j = i
    in_string = False
    while j < len(text):
        ch = text[j]
        if in_string:
            if ch == "\\":
                j += 2
                continue
            if ch == '"':
                in_string = False
        elif ch == '"':
            in_string = True
        elif ch in pairs:
            depth += 1
        elif ch in pairs.values():
            depth -= 1
            if depth == 0:
                return i + 1, j
        j += 1

    sys.exit(f"{path}: unterminated initialiser after {anchor!r}")


def covered(text: str, span: tuple[int, int]) -> list[int]:
    return [int(n) for n in re.findall(r"(?<![\w.])(\d{2,4})(?![\w.])", text[span[0]:span[1]])]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", type=int, metavar="PROTOCOL", help="report which tables lack a row")
    mode.add_argument("--apply", type=int, metavar="PROTOCOL", help="insert the row where it is just a number")
    args = parser.parse_args()

    protocol = args.check if args.check is not None else args.apply
    applying = args.apply is not None

    missing_auto: list[str] = []
    missing_hand: list[str] = []
    for relative, anchor, automatic, note in TABLES:
        path = REPO / relative
        if not path.is_file():
            sys.exit(f"{relative}: no such file (the table moved; update this script)")
        text = path.read_text(encoding="utf-8")
        span = block(text, anchor, relative)
        rows = covered(text, span)
        if protocol in rows:
            print(f"  ok      {relative}")
            continue

        detail = f" ({note})" if note else ""
        if not automatic:
            missing_hand.append(relative)
            print(f"  BY HAND {relative}{detail}")
            continue

        missing_auto.append(relative)
        if not applying:
            print(f"  MISSING {relative}{detail}")
            continue

        last = max(rows)
        body = text[span[0]:span[1]]
        pattern = re.compile(rf"(?<![\w.]){last}(?![\w.])")
        if len(pattern.findall(body)) != 1:
            sys.exit(f"{relative}: {last} appears more than once in the table; insert {protocol} by hand")
        body = pattern.sub(f"{last}, {protocol}", body, count=1)
        path.write_text(text[:span[0]] + body + text[span[1]:], encoding="utf-8")
        print(f"  ADDED   {relative}: {protocol} after {last}{detail}")

    print()
    if applying and missing_auto:
        print(f"{len(missing_auto)} table(s) rewritten; re-run `dotnet format` on the touched files.")
    if missing_hand:
        print(f"{len(missing_hand)} table(s) need a hand-written row, one column at a time:")
        for relative in missing_hand:
            print(f"  {relative}")
        return 1
    if missing_auto and not applying:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
