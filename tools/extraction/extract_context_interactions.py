"""Validate native 1.13–1.14.3 placement-context measurements before writing canonical interaction contracts."""

import argparse
import hashlib
import json
from pathlib import Path

from extract_block_interactions import validate_measurement
from extract_legacy_interactions import validate_geometry
from measure_legacy_interactions import verify_origins
from measure_legacy_targeting import sha256, verify_artifact


def fold(raw: dict, geometry: dict, blocks: dict, items: dict) -> dict:
    if blocks["identity"] != "flat" or items["identity"] != "flat":
        raise ValueError("Expected flattened interaction registries")
    states = {state: block["name"] for block in blocks["blocks"] for state in range(block["min_state"], block["max_state"] + 1)}
    native_blocks, native_items = validate_measurement(raw, states, {item["name"] for item in items["entries"]})
    validate_geometry(raw, geometry)
    return {"schema": 1, "identity": "flat", "state_count": len(states), "blocks": native_blocks, "items": native_items}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True)
    args = parser.parse_args()
    extraction = Path(__file__).parent
    tools = json.loads((args.measurements / "measurement-tools.json").read_text(encoding="utf-8"))["source_sha256"]
    for name, expected in tools.items():
        path = extraction / "shape-extractor" / name if name.endswith(".java") else extraction / name
        if sha256(path) != expected:
            raise ValueError(f"Context interaction source changed since measurement: {name}")
    catalog = [entry for entry in json.loads((args.data / "versions.json").read_text(encoding="utf-8"))["versions"] if 393 <= entry["protocol"] < 498]
    reports = {row["version"]: row for row in json.loads((args.measurements / "context-interaction-client-results.json").read_text(encoding="utf-8"))}
    documents = {}
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        report = reports.get(version, {})
        if report.get("status") != "complete" or report.get("protocol") != protocol or report.get("semantic_side") != "client" or report.get("server_role") != "libraries_only":
            raise ValueError(f"Missing native client placement-context measurement: {version}")
        directory = args.measurements / version
        for name, key in (("interaction-client.json", "measurement_sha256"), ("interaction-geometry.json", "geometry_sha256"), ("interaction-symbols.json", "bindings_sha256")):
            if sha256(directory / name) != report.get(key):
                raise ValueError(f"Native interaction input changed: {version}: {name}")
        symbols = json.loads((directory / "interaction-symbols.json").read_text(encoding="utf-8"))
        expected_classes = {symbols[key] for key in ("block", "state", "item")}
        expected_classes.update(symbols["interaction_bindings"][key] for key in ("item_block", "shape", "box_type"))
        unsigned = directory / "client-unsigned.jar"
        origins = verify_origins((directory / "interaction-client.log").read_text(encoding="utf-8"), expected_classes, unsigned)
        if report.get("native_client_classes") != origins or not report.get("preservation", {}).get("unchanged_class_files") or sha256(unsigned) != report["preservation"]["unsigned_jar_sha256"]:
            raise ValueError(f"Native class origins or preservation evidence changed: {version}")
        for name, expected in symbols["client_sources"].items():
            if sha256(args.official / (version + "-client-decompiled") / name) != expected:
                raise ValueError(f"Native client source changed: {version}: {name}")
        registry = args.data / str(protocol)
        document = fold(json.loads((directory / "interaction-client.json").read_text(encoding="utf-8")), json.loads((directory / "interaction-geometry.json").read_text(encoding="utf-8")),
                        json.loads((registry / "blocks.json").read_text(encoding="utf-8")), json.loads((registry / "items.json").read_text(encoding="utf-8")))
        metadata = json.loads((args.official / "downloads" / version / "version.json").read_text(encoding="utf-8"))
        artifacts = {}
        for side in ("server", "client"):
            path = args.official / "downloads" / version / (side + ".jar")
            verify_artifact(path, metadata["downloads"][side]["sha1"])
            digest = sha256(path)
            if digest != report.get(side + "_sha256"):
                raise ValueError(f"Official artifact differs from native run: {version}: {side}")
            artifacts[side + ".jar"] = digest
        if protocol in documents:
            raise ValueError(f"Unexpected protocol alias in placement-context catalog: {protocol}")
        document["_provenance"] = {"kind": "extracted", "tool": "tools/extraction/extract_context_interactions.py", "tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                                   "tools_sha256": tools, "version": version, "artifacts_sha256": artifacts, "client_source_sha256": symbols["client_sources"], "native_client_classes": sorted(expected_classes),
                                   "measurement_sha256": report["measurement_sha256"], "geometry_sha256": report["geometry_sha256"], "bindings_sha256": report["bindings_sha256"], "client_class_preservation": report["preservation"],
                                   "registered_states_visited": report["registered_states_visited"], "equal_wire_state_aliases": report["duplicate_state_id_aliases"],
                                   "note": "Native client placement-context inheritance; every eligible state's actual voxel collision shape is measured before recognizing ordinary cubes. Material replacement is read from the native state. The server jar supplies libraries only. Unknown subclasses and runtime world, player and stack conditions still apply."}
        documents[protocol] = document
    for protocol, document in documents.items():
        (args.data / str(protocol) / "block-interactions.json").write_text(json.dumps(document, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        print(f"{protocol}: {document['state_count']} states, {len(document['blocks'])} blocks, {len(document['items'])} items")


if __name__ == "__main__":
    main()
