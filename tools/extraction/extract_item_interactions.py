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


def extract_tool_values(items: dict, registries: dict, *, shape: str = 'component-array', complete_report: bool = False) -> dict:
    try:
        item_entries = registries['minecraft:item']['entries']
        block_entries = registries['minecraft:block']['entries']
    except (KeyError, TypeError) as error:
        raise ValueError('Incomplete native registry report') from error
    if not isinstance(item_entries, dict) or not item_entries or not isinstance(block_entries, dict) or not block_entries:
        raise ValueError('Incomplete native registry report')
    if shape not in {'component-array', 'component-object', 'per-registry-component-files'}:
        raise ValueError('Unsupported native report format')
    if not isinstance(items, dict):
        raise ValueError('Missing native item report')
    sparse = shape == 'per-registry-component-files'
    if sparse:
        if not complete_report:
            raise ValueError('Missing complete native producer proof: availability is unknown')
        if set(items) - set(item_entries):
            raise ValueError('Native report contains foreign item keys')
    elif set(items) != set(item_entries):
        raise ValueError('Item report must cover exact native item registry')
    result = {}
    for name in item_entries:
        if sparse and name not in items:
            # Original completed RegistryComponentsReport omits exactly empty maps.
            result[name] = None
            continue
        item = items[name]
        if not isinstance(item, dict) or set(item) != {'components'}:
            raise ValueError('Missing or unexpected native component report fields: ' + name)
        components = item['components']
        tool = None
        if shape == 'component-array':
            if not isinstance(components, list):
                raise ValueError('Missing complete component array: ' + name)
            seen = set()
            for component in components:
                if not isinstance(component, dict) or set(component) != {'type', 'value'} or not isinstance(component['type'], str):
                    raise ValueError('Malformed component type/value entry: ' + name)
                kind = component['type']
                if kind in seen:
                    raise ValueError('Native component array contains duplicate type: ' + name)
                seen.add(kind)
                if kind == 'minecraft:tool':
                    if not isinstance(component['value'], dict):
                        raise ValueError('Native TOOL value is not an object: ' + name)
                    tool = copy.deepcopy(component['value'])
        else:
            if not isinstance(components, dict):
                raise ValueError('Missing complete component object: ' + name)
            if any(not isinstance(key, str) or key.startswith('!') for key in components):
                raise ValueError('Unexpected component removal patch: ' + name)
            if 'minecraft:tool' in components:
                if not isinstance(components['minecraft:tool'], dict):
                    raise ValueError('Native TOOL value is not an object: ' + name)
                tool = copy.deepcopy(components['minecraft:tool'])
        result[name] = tool
    return result

def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_pin(record: dict) -> Path:
    path = Path(record["path"]).resolve()
    if path.stat().st_size != record["bytes"] or sha256(path) != record["sha256"]:
        raise ValueError(f"Native report input hash mismatch: {path}")
    return path


