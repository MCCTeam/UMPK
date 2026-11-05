"""Build portable player-info lookup fixtures from hash-checked native observations."""

import argparse
from collections.abc import Callable
import hashlib
import json
from pathlib import Path
import re

from measure_block_interactions import interaction_catalog

OPERATIONS = ("missing", "add_survival", "replace_with_spectator", "remove_current", "mutate_detached_first", "add_new_survival", "fresh_actor", "mutate_current_to_spectator", "fresh_actor_missing", "retry_missing_with_spectator")
ABSTRACT = "net.minecraft.client.player.AbstractClientPlayer"
LISTENER = "net.minecraft.client.multiplayer.ClientPacketListener"


def verify(raw: dict) -> dict:
    owners = raw.get("method_owners", {})
    if raw.get("actor") != "net.minecraft.client.player.RemotePlayer" or owners.get("spectator") not in (ABSTRACT, "net.minecraft.world.entity.player.Player") or owners.get("info_lookup") != LISTENER or owners.get("cached_lookup") != ABSTRACT or not isinstance(raw.get("scope"), str):
        raise ValueError("Native lookup actor, method owners or scope is unproven")
    queries = raw.get("queries", [])
    if tuple(row.get("operation") for row in queries) != OPERATIONS:
        raise ValueError("Native lookup observations are missing, duplicated or reordered")
    first = queries[1].get("after_cache")
    if first not in ("null", "first"):
        raise ValueError("The first native lookup has an unknown cache lifetime")
    cached = first == "first"
    flags = (False, False, False, False, True, True, False, True, False, True) if cached else (False, False, True, False, False, False, False, True, False, True)
    before = ("null", "null", "first", "first", "first", "first", "null", "third", "null", "null") if cached else ("null",) * 10
    after = ("null", "first", "first", "first", "first", "first", "third", "third", "null", "second") if cached else ("null",) * 10
    for index, row in enumerate(queries):
        if row.get("spectator") is not flags[index] or row.get("before_cache") != before[index] or row.get("after_cache") != after[index]:
            raise ValueError("A native spectator result or cache transition differs")
    return {"queries": 10, "caches_info": cached}


def extract(reports: list[dict], expected: set[str], read: Callable[[str], bytes]) -> dict:
    versions = [row.get("version") for row in reports]
    if not reports or len(versions) != len(set(versions)) or set(versions) != expected or any(row.get("status") != "complete" for row in reports):
        raise ValueError("Native player-info releases are missing, duplicated or failed")
    cases, indices, releases, aliases = [], {}, [], {}
    scope = None
    for report in reports:
        if any(not isinstance(report.get(name), str) or re.fullmatch(r"[0-9a-f]{64}", report[name]) is None for name in ("measurement_sha256", "client_sha256", "server_sha256")):
            raise ValueError("Native player-info artifact or measurement hashes are missing")
        version, protocol = report["version"], report["protocol"]
        contents = read(version)
        if hashlib.sha256(contents).hexdigest() != report["measurement_sha256"]:
            raise ValueError("Native player-info measurement hash differs")
        raw = json.loads(contents)
        result = verify(raw)
        if result["caches_info"] != (protocol >= 761):
            raise ValueError("Native player-info cache differs from the audited era boundary")
        if protocol in aliases and raw != aliases[protocol]:
            raise ValueError("Native player-info protocol aliases differ")
        aliases[protocol] = raw
        if scope is not None and raw["scope"] != scope:
            raise ValueError("Native player-info measurement scopes differ")
        scope = raw["scope"]
        key = json.dumps(raw["queries"], sort_keys=True, separators=(",", ":"))
        if key not in indices:
            indices[key] = len(cases)
            cases.append(raw["queries"])
        releases.append({name: report[name] for name in ("version", "protocol", "measurement_sha256", "client_sha256", "server_sha256")} | {"case": indices[key]})
    return {"schema": 1, "scope": scope, "releases": releases, "cases": cases}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    expected = {row["name"] for row in interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], args.official)}
    result = extract(json.loads((args.measurements / "player-info-lookup-results.json").read_text()), expected, lambda version: (args.measurements / version / "player-info-lookups.json").read_bytes())
    tools = json.loads((args.measurements / "measurement-tools.json").read_text())
    for name, expected_hash in tools["source_sha256"].items():
        if hashlib.sha256((repo / "tools/extraction/shape-extractor" / name).read_bytes()).hexdigest() != expected_hash:
            raise ValueError("The native probe source differs from the measured helper bundle")
    result["provenance"] = tools
    result["extraction"] = {"source_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    print(f"extracted {len(result['releases'])} releases and {len(result['cases'])} native player-info query lifetimes")


if __name__ == "__main__":
    main()
