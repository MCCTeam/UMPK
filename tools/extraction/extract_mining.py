"""Convert a complete MiningDump measurement into per-block vanilla mining facts."""

import argparse
import copy
import hashlib
import json
import math
from collections import Counter
from pathlib import Path


def validate_row(row: list, minimum: float, context: str) -> None:
    if not isinstance(row, list) or len(row) != 2:
        raise ValueError(f"Invalid mining row for {context}")
    value, flag = row
    if type(value) not in (int, float) or not math.isfinite(value) or value < minimum:
        raise ValueError(f"Invalid mining number for {context}: {value!r}")
    if type(flag) is not bool:
        raise ValueError(f"Invalid mining flag for {context}: {flag!r}")


def fold(dump: dict, states: dict[int, str], items: set[str]) -> dict:
    expected_states = {str(state) for state in states}
    if set(dump["states"]) != expected_states:
        raise ValueError("Measured state IDs do not match the canonical state IDs")
    if set(dump["items"]) != items:
        raise ValueError("Measured items do not match the canonical item names")
    blocks = {}
    for state, name in states.items():
        measured = dump["states"][str(state)]
        if not isinstance(measured, list) or len(measured) != 3 or measured[0] != name:
            raise ValueError(f"Measured state {state} does not name {name}")
        row = measured[1:]
        validate_row(row, -1.0, f"state {state}")
        if row[0] < 0 and row[0] != -1:
            raise ValueError(f"Invalid negative hardness for state {state}")
        if name in blocks and blocks[name] != row:
            raise ValueError(f"Block {name} has state-dependent mining facts")
        blocks[name] = row

    tools = {}
    state_counts = Counter(states.values())
    for item in sorted(items):
        if not set(dump["items"][item]).issubset(expected_states):
            raise ValueError(f"Tool {item} refers to an unknown state")
        rules = {}
        measured_counts = Counter()
        for raw_state, row in dump["items"][item].items():
            state = int(raw_state)
            name = states[state]
            validate_row(row, 0.0, f"tool {item}, state {state}")
            if name in rules and rules[name] != row:
                raise ValueError(f"Tool {item} has state-dependent behavior for {name}")
            rules[name] = row
            measured_counts[name] += 1
        for name, row in rules.items():
            if row != [1.0, False] and measured_counts[name] != state_counts[name]:
                raise ValueError(f"Tool {item} has state-dependent behavior for {name}, including implicit defaults")
        rules = {name: row for name, row in sorted(rules.items()) if row != [1.0, False]}
        if rules:
            tools[item] = rules
    return {"blocks": dict(sorted(blocks.items())), "tools": tools}


