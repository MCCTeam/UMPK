"""Measure block-use inheritance and default placement contracts from matching native client and server jars."""

import argparse
import io
import json
from pathlib import Path
import tempfile
from urllib.parse import unquote, urlsplit
import zipfile

from measure_legacy_targeting import run_checked, sha256, unsigned_client, verify_artifact, write_json


def unpack_jar(archive: zipfile.ZipFile, destination: Path) -> None:
    for entry in archive.infolist():
        name = entry.filename.upper()
        if name.startswith("META-INF/") and (name == "META-INF/MANIFEST.MF" or name.endswith((".SF", ".RSA", ".DSA", ".EC")) or name.startswith("META-INF/SIG-")):
            continue
        target = destination / entry.filename
        if not target.resolve().is_relative_to(destination.resolve()):
            raise ValueError(f"Unsafe jar entry: {entry.filename}")
        if entry.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(entry))


def verify_client_origins(log: str, classes: dict[str, str], client: Path) -> dict[str, str]:
    for name, runtime in classes.items():
        prefix = f"[class,load] {runtime} source: "
        origins = [line.split(prefix, 1)[1] for line in log.splitlines() if prefix in line]
        if len(origins) != 1 or not origins[0].startswith("file:") or Path(unquote(urlsplit(origins[0]).path)).resolve() != client.resolve():
            raise ValueError(f"Native {name} did not load from the client artifact")
    return classes


def class_bindings(mapping: Path, wanted: set[str] | None = None) -> dict[str, str]:
    if wanted is None:
        wanted = {"net.minecraft.world.level.block.Block", "net.minecraft.world.level.block.state.BlockState",
                  "net.minecraft.world.item.Item", "net.minecraft.world.item.BlockItem"}
    if not mapping.exists():
        return {name: name for name in wanted}
    found = {}
    for line in mapping.read_text().splitlines():
        if not line.startswith((" ", "#")) and line.endswith(":") and " -> " in line:
            name, runtime = line[:-1].split(" -> ")
            if name in wanted:
                found[name] = runtime
    if set(found) != wanted:
        raise ValueError("Missing required interaction classes in client mappings")
    return found


