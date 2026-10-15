"""Measure legacy client/server selection, metadata aliases and door contexts in a fresh external workspace."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

from expand_legacy_outlines import expand_metadata
from verify_legacy_outlines import verify


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_artifact(path: Path, expected: str) -> None:
    if hashlib.sha1(path.read_bytes()).hexdigest() != expected:
        raise ValueError(f"Official artifact hash mismatch: {path}")


def unsigned_client(source: Path, destination: Path) -> dict:
    classes = {}
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(destination, "w") as copy:
        for entry in original.infolist():
            name = entry.filename.upper()
            if name.startswith("META-INF/") and (name == "META-INF/MANIFEST.MF" or name.endswith((".SF", ".RSA", ".DSA", ".EC")) or name.startswith("META-INF/SIG-")):
                continue
            value = original.read(entry.filename)
            copy.writestr(entry, value)
            if entry.filename.endswith(".class"):
                classes[entry.filename] = hashlib.sha256(value).hexdigest()
    with zipfile.ZipFile(destination) as copy:
        for name, expected in classes.items():
            if hashlib.sha256(copy.read(name)).hexdigest() != expected:
                raise ValueError(f"Client class bytes changed: {name}")
    return {"unchanged_class_files": len(classes), "unsigned_jar_sha256": sha256(destination),
            "classes_sha256": hashlib.sha256(json.dumps(classes, sort_keys=True).encode()).hexdigest()}


def run_checked(command: list[str], directory: Path, log: Path, timeout: float) -> None:
    with log.open("w") as output, subprocess.Popen(command, cwd=directory, stdout=output, stderr=subprocess.STDOUT) as process:
        try:
            code = process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
    if code != 0:
        raise RuntimeError(f"{command[0]} exited with {code}; see {log}")


def write_json(path: Path, value: dict | list) -> None:
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n")


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True, help="Fresh directory outside the source checkout")
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+", help="Defaults to all stable metadata-era releases in versions.json")
    parser.add_argument("--java", type=Path)
    parser.add_argument("--javac", type=Path)
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args()
    official, destination = args.official.resolve(), args.measurements.resolve()
    if destination.is_relative_to(repo):
        raise ValueError("Measurements and native bootstrap logs must stay outside the source checkout")
    if not 0 < args.timeout <= 300:
        raise ValueError("Native command timeout must be in (0, 300] seconds")
    catalog = [entry for entry in json.loads((args.data / "versions.json").read_text())["versions"] if entry["protocol"] < 393]
    if args.versions:
        requested = set(args.versions)
        if requested - {entry["name"] for entry in catalog}:
            raise ValueError("Requested release is absent from the metadata-era catalog")
        catalog = [entry for entry in catalog if entry["name"] in requested]
    java = args.java or official / "runtimes/java11/bin/java"
    javac = args.javac or java.with_name("javac")
    destination.mkdir(parents=True, exist_ok=False)
    classes = destination / "classes"
    classes.mkdir()
    extraction = repo / "tools/extraction"
    helpers = ["LegacyMiningGeneric", "LegacyTagBinding", "LegacyWorldSource", "LegacyOutlineDump", "LegacyStateAliasDump", "LegacyDoorOutlineDump"]
    run_checked([str(javac), "--release", "11", "-d", str(classes), *[str(extraction / "shape-extractor" / (name + ".java")) for name in helpers]], destination, destination / "compile.log", args.timeout)
    reports = {name: [] for name in ("legacy-outline-native", "legacy-outline-native-client", "legacy-state-alias", "legacy-door-outline", "legacy-outline-expanded")}
    write_json(destination / "measurement-tools.json", {"java": str(java), "source_sha256": {name + ".java": sha256(extraction / "shape-extractor" / (name + ".java")) for name in helpers}})
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = destination / version
        directory.mkdir()
        rows = {name: {"version": version, "protocol": protocol, "status": "failed"} for name in reports}
        try:
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            server, client = downloads / "server.jar", downloads / "client.jar"
            for side, artifact in (("server", server), ("client", client)):
                verify_artifact(artifact, metadata["downloads"][side]["sha1"])
            run_checked([sys.executable, str(extraction / "resolve_legacy_mining.py"), version, "--official", str(official), "--out", str(directory / "symbols.json")], directory, directory / "resolve-mining.log", args.timeout)
            run_checked([sys.executable, str(extraction / "resolve_legacy_outlines.py"), "--official", str(official), "--symbols", str(directory / "symbols.json"), "--out", str(directory / "door-outline-symbols.json")], directory, directory / "resolve-outlines.log", args.timeout)
            (directory / "outline-symbols.json").write_bytes((directory / "door-outline-symbols.json").read_bytes())
            adapter = directory / "shape-classes"
            adapter.mkdir()
            run_checked([str(java), "-cp", str(classes) + ":" + str(server), "LegacyWorldSource", str(directory / "symbols.json"), str(adapter / "LegacyWorldAdapter.java")], directory, directory / "world-source.log", args.timeout)
            run_checked([str(javac), "--release", "11", "-cp", str(server), "-d", str(adapter), str(adapter / "LegacyWorldAdapter.java")], directory, directory / "world-compile.log", args.timeout)
            unsigned = directory / "client-geometry-unsigned.jar"
            preservation = unsigned_client(client, unsigned)
            server_cp = ":".join(map(str, (classes, adapter, server)))
            client_cp = ":".join(map(str, (classes, adapter, unsigned, server)))

            def native(main: str, arguments: list[str], log: str, client_side: bool = True) -> None:
                run_checked([str(java), "-Xmx512m", "-XX:-UsePerfData", "-cp", client_cp if client_side else server_cp, main, *arguments], directory, directory / log, args.timeout)

            symbols = str(directory / "outline-symbols.json")
            measured = {}
            for side, suffix in (("server", ""), ("client", "-client")):
                output, witnesses = directory / ("outline-native" + suffix + ".json"), directory / ("outline-native" + suffix + "-witnesses.json")
                log = "outline-native" + suffix + ".log"
                native("LegacyOutlineDump", [symbols, str(output), str(witnesses)], log, side == "client")
                data, oracle = json.loads(output.read_text()), json.loads(witnesses.read_text())
                count = verify(data, oracle)
                measured[side] = (data, oracle)
                report = rows["legacy-outline-native-client" if side == "client" else "legacy-outline-native"]
                report.update(status="complete", native_states=len(data), native_ray_witnesses=count,
                              sha256={path.name: sha256(path) for path in (output, witnesses, directory / "outline-symbols.json")})
                report[side + "_sha1"] = metadata["downloads"][side]["sha1"]
                if side == "client":
                    loaded = [line for line in (directory / log).read_text().splitlines() if line.startswith("native geometry class ")]
                    if len(loaded) != 5 or any("from file:" + str(unsigned) not in line for line in loaded):
                        raise ValueError("Native geometry did not load from the client artifact")
                    report.update(preservation)
            if measured["client"] != measured["server"]:
                raise ValueError("Native client/server selection or ray witnesses differ")
            rows["legacy-outline-native-client"]["client_server_equal"] = True
            native("LegacyStateAliasDump", [str(directory / "symbols.json"), str(directory / "state-native-aliases.json")], "state-native-aliases.log")
            aliases = json.loads((directory / "state-native-aliases.json").read_text())
            expanded = expand_metadata(measured["client"][0], aliases, json.loads((args.data / str(protocol) / "blocks.json").read_text()))
            write_json(directory / "outline-native-expanded.json", expanded)
            rows["legacy-state-alias"].update(status="complete", blocks=aliases["block_count"], aliases=len(aliases["aliases"]), native_decode_failures=len(aliases["decode_failures"]), sha256=sha256(directory / "state-native-aliases.json"))
            rows["legacy-outline-expanded"].update(status="complete", wire_states=len(expanded["states"]), native_rejections=len(expanded["rejected_metadata"]))
            native("LegacyDoorOutlineDump", [str(directory / "door-outline-symbols.json"), str(directory / "state-native-aliases.json"), str(directory / "door-outline-native.json")], "door-outline-native.log")
            doors = json.loads((directory / "door-outline-native.json").read_text())
            rays = verify(doors["witness_rows"], doors["oracle"])
            rows["legacy-door-outline"].update(status="complete", native_contexts=doors["contexts"], negative_controls=doors["contexts"], native_rays=rays,
                                             sha256={name: sha256(directory / name) for name in ("door-outline-symbols.json", "state-native-aliases.json", "door-outline-native.json")})
            print(f"{version}: client/server rays {rows['legacy-outline-native']['native_ray_witnesses']}, door contexts {doors['contexts']}, door rays {rays}", flush=True)
        except Exception as error:
            for row in rows.values():
                row.update(status="failed", error=str(error))
            print(f"{version}: FAILED: {error}", flush=True)
        for name, row in rows.items():
            reports[name].append(row)
            write_json(destination / (name + "-results.json"), reports[name])
    complete = all(row["status"] == "complete" for rows in reports.values() for row in rows)
    print(f"Measurement {'complete' if complete else 'failed'}; reports: {destination}", flush=True)
    if not complete:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
