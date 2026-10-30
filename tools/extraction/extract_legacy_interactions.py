"""Fold complete native metadata-era interaction measurements into canonical block and item contracts."""

import copy
import argparse
import hashlib
import json
import math
from pathlib import Path

from extract_block_interactions import validate_measurement


def validate_geometry(raw: dict, evidence: dict) -> None:
    visited = evidence.get("registered_states_visited")
    aliases = evidence.get("duplicate_state_id_aliases")
    if type(visited) is not int or type(aliases) is not int or aliases < 0 or visited != len(raw["states"]) + aliases:
        raise ValueError("Native registry visit and equal-alias counts disagree")
    geometry = evidence["geometry"]
    for state, row in geometry.items():
        if state not in raw["states"] or not isinstance(row, list) or len(row) != 2 or row[0] != raw["states"][state][0] or not isinstance(row[1], list) or len(row[1]) not in (0, 6):
            raise ValueError(f"Invalid native collision evidence: {state}")
        if any(type(value) not in (float, int) or not math.isfinite(value) for value in row[1]):
            raise ValueError(f"Non-finite native collision evidence: {state}")
    simple = {row[1] for row in raw["items"].values() if row[0] == "simple_cube"}
    for state, row in raw["states"].items():
        if row[0] in simple and geometry.get(state) != [row[0], [0, 0, 0, 1, 1, 1]]:
            raise ValueError(f"Simple cube lacks complete native unit-box evidence: {state}")


def qualified(name: str) -> str:
    return name if ":" in name else "minecraft:" + name


def fold(raw: dict, blocks: dict, items: dict) -> dict:
    if blocks["identity"] != "legacy" or set(raw) != {"states", "items"}:
        raise ValueError("Expected metadata-era interaction measurements")
    names = {block["block_id"]: block.get("name", block.get("material")) for block in blocks["blocks"]}
    normalized = copy.deepcopy(raw)
    states = {}
    seen = set()
    for raw_id, row in normalized["states"].items():
        state = int(raw_id)
        block_id = state >> 4
        if state < 0 or str(state) != raw_id or block_id not in names or not isinstance(row, list) or len(row) != 2 or row[0] != qualified(names[block_id]):
            raise ValueError(f"Unrecognized native metadata state: {raw_id}")
        states[state] = names[block_id]
        row[0] = names[block_id]
        seen.add(block_id)
    if seen != set(names):
        raise ValueError("Native interaction measurement misses registered blocks")
    item_names = {qualified(item["name"]): item["name"] for item in items["entries"]}
    block_names = {qualified(name): name for name in names.values()}
    if set(normalized["items"]) != set(item_names):
        raise ValueError("Native interaction measurement misses registered items")
    normalized_items = {}
    for name, row in normalized["items"].items():
        if not isinstance(row, list) or len(row) != 2 or row[1] and row[1] not in block_names:
            raise ValueError(f"Invalid native block-item mapping: {name}")
        normalized_items[item_names[name]] = [row[0], block_names[row[1]] if row[1] else ""]
    normalized["items"] = normalized_items
    measured_blocks, measured_items = validate_measurement(normalized, states, set(item_names.values()))
    return {"schema": 1, "identity": "legacy", "state_count": len(blocks["blocks"]), "measured_state_count": len(states), "blocks": measured_blocks, "items": measured_items}


