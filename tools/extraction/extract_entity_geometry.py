"""Extract audited villager geometry from native metadata and pose transition witnesses."""

import argparse
import hashlib
import json
import re
from pathlib import Path
import zipfile

from verify_entity_box_transitions import centered, finite, refresh, size

POSES = {name: index for index, name in enumerate(("STANDING", "FALL_FLYING", "SLEEPING", "SWIMMING", "SPIN_ATTACK", "CROUCHING", "LONG_JUMPING", "DYING", "CROAKING", "USING_TONGUE", "SITTING", "ROARING", "SNIFFING", "EMERGING", "DIGGING", "SLIDING", "SHOOTING", "INHALING"))}
POSES["SNEAKING"] = POSES["CROUCHING"]


def villager_profile(raw: dict, *, scale_attribute: str) -> dict:
    poses, positions = raw["poses"], raw["positions"]
    if not poses or len(set(poses)) != len(poses) or any(pose not in POSES for pose in poses) or "STANDING" not in poses or not positions or type(raw["metadata_index"]) is not int or not 0 <= raw["metadata_index"] < 255:
        raise ValueError("Native villager pose, position or metadata coverage is invalid")
    for position in positions:
        finite(position, 3)
    expected = {(a, b, c, d, p) for a in range(2) for b in range(2) for c in range(len(poses)) for d in range(len(poses)) for p in range(len(positions))}
    seen, dimensions, models = set(), {}, {True: True, False: True}
    for row in raw["rows"]:
        key = tuple(row[name] for name in ("from_age", "to_age", "from_pose", "to_pose", "position"))
        if any(type(index) is not int for index in key) or key not in expected or key in seen:
            raise ValueError("Duplicate or invalid native villager transition")
        seen.add(key)
        a, b, c, d, p = key
        old, aged, target = (size(row[name]) for name in ("from_size", "age_size", "to_size"))
        for state, measured in (((a, c), old), ((b, c), aged), ((b, d), target)):
            if dimensions.setdefault(state, measured) != measured:
                raise ValueError("Inconsistent native villager dimensions")
        before = finite(row["before"], 6)
        for name in ("after_age_sync", "after_pose_sync", "after_refresh", "after_position"):
            finite(row[name], 6)
        if before != centered(positions[p], old) or row["after_position"] != centered(positions[p], target):
            raise ValueError("Native position reapplication differs from measured dimensions")
        for retained in models:
            age = refresh(before, old[0], aged, positions[p], retained) if a != b else before
            pose = refresh(age, aged[0], target, positions[p], retained) if c != d else age
            explicit = refresh(pose, target[0], target, positions[p], retained)
            models[retained] &= row["after_age_sync"] == age and row["after_pose_sync"] == pose and row["after_refresh"] == explicit
    if seen != expected or sum(models.values()) != 1:
        raise ValueError("Native transitions do not establish one complete geometry profile")
    variants = []
    for (age, pose), measured in sorted(dimensions.items(), key=lambda pair: (pair[0][0], POSES[poses[pair[0][1]]])):
        original = next(row["to_size"] for row in raw["rows"] if row["to_age"] == age and row["to_pose"] == pose)
        variants.append([age, POSES[poses[pose]], *original, True])
    return {"selector": [raw["metadata_index"], "boolean", 1, 0], "pose_index": 6, "refresh": "retain_minimum_on_growth" if models[True] else "recenter_on_refresh", "scale_attribute": scale_attribute, "variants": variants}


def legacy_villager_profile(raw: dict) -> dict:
    if "poses" in raw:
        if type(raw.get("pose_index")) is not int or raw["pose_index"] != 6 or raw.get("metadata_default") is not False or raw.get("blocks_building") is not True:
            raise ValueError("Unmapped villager pose defaults or building flags are incomplete")
        return villager_profile(raw, scale_attribute="")
    positions = raw["positions"]
    if not positions or raw["blocks_building"] is not True or type(raw["metadata_index"]) is not int or raw["metadata_index"] not in (11, 12):
        raise ValueError("Legacy villager contract is incomplete")
    for position in positions:
        finite(position, 3)
    expected = {(a, b, p) for a in range(2) for b in range(2) for p in range(len(positions))}
    seen, dimensions = set(), {}
    models = {name: True for name in ("retain_minimum", "retain_minimum_on_growth", "recenter_on_refresh")}
    for row in raw["rows"]:
        key = (row["from"], row["to"], row["position"])
        if any(type(index) is not int for index in key) or key not in expected or key in seen:
            raise ValueError("Incomplete legacy villager transitions")
        seen.add(key)
        a, b, p = key
        old, target = size([*row["before_size"], False]), size([*row["after_size"], False])
        for state, measured in ((a, old), (b, target)):
            if dimensions.setdefault(state, measured) != measured:
                raise ValueError("Inconsistent legacy villager dimensions")
        before = centered(positions[p], old)
        for name in ("before", "after_sync", "after_position"):
            finite(row[name], 6)
        if row["before"] != before or row["after_position"] != centered(positions[p], target):
            raise ValueError("Legacy villager position bounds differ")
        for model in models:
            if a == b:
                synchronized = before
            elif model == "retain_minimum":
                synchronized = [*before[:3], before[0] + target[0], before[1] + target[1], before[2] + target[0]]
            else:
                synchronized = refresh(before, old[0], target, positions[p], model == "retain_minimum_on_growth")
            models[model] &= row["after_sync"] == synchronized
    if seen != expected or sum(models.values()) != 1:
        raise ValueError("Native transitions do not establish one complete legacy geometry profile")
    variants = [[age, 0, *next(row["after_size"] for row in raw["rows"] if row["to"] == age), False, True] for age in range(2)]
    return {"selector": [raw["metadata_index"], "boolean", 1, 0], "pose_index": -1, "refresh": next(model for model, matches in models.items() if matches), "scale_attribute": "", "variants": variants}


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def group_legacy(records: list[dict]) -> dict[int, list[dict]]:
    groups, versions = {}, set()
    for record in records:
        protocol, version = record["protocol"], record["version"]
        if type(protocol) is not int or protocol <= 0 or not isinstance(version, str) or not version or version in versions:
            raise ValueError("Duplicate or invalid legacy geometry release")
        versions.add(version)
        group = groups.setdefault(protocol, [])
        profile = legacy_villager_profile(record["raw"])
        if group and profile != legacy_villager_profile(group[0]["raw"]):
            raise ValueError(f"Legacy geometry differs between aliases of protocol {protocol}")
        group.append(record)
    return groups


