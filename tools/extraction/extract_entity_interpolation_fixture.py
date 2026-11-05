"""Build portable managed-replay fixtures from a complete, hash-checked native interpolation run."""

import argparse
import hashlib
import json
from pathlib import Path
from collections.abc import Callable

from measure_block_interactions import interaction_catalog
from verify_entity_interpolation import CASES, verify, verify_edges


def extract(reports: list[dict], expected: set[str], read: Callable[[str], bytes], profile_matrix: dict[str, dict] | None = None) -> dict:
    versions = [row.get("version") for row in reports]
    if not reports or len(versions) != len(set(versions)) or set(versions) != expected or any(row.get("status") != "complete" for row in reports):
        raise ValueError("Native interpolation releases are missing, duplicated or failed")
    cases, indices, releases, groups, aliases = [], {}, [], {}, {}
    suite = None
    for row in reports:
        contents = read(row["version"])
        if hashlib.sha256(contents).hexdigest() != row["measurement_sha256"]:
            raise ValueError("Native interpolation measurement hash differs")
        raw = json.loads(contents)
        current_suite = raw.get("suite", "standard")
        if suite is not None and suite != current_suite:
            raise ValueError("Native interpolation suites cannot be mixed")
        suite = current_suite
        if suite == "standard":
            verify(raw)
        elif suite == "edges":
            verify_edges(raw)
        else:
            raise ValueError("Unknown native interpolation suite")
        protocol = row["protocol"]
        if protocol in aliases and raw != aliases[protocol]:
            raise ValueError("Native interpolation protocol aliases differ")
        aliases[protocol] = raw
        if profile_matrix is not None:
            binding = profile_matrix.get(row["version"])
            if binding is None or binding["protocol"] != protocol:
                raise ValueError("Native release is absent from its profile matrix")
            group = binding["trace_group"]
        elif suite == "standard":
            common = [{"name": case["name"], "ticks": case["ticks"]} for case in raw["cases"] if case["name"] in CASES]
            key = json.dumps(common, sort_keys=True, separators=(",", ":"))
            group = groups.setdefault(key, row["version"])
        else:
            raise ValueError("Edge fixtures require the standard native profile matrix")
        refs = []
        for case in raw["cases"]:
            key = json.dumps(case, sort_keys=True, separators=(",", ":"))
            if key not in indices:
                indices[key] = len(cases)
                cases.append(case)
            refs.append(indices[key])
        releases.append({"version": row["version"], "protocol": protocol, "profile": raw["profile"], "trace_group": group, "cases": refs, "measurement_sha256": row["measurement_sha256"], "client_sha256": row["client_sha256"], "server_sha256": row["server_sha256"], "native_client_classes": row["native_client_classes"], "source_audit": row["source_audit"]})
    return {"schema": 1, "suite": suite, "cases": cases, "releases": releases}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--profile-matrix", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    expected = {entry["name"] for entry in interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)}
    if args.versions:
        if set(args.versions) - expected:
            raise ValueError("Requested fixture release has no native bindings")
        expected = set(args.versions)
    matrix = None if args.profile_matrix is None else {row["version"]: row for row in json.loads(args.profile_matrix.read_text())["releases"]}
    reports = json.loads((args.measurements / "entity-interpolation-results.json").read_text())
    result = extract(reports, expected, lambda version: (args.measurements / version / "entity-interpolation.json").read_bytes(), matrix)
    result["provenance"] = json.loads((args.measurements / "measurement-tools.json").read_text())
    result["extraction"] = {"tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "profile_matrix_sha256": None if args.profile_matrix is None else hashlib.sha256(args.profile_matrix.read_bytes()).hexdigest()}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    print(f"extracted {len(result['releases'])} releases and {len(result['cases'])} deduplicated native cases")


if __name__ == "__main__":
    main()
