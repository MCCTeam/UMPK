"""Merge native farm age/pose measurements for the first four 1.14 releases."""

import argparse
import json
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes, read_legacy, role_profile
from extract_legacy_ageable_geometry import ROLES
from resolve_legacy_mining import methods, params

PROTOCOLS = {477, 480, 485, 490}
NATIVE_POSES = ["STANDING", "FALL_FLYING", "SLEEPING", "SWIMMING", "SPIN_ATTACK", "SNEAKING", "DYING"]


def pose_farm_profile(raw: dict, protocol: int) -> dict:
    if type(protocol) is not int or protocol not in PROTOCOLS or raw.get("entity") not in ROLES or type(raw.get("metadata_index")) is not int or raw["metadata_index"] != 14 or raw.get("poses") != NATIVE_POSES:
        raise ValueError("Pose farm entity, selector or pose coverage is outside the audited cohort")
    if len(raw["positions"]) != 3 or len({tuple(point) for point in raw["positions"]}) != 3:
        raise ValueError("Pose farm measurements require three distinct positions")
    return role_profile(raw)


def group_pose_farms(records: list[dict], expected_versions: dict[str, int]) -> dict[int, dict[str, list[dict]]]:
    if set(expected_versions.values()) != PROTOCOLS:
        raise ValueError("Pose farm releases do not cover the audited protocol cohort")
    groups, seen = {}, set()
    for record in records:
        version, entity, protocol = (record[key] for key in ("version", "entity", "protocol"))
        if type(protocol) is not int or expected_versions.get(version) != protocol or entity not in ROLES or (version, entity) in seen or record["raw"].get("entity") != entity:
            raise ValueError("Pose farm release or entity identity differs")
        seen.add((version, entity))
        rows = groups.setdefault(protocol, {}).setdefault(entity, [])
        profile = pose_farm_profile(record["raw"], protocol)
        if rows and profile != pose_farm_profile(rows[0]["raw"], protocol):
            raise ValueError("Pose farm profiles differ between protocol aliases")
        rows.append(record)
    if seen != {(version, entity) for version in expected_versions for entity in ROLES}:
        raise ValueError("Pose farm measurements must cover every release and type")
    return groups