def select_profile(protocol: int, releases: list[tuple[str, dict]]) -> tuple[dict, list[dict]]:
    profile_name, profile = releases[-1]
    differences = []
    for version, measured in releases:
        if measured == profile:
            continue
        expected = copy.deepcopy(profile)
        expected["measured_state_count"] = 1391
        expected["blocks"]["air"] = 3
        if protocol != 47 or profile_name != "1.8.9" or version != "1.8" or profile["measured_state_count"] != 1394 or measured != expected:
            raise ValueError(f"Unreviewed client interaction difference for protocol {protocol}: {version}")
        differences.append({"version": version, "blocks": {"air": 3}, "measured_state_count": 1391})
    return copy.deepcopy(profile), differences


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    args = parser.parse_args()
    catalog = [entry for entry in json.loads((args.data / "versions.json").read_text())["versions"] if entry["protocol"] < 393]
    reports = {row["version"]: row for row in json.loads((args.measurements / "legacy-interaction-client-results.json").read_text())}
    tools = json.loads((args.measurements / "measurement-tools.json").read_text())["source_sha256"]
    extraction = Path(__file__).parent
    for name, expected in tools.items():
        path = extraction / "shape-extractor" / name if name.endswith(".java") else extraction / name
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError(f"Legacy interaction source changed since measurement: {name}")
    groups, provenance = {}, {}
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        report = reports.get(version, {})
        if report.get("status") != "complete" or report.get("protocol") != protocol or report.get("semantic_side") != "client":
            raise ValueError(f"Missing successful native client interaction measurement: {version}")
        directory = args.measurements / version
        for name, key in (("interaction-client.json", "measurement_sha256"), ("interaction-geometry.json", "geometry_sha256"), ("interaction-symbols.json", "bindings_sha256")):
            if hashlib.sha256((directory / name).read_bytes()).hexdigest() != report.get(key):
                raise ValueError(f"Native interaction input changed: {version}: {name}")
        symbols = json.loads((directory / "interaction-symbols.json").read_text())
        expected_classes = {symbols[key] for key in ("block", "state", "item")}
        expected_classes.update(symbols["interaction_bindings"][key] for key in ("item_block", "box_type"))
        if set(report.get("native_client_classes", {})) != expected_classes or not report.get("preservation", {}).get("unchanged_class_files"):
            raise ValueError(f"Missing native client class origins or class preservation: {version}")
        for name, expected in symbols["client_sources"].items():
            if hashlib.sha256((args.official / (version + "-client-decompiled") / name).read_bytes()).hexdigest() != expected:
                raise ValueError(f"Native interaction source evidence changed: {version}: {name}")
        native = json.loads((directory / "interaction-client.json").read_text())
        validate_geometry(native, json.loads((directory / "interaction-geometry.json").read_text()))
        data = args.data / str(protocol)
        document = fold(native, json.loads((data / "blocks.json").read_text()), json.loads((data / "items.json").read_text()))
        metadata = json.loads((args.official / "downloads" / version / "version.json").read_text())
        artifacts = {}
        for side in ("server", "client"):
            contents = (args.official / "downloads" / version / (side + ".jar")).read_bytes()
            digest = hashlib.sha256(contents).hexdigest()
            if hashlib.sha1(contents).hexdigest() != metadata["downloads"][side]["sha1"] or digest != report.get(side + "_sha256"):
                raise ValueError(f"Official interaction artifact differs: {version}: {side}")
            artifacts[side + ".jar"] = digest
        groups.setdefault(protocol, []).append((version, document))
        provenance.setdefault(protocol, []).append({"version": version, "measurement_sha256": report["measurement_sha256"], "geometry_sha256": report["geometry_sha256"], "bindings_sha256": report["bindings_sha256"], "artifacts_sha256": artifacts, "client_source_sha256": symbols["client_sources"], "native_client_classes": sorted(expected_classes), "client_class_preservation": report["preservation"], "registered_states_visited": report["registered_states_visited"], "equal_wire_state_aliases": report["duplicate_state_id_aliases"]})
    documents = {}
    for protocol, releases in groups.items():
        document, differences = select_profile(protocol, releases)
        document["_provenance"] = {"kind": "extracted", "tool": "tools/extraction/extract_legacy_interactions.py", "tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "tools_sha256": tools, "measurements": provenance[protocol], "client_profile": releases[-1][0], "different_earlier_client_profiles": differences, "note": "Native client inheritance contracts; every eligible state's actual collision box is measured before recognizing simple cubes. The server jar supplies libraries only because the placement precheck is client-only. Unknown subclasses remain explicit. Runtime player, world and stack conditions still apply. Protocol 47 follows the existing 1.8.9 client profile; the measured 1.8 air-replacement difference is retained separately and requires release-specific profile support."}
        documents[protocol] = document
    for protocol, document in documents.items():
        (args.data / str(protocol) / "block-interactions.json").write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: {len(provenance[protocol])} client releases, {len(document['blocks'])} blocks, {len(document['items'])} items")


if __name__ == "__main__":
    main()
