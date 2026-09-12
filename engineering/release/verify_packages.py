#!/usr/bin/env python3
"""Verify the complete UMPK NuGet release artifact set."""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree


PACKAGE_IDS = frozenset(
    {
        "Umpk",
        "Umpk.Auth",
        "Umpk.Client",
        "Umpk.Commands",
        "Umpk.Core",
        "Umpk.Data.Java",
        "Umpk.Data.Lang",
        "Umpk.Game",
        "Umpk.Nbt",
        "Umpk.Pathfinding",
        "Umpk.Physics",
        "Umpk.Protocol.Java",
        "Umpk.Realms",
        "Umpk.Text",
    }
)

INTERNAL_DEPENDENCIES = {
    "Umpk": {"Umpk.Auth", "Umpk.Client", "Umpk.Data.Java", "Umpk.Data.Lang", "Umpk.Pathfinding", "Umpk.Physics"},
    "Umpk.Auth": {"Umpk.Core", "Umpk.Protocol.Java"},
    "Umpk.Client": {"Umpk.Commands", "Umpk.Core", "Umpk.Data.Java", "Umpk.Game", "Umpk.Pathfinding", "Umpk.Physics", "Umpk.Protocol.Java", "Umpk.Text"},
    "Umpk.Commands": {"Umpk.Core", "Umpk.Text"},
    "Umpk.Core": set(),
    "Umpk.Data.Java": {"Umpk.Protocol.Java"},
    "Umpk.Data.Lang": {"Umpk.Text"},
    "Umpk.Game": {"Umpk.Core", "Umpk.Nbt", "Umpk.Text"},
    "Umpk.Nbt": {"Umpk.Core"},
    "Umpk.Pathfinding": {"Umpk.Core", "Umpk.Game", "Umpk.Physics"},
    "Umpk.Physics": {"Umpk.Core", "Umpk.Game"},
    "Umpk.Protocol.Java": {"Umpk.Core", "Umpk.Game", "Umpk.Nbt", "Umpk.Text"},
    "Umpk.Realms": {"Umpk.Auth"},
    "Umpk.Text": {"Umpk.Core", "Umpk.Nbt"},
}


def element_text(parent: ElementTree.Element, name: str) -> str:
    element = parent.find(f"{{*}}{name}")
    return "" if element is None or element.text is None else element.text.strip()