def verify_pose_farm_identity(raw: dict, receipt: dict, binding: dict, sources: dict[str, str], helper_source_sha256: str, helper_class_sha256: str, numeric: int) -> None:
    entity, actor = receipt.get("entity"), binding.get("actor")
    if entity not in ROLES or binding.get("entity") != entity or not isinstance(actor, str) or not actor or raw.get("entity") != entity or raw.get("entity_class") != actor or receipt.get("registry_class") != actor or receipt.get("binding") != binding or raw.get("entity_type_matches_factory") is not True or receipt.get("registry_lookup_audited") is not True:
        raise ValueError("Pose farm identity differs from its original actor or factory")
    if receipt.get("helper_sha256") != helper_source_sha256 or receipt.get("helper_class_sha256") != helper_class_sha256:
        raise ValueError("Pose farm helper source or class bytes changed")
    expected_owners = {key: binding[owner] for key, owner in (("dimensions", "living"), ("scale", "living"), ("baby", "ageable"), ("position", "root"), ("refresh", "root"), ("box", "root"))}
    if raw.get("method_owners") != expected_owners:
        raise ValueError("Pose farm geometry methods have unaudited owners")
    sources = {name: re.sub(r"/\*.*?\*/", "", text, flags=re.S) for name, text in sources.items()}
    hierarchy = [actor]
    while hierarchy[-1] != binding["root"]:
        parent = re.search(r"public (?:abstract )?class " + re.escape(hierarchy[-1]) + r" extends (\w+)", sources.get(hierarchy[-1] + ".java", ""))
        if parent is None or parent[1] in hierarchy or len(hierarchy) >= 32:
            raise ValueError("Pose farm actor does not inherit the original entity base")
        hierarchy.append(parent[1])
    if binding["ageable"] not in hierarchy or binding["living"] not in hierarchy or raw.get("actor_hierarchy") != hierarchy:
        raise ValueError("Pose farm native hierarchy differs from its original source")
    required = {binding[key] for key in ("root", "living", "world", "client_world", "ageable", "accessor", "data", "dimensions_class", "pose", "entity_registry", "registry_owner")} | set(hierarchy)
    registry, origins = raw.get("registry", {}), raw.get("origins", {})
    registry_origins = registry.get("origins", {})
    if not required <= set(origins) or not {binding["registry_owner"], registry.get("id_owner"), registry.get("key_owner")} <= set(registry_origins) or not set(registry_origins) <= set(origins) or not origins or len(set(origins.values())) != 1 or set(origins.values()) != set(registry_origins.values()) or not all(isinstance(value, str) and value for value in origins.values()):
        raise ValueError("Pose farm origins do not cover the original hierarchy and registry")
    if not {name + ".java" for name in set(origins) | required} <= set(sources):
        raise ValueError("Pose farm source coverage is incomplete")
    if type(numeric) is not int or type(registry.get("id")) is not int or registry["id"] != numeric or type(receipt.get("registry_numeric_id")) is not int or receipt["registry_numeric_id"] != numeric or registry.get("name") != entity or registry.get("kind") != "type-registry" or registry.get("key_method") != binding["registry_key_method"]:
        raise ValueError("Pose farm native registry differs from its canonical identity")
    registry_source = sources[binding["entity_registry"] + ".java"]
    factory = r"public static final " + re.escape(binding["entity_registry"]) + "<" + re.escape(actor) + "> " + re.escape(binding["actor_factory"]) + r'\s*=\s*\w+\("' + re.escape(entity.split(":")[1]) + r'",[^;]*?\b' + re.escape(actor) + r"::new\b"
    if len(re.findall(factory, registry_source)) != 1:
        raise ValueError("Pose farm actor does not match its original typed factory")
    key_bodies = [body for match, body in methods(registry_source, r"public\s+static\s+[\w<?>.]+\s+(\w+)\(([^)]*)\)\s*\{") if params(match[2]) == [binding["entity_registry"] + "<?>"]]
    receiver = re.escape(binding["registry_owner"] + "." + binding["registry_field"] + "." + binding["registry_key_method"])
    if len(key_bodies) != 1 or re.fullmatch(r"\s*return " + receiver + r"\(\w+\);\s*", key_bodies[0]) is None:
        raise ValueError("Pose farm registry receiver differs from the original key getter")
    root_source = sources[binding["root"] + ".java"]
    getters = [body for match, body in methods(root_source, r"public\s+" + re.escape(binding["entity_registry"]) + r"<\?>\s+(" + re.escape(binding["entity_type_getter"]) + r")\(([^)]*)\)\s*\{") if not params(match[2])]
    if len(getters) != 1 or re.fullmatch(r"\s*return this\.\w+;\s*", getters[0]) is None:
        raise ValueError("Pose farm actual entity-type getter differs from its source")
    constants = raw.get("pose_constants", [])
    if len(constants) != 7 or len(set(constants)) != 7 or constants[0] != binding["default_pose"] or constants[2] != binding["sleeping_pose"] or re.search(r"static final " + re.escape(binding["accessor"]) + "<" + re.escape(binding["pose"]) + "> " + re.escape(binding["pose_accessor"]) + r"\s*=", root_source) is None:
        raise ValueError("Pose farm pose accessor or constants differ from the native binding")
    living = sources[binding["living"] + ".java"]
    if re.search(r"public float " + re.escape(binding["scale_method"]) + r"\(\)\s*\{\s*return this\." + re.escape(binding["is_baby_method"]) + r"\(\)\s*\?\s*0\.5F\s*:\s*1\.0F\s*;", living) is None:
        raise ValueError("Pose farm dimensions need a separately audited scale model")
    ageable = sources[binding["ageable"] + ".java"]
    accessor, field = re.escape(binding["accessor"]), re.escape(binding["accessor_field"])
    if re.search(r"static final " + accessor + r"<Boolean> " + field + r"\s*=", ageable) is None:
        raise ValueError("Pose farm age accessor is not the original Boolean field")
    predicates = [body for match, body in methods(ageable, r"public\s+boolean\s+(" + re.escape(binding["is_baby_method"]) + r")\(([^)]*)\)\s*\{") if not params(match[2])]
    age_call = re.fullmatch(r"\s*return this\.(\w+)\(\)\s*<\s*0;\s*", predicates[0]) if len(predicates) == 1 else None
    age_bodies = [] if age_call is None else [body for match, body in methods(ageable, r"public\s+int\s+(" + re.escape(age_call[1]) + r")\(([^)]*)\)\s*\{") if not params(match[2])]
    if len(age_bodies) != 1 or re.search(r"return this\." + re.escape(binding["data_field"]) + r"\.\w+\(" + field + r"\)\s*\?\s*-1\s*:\s*1\s*;", age_bodies[0]) is None:
        raise ValueError("Pose farm child predicate differs from its native Boolean metadata")
    callbacks = [(match, body) for match, body in methods(ageable, r"public\s+void\s+(\w+)\(([^)]*)\)\s*\{") if params(match[2]) == [binding["accessor"] + "<?>"]]
    if len(callbacks) != 1 or re.search(field + r"\.equals\(\w+\)", callbacks[0][1]) is None or re.search(r"this\." + re.escape(binding["refresh"]) + r"\(\);", callbacks[0][1]) is None:
        raise ValueError("Pose farm age callback does not invoke the native dimensions refresh")
    callback = callbacks[0][0][1]
    for actor in hierarchy[:hierarchy.index(binding["ageable"])]:
        overrides = [(match, body) for match, body in methods(sources[actor + ".java"], r"public\s+void\s+(" + re.escape(callback) + r")\(([^)]*)\)\s*\{") if params(match[2]) == [binding["accessor"] + "<?>"]]
        if len(overrides) > 1:
            raise ValueError("Pose farm metadata callback is ambiguous")
        if overrides:
            declaration, body = overrides[0]
            call = "super." + callback + "(" + declaration[2].split()[-1] + ");"
            compact = re.sub(r"\s+", "", body)
            prefix = compact.split(call)[0]
            if compact.count(call) != 1 or prefix.count("{") != prefix.count("}") or "return" in prefix or "throw" in prefix:
                raise ValueError("Pose farm callback does not delegate unconditionally to the ageable base")


