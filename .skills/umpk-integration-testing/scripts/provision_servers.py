#!/usr/bin/env python3
"""Download official Minecraft Java artifacts into the ignored local oracle tree."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import urllib.request
from pathlib import Path

MANIFEST_URL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"


def read_json(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def download(url: str, destination: Path, expected_sha1: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".part")
    digest = hashlib.sha1()
    with urllib.request.urlopen(url, timeout=120) as response, temporary.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
            digest.update(chunk)
    actual = digest.hexdigest()
    if actual.lower() != expected_sha1.lower():
        temporary.unlink(missing_ok=True)
        raise RuntimeError(f"SHA-1 mismatch for {destination.name}: expected {expected_sha1}, got {actual}")
    temporary.replace(destination)


def ensure_download(entry: dict, destination: Path) -> None:
    expected = entry["sha1"]
    if destination.is_file():
        actual = hashlib.sha1(destination.read_bytes()).hexdigest()
        if actual.lower() == expected.lower():
            print(f"reuse {destination}")
            return
    download(entry["url"], destination, expected)
    print(f"downloaded {destination}")


def default_root() -> Path:
    repo_root = Path(__file__).resolve().parents[3]
    configured = os.environ.get("UMPK_ORACLE_ROOT")
    return Path(configured).expanduser().resolve() if configured else repo_root / "MinecraftOfficial"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("versions", nargs="+", help="Mojang version IDs, such as 1.20.4 or 26.2")
    parser.add_argument("--root", type=Path, default=default_root())
    parser.add_argument("--client", action="store_true", help="also download the client jar and mappings")
    parser.add_argument("--accept-eula", action="store_true", help="write eula=true for local server tests")
    args = parser.parse_args()

    root = args.root.expanduser().resolve()
    manifest = read_json(MANIFEST_URL)
    versions = {entry["id"]: entry["url"] for entry in manifest["versions"]}

    for version in args.versions:
        metadata_url = versions.get(version)
        if metadata_url is None:
            print(f"unknown Mojang version: {version}", file=sys.stderr)
            return 2
        metadata = read_json(metadata_url)
        target = root / "downloads" / version
        downloads = metadata.get("downloads", {})
        ensure_download(downloads["server"], target / "server.jar")
        if "server_mappings" in downloads:
            ensure_download(downloads["server_mappings"], target / "server_mappings.txt")
        if args.client:
            ensure_download(downloads["client"], target / "client.jar")
            if "client_mappings" in downloads:
                ensure_download(downloads["client_mappings"], target / "client_mappings.txt")
        if args.accept_eula:
            (target / "eula.txt").write_text("eula=true\n", encoding="utf-8")

    print(f"official_root={root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
