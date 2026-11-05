"""Measure unmodified native living interpolation with explicit collision answers, tick rates and a bounded legacy tick head."""

import argparse
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

from measure_block_interactions import class_bindings, interaction_catalog, unpack_jar, verify_client_origins
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json
from verify_entity_interpolation import verify, verify_edges


def source_audit(official: Path, version: str) -> dict:
    source_root = official / (version + "-client-decompiled")
    source = source_root / "net/minecraft/world/entity"
    living = (source / "LivingEntity.java").read_text()
    result = {"source_sha256": {str(path.relative_to(source_root)): sha256(path) for path in [source / "Entity.java", source / "LivingEntity.java", *source.glob("*InterpolationHandler.java")]}}
    if "lerpSteps" not in living:
        result["legacy_tick_boundary"] = "not_used"
        return result
    match = re.search(r"public void aiStep\(\)\s*\{", living)
    if match is None:
        raise ValueError("Native living tick entry is absent")
    start = match.end()
    depth, end = 1, start
    while depth and end < len(living):
        depth += (living[end] == "{") - (living[end] == "}")
        end += 1
    body = living[start:end - 1]
    interpolation = body.find("this.lerpSteps--")
    velocity = body.find("this.setDeltaMovement(")
    if depth or interpolation < 0 or velocity <= interpolation or "this.lerpSteps > 0" not in body[:interpolation]:
        raise ValueError("The first native velocity write no longer follows interpolation")
    result.update(legacy_tick_boundary="first_velocity_set_after_interpolation", method="LivingEntity.aiStep", method_start_line=living[:match.start()].count("\n") + 1)
    return result


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--probe", choices=("standard", "edges"), default="standard")
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, output = args.official.resolve(), args.measurements.resolve()
    if output.is_relative_to(repo) or not 0 < args.timeout <= 300:
        raise ValueError("Use a fresh external directory and a timeout in (0, 300] seconds")
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], official)
    if args.versions:
        if set(args.versions) - {entry["name"] for entry in catalog}:
            raise ValueError("Requested release has no native entity bindings")
        catalog = [entry for entry in catalog if entry["name"] in args.versions]
    output.mkdir(parents=True, exist_ok=False)
    classes = output / "classes"
    classes.mkdir()
    helper_root = repo / "tools/extraction/shape-extractor"
    helpers = [helper_root / (name + ".java") for name in ("PushDump", "MiningDump", "EntityPlacementProbe", "EntityPlacementGeometry", "EntityWorldSource", "EntityInterpolationSource", "EntityInterpolationDump")]
    if args.probe == "edges":
        helpers.append(helper_root / "EntityInterpolationEdgeDump.java")
    run_checked([str(official / "runtimes/java11/bin/javac"), "--release", "11", "-d", str(classes), *map(str, helpers)], output, output / "compile.log", args.timeout)
    write_json(output / "measurement-tools.json", {"source_sha256": {path.name: sha256(path) for path in helpers}, "driver_sha256": sha256(Path(__file__)), "verifier_sha256": sha256(repo / "tools/extraction/verify_entity_interpolation.py")})
    reports = []
    wanted = {"net.minecraft.world.entity.Entity", "net.minecraft.world.entity.LivingEntity", "net.minecraft.world.entity.player.Player", "net.minecraft.world.phys.AABB", "net.minecraft.world.phys.Vec3", "net.minecraft.util.Mth"}
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = output / version
        directory.mkdir()
        row = {"version": version, "protocol": protocol, "status": "failed"}
        try:
            audit = source_audit(official, version)
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            for key, name in (("client", "client.jar"), ("server", "server.jar"), ("client_mappings", "client_mappings.txt")):
                if key in metadata["downloads"]:
                    verify_artifact(downloads / name, metadata["downloads"][key]["sha1"])
            with tempfile.TemporaryDirectory(prefix="native-interpolation-", dir=directory) as temporary:
                cp = Path(temporary)
                with zipfile.ZipFile(downloads / "server.jar") as bundle:
                    jars = [name for name in bundle.namelist() if name.endswith(".jar") and name.startswith(("META-INF/versions/", "META-INF/libraries/"))]
                    if jars:
                        for name in jars:
                            with zipfile.ZipFile(io.BytesIO(bundle.read(name))) as jar:
                                unpack_jar(jar, cp)
                    else:
                        unpack_jar(bundle, cp)
                client = cp / "client-unsigned.jar"
                preservation = unsigned_client(downloads / "client.jar", client)
                parts = [str(classes), str(client), str(cp)]
                if version in ("1.17", "1.17.1", "1.18", "1.18.1"):
                    shim = directory / "shim"
                    shim.mkdir()
                    run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-cp", str(cp), "-d", str(shim), str(helper_root / "log4j-shim/LogManager.java")], directory, directory / "shim-compile.log", args.timeout)
                    parts.insert(0, str(shim))
                java = official / "runtimes" / ("java11" if protocol <= 754 else "java25") / "bin/java"
                mapping = downloads / "client_mappings.txt"
                bindings = str(mapping) if mapping.exists() else "none"
                classpath = os.pathsep.join(parts)
                adapter = directory / "world-classes"
                adapter.mkdir()
                source = adapter / "EntityWorldAdapter.java"
                run_checked([str(java), "-Xmx2G", "-cp", classpath, "EntityInterpolationSource", bindings, str(source)], directory, directory / "adapter-source.log", args.timeout)
                run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-cp", classpath, "-d", str(adapter), str(source), str(adapter / "EntityPlayerAdapter.java")], directory, directory / "adapter-compile.log", args.timeout)
                classpath = str(adapter) + os.pathsep + classpath
                measurement = directory / "entity-interpolation.json"
                origins_log = directory / "client-classes.log"
                helper = "EntityInterpolationEdgeDump" if args.probe == "edges" else "EntityInterpolationDump"
                run_checked([str(java), "-Xmx2G", "-Xlog:class+load=info:file=" + str(origins_log), "-cp", classpath, helper, bindings, str(cp), str(measurement)], directory, directory / "measure.log", args.timeout)
                raw = json.loads(measurement.read_text())
                verification = verify_edges(raw) if args.probe == "edges" else verify(raw)
                entities = json.loads((args.data / str(protocol) / "entities.json").read_text())
                expected_entities = {"minecraft:player"} if args.probe == "edges" else {entity["id"] for entity in entities["entries"]}
                if set(raw["entity_profiles"]) != expected_entities or raw["legacy_tick_boundary"] != audit["legacy_tick_boundary"]:
                    raise ValueError("Native interpolation registry coverage or tick boundary differs")
                classes_used = set(wanted)
                if raw["profile"] != "living_inline":
                    classes_used.add("net.minecraft.world.entity.InterpolationHandler")
                if raw["profile"] == "living_steps":
                    classes_used.update({"net.minecraft.world.entity.AbstractInterpolationHandler", "net.minecraft.world.entity.SteppedInterpolationHandler"})
                origins = verify_client_origins(origins_log.read_text(), class_bindings(mapping, classes_used), client)
                row.update(status="complete", verification=verification, source_audit=audit, client_sha256=sha256(downloads / "client.jar"), server_sha256=sha256(downloads / "server.jar"), measurement_sha256=sha256(measurement), preservation=preservation, native_client_classes=origins,
                           adapter_source_sha256={path.name: sha256(path) for path in (source, adapter / "EntityPlayerAdapter.java")})
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            row["error"] = str(error)
        reports.append(row)
        write_json(output / "entity-interpolation-results.json", reports)
        print(version, row["status"], row.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in reports):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
