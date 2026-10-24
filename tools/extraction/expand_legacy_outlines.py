"""Expand registered selection measurements through vanilla's legacy metadata decoder."""

import argparse
import json
from pathlib import Path


def expand_metadata(native: dict, aliases: dict, blocks: dict) -> dict:
    if blocks.get("identity") != "legacy":
        raise ValueError("Metadata expansion requires a legacy block registry")
    names = {}
    for block in blocks["blocks"]:
        identifier = block.get("name", block.get("material"))
        if not identifier:
            raise ValueError("Canonical legacy block name is missing")
        names[block["block_id"]] = identifier if ":" in identifier else "minecraft:" + identifier
    accepted, rejected = aliases["aliases"], aliases["decode_failures"]
    if set(accepted) & set(rejected):
        raise ValueError("Wire metadata was both accepted and rejected")
    expected = {str((block << 4) | meta) for block in names for meta in range(16)}
    if set(accepted) | set(rejected) != expected or aliases["block_count"] != len(names):
        raise ValueError("Native wire metadata coverage differs from the canonical block registry")
    if {str(value) for value in accepted.values()} != set(aliases["canonical_states"]):
        raise ValueError("Native alias targets differ from the decoded canonical states")
    rows = {}
    for raw_wire, target in accepted.items():
        wire = int(raw_wire)
        if type(target) is not int or target < 0 or target >> 4 != wire >> 4:
            raise ValueError(f"Native alias changed block ownership: {raw_wire} -> {target}")
        raw_target = str(target)
        if raw_target not in native:
            raise ValueError(f"Alias {raw_wire} targets an unmeasured native state {target}")
        name = names[wire >> 4]
        if native[raw_target].get("block") != name or aliases["canonical_states"][raw_target] != name:
            raise ValueError(f"Native alias or measurement has a different registry name at {wire}")
        rows[raw_wire] = {**native[raw_target], "canonical_state": target}
    for wire, failure in rejected.items():
        if not isinstance(failure, list) or len(failure) != 3 or failure[0] != names[int(wire) >> 4] or not isinstance(failure[1], str):
            raise ValueError(f"Invalid native metadata decoder failure at {wire}")
    return {"block_count": len(names), "wire_state_count": len(expected), "states": rows, "rejected_metadata": rejected}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blocks", required=True, type=Path)
    parser.add_argument("--aliases", required=True, type=Path)
    parser.add_argument("--outlines", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    result = expand_metadata(json.loads(args.outlines.read_text()), json.loads(args.aliases.read_text()), json.loads(args.blocks.read_text()))
    args.out.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n")
    print(f"Expanded {len(result['states'])} decoded wire states; {len(result['rejected_metadata'])} native decoder failures remain explicit")


if __name__ == "__main__":
    main()
