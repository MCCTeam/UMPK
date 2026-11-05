"""Measure native client pose metadata serializers on mapped or unobfuscated releases."""

import argparse
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile

from verify_entity_pose_wire import verify
from measure_block_interactions import class_bindings, interaction_catalog, unpack_jar, verify_client_origins
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json


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
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], official)
    if args.versions:
        if set(args.versions) - {entry["name"] for entry in catalog}:
            raise ValueError("Requested release has no native entity bindings")
        catalog = [entry for entry in catalog if entry["name"] in args.versions]
    destination.mkdir(parents=True, exist_ok=False)
    classes = destination / "classes"
    classes.mkdir()
    extraction = repo / "tools/extraction/shape-extractor"
    helpers = [extraction / (name + ".java") for name in ("PushDump", "MiningDump", "EntityPlacementProbe", "EntityPlacementGeometry", "EntityWorldSource", "EntityPoseWireDump")]
    run_checked([str(official / "runtimes/java11/bin/javac"), "--release", "11", "-d", str(classes), *map(str, helpers)], destination, destination / "compile.log", args.timeout)
    write_json(destination / "measurement-tools.json", {"source_sha256": {path.name: sha256(path) for path in helpers}, "driver_sha256": sha256(Path(__file__)), "verifier_sha256": sha256(repo / "tools/extraction/verify_entity_pose_wire.py")})
    wanted = {"net.minecraft.network.FriendlyByteBuf", "net.minecraft.network.syncher.EntityDataSerializer", "net.minecraft.network.syncher.EntityDataSerializers", "net.minecraft.world.entity.Pose"}
    reports = []
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = destination / version
        directory.mkdir()
        row = {"version": version, "status": "failed"}
        try:
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            for side, name in (("server", "server.jar"), ("client", "client.jar"), ("client_mappings", "client_mappings.txt")):
                if side in metadata["downloads"]:
                    verify_artifact(downloads / name, metadata["downloads"][side]["sha1"])
            with tempfile.TemporaryDirectory(prefix="native-libraries-", dir=directory) as temporary:
                cp = Path(temporary)
                with zipfile.ZipFile(downloads / "server.jar") as bundle:
                    jars = [name for name in bundle.namelist() if name.endswith(".jar") and name.startswith(("META-INF/versions/", "META-INF/libraries/"))]
                    if jars:
                        for name in jars:
                            with zipfile.ZipFile(io.BytesIO(bundle.read(name))) as archive:
                                unpack_jar(archive, cp)
                    else:
                        unpack_jar(bundle, cp)
                client = cp / "client-unsigned.jar"
                preservation = unsigned_client(downloads / "client.jar", client)
                parts = [str(classes), str(client), str(cp)]
                if version in ("1.17", "1.17.1", "1.18", "1.18.1"):
                    shim = directory / "shim"
                    shim.mkdir()
                    run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-cp", str(cp), "-d", str(shim), str(extraction / "log4j-shim/LogManager.java")], directory, directory / "shim-compile.log", args.timeout)
                    parts.insert(0, str(shim))
                java = official / "runtimes" / ("java11" if protocol <= 754 else "java25") / "bin/java"
                mapping = downloads / "client_mappings.txt"
                bindings = str(mapping) if mapping.exists() else "none"
                classpath = os.pathsep.join(parts)
                result = directory / "entity-pose-wire.json"
                load_log = directory / "client-classes.log"
                run_checked([str(java), "-Xmx2G", "-Xlog:class+load=info:file=" + str(load_log), "-cp", classpath, "EntityPoseWireDump", bindings, str(cp), str(result)], directory, directory / "entity-pose-wire.log", args.timeout)
                measured = json.loads(result.read_text())
                verified = verify(measured, standing_fallback=protocol >= 766)
                origins = verify_client_origins(load_log.read_text(), class_bindings(mapping, wanted), client)
                row.update(status="complete", protocol=protocol, verification=verified, server_sha256=sha256(downloads / "server.jar"), client_sha256=sha256(downloads / "client.jar"), measurement_sha256=sha256(result), preservation=preservation, native_client_classes=origins)
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            row["error"] = str(error)
        reports.append(row)
        write_json(destination / "entity-pose-wire-results.json", reports)
        print(version, row["status"], row.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in reports):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
