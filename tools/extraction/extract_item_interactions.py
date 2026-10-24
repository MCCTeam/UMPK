"""Validate native default Tool components and merge equal release measurements by protocol."""

import argparse
import copy
import hashlib
import json
import math
import re
from pathlib import Path


def validate_default_tools(raw: dict, items: set[str], blocks: set[str]) -> dict:
    if set(raw) != items:
        raise ValueError("Default Tool measurement does not cover the exact item registry")
    identifier = re.compile(r"[a-z0-9_.-]+:[a-z0-9_./-]+\Z")
    for item, tool in raw.items():
        if tool is None:
            continue
        if not isinstance(tool, dict) or not isinstance(tool.get("rules"), list):
            raise ValueError(f"Invalid Tool component for {item}")
        speed = tool.get("default_mining_speed", 1.0)
        damage = tool.get("damage_per_block", 1)
        creative = tool.get("can_destroy_blocks_in_creative", True)
        if type(speed) not in (float, int) or not math.isfinite(speed) or speed < 0:
            raise ValueError(f"Invalid Tool default speed for {item}")
        if type(damage) is not int or damage < 0 or type(creative) is not bool:
            raise ValueError(f"Invalid Tool damage or creative flag for {item}")
        for rule in tool["rules"]:
            selector = rule.get("blocks")
            if isinstance(selector, str) and selector.startswith("#"):
                if not identifier.fullmatch(selector[1:]):
                    raise ValueError(f"Invalid Tool tag for {item}")
            else:
                members = [selector] if isinstance(selector, str) else selector
                if not isinstance(members, list) or any(not isinstance(member, str) or member not in blocks for member in members):
                    raise ValueError(f"Invalid Tool block selector for {item}")
            if "speed" in rule and (type(rule["speed"]) not in (float, int) or not math.isfinite(rule["speed"]) or rule["speed"] <= 0):
                raise ValueError(f"Invalid Tool rule speed for {item}")
            if "correct_for_drops" in rule and type(rule["correct_for_drops"]) is not bool:
                raise ValueError(f"Invalid Tool harvest flag for {item}")
    return copy.deepcopy(dict(sorted(raw.items())))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    args = parser.parse_args()
    catalog = json.loads((args.data / "versions.json").read_text())["versions"]
    grouped: dict[int, dict] = {}
    provenance: dict[int, list] = {}
    for entry in catalog:
        protocol, version = entry["protocol"], entry["name"]
        if protocol < 766:
            continue
        directory = args.data / str(protocol)
        items = {item["name"] for item in json.loads((directory / "items.json").read_text())["entries"]}
        blocks = {block["name"] for block in json.loads((directory / "blocks.json").read_text())["blocks"]}
        measurement = args.measurements / version / "item-interaction-raw.json"
        raw_bytes = measurement.read_bytes()
        measured = validate_default_tools(json.loads(raw_bytes), items, blocks)
        if protocol in grouped and grouped[protocol] != measured:
            raise ValueError(f"Default Tool components differ between releases sharing protocol {protocol}: {version}")
        grouped[protocol] = measured
        downloads = args.official / "downloads" / version
        metadata = json.loads((downloads / "version.json").read_text())
        artifacts = {}
        for key, name in (("server", "server.jar"), ("server_mappings", "server_mappings.txt")):
            if key not in metadata["downloads"]:
                continue
            artifact = (downloads / name).read_bytes()
            if hashlib.sha1(artifact).hexdigest() != metadata["downloads"][key]["sha1"]:
                raise ValueError(f"Official {key} hash mismatch for {version}")
            artifacts[name] = hashlib.sha256(artifact).hexdigest()
        provenance.setdefault(protocol, []).append({"version": version, "measurement_sha256": hashlib.sha256(raw_bytes).hexdigest(), "artifacts_sha256": artifacts})
    for protocol, measured in grouped.items():
        path = args.data / str(protocol) / "block-mining.json"
        document = json.loads(path.read_text())
        document["default_components"] = measured
        document["_provenance"]["default_components"] = {
            "tool": "tools/extraction/shape-extractor/ItemInteractionDump.java",
            "tool_sha256": hashlib.sha256((Path(__file__).parent / "shape-extractor/ItemInteractionDump.java").read_bytes()).hexdigest(),
            "measurements": provenance[protocol],
            "note": "Native Tool.CODEC with RegistryOps<JsonOps>. Every registered item is measured; null means no Tool component. Rule order, tag names, direct block selectors and optional fields are retained.",
        }
        path.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: {len(provenance[protocol])} releases, {len(measured)} items, {sum(tool is not None for tool in measured.values())} tools")


if __name__ == "__main__":
    main()