def legacy_fixture(records: list[dict]) -> dict:
    group_legacy(records)
    cases, releases, seen = [], [], {}
    for record in sorted(records, key=lambda row: (row["protocol"], row["version"])):
        raw = record["raw"]
        case = {key: raw[key] for key in ("metadata_index", "blocks_building", "positions")}
        fields = ("from", "to", "position", "before_size", "before", "after_size", "after_sync", "after_position")
        if "poses" in raw:
            case.update({key: raw[key] for key in ("poses", "pose_index", "metadata_default", "pose_constants")})
            fields = ("from_age", "to_age", "from_pose", "to_pose", "position", "from_size", "before", "age_size", "after_age_sync", "to_size", "after_pose_sync", "after_refresh", "after_position")
        case["rows"] = [{key: row[key] for key in fields} for row in raw["rows"]]
        key = json.dumps(case, sort_keys=True)
        if key not in seen:
            seen[key] = len(cases)
            cases.append(case)
        receipt = record["receipt"]
        releases.append({"version": record["version"], "protocol": record["protocol"], "case": seen[key], "measurement_sha256": receipt["measurement_sha256"], "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"]})
    return {"releases": releases, "cases": cases}


def read_legacy(root: Path, official: Path, catalog: list[dict]) -> dict:
    receipt = json.loads((root / "result.json").read_text(encoding="utf-8"))
    version, protocol = receipt.get("version", "1.10.2"), receipt.get("protocol", 210)
    if not any(row["name"] == version and row["protocol"] == protocol for row in catalog):
        raise ValueError("Legacy geometry release does not match the version catalog")
    path = root / "geometry.json"
    raw = json.loads(path.read_text(encoding="utf-8"))
    legacy_villager_profile(raw)
    if receipt["status"] != "complete" or sha(path) != receipt["measurement_sha256"] or receipt.get("witnesses", len(raw["rows"])) != len(raw["rows"]):
        raise ValueError("Legacy measurement receipt mismatch")
    seed = official / "downloads" / version
    metadata = json.loads((seed / "version.json").read_text(encoding="utf-8"))
    for side in ("client", "server"):
        artifact = seed / (side + ".jar")
        if sha(artifact) != receipt[side + "_sha256"] or hashlib.sha1(artifact.read_bytes()).hexdigest() != metadata["downloads"][side]["sha1"]:
            raise ValueError(f"Official {side} artifact mismatch: {version}")
    source_hashes = receipt.get("sources_sha256")
    if source_hashes is None:
        tree = official / f"{version}-client-decompiled"
        source_hashes = {str(Path(name).relative_to(tree)): value for name, value in receipt.get("source_sha256", {}).items() if Path(name).is_relative_to(tree)}
    for name, expected in source_hashes.items():
        if sha(official / f"{version}-client-decompiled" / name) != expected:
            raise ValueError(f"Audited geometry source changed: {version}/{name}")
    if "poses" in raw:
        binding = receipt["binding"]
        source = official / f"{version}-client-decompiled" / (binding["living"] + ".java")
        text = re.sub(r"/\*.*?\*/", "", source.read_text(encoding="utf-8"), flags=re.S)
        scale = re.escape(binding["scale_method"])
        if raw.get("entity_class") != binding["villager"] or not re.search(r"public float " + scale + r"\(\)\s*\{\s*return this\.\w+\(\)\s*\?\s*0\.5F\s*:\s*1\.0F\s*;", text):
            raise ValueError("Unmapped villager dimensions need a separate attribute-scale model")
    origins = raw.get("origins", {})
    if not origins or len(set(origins.values())) != 1:
        raise ValueError("Native geometry classes have inconsistent origins")
    preserved = Path(next(iter(origins.values())))
    if sha(preserved) != receipt["preservation"]["unsigned_jar_sha256"]:
        raise ValueError("Preserved geometry client changed")
    with zipfile.ZipFile(seed / "client.jar") as original, zipfile.ZipFile(preserved) as measured:
        classes = {name for name in original.namelist() if name.endswith(".class")}
        if classes != {name for name in measured.namelist() if name.endswith(".class")} or len(classes) != receipt["preservation"]["unchanged_class_files"] or any(original.read(name) != measured.read(name) for name in classes):
            raise ValueError("Original geometry client class preservation failed")
    return {"version": version, "protocol": protocol, "raw": raw, "receipt": receipt}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--transitions", type=Path, required=True)
    parser.add_argument("--legacy", type=Path, action="append", required=True)
    parser.add_argument("--legacy-fixture", type=Path)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    catalog = json.loads((args.data / "versions.json").read_text(encoding="utf-8"))["versions"]
    geometry = {row["version"]: row for row in json.loads((args.measurements / "entity-placement-client-results.json").read_text(encoding="utf-8"))}
    transitions = {row["version"]: row for row in json.loads((args.transitions / "villager-box-transition-results.json").read_text(encoding="utf-8"))}
    legacy_records = [read_legacy(root, args.official, catalog) for root in args.legacy]
    legacy = group_legacy(legacy_records)
    selected = {}
    for version in catalog:
        if version["name"] in geometry or version["protocol"] in legacy:
            selected[version["protocol"]] = version
    for protocol, version in sorted(selected.items()):
        name = version["name"]
        if protocol in legacy:
            record = next((row for row in legacy[protocol] if row["version"] == name), None)
            if record is None:
                raise ValueError(f"Native geometry is missing the canonical alias: {name}")
            receipt, raw = record["receipt"], record["raw"]
            profile = legacy_villager_profile(raw)
            proof = {"measurement_sha256": receipt["measurement_sha256"], "native_witnesses": len(raw["rows"])}
            if len(legacy[protocol]) > 1:
                proof["aliases"] = [{"version": row["version"], "measurement_sha256": row["receipt"]["measurement_sha256"], "client_sha256": row["receipt"]["client_sha256"], "server_sha256": row["receipt"]["server_sha256"]} for row in sorted(legacy[protocol], key=lambda row: row["version"])]
        else:
            receipt, transition_receipt = geometry[name], transitions[name]
            path = args.measurements / name / "entity-placement-client.json"
            phase_path = args.transitions / name / "villager-box-transitions.json"
            if receipt["status"] != "complete" or transition_receipt["status"] != "complete" or sha(path) != receipt["measurement_sha256"] or sha(phase_path) != transition_receipt["measurement_sha256"]:
                raise ValueError(f"Native geometry receipt mismatch: {name}")
            native = json.loads(path.read_text(encoding="utf-8"))["minecraft:villager"]
            phases = json.loads(phase_path.read_text(encoding="utf-8"))
            if native["constructor_blocks_building"] is not True or native["blocks_building"] is not True or not native["class"].endswith(".Villager") or native["method_owners"]["getBoundingBox"] != "net.minecraft.world.entity.Entity":
                raise ValueError(f"Unaudited villager building or box owner: {name}")
            source = args.official / f"{name}-client-decompiled/net/minecraft/world/entity/LivingEntity.java"
            scale = "minecraft:scale" if "getValue(Attributes.SCALE)" in source.read_text(encoding="utf-8") else ""
            profile = villager_profile(phases, scale_attribute=scale)
            proof = {"measurement_sha256": sha(path), "transition_sha256": sha(phase_path), "native_witnesses": len(phases["rows"]), "living_source_sha256": sha(source)}
        for side in ("client", "server"):
            if sha(args.official / "downloads" / name / f"{side}.jar") != receipt[f"{side}_sha256"]:
                raise ValueError(f"Official {side} artifact mismatch: {name}")
        document = {"_provenance": {"kind": "extracted", "version": name, "protocol": protocol, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], **proof, "scope": "Original vanilla villager dimensions, age/pose callbacks and bounds refresh; controlled world inputs, no entity/world tick or attribute-scale sweep"}, "schema": 1, "entities": {"minecraft:villager": profile}}
        output = json.dumps(document, indent=2) + "\n"
        destination = args.data / version["dataset"] / "entity-geometry.json"
        if args.check:
            if not destination.is_file() or destination.read_text(encoding="utf-8") != output:
                raise ValueError(f"Canonical geometry differs: {destination}")
        else:
            destination.write_text(output, encoding="utf-8")
    if args.legacy_fixture is not None:
        output = json.dumps(legacy_fixture(legacy_records), indent=2) + "\n"
        if args.check:
            if not args.legacy_fixture.is_file() or args.legacy_fixture.read_text(encoding="utf-8") != output:
                raise ValueError("Native legacy geometry fixture differs")
        else:
            args.legacy_fixture.write_text(output, encoding="utf-8")
    print(f"{'Checked' if args.check else 'Extracted'} {len(selected)} native villager geometry datasets")


if __name__ == "__main__":
    main()