def pose_farm_fixture(records: list[dict], measurement: str) -> dict:
    cases, releases = [], []
    for record in sorted(records, key=lambda row: (row["protocol"], row["entity"])):
        raw, receipt = record["raw"], record["receipt"]
        case = {key: value for key, value in raw.items() if key not in ("origins", "registry", "entity_class", "method_owners", "pose_constants", "entity_type_matches_factory", "actor_hierarchy")}
        if case not in cases:
            cases.append(case)
        releases.append({key: receipt[key] for key in ("version", "entity", "protocol", "registry_numeric_id", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": cases.index(case)})
    first = records[0]["receipt"]
    return {"schema": 1, "measurement": measurement, "scope": first["scope"], "helper_source_sha256": first["helper_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": releases, "cases": cases}


def extract(measurements: Path, official: Path, data: Path, fixture: Path | None, *, check: bool) -> None:
    catalog = json.loads((data / "versions.json").read_text(encoding="utf-8"))["versions"]
    expected = {row["name"]: row["protocol"] for row in catalog if row["protocol"] in PROTOCOLS}
    records = []
    for receipt in json.loads((measurements / "results.json").read_text(encoding="utf-8")):
        if receipt.get("format") != "pose-farm-role-v1" or receipt.get("entity") not in ROLES or expected.get(receipt.get("version")) != receipt.get("protocol"):
            raise ValueError("Pose farm summary contains an unaudited release or type")
        record = read_legacy(measurements / receipt["version"] / receipt["entity"].split(":")[1], official, catalog)
        if record["receipt"] != receipt:
            raise ValueError("Pose farm summary differs from its individual receipt")
        records.append(record)
    groups = group_pose_farms(records, expected)
    outputs = []
    for protocol, entities in sorted(groups.items()):
        version = next(row for row in reversed(catalog) if row["protocol"] == protocol)
        path = data / version["dataset"] / "entity-geometry.json"
        document = json.loads(path.read_text(encoding="utf-8"))
        for entity, rows in sorted(entities.items()):
            primary = next(row for row in rows if row["version"] == version["name"])
            document["entities"][entity] = pose_farm_profile(primary["raw"], protocol)
        fields = ("version", "entity", "protocol", "registry_numeric_id", "registry_class", "measurement_sha256", "binding_sha256", "client_sha256", "server_sha256", "sources_sha256", "preservation")
        cohort = sorted((row for rows in entities.values() for row in rows), key=lambda row: row["entity"])
        first = cohort[0]["receipt"]
        document["_provenance"]["pose_farm_roles"] = {"measurement": measurements.name, "scope": first["scope"], "helper_source_sha256": first["helper_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": [{key: row["receipt"][key] for key in fields} for row in cohort]}
        outputs.append((path, (json.dumps(document, indent=2) + "\n").encode()))
    if fixture is not None:
        outputs.append((fixture, fixture_bytes(pose_farm_fixture(records, measurements.name), compressed=fixture.suffix == ".gz")))
    for path, content in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != content:
                raise ValueError(f"Pose farm geometry output differs: {path}")
        else:
            path.write_bytes(content)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    extract(args.measurements, args.official, args.data, args.fixture, check=args.check)


if __name__ == "__main__":
    main()
