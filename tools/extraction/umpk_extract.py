#!/usr/bin/env python3
"""Shared helpers for the UMPK extraction drivers.

Stdlib-only. This module holds the plumbing every driver reuses: locating the
local server jars and decompiled trees, running the Mojang data-report
generator, and the small shared regex scrapers used by the extraction drivers.

See tools/extraction/README.md for the full pipeline and exact invocations.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

# Official artifacts are local, untracked inputs. Resolve the root from UMPK_ORACLE_ROOT or use the repository-local MinecraftOfficial directory.
def _resolve_official_root() -> Path:
    import os
    env = os.environ.get("UMPK_ORACLE_ROOT")
    return Path(env).expanduser().resolve() if env else Path(__file__).resolve().parents[2] / "MinecraftOfficial"


REPO_ROOT = Path(__file__).resolve().parents[2]
DECOMPILED_ROOT = _resolve_official_root()
DOWNLOADS = DECOMPILED_ROOT / "downloads"
SHAPE_BOOTSTRAP = REPO_ROOT / "tools" / "extraction" / "assets" / "block-shape-bootstrap.json"


def server_jar(version: str) -> Path:
    """Absolute path to a local server jar for a version name (e.g. "1.21.5")."""
    jar = DOWNLOADS / version / "server.jar"
    if not jar.exists():
        raise FileNotFoundError(f"server jar not found: {jar}")
    return jar


def decompiled_tree(version: str) -> Path | None:
    """Return an existing decompiled tree for a version, or None.

    The driver reuses these when present instead of re-decompiling: where the
    local decompiled trees already exist, the driver detects and reuses them.
    """
    for suffix in (f"{version}-decompiled", f"{version}"):
        candidate = DECOMPILED_ROOT / suffix
        if candidate.is_dir():
            return candidate
    return None


def _is_bundler_jar(jar: Path) -> bool:
    """Bundler-format server jars (1.18+) carry META-INF/versions.list; the data
    generator runs via -DbundlerMainClass. Plain jars (1.13-1.17) run the data
    generator as an explicit classpath main class instead."""
    import zipfile

    try:
        with zipfile.ZipFile(jar) as zf:
            return "META-INF/versions.list" in zf.namelist()
    except zipfile.BadZipFile:
        return False


def java_bin() -> str:
    """The Java executable to run the data generator with.

    Plain `java` on PATH is whatever the machine happens to default to, and 1.13-1.16.5 need an
    older runtime than a current default provides. `UMPK_JAVA_BIN` (or `JAVA_BIN`, which the live
    integration harness already sets) names it explicitly; PATH is the fallback.
    """
    import os

    return os.environ.get("UMPK_JAVA_BIN") or os.environ.get("JAVA_BIN") or "java"


def run_reports(version: str, out_dir: Path) -> Path:
    """Run `--reports` for a version, returning the reports directory.

    Java 1.13+ only; older versions have no data reports. Handles both
    the bundler jar layout (1.18+) and the plain jar layout (1.13-1.17).
    """
    jar = server_jar(version)
    out_dir.mkdir(parents=True, exist_ok=True)
    java = java_bin()
    if _is_bundler_jar(jar):
        cmd = [
            java,
            "-DbundlerMainClass=net.minecraft.data.Main",
            "-jar",
            str(jar),
            "--reports",
            "--output",
            str(out_dir),
        ]
    else:
        cmd = [
            java,
            "-cp",
            str(jar),
            "net.minecraft.data.Main",
            "--reports",
            "--output",
            str(out_dir),
        ]
    subprocess.run(
        cmd,
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    reports = out_dir / "reports"
    # Require the common report here and let each extractor validate the additional reports that it consumes.
    if not (reports / "blocks.json").exists():
        raise RuntimeError(f"report generation produced no blocks.json in {reports}")
    return reports


def find_java_file(version_dir: Path, *candidates: str) -> Path | None:
    for rel in candidates:
        full = version_dir / rel
        if full.exists():
            return full
    return None


# --- Shared regex scrapers ---


def extract_field_names(filepath: Path, pattern: str) -> list[str]:
    """Field names from `public static final` declarations matching a pattern."""
    results: list[str] = []
    with open(filepath, encoding="utf-8") as handle:
        for line in handle:
            match = re.match(pattern, line)
            if match:
                results.append(match.group(1))
    return results


def extract_register_calls(filepath: Path) -> list[str]:
    """Ordered `register("name", ...)` string args, multiline-tolerant."""
    with open(filepath, encoding="utf-8") as handle:
        flat = re.sub(r"\s+", " ", handle.read())
    return re.findall(r'(?:= |return )register\(\s*"([^"]+)"', flat)


def extract_serializer_order(filepath: Path) -> list[str]:
    """`registerSerializer(FIELD)` order from the static block of a Java file.

    This is the entity-metadata serializer wire order; it has no report
    source and must come from decompiled EntityDataSerializers.java.
    """
    results: list[str] = []
    in_static = False
    with open(filepath, encoding="utf-8") as handle:
        for line in handle:
            if "static {" in line:
                in_static = True
                continue
            if in_static and "registerSerializer(" in line:
                match = re.search(r"registerSerializer\((\w+)\)", line)
                if match:
                    results.append(match.group(1))
            if in_static and "}" in line and "registerSerializer" not in line and results:
                break
    return results


def write_json(path: Path, payload: object) -> None:
    """Deterministic, diffable JSON: 2-space indent, sorted where the caller
    already ordered lists, trailing newline. No em dash anywhere in output."""
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(payload, indent=2, ensure_ascii=False)
    if chr(0x2014) in text:  # em dash; forbidden by house rules
        raise ValueError("em dash (U+2014) present in extraction output; forbidden by house rules")
    path.write_text(text + "\n", encoding="utf-8")


def sorted_registry(entries: dict[str, dict]) -> list[dict]:
    """Turn a report registry ({name: {protocol_id}}) into an ordered list."""
    ordered = sorted(entries.items(), key=lambda kv: kv[1]["protocol_id"])
    return [{"name": name, "id": meta["protocol_id"]} for name, meta in ordered]


def die(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)