def fold_dataset(dump: dict, blocks: dict, items: dict) -> tuple[dict, int]:
    if blocks["identity"] == "flat":
        states = {}
        for block in blocks["blocks"]:
            for state in range(block["min_state"], block["max_state"] + 1):
                if state in states:
                    raise ValueError(f"Duplicate canonical state ID {state}")
                states[state] = block["name"]
        return fold(dump, states, {item["name"] for item in items["entries"]}), len(states)
    if blocks["identity"] != "legacy":
        raise ValueError("Unknown block identity")

    def qualified(name: str) -> str:
        return name if ":" in name else "minecraft:" + name

    names = {block["block_id"]: block.get("name", block.get("material")) for block in blocks["blocks"]}
    normalized = copy.deepcopy(dump)
    states = {}
    seen_blocks = set()
    for raw_id, row in normalized["states"].items():
        state = int(raw_id)
        block_id = state >> 4
        if state < 0 or str(state) != raw_id or block_id not in names:
            raise ValueError(f"Unknown legacy state {raw_id}")
        if not isinstance(row, list) or len(row) != 3 or row[0] != qualified(names[block_id]):
            raise ValueError(f"Legacy state {state} does not name its native block")
        states[state] = names[block_id]
        row[0] = names[block_id]
        seen_blocks.add(block_id)
    if seen_blocks != set(names):
        raise ValueError("Measured legacy block IDs do not match the canonical registry")
    item_names = {qualified(item["name"]): item["name"] for item in items["entries"]}
    if set(normalized["items"]) != set(item_names):
        raise ValueError("Measured legacy items do not match the canonical registry")
    normalized["items"] = {item_names[name]: rules for name, rules in normalized["items"].items()}
    return fold(normalized, states, set(item_names.values())), len(states)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("protocol", type=int)
    parser.add_argument("--version", required=True)
    parser.add_argument("--dump", type=Path, required=True)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--symbols", type=Path, help="Source-derived bindings used for an obfuscated artifact")
    args = parser.parse_args()
    catalog = json.loads((args.data / "versions.json").read_text(encoding="utf-8"))["versions"]
    if not any(v["name"] == args.version and v["protocol"] == args.protocol for v in catalog):
        raise ValueError("The version and protocol must match the canonical catalog")
    directory = args.data / str(args.protocol)
    blocks = json.loads((directory / "blocks.json").read_text(encoding="utf-8"))
    item_document = json.loads((directory / "items.json").read_text(encoding="utf-8"))
    items = item_document["entries"]
    downloads = args.official / "downloads" / args.version
    metadata = json.loads((downloads / "version.json").read_text(encoding="utf-8"))
    artifact = (downloads / "server.jar").read_bytes()
    artifact_sha1 = hashlib.sha1(artifact).hexdigest()
    if artifact_sha1 != metadata["downloads"]["server"]["sha1"]:
        raise ValueError("Server artifact hash differs from the official version metadata")
    raw = args.dump.read_bytes()
    facts, measured_states = fold_dataset(json.loads(raw), blocks, item_document)
    mappings = downloads / "server_mappings.txt"
    sources = [f"downloads/{args.version}/server.jar"]
    if mappings.exists():
        expected = metadata["downloads"]["server_mappings"]["sha1"]
        if hashlib.sha1(mappings.read_bytes()).hexdigest() != expected:
            raise ValueError("Server mappings hash differs from the official version metadata")
        sources.append(f"downloads/{args.version}/server_mappings.txt")
    document = {
        "_provenance": {
            "version": args.version,
            "protocol": args.protocol,
            "kind": "extracted",
            "mapping_source": "obfuscated-source-derived" if args.symbols else "mojang-proguard" if mappings.exists() else "unobfuscated",
            "sources": sources,
            "server_sha1": artifact_sha1,
            "server_sha256": hashlib.sha256(artifact).hexdigest(),
            "measurement_sha256": hashlib.sha256(raw).hexdigest(),
            "tool": "tools/extraction/shape-extractor/LegacyMiningGeneric.java" if args.symbols else "tools/extraction/shape-extractor/MiningDump.java",
            "note": "Vanilla block tags are bound before measuring default item stacks. Every state is checked before collapsing facts by block name. Custom stack components and server data packs can override default tool behavior.",
        },
        "schema": 1,
        "identity": blocks["identity"],
        "state_count": len(blocks["blocks"]) if blocks["identity"] == "legacy" else measured_states,
        "measured_state_count": measured_states,
        "item_count": len(items),
        "default_tool": [1.0, False],
        **facts,
    }
    if args.symbols:
        symbols = json.loads(args.symbols.read_bytes())
        if symbols["version"] != args.version:
            raise ValueError("Source bindings name a different version")
        tree = args.official / (args.version + "-decompiled")
        for name, expected in symbols["sources"].items():
            source = tree / (name + ".java")
            if hashlib.sha256(source.read_bytes()).hexdigest() != expected:
                raise ValueError(f"Decompiled source changed: {source}")
        document["_provenance"]["source_bindings"] = symbols
    output = directory / "block-mining.json"
    output.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(f"{output}: {len(facts['blocks'])} blocks, {len(facts['tools'])} tools")


if __name__ == "__main__":
    main()
