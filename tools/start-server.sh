#!/usr/bin/env bash
# Start an isolated local server. Stop only the process owned by this invocation.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 "$script_dir/minecraft-official/server.py" "$@"