def interaction_catalog(catalog: list[dict], official: Path) -> list[dict]:
    known = {entry["name"]: entry["protocol"] for entry in catalog}
    names = set(known)
    if (official / "versions.json").exists():
        names.update(entry["id"] for entry in json.loads((official / "versions.json").read_text()))
    selected = []
    for name in sorted(names, key=lambda value: tuple(int(part) if part.isdigit() else -1 for part in value.split("."))):
        if not all(part.isdigit() for part in name.split(".")):
            continue
        downloads = official / "downloads" / name
        if not (downloads / "server_mappings.txt").exists() and not name.startswith("26."):
            continue
        with zipfile.ZipFile(downloads / "client.jar") as client:
            embedded = json.loads(client.read("version.json"))
        protocol = embedded["protocol_version"]
        if embedded["id"] != name or type(protocol) is not int:
            raise ValueError(f"Invalid embedded client version: {name}")
        if name in known and known[name] != protocol:
            raise ValueError(f"Dataset protocol differs from native client: {name}")
        if protocol in known.values():
            selected.append({"name": name, "protocol": protocol})
    return selected


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--measurements", type=Path, required=True, help="Fresh directory outside the source checkout")
    parser.add_argument("--data", type=Path, default=repo / "data/java")
    parser.add_argument("--versions", nargs="+")
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    official, destination = args.official.resolve(), args.measurements.resolve()
    if destination.is_relative_to(repo):
        raise ValueError("Native measurements and logs must stay outside the source checkout")
    if not 0 < args.timeout <= 300:
        raise ValueError("Native timeout must be in (0, 300] seconds")
    catalog = interaction_catalog(json.loads((args.data / "versions.json").read_text())["versions"], official)
    if args.versions:
        if set(args.versions) - {entry["name"] for entry in catalog}:
            raise ValueError("Requested release has no supported native interaction bindings")
        catalog = [entry for entry in catalog if entry["name"] in args.versions]
    destination.mkdir(parents=True, exist_ok=False)
    classes = destination / "classes"
    classes.mkdir()
    extraction = repo / "tools/extraction/shape-extractor"
    helpers = [extraction / (name + ".java") for name in ("PushDump", "MiningDump", "BlockInteractionDump")]
    javac = official / "runtimes/java11/bin/javac"
    run_checked([str(javac), "--release", "11", "-d", str(classes), *map(str, helpers)], destination, destination / "compile.log", args.timeout)
    write_json(destination / "measurement-tools.json", {"source_sha256": {path.name: sha256(path) for path in helpers}})
    reports: dict[str, list] = {"probe": [], "client": []}
    for entry in catalog:
        version = entry["name"]
        directory = destination / version
        directory.mkdir()
        rows = {side: {"version": version, "status": "failed"} for side in reports}
        try:
            downloads = official / "downloads" / version
            metadata = json.loads((downloads / "version.json").read_text())
            server, client = downloads / "server.jar", downloads / "client.jar"
            for side, artifact in (("server", server), ("client", client)):
                verify_artifact(artifact, metadata["downloads"][side]["sha1"])
            for side in ("server", "client"):
                mapping = downloads / (side + "_mappings.txt")
                if side + "_mappings" in metadata["downloads"]:
                    verify_artifact(mapping, metadata["downloads"][side + "_mappings"]["sha1"])
            with tempfile.TemporaryDirectory(prefix="native-cp-", dir=destination) as temporary:
                cp = Path(temporary)
                with zipfile.ZipFile(server) as bundled:
                    jars = [name for name in bundled.namelist() if name.endswith(".jar") and name.startswith(("META-INF/versions/", "META-INF/libraries/"))]
                    if jars:
                        for name in jars:
                            with zipfile.ZipFile(io.BytesIO(bundled.read(name))) as archive:
                                unpack_jar(archive, cp)
                    else:
                        unpack_jar(bundled, cp)
                unsigned = cp / "client-unsigned.jar"
                preservation = unsigned_client(client, unsigned)
                legacy_jdk = not version.startswith("26.") and tuple(map(int, version.split("."))) < (1, 17)
                java = official / "runtimes" / ("java11" if legacy_jdk else "java25") / "bin/java"
                shim = directory / "shim"
                if version in ("1.17", "1.17.1", "1.18", "1.18.1"):
                    shim.mkdir()
                    run_checked([str(javac), "--release", "11", "-cp", str(cp), "-d", str(shim), str(extraction / "log4j-shim/LogManager.java")], directory, directory / "shim-compile.log", args.timeout)
                measured = {}
                for side, native_side in (("probe", "server"), ("client", "client")):
                    prefix = "block-interaction-" + side
                    mapping = downloads / (native_side + "_mappings.txt")
                    paths = [classes, *([unsigned] if side == "client" else []), cp]
                    if shim.exists():
                        paths.insert(0, shim)
                    class_log = directory / (prefix + "-classes.log")
                    output = directory / (prefix + ".json")
                    run_checked([str(java), "-Xmx1G", "-XX:-UsePerfData", "-Xlog:class+load=info:file=" + str(class_log), "-cp", ":".join(map(str, paths)), "BlockInteractionDump", str(mapping) if mapping.exists() else "none", str(cp), str(output)], directory, directory / (prefix + ".log"), args.timeout)
                    measured[side] = json.loads(output.read_text())
                    rows[side].update(status="complete", server_sha1=metadata["downloads"]["server"]["sha1"], server_sha256=sha256(server),
                                      states=len(measured[side]["states"]), items=len(measured[side]["items"]), measurement_sha256=sha256(output), java=str(java))
                    if side == "client":
                        origins = verify_client_origins(class_log.read_text(), class_bindings(mapping), unsigned)
                        rows[side].update(client_sha256=sha256(client), preservation=preservation, native_client_classes=origins)
                if measured["probe"] != measured["client"]:
                    raise ValueError("Native client/server interaction contracts differ")
                rows["client"]["client_server_equal"] = True
            print(f"{version}: native client/server contracts equal, {rows['client']['states']} states, {rows['client']['items']} items", flush=True)
        except Exception as error:
            for row in rows.values():
                row.update(status="failed", error=str(error))
            print(f"{version}: FAILED: {error}", flush=True)
        for side, row in rows.items():
            reports[side].append(row)
            write_json(destination / ("block-interaction-" + side + "-results.json"), reports[side])
    if any(row["status"] != "complete" for rows in reports.values() for row in rows):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
