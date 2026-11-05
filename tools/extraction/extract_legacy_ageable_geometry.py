"""Merge original-client Boolean-age farm measurements into legacy geometry data."""

import argparse
import json
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes, legacy_villager_profile, read_legacy, sha
from resolve_legacy_mining import methods, params

ROLES = {"minecraft:cow", "minecraft:pig", "minecraft:sheep", "minecraft:chicken"}
METADATA = {107: 11, 108: 11, 109: 11, 110: 11, 210: 12, 315: 12, 316: 12, 335: 12, 338: 12, 340: 12, 393: 12, 401: 12, 404: 12}


def ageable_profile(raw: dict, protocol: int) -> dict:
    if type(protocol) is not int or protocol not in METADATA or raw.get("entity") not in ROLES or type(raw.get("metadata_index")) is not int or raw["metadata_index"] != METADATA[protocol] or "poses" in raw or "predicates" in raw:
        raise ValueError("Legacy farm selector or entity is outside the audited cohort")
    if len(raw["positions"]) != 3 or len({tuple(point) for point in raw["positions"]}) != 3:
        raise ValueError("Legacy farm measurements require three distinct positions")
    for row in raw["rows"]:
        if any(type(row.get(field)) is not bool or row[field] != bool(row[selector]) for field, selector in (("before_child", "from"), ("after_child", "to"))):
            raise ValueError("Legacy farm predicate differs from its Boolean metadata")
    return legacy_villager_profile(raw)


def group_ageable_roles(records: list[dict], expected_versions: dict[str, int]) -> dict[int, dict[str, list[dict]]]:
    if set(expected_versions.values()) != set(METADATA):
        raise ValueError("Legacy farm aliases do not cover the audited protocol cohort")
    groups, seen = {}, set()
    for record in records:
        version, entity, protocol = (record[key] for key in ("version", "entity", "protocol"))
        if type(protocol) is not int or expected_versions.get(version) != protocol or entity not in ROLES or (version, entity) in seen or record["raw"].get("entity") != entity:
            raise ValueError("Legacy farm release or entity identity differs")
        seen.add((version, entity))
        rows = groups.setdefault(protocol, {}).setdefault(entity, [])
        profile = ageable_profile(record["raw"], protocol)
        if rows and profile != ageable_profile(rows[0]["raw"], protocol):
            raise ValueError("Legacy farm profiles differ between protocol aliases")
        rows.append(record)
    if seen != {(version, entity) for version in expected_versions for entity in ROLES}:
        raise ValueError("Legacy farm measurements must cover every release and type")
    return groups


