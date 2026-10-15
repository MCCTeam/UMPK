"""Validate native block-use and ordinary-cube placement contracts and fold equal protocol aliases."""

import argparse
import hashlib
import json
from pathlib import Path

from measure_block_interactions import interaction_catalog


def validate_measurement(raw: dict, states: dict[int, str], items: set[str]) -> tuple[dict[str, int], dict[str, list[str]]]:
    if set(raw) != {"states", "items"} or set(raw["states"]) != {str(state) for state in states}:
        raise ValueError("Interaction measurement does not cover the exact state registry")
    blocks: dict[str, int] = {}
    for state, name in states.items():
        row = raw["states"][str(state)]
        if not isinstance(row, list) or len(row) != 2 or row[0] != name or type(row[1]) is not int or not 0 <= row[1] <= 3:
            raise ValueError(f"Invalid interaction state contract at {state}")
        if name in blocks and blocks[name] != row[1]:
            raise ValueError(f"Nonuniform interaction flags require a per-state schema: {name}")
        blocks[name] = row[1]
    if set(raw["items"]) != items:
        raise ValueError("Interaction measurement does not cover the exact item registry")
    for name, row in raw["items"].items():
        if not isinstance(row, list) or len(row) != 2 or row[0] not in ("unknown", "pass", "simple_cube"):
            raise ValueError(f"Invalid item use contract: {name}")
        if row[1] != "" and row[1] not in blocks:
            raise ValueError(f"Unknown placed block: {name}")
        if row[0] == "simple_cube" and not row[1] or row[0] == "pass" and row[1]:
            raise ValueError(f"Inconsistent placed block: {name}")
    return dict(sorted(blocks.items())), dict(sorted(raw["items"].items()))


def validate_client_measurement(server: dict, client: dict, report: dict) -> None:
    classes = {"net.minecraft.world.level.block.Block", "net.minecraft.world.level.block.state.BlockState", "net.minecraft.world.item.Item", "net.minecraft.world.item.BlockItem"}
    if report.get("status") != "complete" or report.get("client_server_equal") is not True or set(report.get("native_client_classes", {})) != classes:
        raise ValueError("Missing completed native client parity or client artifact class origins")
    if server != client:
        raise ValueError("Native client/server interaction measurements differ")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    args = parser.parse_args()
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)
    reports = {row["version"]: row for row in json.loads((args.measurements / "block-interaction-probe-results.json").read_text())}
    clients = {row["version"]: row for row in json.loads((args.measurements / "block-interaction-client-results.json").read_text())}
    helper_hashes = json.loads((args.measurements / "measurement-tools.json").read_text())["source_sha256"]
    helper_root = Path(__file__).parent / "shape-extractor"
    for name in ("PushDump.java", "MiningDump.java", "BlockInteractionDump.java"):
        if helper_hashes.get(name) != hashlib.sha256((helper_root / name).read_bytes()).hexdigest():
            raise ValueError(f"Native interaction extractor source changed since measurement: {name}")
    grouped: dict[int, dict] = {}
    provenance: dict[int, list] = {}
    for entry in catalog:
        protocol, version = entry["protocol"], entry["name"]
        downloads = args.official / "downloads" / version
        if not (downloads / "server_mappings.txt").exists() and not version.startswith("26."):
            continue
        if version not in reports or reports[version]["status"] != "complete":
            raise ValueError(f"Missing completed native interaction measurement: {version}")
        directory = args.data / str(protocol)
        registry = json.loads((directory / "blocks.json").read_text())
        states = {state: block["name"] for block in registry["blocks"] for state in range(block["min_state"], block["max_state"] + 1)}
        items = {item["name"] for item in json.loads((directory / "items.json").read_text())["entries"]}
        measured_path = args.measurements / version / "block-interaction-probe.json"
        measured_bytes = measured_path.read_bytes()
        client_bytes = (args.measurements / version / "block-interaction-client.json").read_bytes()
        native = json.loads(measured_bytes)
        validate_client_measurement(native, json.loads(client_bytes), clients.get(version, {}))
        for side, contents, report in (("server", measured_bytes, reports[version]), ("client", client_bytes, clients[version])):
            if report.get("measurement_sha256") != hashlib.sha256(contents).hexdigest():
                raise ValueError(f"Native {side} measurement changed since extraction: {version}")
        blocks, measured_items = validate_measurement(native, states, items)
        document = {"schema": 1, "identity": "flat", "state_count": len(states), "blocks": blocks, "items": measured_items}
        if protocol in grouped and grouped[protocol] != document:
            raise ValueError(f"Native interaction contracts differ between releases sharing protocol {protocol}: {version}")
        grouped[protocol] = document
        metadata = json.loads((downloads / "version.json").read_text())
        artifacts = {}
        for key, name in (("server", "server.jar"), ("server_mappings", "server_mappings.txt"), ("client", "client.jar"), ("client_mappings", "client_mappings.txt")):
            if key not in metadata["downloads"]:
                continue
            artifact = (downloads / name).read_bytes()
            if hashlib.sha1(artifact).hexdigest() != metadata["downloads"][key]["sha1"]:
                raise ValueError(f"Official {key} hash mismatch for {version}")
            artifacts[name] = hashlib.sha256(artifact).hexdigest()
        if reports[version]["server_sha256"] != artifacts["server.jar"]:
            raise ValueError(f"Interaction measurement names a different server artifact: {version}")
        if clients[version]["client_sha256"] != artifacts["client.jar"]:
            raise ValueError(f"Interaction measurement names a different client artifact: {version}")
        provenance.setdefault(protocol, []).append({"version": version, "measurement_sha256": hashlib.sha256(measured_bytes).hexdigest(), "client_measurement_sha256": hashlib.sha256(client_bytes).hexdigest(), "artifacts_sha256": artifacts,
                                                  "native_client_classes": clients[version]["native_client_classes"], "client_class_preservation": clients[version]["preservation"]})
    tool = Path(__file__).parent / "shape-extractor/BlockInteractionDump.java"
    for protocol, document in grouped.items():
        document["_provenance"] = {
            "kind": "extracted", "tool": "tools/extraction/shape-extractor/BlockInteractionDump.java", "tool_sha256": hashlib.sha256(tool.read_bytes()).hexdigest(),
            "measurements": provenance[protocol],
            "note": "Flags 1=base passive block use, 2=base nonreplaceable block. Item contracts retain explicit unknown subclasses. simple_cube requires inherited placement, survival and full-cube collision methods and the ordinary feature set; runtime world, player and stack preconditions still apply.",
        }
        (args.data / str(protocol) / "block-interactions.json").write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: {len(provenance[protocol])} releases, {len(document['blocks'])} blocks, {len(document['items'])} items")


if __name__ == "__main__":
    main()
