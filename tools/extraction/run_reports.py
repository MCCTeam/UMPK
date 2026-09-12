#!/usr/bin/env python3
"""Report runner: `java ... --reports` for one version into a cache directory.

This thin wrapper over `umpk_extract.run_reports` gives the report step a stable command in the current-version extraction pipeline. Older releases without complete reports are retained only as committed datasets.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import umpk_extract as ux


def main() -> int:
    parser = argparse.ArgumentParser(description="Run Mojang server data reports")
    parser.add_argument("version", help="version name, e.g. 1.21.5")
    parser.add_argument("--out", required=True, help="cache directory for reports")
    args = parser.parse_args()

    reports = ux.run_reports(args.version, Path(args.out))
    print(f"reports for {args.version} -> {reports}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