def verify_ageable_identity(raw: dict, receipt: dict, binding: dict, sources: dict[str, str], helper_source_sha256: str, helper_class_sha256: str, numeric: int) -> None:
    entity = receipt.get("entity")
    actor, ageable = binding.get("villager"), binding.get("ageable")
    if entity not in ROLES or not isinstance(actor, str) or not actor or raw.get("entity") != entity or raw.get("entity_class") != actor or receipt.get("registry_class") != actor or receipt.get("binding") != binding:
        raise ValueError("Legacy farm identity differs from its original actor")
    if raw.get("child_predicate_owner") != ageable:
        raise ValueError("Legacy farm child predicate has an unaudited owner")
    if receipt.get("helper_source_sha256") != helper_source_sha256 or receipt.get("helper_class_sha256") != helper_class_sha256:
        raise ValueError("Legacy farm helper source or class bytes changed")
    sources = {name: re.sub(r"/\*.*?\*/", "", text, flags=re.S) for name, text in sources.items()}
    hierarchy = [actor]
    while hierarchy[-1] != ageable:
        parent = re.search(r"public (?:abstract )?class " + re.escape(hierarchy[-1]) + r" extends (\w+)", sources.get(hierarchy[-1] + ".java", ""))
        if parent is None or parent[1] in hierarchy or len(hierarchy) >= 32:
            raise ValueError("Legacy farm actor does not inherit the original ageable base")
        hierarchy.append(parent[1])
    if binding.get("hierarchy") != hierarchy:
        raise ValueError("Legacy farm hierarchy differs from its source")
    registry = raw.get("registry", {})
    required = {binding[key] for key in ("root", "living", "world", "ageable", "accessor", "data", "entity_registry", "registry_owner")} | set(hierarchy)
    origins, registry_origins = raw.get("origins", {}), registry.get("origins", {})
    if not required - {binding["registry_owner"]} <= set(origins) or not {binding["registry_owner"], registry.get("id_owner"), registry.get("key_owner")} <= set(registry_origins) or not origins or set(origins.values()) != set(registry_origins.values()) or len(set(origins.values())) != 1 or not all(isinstance(value, str) and value for value in origins.values()):
        raise ValueError("Legacy farm class origins do not cover its native hierarchy and registry")
    required |= set(origins) | set(registry_origins)
    if not {name + ".java" for name in required} <= set(sources):
        raise ValueError("Legacy farm source coverage is incomplete")
    native_name = registry.get("name", "").lower()
    if ":" not in native_name:
        native_name = "minecraft:" + native_name
    if type(numeric) is not int or type(registry.get("id")) is not int or registry["id"] != numeric or type(receipt.get("registry_numeric_id")) is not int or receipt["registry_numeric_id"] != numeric or native_name != entity or registry.get("kind") != binding["registry_kind"] or registry.get("key_method") != binding["registry_key_method"]:
        raise ValueError("Legacy farm native registry differs from the canonical identity")
    name = re.escape(entity.split(":")[1])
    actor_pattern = re.escape(actor)
    patterns = {
        "entity-list": r'\ba\(\s*' + actor_pattern + r'\.class,\s*"' + name + r'",\s*' + str(numeric) + r"\b",
        "class-registry": r'\ba\(\s*' + str(numeric) + r',\s*"' + name + r'",\s*' + actor_pattern + r"\.class\b",
        "type-registry": r"public static final " + re.escape(binding["entity_registry"]) + "<" + actor_pattern + "> " + re.escape(binding.get("registry_value_field", "")) + r' = \w+\("' + name + r'",[^;]*?\b' + actor_pattern + r"\.class\b",
    }
    kind = binding["registry_kind"]
    if kind not in patterns or re.search(patterns[kind], sources[binding["entity_registry"] + ".java"], re.I) is None:
        raise ValueError("Legacy farm class does not match its original registration")
    if kind == "entity-list" and (registry.get("id_method") != binding["registry_id_method"] or registry.get("id_owner") != binding["entity_registry"] or registry.get("key_owner") != binding["entity_registry"]):
        raise ValueError("Legacy farm list getters have unaudited owners")
    text = sources[ageable + ".java"]
    accessor = re.escape(binding["accessor"])
    field = re.escape(binding["accessor_field"])
    if re.search(r"static final " + accessor + r"<Boolean> " + field + r"\s*=", text) is None:
        raise ValueError("Legacy farm accessor is not the original Boolean field")
    callback = re.escape(binding["synced_callback"])
    callbacks = [(m, body) for m, body in methods(text, r"public\s+void\s+(" + callback + r")\(([^)]*)\)\s*\{") if params(m[2]) == [binding["accessor"] + "<?>"]]
    if len(callbacks) != 1 or re.search(field + r"\.equals\(\w+\)", callbacks[0][1]) is None or re.search(r"this\.\w+\(this\." + re.escape(binding["child_predicate"]) + r"\(\)\)", callbacks[0][1]) is None:
        raise ValueError("Legacy farm metadata callback does not resize from the child predicate")
    callback_owner = None
    for actor in hierarchy[:-1]:
        overrides = [(m, body) for m, body in methods(sources[actor + ".java"], r"public\s+void\s+(" + callback + r")\(([^)]*)\)\s*\{") if params(m[2]) == [binding["accessor"] + "<?>"]]
        if len(overrides) > 1:
            raise ValueError("Legacy farm metadata callback is ambiguous")
        if overrides:
            if callback_owner is None:
                callback_owner = actor
            declaration, body = overrides[0]
            call = "super." + binding["synced_callback"] + "(" + declaration[2].split()[-1] + ");"
            compact = re.sub(r"\s+", "", body)
            prefix = compact.split(call)[0]
            if compact.count(call) != 1 or prefix.count("{") != prefix.count("}") or "return" in prefix or "throw" in prefix:
                raise ValueError("Legacy farm callback does not delegate unconditionally to its ageable base")
    if raw.get("metadata_callback_owner") != (callback_owner or ageable):
        raise ValueError("Legacy farm callback owner differs from the original hierarchy")


