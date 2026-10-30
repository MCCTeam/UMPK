"""Build portable exact-state replay fixtures from complete hash-checked native RemotePlayer measurements."""

import argparse
from collections.abc import Callable
import hashlib
import json
from pathlib import Path

from measure_block_interactions import interaction_catalog
from verify_remote_player import verify


def extract(reports: list[dict], expected: set[str], read: Callable[[str], bytes], profiles: dict[str, dict]) -> dict:
    versions = [row.get("version") for row in reports]
    if not reports or len(versions) != len(set(versions)) or set(versions) != expected or any(row.get("status") != "complete" for row in reports):
        raise ValueError("Native remote-player releases are missing, duplicated or failed")
    cases, indices, releases, aliases = [], {}, [], {}
    for report in reports:
        version, protocol = report["version"], report["protocol"]
        contents = read(version)
        if hashlib.sha256(contents).hexdigest() != report["measurement_sha256"]:
            raise ValueError("Native remote-player measurement hash differs")
        raw = json.loads(contents)
        verification = verify(raw)
        if protocol in aliases and raw != aliases[protocol]:
            raise ValueError("Native remote-player protocol aliases differ")
        aliases[protocol] = raw
        profile = profiles.get(version)
        if profile is None or profile["protocol"] != protocol:
            raise ValueError("Native remote-player release lacks its verified position-controller profile")
        refs = []
        for case in raw["cases"]:
            key = json.dumps(case, sort_keys=True, separators=(",", ":"))
            if key not in indices:
                indices[key] = len(cases)
                cases.append(case)
            refs.append(indices[key])
        motion = next(row for row in raw["cases"] if row["name"] == "motion")
        velocity_steps = next((row["tick"] + 1 for row in motion["ticks"] if row["velocity"] == [0.3, -0.2, 0.7]), None)
        if velocity_steps not in (1, 3):
            raise ValueError("Unexpected native remote-player velocity period")
        releases.append({**report, "position_profile": profile["trace_group"], "position_steps": verification["position_steps"], "velocity_steps": 0 if velocity_steps == 1 else velocity_steps, "cases": refs})
    return {"schema": 1, "cases": cases, "releases": releases}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--position-profiles", type=Path, default=repo / "tests/Umpk.Client.Tests/Fixtures/EntityInterpolation.native.json")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    expected = {row["name"] for row in interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)}
    profiles = {row["version"]: row for row in json.loads(args.position_profiles.read_text())["releases"]}
    result = extract(json.loads((args.measurements / "results.json").read_text()), expected, lambda version: (args.measurements / version / "remote-ai.json").read_bytes(), profiles)
    result["provenance"] = json.loads((args.measurements / "tools.json").read_text())
    result["extraction"] = {"source_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "verifier_sha256": hashlib.sha256((repo / "tools/extraction/verify_remote_player.py").read_bytes()).hexdigest(), "position_profiles_sha256": hashlib.sha256(args.position_profiles.read_bytes()).hexdigest()}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    print(f"extracted {len(result['releases'])} native remote-player releases and {len(result['cases'])} shared cases")


if __name__ == "__main__":
    main()