def load_native_reports(path: Path, expected_sha256: str, catalog: list[dict]) -> dict:
    if sha256(path) != expected_sha256:
        raise ValueError("Native report recipe hash mismatch")
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if recipe.get("schema_version") != 1 or recipe.get("origin") != "vanilla-server-data-generator":
        raise ValueError("Unsupported native report recipe origin")
    verify_pin(recipe["source_equivalence"])
    expected = {row["name"]: row["protocol"] for row in catalog if row["protocol"] >= 766}
    rows = recipe["releases"]
    if len(rows) != len(expected) or {row["version"]: row["protocol"] for row in rows} != expected:
        raise ValueError("Native report recipe must cover the exact release cohort")
    result = {}
    for row in rows:
        protocol = row["protocol"]
        shape = "component-array" if protocol == 766 else "component-object" if protocol < 775 else "per-registry-component-files"
        if row["format"] != shape:
            raise ValueError("Native report format does not match the protocol")
        if type(row["native_child_exit_code"]) is not int or row["native_child_exit_code"] != 0 or row["all_providers_completed"] is not True:
            raise ValueError("Missing complete native producer proof: availability is unknown")
        verify_pin(row["native_execution"])
        root = Path(row["reports_root"]).resolve()
        files = row["files"]
        if {str(file.relative_to(root)) for file in root.rglob("*") if file.is_file()} != set(files):
            raise ValueError("Native report manifest does not cover the exact output tree")
        for relative, record in files.items():
            actual = verify_pin(record)
            if not actual.is_relative_to(root) or actual != (root / relative).resolve():
                raise ValueError("Native report manifest path escapes the report root")
        registries = json.loads((root / "registries.json").read_text(encoding="utf-8"))
        if shape == "per-registry-component-files":
            items = {}
            for relative in files:
                parts = Path(relative).parts
                if len(parts) >= 4 and parts[1:3] == ("components", "item") and parts[-1].endswith(".json"):
                    name = parts[0] + ":" + "/".join((*parts[3:-1], parts[-1][:-5]))
                    items[name] = json.loads((root / relative).read_text(encoding="utf-8"))
        else:
            items = json.loads((root / "items.json").read_text(encoding="utf-8"))
        tools = extract_tool_values(items, registries, shape=shape, complete_report=True)
        result[row["version"]] = {"tools": tools, "items": set(registries["minecraft:item"]["entries"]), "blocks": set(registries["minecraft:block"]["entries"]), "provenance": {"version": row["version"], "native_report_format": shape, "native_execution_sha256": row["native_execution"]["sha256"], "native_report_file_manifest_digest": hashlib.sha256(json.dumps(files, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()}}
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument("--measurements", type=Path)
    inputs.add_argument("--native-reports", type=Path, help="Reviewed, pinned native server report recipe; this does not recover historical ItemInteractionDump inputs")
    parser.add_argument("--native-reports-sha256", help="SHA-256 of the reviewed native report recipe")
    args = parser.parse_args()
    if bool(args.native_reports) != bool(args.native_reports_sha256):
        parser.error("--native-reports and --native-reports-sha256 must be supplied together")
    catalog = json.loads((args.data / "versions.json").read_text())["versions"]
    native = load_native_reports(args.native_reports, args.native_reports_sha256, catalog) if args.native_reports else None
    grouped: dict[int, dict] = {}
    provenance: dict[int, list] = {}
    for entry in catalog:
        protocol, version = entry["protocol"], entry["name"]
        if protocol < 766:
            continue
        directory = args.data / str(protocol)
        items = {item["name"] for item in json.loads((directory / "items.json").read_text())["entries"]}
        blocks = {block["name"] for block in json.loads((directory / "blocks.json").read_text())["blocks"]}
        if native is None:
            measurement = args.measurements / version / "item-interaction-raw.json"
            raw_bytes = measurement.read_bytes()
            measured = validate_default_tools(json.loads(raw_bytes), items, blocks)
            measured_origin = {"version": version, "measurement_sha256": hashlib.sha256(raw_bytes).hexdigest()}
        else:
            report = native[version]
            if report["items"] != items or report["blocks"] != blocks:
                raise ValueError(f"Native report registries differ from the dataset for {version}")
            measured = validate_default_tools(report["tools"], items, blocks)
            measured_origin = report["provenance"]
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
        provenance.setdefault(protocol, []).append(measured_origin | {"artifacts_sha256": artifacts})
    for protocol, measured in grouped.items():
        path = args.data / str(protocol) / "block-mining.json"
        document = json.loads(path.read_text())
        document["default_components"] = measured
        if native is None:
            document["_provenance"]["default_components"] = {
                "tool": "tools/extraction/shape-extractor/ItemInteractionDump.java",
                "tool_sha256": hashlib.sha256((Path(__file__).parent / "shape-extractor/ItemInteractionDump.java").read_bytes()).hexdigest(),
                "measurements": provenance[protocol],
                "note": "Native Tool.CODEC with RegistryOps<JsonOps>. Every registered item is measured; null means no Tool component. Rule order, tag names, direct block selectors and optional fields are retained.",
            }
        else:
            document["_provenance"]["default_components"] = {
                "tool": "tools/extraction/extract_item_interactions.py --native-reports",
                "tool_sha256": sha256(Path(__file__)),
                "origin": "vanilla-server-data-generator",
                "native_report_recipe_sha256": args.native_reports_sha256,
                "source_equivalence_sha256": json.loads(args.native_reports.read_text(encoding="utf-8"))["source_equivalence"]["sha256"],
                "measurements": provenance[protocol],
                "note": "Original server ItemListReport or RegistryComponentsReport output with Tool.CODEC values. Matching client and server item initialization and codec sources are bound by the reviewed recipe. Every native item registry entry is covered; null denotes proven absence after complete report production. Rule order, selectors, optional fields and value types are retained. These reports do not recover historical ItemInteractionDump payloads or establish complete mining behavior.",
            }
        path.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: {len(provenance[protocol])} releases, {len(measured)} items, {sum(tool is not None for tool in measured.values())} tools")


if __name__ == "__main__":
    main()
