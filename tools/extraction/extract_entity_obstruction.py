"""Validate native entity building flags and extract entities that never obstruct vanilla placement."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import re

from measure_block_interactions import interaction_catalog


def fixed_dimensions(row: dict) -> list[float]:
    """Validate the audited root-entity geometry contract without guessing missing dimensions."""
    base = "net.minecraft.world.entity.Entity"
    owners = row.get("method_owners", {})
    geometry = row.get("geometry", {})
    if row.get("class") != "net.minecraft.world.entity.boss.enderdragon.EndCrystal" or row.get("constructor_blocks_building") is not True or row.get("blocks_building") is not True:
        raise ValueError("Fixed entity geometry needs the audited native building contract")
    if owners.get("getDimensions") != base or owners.get("getBoundingBox") != base or owners.get("makeBoundingBox", base) != base or geometry.get("selectors") != []:
        raise ValueError("Fixed entity geometry has a specialized owner or metadata selector")
    dimensions = row.get("dimensions", {})
    poses = geometry.get("default", {}).get("poses", [])
    if not dimensions or len(poses) != len(dimensions) or {pose[0] for pose in poses} != set(dimensions) or geometry.get("default", {}).get("blocks_building") is not True:
        raise ValueError("Fixed entity geometry has incomplete native pose coverage")
    size = list(next(iter(dimensions.values()))[:2])
    if len(size) != 2 or any(type(value) not in (int, float) or not math.isfinite(value) or value <= 0 for value in size):
        raise ValueError("Fixed entity dimensions must be finite and positive")
    width, height = size
    expected = [1.5 - width / 2, 64.0, 1.5 - width / 2, 1.5 + width / 2, 64.0 + height, 1.5 + width / 2]
    for pose in poses:
        if list(dimensions[pose[0]][:2]) != size or list(pose[1:3]) != size or pose[4] != expected:
            raise ValueError("Native pose dimensions or centered bounds differ from the fixed contract")
    return size


def validate_measurement(raw: dict, entities: set[str]) -> dict[str, int]:
    if set(raw) != entities:
        raise ValueError("Entity obstruction measurement must cover the exact entity registry")
    result = {}
    for name, row in raw.items():
        if "error" in row or type(row.get("constructor_blocks_building")) is not bool or type(row.get("blocks_building")) is not bool:
            raise ValueError(f"Missing successful native entity building flags: {name}")
        result[name] = int(not row["constructor_blocks_building"] and not row["blocks_building"])
    return dict(sorted(result.items()))


def validate_writes(rows: list[tuple[str, str]]) -> None:
    writers = {"LivingEntity", "vehicle/Boat", "vehicle/AbstractBoat", "vehicle/boat/AbstractBoat", "vehicle/AbstractMinecart", "vehicle/minecart/AbstractMinecart", "item/FallingBlockEntity", "item/PrimedTnt", "boss/enderdragon/EndCrystal", "monster/Strider", "decoration/ArmorStand"}
    paths = {"net/minecraft/world/entity/" + writer + ".java" for writer in writers}
    for path, statement in rows:
        owner = Path(path).stem
        expected = "this.blocksBuilding = !this.isMarker();" if owner == "ArmorStand" else "this.blocksBuilding = true;"
        if path not in paths or statement.strip() != expected:
            raise ValueError(f"Unclassified native entity building-flag write: {path}: {statement}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    args = parser.parse_args()
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)
    reports = {row["version"]: row for row in json.loads((args.measurements / "entity-placement-client-results.json").read_text())}
    tools = json.loads((args.measurements / "measurement-tools.json").read_text())["source_sha256"]
    helper_root = Path(__file__).parent / "shape-extractor"
    for name in ("PushDump.java", "MiningDump.java", "EntityPlacementProbe.java", "EntityPlacementGeometry.java", "EntityWorldSource.java"):
        if tools.get(name) != hashlib.sha256((helper_root / name).read_bytes()).hexdigest():
            raise ValueError(f"Native entity source changed since measurement: {name}")
    classes = {"net.minecraft.world.entity.Entity", "net.minecraft.world.entity.EntityType", "net.minecraft.world.entity.LivingEntity", "net.minecraft.world.entity.EntityDimensions", "net.minecraft.world.phys.AABB", "net.minecraft.network.syncher.SynchedEntityData"}
    grouped = {}
    fixed_grouped = {}
    provenance = {}
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        report = reports.get(version, {})
        if report.get("status") != "complete" or set(report.get("native_client_classes", {})) != classes or not report.get("preservation", {}).get("unchanged_class_files"):
            raise ValueError(f"Missing successful native client entity measurement: {version}")
        raw_bytes = (args.measurements / version / "entity-placement-client.json").read_bytes()
        if report.get("measurement_sha256") != hashlib.sha256(raw_bytes).hexdigest():
            raise ValueError(f"Native entity measurement changed: {version}")
        registry = json.loads((args.data / str(protocol) / "entities.json").read_text())
        measured = json.loads(raw_bytes)
        rules = validate_measurement(measured, {row["id"] for row in registry["entries"]})
        source_root = args.official / (version + "-client-decompiled")
        crystal = source_root / "net/minecraft/world/entity/boss/enderdragon/EndCrystal.java"
        crystal_source = crystal.read_text()
        if not re.search(r"class EndCrystal extends Entity\b", crystal_source) or any(re.search(r"\b" + name + r"\s*\(", crystal_source) for name in ("getDimensions", "getBoundingBox", "makeBoundingBox", "lerpTo", "getInterpolation", "createInterpolationHandler")):
            raise ValueError(f"Native fixed entity source contract changed: {version}")
        fixed = {"minecraft:end_crystal": fixed_dimensions(measured["minecraft:end_crystal"])}
        writes = []
        source_hashes = {}
        for path in source_root.rglob("*.java"):
            for line in path.read_text(errors="replace").splitlines():
                if re.search(r"\.blocksBuilding\s*=", line):
                    relative = path.relative_to(source_root).as_posix()
                    writes.append((relative, line.strip()))
                    source_hashes[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
        if not writes:
            raise ValueError(f"Missing native building-flag source evidence: {version}")
        validate_writes(writes)
        metadata = json.loads((args.official / "downloads" / version / "version.json").read_text())
        artifacts = {}
        for key, name in (("client", "client.jar"), ("server", "server.jar"), ("client_mappings", "client_mappings.txt")):
            if key not in metadata["downloads"]:
                continue
            contents = (args.official / "downloads" / version / name).read_bytes()
            if hashlib.sha1(contents).hexdigest() != metadata["downloads"][key]["sha1"]:
                raise ValueError(f"Official artifact hash differs: {version}: {name}")
            artifacts[name] = hashlib.sha256(contents).hexdigest()
        if report.get("client_sha256") != artifacts["client.jar"] or report.get("server_sha256") != artifacts["server.jar"]:
            raise ValueError(f"Native entity measurement uses different artifacts: {version}")
        if protocol in grouped and grouped[protocol] != rules:
            raise ValueError(f"Entity building rules differ between protocol aliases: {protocol}")
        if protocol in fixed_grouped and fixed_grouped[protocol] != fixed:
            raise ValueError(f"Fixed entity geometry differs between protocol aliases: {protocol}")
        grouped[protocol] = rules
        fixed_grouped[protocol] = fixed
        provenance.setdefault(protocol, []).append({"version": version, "measurement_sha256": report["measurement_sha256"], "artifacts_sha256": artifacts, "native_client_classes": report["native_client_classes"], "class_preservation": report["preservation"], "building_flag_source_sha256": source_hashes, "fixed_box_source_sha256": {"net/minecraft/world/entity/boss/enderdragon/EndCrystal.java": hashlib.sha256(crystal.read_bytes()).hexdigest()}})
    for protocol, rules in grouped.items():
        document = {"schema": 1, "entities": rules, "fixed_boxes": fixed_grouped[protocol], "_provenance": {"kind": "extracted", "tool": "tools/extraction/extract_entity_obstruction.py", "tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "tools_sha256": tools, "measurements": provenance[protocol], "note": "1 means the native constructor and post-spawn entity never block building under the audited vanilla flag writers. 0 includes obstructing entities and unknown geometry. fixed_boxes records measured centered dimensions that do not depend on pose or metadata; it does not describe other movement or world effects."}}
        (args.data / str(protocol) / "entity-obstruction.json").write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: {len(rules)} entities; {sum(rules.values())} never obstruct building")


if __name__ == "__main__":
    main()
