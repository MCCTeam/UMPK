"""Extract classic signed-age geometry from retained original-client measurements."""

import argparse
import json
from pathlib import Path

from extract_entity_geometry import CLASSIC_ROLES, fixture_bytes, group_legacy, legacy_villager_profile, read_legacy, sha


def group_classic_roles(records: list[dict], expected_versions: set[str]) -> dict[str, list[dict]]:
    if not expected_versions:
        raise ValueError("Classic role aliases are missing")
    groups, seen = {}, set()
    for record in records:
        entity, version = record["entity"], record["version"]
        if type(record["protocol"]) is not int or record["protocol"] != 47 or entity not in CLASSIC_ROLES or version not in expected_versions or (entity, version) in seen or record["raw"].get("entity") != entity:
            raise ValueError("Classic role has a duplicate or invalid release identity")
        seen.add((entity, version))
        rows = groups.setdefault(entity, [])
        profile = legacy_villager_profile(record["raw"])
        if rows and profile != legacy_villager_profile(rows[0]["raw"]):
            raise ValueError("Classic role dimensions differ between protocol aliases")
        rows.append(record)
    if set(groups) != set(CLASSIC_ROLES) or any({row["version"] for row in rows} != expected_versions for rows in groups.values()):
        raise ValueError("Classic role evidence must cover all four types and every alias")
    return groups


def classic_role_fixture(records: list[dict], measurement: str) -> dict:
    cases, releases = [], []
    for record in sorted(records, key=lambda row: (row["version"], row["entity"])):
        raw, receipt = record["raw"], record["receipt"]
        case = {key: value for key, value in raw.items() if key not in ("origins", "age_phase_owner", "child_predicate_owner", "entity_class")}
        if case not in cases:
            cases.append(case)
        releases.append({key: receipt[key] for key in ("version", "entity", "protocol", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": cases.index(case)})
    first = records[0]["receipt"]
    return {"schema": 1, "measurement": measurement, "scope": first["scope"], "helper_source_sha256": first["helper_source_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": releases, "cases": cases}


def extract(measurements: Path, official: Path, data: Path, fixture: Path | None, *, check: bool, roles: Path | None = None, role_fixture: Path | None = None) -> None:
    if role_fixture is not None and roles is None:
        raise ValueError("A classic role fixture requires its native measurements")
    catalog = json.loads((data / "versions.json").read_text(encoding="utf-8"))["versions"]
    expected = {row["name"] for row in catalog if row["protocol"] == 47}
    receipts = json.loads((measurements / "results.json").read_text(encoding="utf-8"))
    if len(receipts) != len(expected) or {row["version"] for row in receipts} != expected or any(row["protocol"] != 47 for row in receipts):
        raise ValueError("Classic geometry must cover every protocol 47 release")
    records = [read_legacy(measurements / receipt["version"], official, catalog) for receipt in receipts]
    group_legacy(records)
    helper_hash = sha(measurements / "ClassicGeometryDump.java")
    cases, releases = [], []
    for record in records:
        receipt, raw = record["receipt"], record["raw"]
        binding = json.loads((measurements / record["version"] / "bindings.json").read_text(encoding="utf-8"))["geometry"]
        if receipt.get("helper_sha256") != helper_hash or raw["age_phase_owner"] != binding["ageable"] or raw["child_predicate_owner"] != binding["ageable"]:
            raise ValueError("Classic age methods have unaudited helper bytes or owners")
        case = {key: value for key, value in raw.items() if key not in ("origins", "age_phase_owner", "child_predicate_owner")}
        if case not in cases:
            cases.append(case)
        releases.append({key: receipt[key] for key in ("version", "protocol", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": cases.index(case)})
    canonical = next(row for row in reversed(catalog) if row["protocol"] == 47)
    record = next(row for row in records if row["version"] == canonical["name"])
    receipt = record["receipt"]
    proof = {"kind": "extracted", "version": canonical["name"], "protocol": 47, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], "measurement_sha256": receipt["measurement_sha256"], "helper_sha256": helper_hash, "native_witnesses": len(record["raw"]["rows"]), "native_predicates": len(record["raw"]["predicates"]), "sources_sha256": receipt["sources_sha256"], "aliases": releases, "scope": receipt["scope"] + "; the loaded-area requirement comes from the verified original World source"}
    document = {"_provenance": proof, "schema": 1, "entities": {"minecraft:villager": legacy_villager_profile(record["raw"])}}
    outputs = []
    if roles is not None:
        role_records = []
        for role_receipt in json.loads((roles / "results.json").read_text(encoding="utf-8")):
            if role_receipt.get("entity") not in CLASSIC_ROLES or role_receipt.get("version") not in expected or role_receipt.get("format") != "classic-role-v1":
                raise ValueError("Classic role summary contains an unaudited release or type")
            role_record = read_legacy(roles / role_receipt["version"] / role_receipt["entity"].split(":")[1], official, catalog)
            if role_record["receipt"] != role_receipt:
                raise ValueError("Classic role summary differs from its individual receipt")
            role_records.append(role_record)
        groups = group_classic_roles(role_records, expected)
        for entity, rows in sorted(groups.items()):
            primary = next(row for row in rows if row["version"] == canonical["name"])
            document["entities"][entity] = legacy_villager_profile(primary["raw"])
        fields = ("version", "entity", "protocol", "measurement_sha256", "binding_sha256", "client_sha256", "server_sha256", "registry_class", "registry_numeric_id", "sources_sha256", "preservation")
        first = role_records[0]["receipt"]
        proof["classic_roles"] = {"measurement": roles.name, "helper_source_sha256": first["helper_source_sha256"], "helper_class_sha256": first["helper_class_sha256"], "releases": [{key: row["receipt"][key] for key in fields} for row in sorted(role_records, key=lambda row: (row["version"], row["entity"]))]}
        if role_fixture is not None:
            outputs.append((role_fixture, fixture_bytes(classic_role_fixture(role_records, roles.name), compressed=role_fixture.suffix == ".gz")))
    outputs.append((data / canonical["dataset"] / "entity-geometry.json", (json.dumps(document, indent=2) + "\n").encode()))
    if fixture is not None:
        native = {"schema": 1, "measurement": measurements.name, "scope": receipt["scope"], "helper_sha256": helper_hash, "releases": releases, "cases": cases}
        outputs.append((fixture, fixture_bytes(native, compressed=fixture.suffix == ".gz")))
    for path, content in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != content:
                raise ValueError(f"Classic geometry output differs: {path}")
        else:
            path.write_bytes(content)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--roles", type=Path)
    parser.add_argument("--role-fixture", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    extract(args.measurements, args.official, args.data, args.fixture, check=args.check, roles=args.roles, role_fixture=args.role_fixture)


if __name__ == "__main__":
    main()
