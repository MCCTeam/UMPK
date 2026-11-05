"""Measure metadata-era placement contracts from official vanilla client jars and their own source bindings."""

import argparse
import json
from pathlib import Path
import subprocess
import sys
from urllib.parse import unquote, urlsplit

from extract_legacy_interactions import fold, validate_geometry
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json


def verify_origins(log: str, expected: set[str], client: Path) -> dict[str, str]:
    origins = {}
    for line in log.splitlines():
        _, marker, message = line.partition("native interaction class ")
        if not marker:
            continue
        name, uri = message.split(" from ", 1)
        if name in origins or urlsplit(uri).scheme != "file" or Path(unquote(urlsplit(uri).path)).resolve() != client.resolve():
            raise ValueError("Native interaction class did not load from the verified client jar")
        origins[name] = uri
    if set(origins) != expected:
        raise ValueError("Missing native client interaction class origins")
    return origins


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, destination = args.official.resolve(), args.measurements.resolve()
    if destination.is_relative_to(repo) or not 0 < args.timeout <= 300:
        raise ValueError("Use a fresh external measurement directory and a timeout in (0, 300] seconds")
    catalog = [entry for entry in json.loads((args.data / "versions.json").read_text())["versions"] if entry["protocol"] < 393]
    if args.versions:
        if set(args.versions) - {entry["name"] for entry in catalog}:
            raise ValueError("Requested release is absent from the metadata-era catalog")
        catalog = [entry for entry in catalog if entry["name"] in args.versions]
    destination.mkdir(parents=True, exist_ok=False)
    classes = destination / "classes"
    classes.mkdir()
    extraction = repo / "tools/extraction"
    helpers = [extraction / "shape-extractor" / (name + ".java") for name in ("LegacyMiningGeneric", "LegacyTagBinding", "LegacyInteractionDump")]
    resolvers = [extraction / (name + ".py") for name in ("resolve_legacy_mining", "resolve_legacy_outlines", "resolve_legacy_interactions")]
    java = official / "runtimes/java11/bin/java"
    run_checked([str(java.with_name("javac")), "--release", "11", "-d", str(classes), *map(str, helpers)], destination, destination / "compile.log", args.timeout)
    write_json(destination / "measurement-tools.json", {"source_sha256": {path.name: sha256(path) for path in helpers + resolvers}, "driver_sha256": sha256(Path(__file__))})
    reports = []
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = destination / version
        directory.mkdir()
        row = {"version": version, "protocol": protocol, "status": "failed", "semantic_side": "client", "server_role": "libraries_only"}
        try:
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            server, client = downloads / "server.jar", downloads / "client.jar"
            for side, path in (("server", server), ("client", client)):
                verify_artifact(path, metadata["downloads"][side]["sha1"])
            base, outline, bindings = directory / "symbols.json", directory / "outline-symbols.json", directory / "interaction-symbols.json"
            commands = [
                [sys.executable, str(resolvers[0]), version, "--official", str(official), "--out", str(base)],
                [sys.executable, str(resolvers[1]), "--official", str(official), "--symbols", str(base), "--out", str(outline)],
                [sys.executable, str(resolvers[2]), "--official", str(official), "--symbols", str(base), "--outline", str(outline), "--out", str(bindings)],
            ]
            for index, command in enumerate(commands):
                run_checked(command, directory, directory / f"resolve-{index}.log", args.timeout)
            unsigned = directory / "client-unsigned.jar"
            preservation = unsigned_client(client, unsigned)
            raw, evidence, log = directory / "interaction-client.json", directory / "interaction-geometry.json", directory / "interaction-client.log"
            run_checked([str(java), "-Xmx512m", "-cp", ":".join(map(str, (classes, unsigned, server))), "LegacyInteractionDump", str(bindings), str(raw), str(evidence)], directory, log, args.timeout)
            symbols = json.loads(bindings.read_text())
            expected = {symbols[name] for name in ("block", "state", "item")}
            expected.update(symbols["interaction_bindings"][name] for name in ("item_block", "box_type"))
            origins = verify_origins(log.read_text(), expected, unsigned)
            native = json.loads(raw.read_text())
            geometry = json.loads(evidence.read_text())
            validate_geometry(native, geometry)
            data = args.data / str(protocol)
            measured = fold(native, json.loads((data / "blocks.json").read_text()), json.loads((data / "items.json").read_text()))
            row.update(status="complete", states=len(native["states"]), items=len(native["items"]), simple_items=sum(value[0] == "simple_cube" for value in measured["items"].values()),
                       measurement_sha256=sha256(raw), geometry_sha256=sha256(evidence), bindings_sha256=sha256(bindings), native_client_classes=origins, preservation=preservation,
                       client_sha256=sha256(client), server_sha256=sha256(server), registered_states_visited=geometry["registered_states_visited"], duplicate_state_id_aliases=geometry["duplicate_state_id_aliases"])
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            row["error"] = str(error)
        reports.append(row)
        write_json(destination / "legacy-interaction-client-results.json", reports)
        print(version, row["status"], row.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in reports):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
