#!/usr/bin/env python3
"""Decompile driver with the three mapping paths.

Mapping path selection:
  * 1.14.4+ : Mojang-published Proguard mappings (server_mappings.txt), so the
    decompile binds readable Mojang names directly.
  * 1.8 - 1.14.3 : MCP-era community mappings; stock decompiles are
    single-letter obfuscated, so a community-mapped tree is required.
  * 26.1+ : Vineflower directly on the already-unobfuscated jar.

This driver does NOT re-run a decompiler when a usable tree already exists on
disk: where local decompiled trees already exist, the driver detects and
reuses them. In the offline extraction environment each of 47/770/776 already
has a decompiled tree, so this driver's job is detection plus reporting which
mapping source backs each tree, which is recorded in dataset provenance.
"""

from __future__ import annotations

import argparse

import umpk_extract as ux


def mapping_path_for(version: str) -> str:
    """Return the mapping-source label assigned to a version."""
    parts = _numeric(version)
    if parts >= (26, 0):
        return "vineflower-direct"  # 26.1+ jars ship unobfuscated
    if parts >= (1, 14, 4):
        return "mojang-proguard"  # published server_mappings.txt from 1.14.4
    return "mcp-community"  # 1.8 - 1.14.3


def _numeric(version: str) -> tuple[int, ...]:
    bits = []
    for chunk in version.split("."):
        digits = "".join(ch for ch in chunk if ch.isdigit())
        bits.append(int(digits) if digits else 0)
    return tuple(bits)


def resolve(version: str) -> dict:
    """Detect an existing decompiled tree and report the mapping source."""
    tree = ux.decompiled_tree(version)
    mapping = mapping_path_for(version)
    return {
        "version": version,
        "mapping_source": mapping,
        "tree": str(tree) if tree else None,
        "reused": tree is not None,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="UMPK decompile driver (detect/reuse)")
    parser.add_argument("version", help="version name, e.g. 1.21.5")
    args = parser.parse_args()

    result = resolve(args.version)
    if result["tree"] is None:
        print(
            f"no decompiled tree found for {args.version}; expected mapping "
            f"path '{result['mapping_source']}'. Re-decompilation is required "
            "(not performed in the offline extraction environment)."
        )
        return 2
    print(
        f"{args.version}: reusing {result['tree']} "
        f"(mapping source: {result['mapping_source']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
