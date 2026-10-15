"""Measure native modern teleport transforms without executing an entity or packet listener."""

import argparse
from collections.abc import Callable
import hashlib
import io
import json
import math
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile

from audit_entity_lifecycle import methods
from measure_block_interactions import class_bindings, interaction_catalog, unpack_jar, verify_client_origins
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json


def verify(raw: dict) -> None:
    if raw.get("operation") != "PositionMoveRotation.calculateAbsolute" or len(raw.get("inputs", [])) != 8 or len(raw.get("rows", [])) != 4096:
        raise ValueError("Incomplete native teleport measurement")
    def vector(value: object) -> bool:
        return isinstance(value, list) and len(value) == 8 and all(type(number) in (int, float) and math.isfinite(number) for number in value)
    if not all(vector(row.get(key)) for row in raw["inputs"] for key in ("source", "change")):
        raise ValueError("Invalid native teleport input")
    expected = {(index, flags) for index in range(8) for flags in range(512)}
    seen = set()
    for row in raw["rows"]:
        key = (row.get("input"), row.get("flags"))
        if key not in expected or key in seen or not vector(row.get("result")):
            raise ValueError("Invalid, duplicate or nonfinite native teleport case")
        seen.add(key)
    if seen != expected or len(raw.get("sine_sha256", "")) != 64:
        raise ValueError("Missing native teleport cases or sine table hash")


def extract(reports: list[dict], expected: set[str], read: Callable[[str], bytes]) -> dict:
    versions = [row.get("version") for row in reports]
    if not reports or len(versions) != len(set(versions)) or set(versions) != expected or any(row.get("status") != "complete" for row in reports):
        raise ValueError("Native teleport releases are missing, duplicated or failed")
    groups, indices, releases, aliases = [], {}, [], {}
    for row in reports:
        contents = read(row["version"])
        if hashlib.sha256(contents).hexdigest() != row["measurement_sha256"]:
            raise ValueError("Native teleport measurement hash differs")
        raw = json.loads(contents)
        verify(raw)
        key = json.dumps(raw, sort_keys=True, separators=(",", ":"))
        protocol = row["protocol"]
        if protocol in aliases and aliases[protocol] != key:
            raise ValueError("Native teleport protocol aliases differ")
        aliases[protocol] = key
        if key not in indices:
            indices[key] = len(groups)
            groups.append(raw)
        releases.append({**row, "group": indices[key]})
    return {"schema": 1, "groups": groups, "releases": releases}


def audit(official: Path, version: str) -> dict:
    root = official / (version + "-client-decompiled")
    paths = {name: root / path for name, path in {
        "values": "net/minecraft/world/entity/PositionMoveRotation.java",
        "relative": "net/minecraft/world/entity/Relative.java",
        "vector": "net/minecraft/world/phys/Vec3.java",
        "math": "net/minecraft/util/Mth.java",
        "listener": "net/minecraft/client/multiplayer/ClientPacketListener.java",
        "entity": "net/minecraft/world/entity/Entity.java",
    }.items()}
    source = paths["values"].read_text()
    listeners = [row for row in methods(paths["listener"].read_text()) if row["name"] == "handleEntityPositionSync"]
    if len(listeners) != 1 or "setDeltaMovement(" in listeners[0]["body"]:
        raise ValueError("Native position-sync velocity behavior requires review")
    return {"pitch_clamped_before_delta_rotation": "Mth.clamp(" in source,
            "sync_preserves_velocity": True,
            "files": {name: {"path": str(path), "sha256": sha256(path)} for name, path in paths.items()}}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, destination = args.official.resolve(), args.measurements.resolve()
    if destination.is_relative_to(repo) or not 0 < args.timeout <= 300:
        raise ValueError("Use a fresh external measurement directory and a timeout in (0, 300] seconds")
    catalog = [row for row in interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], official) if row["protocol"] >= 768]
    if args.versions:
        if set(args.versions) - {row["name"] for row in catalog}:
            raise ValueError("Requested release has no modern teleport transform")
        catalog = [row for row in catalog if row["name"] in args.versions]
    destination.mkdir(parents=True, exist_ok=False)
    classes = destination / "classes"
    classes.mkdir()
    extraction = repo / "tools/extraction/shape-extractor"
    helpers = [extraction / (name + ".java") for name in ("PushDump", "MiningDump", "EntityTeleportDump")]
    run_checked([str(official / "runtimes/java11/bin/javac"), "--release", "11", "-d", str(classes), *map(str, helpers)], destination, destination / "compile.log", args.timeout)
    provenance = {"source_sha256": {path.name: sha256(path) for path in helpers}, "driver_sha256": sha256(Path(__file__))}
    write_json(destination / "measurement-tools.json", provenance)
    reports = []
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        directory = destination / version
        directory.mkdir()
        row = {"version": version, "protocol": protocol, "status": "failed"}
        try:
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            for side, name in (("server", "server.jar"), ("client", "client.jar"), ("client_mappings", "client_mappings.txt")):
                if side in metadata["downloads"]:
                    verify_artifact(downloads / name, metadata["downloads"][side]["sha1"])
            source_audit = audit(official, version)
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
                mapping = downloads / "client_mappings.txt"
                classpath = os.pathsep.join(map(str, (classes, client, cp)))
                result, load_log = directory / "entity-teleport.json", directory / "client-classes.log"
                command = [str(official / "runtimes/java25/bin/java"), "-Xmx1G", "-XX:-UsePerfData", "-Xlog:class+load=info:file=" + str(load_log), "-cp", classpath, "EntityTeleportDump", str(mapping) if mapping.exists() else "none", str(cp), str(result)]
                run_checked(command, directory, directory / "entity-teleport.log", args.timeout)
                measured = json.loads(result.read_text())
                verify(measured)
                wanted = {"net.minecraft.world.phys.Vec3", "net.minecraft.world.entity.PositionMoveRotation", "net.minecraft.world.entity.Relative", "net.minecraft.util.Mth"}
                origins = verify_client_origins(load_log.read_text(), class_bindings(mapping, wanted), client)
                row.update(status="complete", cases=len(measured["rows"]), server_sha256=sha256(downloads / "server.jar"), client_sha256=sha256(downloads / "client.jar"), measurement_sha256=sha256(result), source_audit=source_audit, preservation=preservation, native_client_classes=origins)
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            row["error"] = str(error)
        reports.append(row)
        write_json(destination / "entity-teleport-results.json", reports)
        print(version, row["status"], row.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in reports):
        raise SystemExit(1)
    result = extract(reports, {row["name"] for row in catalog}, lambda version: (destination / version / "entity-teleport.json").read_bytes())
    result["provenance"] = provenance
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    print(f"extracted {len(reports)} native releases in {len(result['groups'])} groups", flush=True)


if __name__ == "__main__":
    main()
