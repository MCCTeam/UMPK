"""Fold hash-checked native controller catalogues into a separately reviewed entity-motion dataset proposal."""

import argparse
from collections.abc import Callable
import hashlib
import json
from pathlib import Path

from measure_block_interactions import interaction_catalog
from verify_entity_interpolation import verify


def profiles(raw: dict, expected: set[str]) -> dict:
    rows = raw.get("entity_profiles")
    if not isinstance(rows, dict) or set(rows) != expected:
        raise ValueError("Native motion catalogue does not cover the entity registry")
    result = {}
    for name, row in sorted(rows.items()):
        if not isinstance(row, dict) or row.get("factory") not in ("native", "native_special_constructor", "owned_player_subtype"):
            raise ValueError("Unproven native entity factory")
        if type(row.get("living")) is not bool or not isinstance(row.get("class"), str) or not row["class"] or not isinstance(row.get("controller"), str) or not row["controller"]:
            raise ValueError("Invalid native entity motion binding")
        if "update_interval" in row and (type(row["update_interval"]) is not int or not 1 <= row["update_interval"] <= 2147483647):
            raise ValueError("Invalid native entity update interval")
        is_owned_player = row["factory"] == "owned_player_subtype"
        if (name == "minecraft:player") != is_owned_player or is_owned_player and row["class"] != "EntityPlayerAdapter":
            raise ValueError("The owned player measurement cannot be relabeled as a native client player")
        result[name] = dict(row)
        if is_owned_player:
            result[name]["client_player_measured"] = False
    return result


def extract(reports: list[dict], expected: set[str], read: Callable[[str], bytes], registries: dict[int, set[str]]) -> dict[int, dict]:
    versions = [row.get("version") for row in reports]
    if not reports or len(versions) != len(set(versions)) or set(versions) != expected or any(row.get("status") != "complete" for row in reports):
        raise ValueError("Native motion releases are missing, duplicated or failed")
    output = {}
    for report in reports:
        contents = read(report["version"])
        if hashlib.sha256(contents).hexdigest() != report["measurement_sha256"]:
            raise ValueError("Native motion measurement hash differs")
        protocol = report["protocol"]
        if protocol not in registries:
            raise ValueError("Native motion protocol has no canonical entity registry")
        entities = profiles(json.loads(contents), registries[protocol])
        if protocol in output and output[protocol]["entities"] != entities:
            raise ValueError("Native motion protocol aliases differ")
        if protocol not in output:
            output[protocol] = {"_provenance": {"kind": "native-client-controller-catalogue", "protocol": protocol, "versions": [], "measurements_sha256": {}, "client_sha256": {}, "server_sha256": {}, "scope": "Native type factories and their controller owners; the player row uses an owned survival-player subtype, not RemotePlayer. Missing legacy intervals remain unmeasured. This catalogue does not establish complete entity ticks or world physics."}, "schema": 1, "entities": entities}
        provenance = output[protocol]["_provenance"]
        name = report["version"]
        provenance["versions"].append(name)
        for source, target in (("measurement_sha256", "measurements_sha256"), ("client_sha256", "client_sha256"), ("server_sha256", "server_sha256")):
            provenance[target][name] = report[source]
    return output


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.out.resolve().is_relative_to(repo):
        raise ValueError("Write a fresh external proposal for review before changing canonical datasets")
    reports = json.loads((args.measurements / "entity-interpolation-results.json").read_text())
    expected = {row["name"] for row in interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)}
    registries = {}
    for row in reports:
        protocol = row["protocol"]
        registry = json.loads((args.data / str(protocol) / "entities.json").read_text())
        registries[protocol] = {entity["id"] for entity in registry["entries"]}
        verify(json.loads((args.measurements / row["version"] / "entity-interpolation.json").read_text()))
    result = extract(reports, expected, lambda version: (args.measurements / version / "entity-interpolation.json").read_bytes(), registries)
    args.out.mkdir(parents=True, exist_ok=False)
    for protocol, document in sorted(result.items()):
        destination = args.out / str(protocol)
        destination.mkdir()
        document["_provenance"]["extractor_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        document["_provenance"]["measurement_tools"] = json.loads((args.measurements / "measurement-tools.json").read_text())
        (destination / "entity-motion.json").write_text(json.dumps(document, indent=2) + "\n")
    print(f"proposed {len(result)} protocols from {len(reports)} native releases")


if __name__ == "__main__":
    main()