def verify_primary(package: Path, version: str, errors: list[str]) -> str | None:
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
        nuspec_names = [name for name in names if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            errors.append(f"{package.name}: expected one .nuspec, found {len(nuspec_names)}")
            return None

        root = ElementTree.fromstring(archive.read(nuspec_names[0]))
        metadata = root.find("{*}metadata")
        if metadata is None:
            errors.append(f"{package.name}: .nuspec has no metadata element")
            return None

        package_id = element_text(metadata, "id")
        actual_version = element_text(metadata, "version")
        if package_id not in PACKAGE_IDS:
            errors.append(f"{package.name}: unexpected package id {package_id!r}")
        if actual_version != version:
            errors.append(f"{package.name}: version {actual_version!r}, expected {version!r}")

        required_values = {
            "authors": "UMPK contributors",
            "projectUrl": "https://github.com/MCCTeam/UMPK",
            "readme": "README.md",
            "releaseNotes": f"https://github.com/MCCTeam/UMPK/releases/tag/v{version}",
        }
        for name, expected in required_values.items():
            actual = element_text(metadata, name)
            if actual != expected:
                errors.append(f"{package.name}: {name} is {actual!r}, expected {expected!r}")

        description = element_text(metadata, "description")
        if not description or description == "Universal Minecraft Protocol Kit":
            errors.append(f"{package.name}: package-specific description is missing")

        tags = set(element_text(metadata, "tags").split())
        if not {"minecraft", "minecraft-java", "protocol"}.issubset(tags):
            errors.append(f"{package.name}: required search tags are missing")

        license_element = metadata.find("{*}license")
        if (
            license_element is None
            or license_element.attrib.get("type") != "expression"
            or (license_element.text or "").strip() != "MIT"
        ):
            errors.append(f"{package.name}: expected MIT SPDX license expression")

        repository = metadata.find("{*}repository")
        if repository is None:
            errors.append(f"{package.name}: repository metadata is missing")
        else:
            if repository.attrib.get("type") != "git":
                errors.append(f"{package.name}: repository type is not git")
            if repository.attrib.get("url") != "https://github.com/MCCTeam/UMPK.git":
                errors.append(f"{package.name}: repository URL is incorrect")
            if not repository.attrib.get("commit"):
                errors.append(f"{package.name}: repository commit is missing")

        if "README.md" not in names:
            errors.append(f"{package.name}: package README.md is missing")

        library_prefix = "lib/net10.0/"
        library_files = {name for name in names if name.startswith(library_prefix)}
        if package_id == "Umpk":
            if library_files != {f"{library_prefix}_._"}:
                errors.append(f"{package.name}: meta package must contain only the net10.0 _._ placeholder")
        else:
            for extension in ("dll", "xml"):
                expected = f"{library_prefix}{package_id}.{extension}"
                if expected not in names:
                    errors.append(f"{package.name}: missing {expected}")

        actual_internal_dependencies: set[str] = set()
        for dependency in metadata.findall(".//{*}dependency"):
            dependency_id = dependency.attrib.get("id", "")
            dependency_version = dependency.attrib.get("version", "")
            if dependency_id in PACKAGE_IDS:
                actual_internal_dependencies.add(dependency_id)
                if version not in dependency_version:
                    errors.append(
                        f"{package.name}: internal dependency {dependency_id} uses {dependency_version!r}, "
                        f"which does not reference release {version}"
                    )

        expected_internal_dependencies = INTERNAL_DEPENDENCIES.get(package_id, set())
        if actual_internal_dependencies != expected_internal_dependencies:
            errors.append(
                f"{package.name}: internal dependencies are {sorted(actual_internal_dependencies)}, "
                f"expected {sorted(expected_internal_dependencies)}"
            )

        return package_id


def verify_symbols(package: Path, package_id: str, version: str, errors: list[str]) -> None:
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
        nuspec_names = [name for name in names if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            errors.append(f"{package.name}: expected one .nuspec, found {len(nuspec_names)}")
            return

        root = ElementTree.fromstring(archive.read(nuspec_names[0]))
        metadata = root.find("{*}metadata")
        if metadata is None:
            errors.append(f"{package.name}: .nuspec has no metadata element")
            return
        if element_text(metadata, "id") != package_id:
            errors.append(f"{package.name}: symbol package id does not match {package_id}")
        if element_text(metadata, "version") != version:
            errors.append(f"{package.name}: symbol package version does not match {version}")

        expected_pdb = f"lib/net10.0/{package_id}.pdb"
        if expected_pdb not in names:
            errors.append(f"{package.name}: missing {expected_pdb}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()

    primary_packages = sorted(
        path for path in args.packages.glob("*.nupkg") if not path.name.endswith(".snupkg")
    )
    symbol_packages = sorted(args.packages.glob("*.snupkg"))
    errors: list[str] = []
    ids_by_package: dict[str, Path] = {}

    for package in primary_packages:
        package_id = verify_primary(package, args.version, errors)
        if package_id is not None:
            if package_id in ids_by_package:
                errors.append(f"duplicate package id {package_id}: {ids_by_package[package_id]} and {package}")
            ids_by_package[package_id] = package

    actual_ids = set(ids_by_package)
    for package_id in sorted(PACKAGE_IDS - actual_ids):
        errors.append(f"missing primary package {package_id}")
    for package_id in sorted(actual_ids - PACKAGE_IDS):
        errors.append(f"unexpected primary package {package_id}")

    symbols_by_stem = {
        package.name.removesuffix(f".{args.version}.snupkg"): package for package in symbol_packages
    }
    for package_id in sorted(PACKAGE_IDS - {"Umpk"}):
        symbol_package = symbols_by_stem.get(package_id)
        if symbol_package is None:
            errors.append(f"missing symbol package {package_id}")
        else:
            verify_symbols(symbol_package, package_id, args.version, errors)

    unexpected_symbols = set(symbols_by_stem) - (PACKAGE_IDS - {"Umpk"})
    for package_id in sorted(unexpected_symbols):
        errors.append(f"unexpected symbol package {package_id}")

    if errors:
        print("NuGet artifact verification failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        f"Verified {len(primary_packages)} primary packages and {len(symbol_packages)} symbol packages "
        f"for UMPK {args.version}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
