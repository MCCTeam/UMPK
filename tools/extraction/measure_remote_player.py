"""Measure unchanged native RemotePlayer constructors and aiStep with an owned controlled client-level adapter."""

import argparse
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile

from measure_block_interactions import class_bindings, interaction_catalog, unpack_jar, verify_client_origins
from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json
from verify_remote_player import verify


def source_audit(official: Path, version: str) -> dict:
    root = official / (version + "-client-decompiled")
    names = ["net/minecraft/client/player/RemotePlayer.java", "net/minecraft/client/player/AbstractClientPlayer.java", "net/minecraft/world/entity/player/Player.java", "net/minecraft/world/entity/LivingEntity.java", "net/minecraft/world/entity/Entity.java"]
    client_levels = [root / ("net/minecraft/client/multiplayer/" + name + ".java") for name in ("ClientLevel", "MultiPlayerLevel")]
    found = [path for path in client_levels if path.exists()]
    if len(found) != 1:
        raise ValueError("Native remote-player client-level source is ambiguous")
    paths = [root / name for name in names] + found
    avatar = root / "net/minecraft/world/entity/Avatar.java"
    if avatar.exists():
        paths.append(avatar)
    return {"files": {str(path.relative_to(root)): sha256(path) for path in paths}, "scope": "Native RemotePlayer constructors and aiStep; owned survival mode and controlled client-world inputs. The world adapter constructor is not executed. The 26.3 native interpolation phase is advanced explicitly before aiStep."}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, root = args.official.resolve(), args.measurements.resolve()
    if root.is_relative_to(repo) or not 0 < args.timeout <= 300:
        raise ValueError("Use a fresh external measurement directory and a timeout in (0, 300] seconds")
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], official)
    if args.versions:
        if set(args.versions) - {row["name"] for row in catalog}:
            raise ValueError("Requested remote-player release has no native bindings")
        catalog = [row for row in catalog if row["name"] in args.versions]
    root.mkdir(parents=True, exist_ok=False)
    classes = root / "classes"
    classes.mkdir()
    helper = repo / "tools/extraction/shape-extractor"
    helpers = [helper / (name + ".java") for name in ("PushDump", "MiningDump", "EntityPlacementProbe", "EntityPlacementGeometry", "EntityWorldSource", "EntityInterpolationSource", "EntityInterpolationDump", "EntityRemotePlayerSource", "RemotePlayerDump")]
    run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-d", str(classes), *map(str, helpers)], root, root / "compile.log", args.timeout)
    write_json(root / "tools.json", {"sources": {str(path): sha256(path) for path in helpers}, "driver_sha256": sha256(Path(__file__)), "verifier_sha256": sha256(repo / "tools/extraction/verify_remote_player.py")})
    wanted = {"net.minecraft.client.player.RemotePlayer", "net.minecraft.client.player.AbstractClientPlayer", "net.minecraft.world.entity.player.Player", "net.minecraft.world.entity.LivingEntity", "net.minecraft.world.entity.Entity", "net.minecraft.world.phys.Vec3", "net.minecraft.world.phys.AABB", "net.minecraft.util.Mth"}
    results = []
    for entry in catalog:
        version = entry["name"]
        directory = root / version
        directory.mkdir()
        result = {"version": version, "protocol": entry["protocol"], "status": "failed"}
        try:
            audit = source_audit(official, version)
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            for side, name in (("client", "client.jar"), ("server", "server.jar"), ("client_mappings", "client_mappings.txt")):
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
                parts = [classes, client, cp]
                if version in ("1.17", "1.17.1", "1.18", "1.18.1"):
                    shim = directory / "shim"
                    shim.mkdir()
                    run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-cp", str(cp), "-d", str(shim), str(helper / "log4j-shim/LogManager.java")], directory, directory / "shim-compile.log", args.timeout)
                    parts.insert(0, shim)
                classpath = os.pathsep.join(map(str, parts))
                mapping = downloads / "client_mappings.txt"
                bindings = str(mapping) if mapping.exists() else "none"
                java = str(official / "runtimes" / ("java11" if entry["protocol"] <= 754 else "java25") / "bin/java")
                adapters = directory / "adapters"
                adapters.mkdir()
                run_checked([java, "-Xmx1G", "-cp", classpath, "EntityRemotePlayerSource", bindings, str(adapters / "EntityWorldAdapter.java")], directory, directory / "source.log", args.timeout)
                run_checked([str(official / "runtimes/java25/bin/javac"), "--release", "11", "-cp", classpath, "-d", str(adapters), str(adapters / "EntityWorldAdapter.java"), str(adapters / "EntityPlayerAdapter.java")], directory, directory / "adapter-compile.log", args.timeout)
                origins, output = directory / "classes.log", directory / "remote-ai.json"
                run_checked([java, "-Xmx1G", "-XX:-UsePerfData", "-Xlog:class+load=info:file=" + str(origins), "-cp", str(adapters) + os.pathsep + classpath, "RemotePlayerDump", bindings, str(cp), str(output)], directory, directory / "remote-ai.log", args.timeout)
                verification = verify(json.loads(output.read_text()))
                verified = verify_client_origins(origins.read_text(), class_bindings(mapping, wanted), client)
                result.update(status="complete", ticks=verification["ticks"], verification=verification, source_audit=audit, measurement_sha256=sha256(output), client_sha256=sha256(downloads / "client.jar"), server_sha256=sha256(downloads / "server.jar"), preservation=preservation, native_client_classes=verified, adapters={path.name: sha256(path) for path in adapters.glob("*.java")})
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            result["error"] = str(error)
        results.append(result)
        write_json(root / "results.json", results)
        print(version, result["status"], result.get("error", ""), flush=True)
    if any(row["status"] != "complete" for row in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
