#!/usr/bin/env python3

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True, slots=True)
class PackageIdentity:
    revision: str
    version: str
    sha256: str
    sdk_version: str
    path: Path


@dataclass(frozen=True, slots=True)
class ExpectedPackageIdentity:
    revision: str
    version: str
    sha256: str
    sdk_version: str


def run(arguments: list[str], cwd: Path) -> str:
    completed = subprocess.run(
        arguments,
        cwd=cwd,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    return completed.stdout


def git_output(source: Path, *arguments: str) -> str:
    return run(["git", *arguments], source).strip()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def normalize_package(source: Path, destination: Path) -> None:
    fixed_properties_path = "package/services/metadata/core-properties/umpk.psmdcp"
    with zipfile.ZipFile(source, "r") as archive:
        entries: dict[str, bytes] = {}
        for name in archive.namelist():
            content = archive.read(name)
            if name.startswith("package/services/metadata/core-properties/"):
                entries[fixed_properties_path] = content
            else:
                entries[name] = content

    relationships = entries["_rels/.rels"].decode("utf-8")
    relationships = re.sub(
        r"/package/services/metadata/core-properties/[a-f0-9]+\.psmdcp",
        f"/{fixed_properties_path}",
        relationships,
    )
    relationships = re.sub(
        r'(Type="http://schemas\.openxmlformats\.org/package/2006/relationships/metadata/core-properties" Target="[^"]+" Id=")[^"]+(" />)',
        r"\1RUMPKCOREPROPERTIES\2",
        relationships,
    )
    entries["_rels/.rels"] = relationships.encode("utf-8")

    with zipfile.ZipFile(
        destination,
        "w",
        compression=zipfile.ZIP_DEFLATED,
        compresslevel=9,
    ) as archive:
        for name in sorted(entries):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, entries[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def expected_identity(manifest_path: Path) -> ExpectedPackageIdentity:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    package = manifest.get("raknetPackage")
    if not isinstance(package, dict):
        raise ValueError("source manifest has no raknetPackage object")
    revision = package.get("sourceRevision")
    if not isinstance(revision, str) or len(revision) != 40:
        raise ValueError("raknetPackage.sourceRevision must be a full Git revision")
    version = package.get("packageVersion")
    if not isinstance(version, str) or not version:
        raise ValueError("raknetPackage.packageVersion must be a non-empty string")
    package_hash = package.get("packageSha256")
    if not isinstance(package_hash, str) or not re.fullmatch(r"[a-f0-9]{64}", package_hash):
        raise ValueError("raknetPackage.packageSha256 must be a lowercase SHA-256 hash")
    sdk_version = package.get("sdkVersion")
    if not isinstance(sdk_version, str) or not sdk_version:
        raise ValueError("raknetPackage.sdkVersion must be a non-empty string")
    return ExpectedPackageIdentity(
        revision=revision,
        version=version,
        sha256=package_hash,
        sdk_version=sdk_version,
    )


def validate_checkout(source: Path, revision: str) -> None:
    actual = git_output(source, "rev-parse", "HEAD")
    if actual != revision:
        raise RuntimeError(f"RakNet revision mismatch: expected {revision}, found {actual}")
    if git_output(source, "status", "--porcelain"):
        raise RuntimeError("RakNet checkout must be clean")


def pack_once(source: Path, output: Path, revision: str, version: str) -> Path:
    run(
        [
            "dotnet",
            "pack",
            "RakNet.csproj",
            "-c",
            "Release",
            "--no-restore",
            "-o",
            str(output),
            f"-p:PackageVersion={version}",
            f"-p:RepositoryCommit={revision}",
            "-p:RepositoryUrl=https://github.com/MCCTeam/RakNet.git",
            "-p:ContinuousIntegrationBuild=true",
            "-p:Deterministic=true",
            "-warnaserror",
        ],
        source,
    )
    package = output / f"ArkMirage.RakNet.{version}.nupkg"
    if not package.is_file():
        raise RuntimeError(f"expected package was not produced: {package}")
    normalized = output / "normalized" / package.name
    normalized.parent.mkdir()
    normalize_package(package, normalized)
    return normalized


def build(source: Path, manifest: Path, feed: Path, version: str | None) -> PackageIdentity:
    expected = expected_identity(manifest)
    validate_checkout(source, expected.revision)
    if version is not None and version != expected.version:
        raise ValueError(
            f"package version mismatch: manifest requires {expected.version}, received {version}"
        )
    package_version = expected.version
    sdk_version = run(["dotnet", "--version"], source).strip()
    if sdk_version != expected.sdk_version:
        raise RuntimeError(
            f".NET SDK mismatch: expected {expected.sdk_version}, found {sdk_version}"
        )

    run(["dotnet", "restore", "RakNet.csproj", "-m:1", "-nr:false"], source)
    run(["dotnet", "build", "RakNet.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false", "-warnaserror"], source)
    run(["dotnet", "test", "tests/RakNet.Tests/RakNet.Tests.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false", "-warnaserror"], source)

    with (
        tempfile.TemporaryDirectory(prefix="umpk-raknet-pack-a-") as first_directory,
        tempfile.TemporaryDirectory(prefix="umpk-raknet-pack-b-") as second_directory,
    ):
        first = pack_once(source, Path(first_directory), expected.revision, package_version)
        second = pack_once(source, Path(second_directory), expected.revision, package_version)
        first_hash = sha256(first)
        second_hash = sha256(second)
        if first_hash != second_hash:
            raise RuntimeError(
                f"RakNet package is not reproducible: {first_hash} != {second_hash}"
            )
        if first_hash != expected.sha256:
            raise RuntimeError(
                f"RakNet package hash mismatch: expected {expected.sha256}, found {first_hash}"
            )

        feed.mkdir(parents=True, exist_ok=True)
        destination = feed / first.name
        shutil.copyfile(first, destination)

    return PackageIdentity(
        revision=expected.revision,
        version=package_version,
        sha256=first_hash,
        sdk_version=sdk_version,
        path=destination,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build and verify the pinned RakNet package for UMPK."
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=Path.home() / "Minecraft" / "RakNet",
        help="clean RakNet checkout",
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("data/bedrock/source-manifest.json"),
        help="UMPK source manifest",
    )
    parser.add_argument(
        "--feed",
        type=Path,
        default=Path("artifacts/bedrock-raknet-feed"),
        help="verified local package feed",
    )
    parser.add_argument("--version", help="explicit package version")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source = args.source.expanduser().resolve()
    manifest = args.manifest.resolve()
    feed = args.feed.resolve()
    identity = build(source, manifest, feed, args.version)
    print(
        json.dumps(
            {
                "packageId": "ArkMirage.RakNet",
                "packageVersion": identity.version,
                "packageSha256": identity.sha256,
                "sourceRevision": identity.revision,
                "sdkVersion": identity.sdk_version,
                "path": str(identity.path),
            },
            indent=2,
            sort_keys=True,
        )
    )


if __name__ == "__main__":
    main()
