#!/usr/bin/env bash
# Provision vanilla artifacts and source in the shared external oracle directory.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 "$script_dir/minecraft-official/official.py" "$@"
