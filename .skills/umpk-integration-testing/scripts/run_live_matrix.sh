#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/../../.." && pwd)
official_root=${UMPK_ORACLE_ROOT:-$repo_root/MinecraftOfficial}
server_root=${UMPK_SERVER_ROOT:-$official_root/downloads}
output=${UMPK_LIVE_LOG:-/tmp/umpk-live-matrix.log}

cd "$repo_root"
"$script_dir/preflight.sh"

export UMPK_NIGHTLY=1
export UMPK_SERVER_ROOT="$server_root"

before_pids=$(mktemp /tmp/umpk-server-pids-before.XXXXXX)
after_pids=$(mktemp /tmp/umpk-server-pids-after.XXXXXX)
cleanup_snapshots() {
    rm -f "$before_pids" "$after_pids"
}
trap cleanup_snapshots EXIT
pgrep -f 'server\.jar.*nogui' >"$before_pids" || true

set +e
dotnet test tests/Umpk.IntegrationTests/Umpk.IntegrationTests.csproj \
    -c Release --no-build --filter 'Category=Nightly' 2>&1 | tee "$output"
test_status=${PIPESTATUS[0]}
set -e

pgrep -f 'server\.jar.*nogui' >"$after_pids" || true
leaked=0
while IFS= read -r pid; do
    if ! grep -Fxq "$pid" "$before_pids"; then
        printf 'live-matrix: new server process remained after the run: pid=%s\n' "$pid" >&2
        leaked=1
    fi
done <"$after_pids"

if [ "$test_status" -ne 0 ] || [ "$leaked" -ne 0 ]; then
    exit 1
fi

printf 'live-matrix: complete; raw log is outside the repository: %s\n' "$output"
