#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/../../.." && pwd)
official_root=${UMPK_ORACLE_ROOT:-$repo_root/MinecraftOfficial}
server_root=${UMPK_SERVER_ROOT:-$official_root/downloads}

for command in dotnet python3 java git pgrep grep; do
    command -v "$command" >/dev/null || {
        printf 'preflight: missing command: %s\n' "$command" >&2
        exit 1
    }
done

[ -f "$repo_root/UMPK.sln" ] || {
    printf 'preflight: not a UMPK checkout: %s\n' "$repo_root" >&2
    exit 1
}

mkdir -p "$server_root"
printf 'repo=%s\nofficial_root=%s\nserver_root=%s\n' "$repo_root" "$official_root" "$server_root"
printf 'dotnet=%s\njava=%s\n' "$(dotnet --version)" "$(java -version 2>&1 | sed -n '1p')"
