"""Extract classic signed-age geometry from retained original-client measurements."""

import argparse
import json
from pathlib import Path

from extract_entity_geometry import fixture_bytes, group_legacy, legacy_villager_profile, read_legacy, sha


def extract(measurements: Path, official: Path, data: Path, fixture: Path | None, *, check: bool) -> None:
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
    outputs = [(data / canonical["dataset"] / "entity-geometry.json", (json.dumps(document, indent=2) + "\n").encode())]
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
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    extract(args.measurements, args.official, args.data, args.fixture, check=args.check)


if __name__ == "__main__":
    main()
