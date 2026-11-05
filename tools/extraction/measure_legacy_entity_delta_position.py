"""Measure older packet-coordinate caches through uniquely source-resolved native floor methods."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

from measure_block_interactions import unpack_jar, verify_client_origins
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json


def resolve_math(root: Path) -> tuple[str, str]:
    found = subprocess.run(["rg", "-l", "Math.sin", str(root)], capture_output=True, text=True, check=False)
    if found.returncode not in (0, 1):
        raise ValueError("Cannot inspect native math source")
    matches = []
    for filename in found.stdout.splitlines():
        path = Path(filename)
        source = re.sub(r"/\*.*?\*/", "", path.read_text(), flags=re.S)
        if "65536" not in source:
            continue
        for method in re.finditer(r"public static long (\w+)\(double (\w+)\)\s*\{([^}]+)\}", source):
            name, parameter, body = method.groups()
            compact = re.sub(r"\s+", "", body)
            local = re.match(r"long(\w+)=\(long\)" + re.escape(parameter) + ";", compact)
            if local:
                variable = local[1]
                expected = f"long{variable}=(long){parameter};return{parameter}<{variable}?{variable}-1L:{variable};"
                if compact == expected:
                    matches.append((path.stem, name))
    if len(matches) != 1:
        raise ValueError(f"Expected one native long-floor binding, found {matches}")
    return matches[0]


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, output = args.official.resolve(), args.measurements.resolve()
    if output.is_relative_to(repo) or not 0 < args.timeout <= 300:
        raise ValueError("Use a fresh external directory and a timeout in (0, 300] seconds")
    catalog = [row for row in json.loads((repo / "data/java/versions.json").read_text())["versions"] if row["protocol"] < 498]
    if args.versions:
        if set(args.versions) - {row["name"] for row in catalog}:
            raise ValueError("Requested release has no legacy position profile")
        catalog = [row for row in catalog if row["name"] in args.versions]
    output.mkdir(parents=True, exist_ok=False)
    classes = output / "classes"
    classes.mkdir()
    helper = repo / "tools/extraction/shape-extractor/LegacyEntityDeltaPositionDump.java"
    java = official / "runtimes/java11/bin/java"
    run_checked([str(official / "runtimes/java11/bin/javac"), "--release", "11", "-d", str(classes), str(helper)], output, output / "compile.log", args.timeout)
    write_json(output / "measurement-tools.json", {"source_sha256": {helper.name: sha256(helper)}, "driver_sha256": sha256(Path(__file__))})
    reports = []
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = output / version
        directory.mkdir()
        report = {"version": version, "protocol": protocol, "status": "failed"}
        try:
            sources, downloads = official / (version + "-client-decompiled"), official / "downloads" / version
            owner, method = resolve_math(sources)
            handlers = subprocess.run(["rg", "-l", "disconnect.lost", str(sources)], capture_output=True, text=True, check=True).stdout.splitlines()
            if len(handlers) != 1:
                raise ValueError("Cannot uniquely bind the native play listener")
            units = 32 if protocol == 47 else 4096
            bindings = {"owner": owner, "method": method, "units": units, "source_sha256": {str(Path(owner + ".java")): sha256(sources / (owner + ".java")), str(Path(handlers[0]).relative_to(sources)): sha256(Path(handlers[0]))}}
            listener = re.sub(r"/\*.*?\*/", "", Path(handlers[0]).read_text(), flags=re.S)
            if len(re.findall(r"/\s*" + str(units) + r"\.0", listener)) < 3:
                candidates = []
                hits = subprocess.run(["rg", "-l", r"\*\s*" + str(units) + r"\.0", str(sources)], capture_output=True, text=True, check=False)
                for filename in hits.stdout.splitlines():
                    path = Path(filename)
                    source = re.sub(r"/\*.*?\*/", "", path.read_text(), flags=re.S)
                    if path.stem + "." in listener and re.search(r"return\s+" + re.escape(owner + "." + method) + r"\([^;]*\*\s*" + str(units) + r"\.0\)", source):
                        candidates.append(path)
                if len(candidates) != 1:
                    raise ValueError("Native play movement does not uniquely establish the packet-coordinate units")
                bindings["source_sha256"][str(candidates[0].relative_to(sources))] = sha256(candidates[0])
            write_json(directory / "bindings.json", bindings)
            metadata = json.loads((downloads / "version.json").read_text())
            for side in ("server", "client"):
                verify_artifact(downloads / (side + ".jar"), metadata["downloads"][side]["sha1"])
            with tempfile.TemporaryDirectory(prefix="native-libraries-", dir=directory) as temporary:
                cp = Path(temporary)
                with zipfile.ZipFile(downloads / "server.jar") as archive:
                    unpack_jar(archive, cp)
                client = cp / "client-unsigned.jar"
                preservation = unsigned_client(downloads / "client.jar", client)
                classpath = os.pathsep.join(map(str, (classes, client, cp)))
                result, load_log = directory / "entity-delta-position.json", directory / "client-classes.log"
                run_checked([str(java), "-Xmx512M", "-Xlog:class+load=info:file=" + str(load_log), "-cp", classpath, "LegacyEntityDeltaPositionDump", str(directory / "bindings.json"), str(result)], directory, directory / "entity-delta-position.log", args.timeout)
                rows = json.loads(result.read_text())["rows"]
                if len(rows) != (36 if units == 32 else 72):
                    raise ValueError("Incomplete native legacy coordinate sequences")
                origins = verify_client_origins(load_log.read_text(), {owner: owner}, client)
                report.update(status="complete", sequences=len(rows), server_sha256=sha256(downloads / "server.jar"), client_sha256=sha256(downloads / "client.jar"), measurement_sha256=sha256(result), preservation=preservation, native_client_classes=origins, bindings=bindings)
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
            report["error"] = str(error)
        reports.append(report)
        write_json(output / "entity-delta-position-results.json", reports)
        print(version, report["status"], report.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in reports):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