def ageable_fixture(records: list[dict], measurement: str) -> dict:
    cases, releases = [], []
    for record in sorted(records, key=lambda row: (row["protocol"], row["version"], row["entity"])):
        raw, receipt = record["raw"], record["receipt"]
        case = {key: value for key, value in raw.items() if key not in ("origins", "registry", "entity_class", "child_predicate_owner", "metadata_callback_owner")}
        if case not in cases:
            cases.append(case)
        releases.append({key: receipt[key] for key in ("version", "entity", "protocol", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": cases.index(case)})
    first = records[0]["receipt"]
    return {"schema": 1, "measurement": measurement, "scope": first["scope"], "helper_source_sha256": first["helper_source_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": releases, "cases": cases}


def extract(measurements: Path, official: Path, data: Path, fixture: Path | None, *, check: bool) -> None:
    catalog = json.loads((data / "versions.json").read_text(encoding="utf-8"))["versions"]
    expected = {row["name"]: row["protocol"] for row in catalog if row["protocol"] in METADATA}
    records = []
    for receipt in json.loads((measurements / "results.json").read_text(encoding="utf-8")):
        if receipt.get("format") != "legacy-ageable-role-v1" or receipt.get("entity") not in ROLES or expected.get(receipt.get("version")) != receipt.get("protocol"):
            raise ValueError("Legacy farm summary contains an unaudited release or type")
        record = read_legacy(measurements / receipt["version"] / receipt["entity"].split(":")[1], official, catalog)
        if record["receipt"] != receipt:
            raise ValueError("Legacy farm summary differs from its individual receipt")
        records.append(record)
    groups = group_ageable_roles(records, expected)
    outputs = []
    for protocol, entities in sorted(groups.items()):
        version = next(row for row in reversed(catalog) if row["protocol"] == protocol)
        path = data / version["dataset"] / "entity-geometry.json"
        document = json.loads(path.read_text(encoding="utf-8"))
        for entity, rows in sorted(entities.items()):
            primary = next(row for row in rows if row["version"] == version["name"])
            document["entities"][entity] = ageable_profile(primary["raw"], protocol)
        fields = ("version", "entity", "protocol", "measurement_sha256", "binding_sha256", "client_sha256", "server_sha256", "registry_class", "registry_numeric_id", "sources_sha256", "preservation", "callback_delegation")
        cohort = sorted((row for rows in entities.values() for row in rows), key=lambda row: (row["version"], row["entity"]))
        first = cohort[0]["receipt"]
        document["_provenance"]["legacy_ageable_roles"] = {"measurement": measurements.name, "scope": first["scope"], "helper_source_sha256": first["helper_source_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": [{key: row["receipt"][key] for key in fields} for row in cohort]}
        outputs.append((path, (json.dumps(document, indent=2) + "\n").encode()))
    if fixture is not None:
        outputs.append((fixture, fixture_bytes(ageable_fixture(records, measurements.name), compressed=fixture.suffix == ".gz")))
    for path, content in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != content:
                raise ValueError(f"Legacy farm geometry output differs: {path}")
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
