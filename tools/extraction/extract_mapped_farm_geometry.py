"""Extract the source-audited mapped pig, sheep, and chicken geometry cohort."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from resolve_legacy_mining import methods, params

ROLES = {"minecraft:pig", "minecraft:sheep", "minecraft:chicken"}
ENTITY = "net.minecraft.world.entity.Entity"
LIVING = "net.minecraft.world.entity.LivingEntity"
AGEABLE = {"net.minecraft.world.entity.AgeableMob", "net.minecraft.world.entity.AgableMob"}


def clean(source: str) -> str:
    return re.sub(r"/\*.*?\*/|//[^\n]*", "", source, flags=re.S)


def method_bodies(source: str, name: str, result: str) -> list[tuple[re.Match[str], str]]:
    pattern = r"\b(?:public|protected)\s+(?:final\s+)?" + result + r"\s+(" + re.escape(name) + r")\(([^)]*)\)\s*\{"
    return list(methods(source, pattern))


def callback_contract(raw: dict, sources: dict[str, str]) -> dict:
    hierarchy = raw.get("actor_hierarchy", [])
    owners = raw.get("method_owners", {})
    age = owners.get("baby")
    if not hierarchy or hierarchy[0] != raw.get("entity_class") or hierarchy[-1] != ENTITY or len(hierarchy) != len(set(hierarchy)) or age not in AGEABLE or age not in hierarchy or LIVING not in hierarchy:
        raise ValueError("Mapped farm native hierarchy or age owner differs")
    expected = {"dimensions": LIVING, "scale": LIVING, "baby": age, "position": ENTITY, "refresh": ENTITY, "box": ENTITY, "synced_data": owners.get("synced_data")}
    if owners != expected or not set(hierarchy) <= set(raw.get("origins", {})):
        raise ValueError("Mapped farm source owners or origins are incomplete")
    texts = {}
    for index, actor in enumerate(hierarchy):
        path = actor.replace(".", "/") + ".java"
        if path not in sources:
            raise ValueError("Mapped farm actor source is missing")
        text = texts[actor] = clean(sources[path])
        package = re.search(r"\bpackage\s+([\w.]+)\s*;", text)
        if package is None or package[1] != actor.rsplit(".", 1)[0]:
            raise ValueError("Mapped farm actor source package differs")
        if index == len(hierarchy) - 1:
            continue
        declaration = re.search(r"\bclass\s+" + re.escape(actor.rsplit(".", 1)[1]) + r"(?:<[^{};]+>)?\s+extends\s+([\w.]+)", text)
        if declaration is None:
            raise ValueError("Mapped farm actor superclass is missing")
        parent = declaration[1]
        if "." not in parent:
            imports = re.findall(r"\bimport\s+([\w.]+\." + re.escape(parent) + r")\s*;", text)
            if len(imports) > 1:
                raise ValueError("Mapped farm superclass import is ambiguous")
            parent = imports[0] if imports else package[1] + "." + parent
        if parent != hierarchy[index + 1]:
            raise ValueError("Mapped farm superclass differs from the native hierarchy")
    prefix = hierarchy[:hierarchy.index(age)]
    if any(method_bodies(texts[actor], name, result) for actor in prefix for name, result in (("isBaby", "boolean"), ("getAge", "int"))):
        raise ValueError("Mapped farm overrides the audited age predicate")
    source = texts[age]
    if re.search(r"static\s+final\s+EntityDataAccessor<Boolean>\s+DATA_BABY_ID\s*=\s*SynchedEntityData\.defineId\(" + re.escape(age.rsplit(".", 1)[1]) + r"\.class,\s*EntityDataSerializers\.BOOLEAN\)", source) is None or re.search(r"\.define\(DATA_BABY_ID,\s*false\)", source) is None:
        raise ValueError("Mapped farm Boolean age accessor or default differs")
    predicates = method_bodies(source, "isBaby", "boolean")
    body = re.sub(r"\s+", "", predicates[0][1]) if len(predicates) == 1 else ""
    gated = body == "returnthis.canBeABaby()&&this.getAge()<0;"
    if not gated and body != "returnthis.getAge()<0;":
        raise ValueError("Mapped farm child predicate is not the negative age")
    if gated:
        gates = [rows for actor in hierarchy for rows in [method_bodies(texts[actor], "canBeABaby", "boolean")] if rows]
        if not gates or len(gates[0]) != 1 or re.sub(r"\s+", "", gates[0][0][1]) != "returntrue;":
            raise ValueError("Mapped farm baby gate has an unaudited dependency")
    ages = method_bodies(source, "getAge", "int")
    if len(ages) != 1 or re.search(r"return\s+this\.entityData\.get\(DATA_BABY_ID\)\s*\?\s*-1\s*:\s*1\s*;", ages[0][1]) is None:
        raise ValueError("Mapped farm client age does not read its Boolean selector")
    callback_owners = []
    for actor in hierarchy[:hierarchy.index(age) + 1]:
        callbacks = [(declaration, body) for declaration, body in method_bodies(texts[actor], "onSyncedDataUpdated", "void") if params(declaration[2]) == ["EntityDataAccessor<?>"]]
        if not callbacks:
            continue
        if len(callbacks) != 1:
            raise ValueError("Mapped farm metadata callback is ambiguous")
        callback_owners.append(actor)
        declaration, body = callbacks[0]
        argument = declaration[2].split()[-1]
        compact = re.sub(r"\s+", "", body)
        call = "super.onSyncedDataUpdated(" + argument + ");"
        before_super = compact.split(call)[0]
        if compact.count(call) != 1 or before_super.count("{") != before_super.count("}") or "return" in before_super or "throw" in before_super:
            raise ValueError("Mapped farm callback does not delegate unconditionally")
        if actor == age and "if(DATA_BABY_ID.equals(" + argument + ")){this.refreshDimensions();}" not in compact:
            raise ValueError("Mapped farm age callback does not refresh native dimensions")
    if not callback_owners or callback_owners[0] != owners.get("synced_data") or age not in callback_owners:
        raise ValueError("Mapped farm native callback owner differs from source")
    return {"age_owner": age, "callback_owner": callback_owners[0], "gated_baby_predicate": gated}


def mapped_farm_profile(raw: dict, protocol: int, scale_attribute: str = "") -> dict:
    from extract_entity_geometry import role_profile

    if type(protocol) is not int or not 498 <= protocol <= 777 or raw.get("entity") not in ROLES or raw.get("selector_kind") != "boolean":
        raise ValueError("Mapped farm release or selector is outside the audited cohort")
    if len(raw["positions"]) != 3 or len({tuple(point) for point in raw["positions"]}) != 3:
        raise ValueError("Mapped farm measurements require three distinct positions")
    for row in raw["rows"]:
        for field, selector in (("before_child", "from_age"), ("after_child", "to_age")):
            if type(row.get(field)) is not bool or row[field] != bool(row[selector]):
                raise ValueError("Mapped farm native child observation differs from its selector")
    return role_profile(raw, scale_attribute=scale_attribute)


def group_mapped_farms(records: list[dict], expected_versions: dict[str, int]) -> dict[int, dict[str, list[dict]]]:
    if not expected_versions or any(type(protocol) is not int or not 498 <= protocol <= 777 for protocol in expected_versions.values()):
        raise ValueError("Mapped farm release catalog is outside the audited cohort")
    groups, seen, profiles = {}, set(), {}
    for record in records:
        version, entity, protocol = (record[key] for key in ("version", "entity", "protocol"))
        if type(protocol) is not int or expected_versions.get(version) != protocol or entity not in ROLES or (version, entity) in seen or record["raw"].get("entity") != entity:
            raise ValueError("Mapped farm release or entity identity differs")
        seen.add((version, entity))
        profile = mapped_farm_profile(record["raw"], protocol, record["scale_attribute"])
        if profiles.setdefault((protocol, entity), profile) != profile:
            raise ValueError("Mapped farm profiles differ between protocol aliases")
        groups.setdefault(protocol, {}).setdefault(entity, []).append(record)
    if seen != {(version, entity) for version in expected_versions for entity in ROLES}:
        raise ValueError("Mapped farm measurements must cover every release and type")
    return groups


def factory_contract(entity: str, actor: str, sources: dict[str, str]) -> dict:
    namespace, role = entity.split(':')
    simple = actor.rsplit('.', 1)[1]
    field = role.upper()
    candidates = []
    if namespace != 'minecraft' or role not in ('pig', 'sheep', 'chicken') or simple.lower() != role:
        raise ValueError('Unaudited mapped farm identity')
    for path in ('net/minecraft/world/entity/EntityType.java', 'net/minecraft/world/entity/EntityTypes.java'):
        if path not in sources:
            continue
        source = clean(sources[path])
        pattern = 'public\\s+static\\s+final\\s+EntityType<' + re.escape(simple) + '>\\s+' + field + '\\s*=\\s*register\\((.*?)\\);'
        for match in re.finditer(pattern, source, re.S):
            candidates.append((path, source, match[1]))
    if len(candidates) != 1:
        raise ValueError('Native farm factory is missing or ambiguous')
    path, source, body = candidates[0]
    package = re.search('\\bpackage\\s+([\\w.]+)\\s*;', source)
    if not package or not (actor.rsplit('.', 1)[0] == package[1] or re.search('\\bimport\\s+' + re.escape(actor) + '\\s*;', source)):
        raise ValueError('Native factory imports a different actor class')
    if re.search('\\b' + re.escape(simple) + '\\s*::\\s*new\\b', body) is None:
        raise ValueError('Native factory constructor differs')
    if re.search('\\bRegistry\\.register\\(\\s*(?:BuiltInRegistries|Registry)\\.ENTITY_TYPE\\b', source) is None:
        raise ValueError('Native factory targets a different registry')
    key = body.split(',', 1)[0].strip()
    key_source = None
    if key not in ('"' + role + '"', '"minecraft:' + role + '"'):
        named = re.fullmatch('(\\w+)\\.' + field, key)
        if named is None:
            raise ValueError('Native factory key differs from its entity name')
        key_source = 'net/minecraft/world/entity/' + named[1] + '.java'
        if key_source not in sources:
            raise ValueError('Native factory key source is missing')
        text = clean(sources[key_source])
        declaration = 'public\\s+static\\s+final\\s+ResourceKey<EntityType<\\?>>\\s+' + field + '\\s*=\\s*create\\("' + role + '"\\);'
        if re.search(declaration, text) is None or re.search('ResourceKey\\.create\\(Registries\\.ENTITY_TYPE\\s*,', text) is None:
            raise ValueError('Native typed key is not the canonical entity key')
    return {'source': path, 'field': field, 'actor': actor, 'key': key, 'key_source': key_source}

def registry_contract(raw: dict, sources: dict[str, str], mapping: str | None) -> dict:
    owner = raw['registry']['id_owner']
    path = owner.replace('.', '/') + '.java'
    if path not in sources or re.search('\\bint\\s+getId\\s*\\(', clean(sources[path])) is None:
        raise ValueError('Native numeric getter implementation source is missing')
    registry = 'net/minecraft/core/Registry.java'
    if registry not in sources or re.search('\\bgetKey\\s*\\(', clean(sources[registry])) is None:
        raise ValueError('Native registry key contract source is missing')
    api = []
    for name in ['net/minecraft/core/IdMap.java', registry]:
        if name in sources and re.search('\\bint\\s+getId\\s*\\(', clean(sources[name])):
            api.append(name)
    if not api:
        raise ValueError('Native registry ID API contract source is missing')
    runtime = 'getId'
    if mapping is not None:
        current = None
        names = []
        for line in mapping.splitlines():
            if line and (not line[0].isspace()) and (' -> ' in line):
                current = line.split(' -> ', 1)[0]
            elif current == owner:
                match = re.fullmatch('\\s*(?:\\d+:\\d+:)?int getId\\(java\\.lang\\.Object\\)(?::\\d+(?::\\d+)?)? -> (\\S+)\\s*', line)
                if match:
                    names.append(match[1])
        if len(names) != 1:
            raise ValueError('Native getId has no unique official method mapping')
        runtime = names[0]
    if raw['registry']['id_method'] != runtime:
        raise ValueError('Native numeric lookup is not source-named getId')
    return {'api_sources': api, 'key_source': registry, 'implementation_source': path, 'runtime_method': runtime}


def audit_binding(receipt: dict, audit: dict, receipt_sha256: str, measurement_root: str) -> None:
    fields = ("version", "protocol", "entity", "measurement_sha256", "helper_class_sha256", "client_sha256", "mappings_sha256")
    if any(audit.get(key) != receipt.get(key) for key in fields) or audit.get("receipt_sha256") != receipt_sha256 or audit.get("measurement_root") != measurement_root:
        raise ValueError("Mapped farm source audit differs from the unchanged measurement receipt")


def verify_mapped_farm_identity(root: Path, official: Path, raw: dict, receipt: dict, audit: dict) -> str:
    from extract_entity_geometry import living_scale_attribute, sha

    measurement_root = root.parents[1]
    audit_binding(receipt, audit, sha(root / "result.json"), str(measurement_root))
    if receipt.get("entity") not in ROLES or raw.get("entity") != receipt["entity"] or raw.get("entity_class") != receipt.get("registry_class") or raw.get("entity_type_matches_factory") is not True:
        raise ValueError("Mapped farm identity differs from the original actor or factory")
    registry = raw.get("registry", {})
    if type(registry.get("id")) is not int or type(receipt.get("registry_numeric_id")) is not int or registry["id"] != receipt["registry_numeric_id"] or registry.get("name") != receipt["entity"]:
        raise ValueError("Mapped farm native registry identity differs")
    sources = {}
    tree = official / (receipt["version"] + "-client-decompiled")
    for hashes in (receipt["sources_sha256"], audit["sources_sha256"]):
        for name, expected in hashes.items():
            path = tree / name
            if not path.resolve().is_relative_to(tree.resolve()) or sha(path) != expected:
                raise ValueError("Mapped farm audited source changed")
            if name.endswith(".java"):
                sources[name] = path.read_text(encoding="utf-8")
    if not set(raw.get("origins", {})) or any(name.split("$")[0].replace(".", "/") + ".java" not in receipt["sources_sha256"] for name in raw["origins"]):
        raise ValueError("Mapped farm native origin source coverage is incomplete")
    for name, expected in receipt["helper_sha256"].items():
        if Path(name).name != name or sha(measurement_root / name) != expected:
            raise ValueError("Mapped farm native helper source changed")
    if sha(measurement_root / "classes/MappedRoleGeometryDump.class") != receipt["helper_class_sha256"]:
        raise ValueError("Mapped farm native helper class changed")
    for key, subdirectory in (("world_adapter_source_sha256", "world-classes"), ("world_adapter_class_sha256", "world-classes"), ("logging_shim_class_sha256", "shim")):
        parent = root.parent / subdirectory
        for name, expected in receipt.get(key, {}).items():
            if not (parent / name).resolve().is_relative_to(parent.resolve()) or sha(parent / name) != expected:
                raise ValueError("Mapped farm controlled-world adapter changed")
    if receipt.get("logging_shim_source_sha256") is not None and sha(Path(__file__).parent / "shape-extractor/log4j-shim/LogManager.java") != receipt["logging_shim_source_sha256"]:
        raise ValueError("Mapped farm logging adapter source changed")
    seed = official / "downloads" / receipt["version"]
    metadata = json.loads((seed / "version.json").read_text(encoding="utf-8"))
    mapping = None
    if "client_mappings" in metadata["downloads"]:
        path = seed / "client_mappings.txt"
        if sha(path) != receipt.get("mappings_sha256") or hashlib.sha1(path.read_bytes()).hexdigest() != metadata["downloads"]["client_mappings"]["sha1"]:
            raise ValueError("Mapped farm official mappings changed")
        mapping = path.read_text(encoding="utf-8")
    elif receipt.get("mappings_sha256") is not None:
        raise ValueError("Mapped farm receipt names unavailable official mappings")
    if factory_contract(receipt["entity"], raw["entity_class"], sources) != audit["factory"] or registry_contract(raw, sources, mapping) != audit["registry"]:
        raise ValueError("Mapped farm source contract differs from its reviewed audit")
    callback_contract(raw, sources)
    return living_scale_attribute(sources["net/minecraft/world/entity/LivingEntity.java"])


def load_audit(path: Path, expected_sha256: str, measurements: list[Path]) -> dict:
    from extract_entity_geometry import sha

    if sha(path) != expected_sha256:
        raise ValueError("Mapped farm source audit hash changed")
    audit = json.loads(path.read_text(encoding="utf-8"))
    if audit.get("schema") != 1 or audit.get("measurements") != len(audit.get("records", [])):
        raise ValueError("Mapped farm source audit record count differs")
    expected = {str(root.resolve()): sha(root / "results.json") for root in measurements}
    inputs = audit.get("inputs", [])
    if len(inputs) != len(expected) or {row["root"]: row["results_sha256"] for row in inputs} != expected:
        raise ValueError("Mapped farm source audit input summary changed")
    for name, checksum in audit.get("audit_files_sha256", {}).items():
        if (path.parent / name).parent != path.parent or sha(path.parent / name) != checksum:
            raise ValueError("Mapped farm source audit evidence changed")
    keys = [(row["version"], row["entity"]) for row in audit["records"]]
    if len(keys) != len(set(keys)):
        raise ValueError("Mapped farm source audit contains duplicate identities")
    return audit


def mapped_farm_fixture(records: list[dict], measurements: list[str], audit_sha256: str) -> dict:

    cases, releases, seen = [], [], {}
    fields = ("entity", "selector_kind", "metadata_index", "metadata_default", "pose_index", "blocks_building", "poses", "positions", "rows")
    for record in sorted(records, key=lambda row: (row["protocol"], row["version"], row["entity"])):
        case = {key: record["raw"][key] for key in fields}
        fingerprint = json.dumps(case, sort_keys=True)
        if fingerprint not in seen:
            seen[fingerprint] = len(cases)
            cases.append(case)
        receipt = record["receipt"]
        releases.append({key: receipt[key] for key in ("version", "protocol", "entity", "registry_numeric_id", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": seen[fingerprint]})
    first = records[0]["receipt"]
    if any(row["receipt"]["helper_sha256"] != first["helper_sha256"] or row["receipt"]["helper_class_sha256"] != first["helper_class_sha256"] for row in records):
        raise ValueError("Mapped farm native helpers differ between measurements")
    return {"schema": 1, "measurements": measurements, "scope": first["scope"], "source_audit_sha256": audit_sha256, "helper_source_sha256": first["helper_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": releases, "cases": cases}


def extract(measurements: list[Path], audit_path: Path, audit_sha256: str, official: Path, data: Path, fixture: Path | None, *, check: bool) -> None:
    from extract_entity_geometry import fixture_bytes, read_legacy

    catalog = json.loads((data / "versions.json").read_text(encoding="utf-8"))["versions"]
    expected = {row["name"]: row["protocol"] for row in catalog if 498 <= row["protocol"] <= 777}
    audit = load_audit(audit_path, audit_sha256, measurements)
    source_records = {(row["version"], row["entity"]): row for row in audit["records"]}
    records, seen = [], set()
    for root in measurements:
        for receipt in json.loads((root / "results.json").read_text(encoding="utf-8")):
            version, entity = receipt.get("version"), receipt.get("entity")
            identity = (version, entity)
            if receipt.get("format") != "mapped-farm-role-v1" or entity not in ROLES or expected.get(version) != receipt.get("protocol") or identity in seen or identity not in source_records:
                raise ValueError("Mapped farm summary contains an unaudited or duplicate identity")
            seen.add(identity)
            record = read_legacy(root / version / entity.split(":")[1], official, catalog, source_audit=source_records[identity])
            if record["receipt"] != receipt:
                raise ValueError("Mapped farm summary differs from its unchanged individual receipt")
            entry = next(row for row in catalog if row["name"] == version)
            entities = json.loads((data / entry["dataset"] / "entities.json").read_text(encoding="utf-8"))
            ids = [row["id_num"] for key in ("entries", "mob_entries") for row in entities.get(key, []) if row["id"] == entity]
            if ids != [receipt["registry_numeric_id"]]:
                raise ValueError("Mapped farm native ID differs from its canonical registry")
            records.append(record)
            print(f"verified {version} {entity}", flush=True)
    if seen != set(source_records):
        raise ValueError("Mapped farm source audit and measurement identities differ")
    groups = group_mapped_farms(records, expected)
    outputs = []
    for protocol, entities in sorted(groups.items()):
        version = next(row for row in reversed(catalog) if row["protocol"] == protocol)
        path = data / version["dataset"] / "entity-geometry.json"
        document = json.loads(path.read_text(encoding="utf-8"))
        cohort = []
        for entity, rows in sorted(entities.items()):
            primary = next(row for row in rows if row["version"] == version["name"])
            document["entities"][entity] = mapped_farm_profile(primary["raw"], protocol, primary["scale_attribute"])
            cohort.extend(rows)
        first = cohort[0]["receipt"]
        fields = ("version", "entity", "protocol", "registry_numeric_id", "registry_class", "measurement_sha256", "client_sha256", "server_sha256", "mappings_sha256", "sources_sha256", "preservation")
        releases = []
        for row in sorted(cohort, key=lambda row: (row["version"], row["entity"])):
            source = source_records[(row["version"], row["entity"])]
            releases.append({key: row["receipt"][key] for key in fields} | {"source_contract": {key: source[key] for key in ("receipt_sha256", "factory", "registry", "sources_sha256")}})
        document["_provenance"]["mapped_farm_roles"] = {"measurements": [root.name for root in measurements], "scope": first["scope"], "source_audit_sha256": audit_sha256, "helper_source_sha256": first["helper_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": releases}
        outputs.append((path, (json.dumps(document, indent=2) + "\n").encode("utf-8")))
    if fixture is not None:
        outputs.append((fixture, fixture_bytes(mapped_farm_fixture(records, [root.name for root in measurements], audit_sha256), compressed=fixture.suffix == ".gz")))
    for path, content in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != content:
                raise ValueError(f"Mapped farm geometry output differs: {path}")
        else:
            path.write_bytes(content)
    print(f"complete: {len(records)} records, {len(groups)} protocols, {sum(len(row['raw']['rows']) for row in records)} native phases", flush=True)


def main() -> None:

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, action="append", required=True)
    parser.add_argument("--source-audit", type=Path, required=True)
    parser.add_argument("--source-audit-sha256", required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    extract(args.measurements, args.source_audit, args.source_audit_sha256, args.official, args.data, args.fixture, check=args.check)


if __name__ == "__main__":
    main()
