#!/usr/bin/env bash
# Resolve a vanilla citation to a file on disk and open it.
#
#   tools/oracle.sh 1.21.5 ClientboundLevelChunkPacketData.java
#   tools/oracle.sh 1.21.5 ClientboundLevelChunkPacketData.java:36
#   tools/oracle.sh 1.21.5 net/minecraft/network/protocol/game/ClientboundLevelChunkPacket.java
#   tools/oracle.sh 1.20.6-client Component.java
#
# Citations in this repository are written "<version>-decompiled <Class>.java:<line>" and carry no path, because the decompiled trees are untracked local artifacts. This resolves that pair against UMPK_ORACLE_ROOT, defaulting to the repository-local MinecraftOfficial directory documents, and hands the file to $VISUAL/$EDITOR (or $PAGER) when one is configured. With neither set it prints the path, which is what a script wants.
#
# Exit codes: 0 resolved, 1 not resolved, 2 bad usage.

set -euo pipefail

usage() {
    printf 'usage: %s <version> <Class.java[:line]>\n' "$0" >&2
    printf '       version is "1.21.5" or "1.20.6-client"; the "-decompiled" suffix is optional.\n' >&2
    exit 2
}

[ $# -eq 2 ] || usage

version=$1
citation=$2

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(dirname -- "$script_dir")
default_root=$repo_root/MinecraftOfficial
root=${UMPK_ORACLE_ROOT:-$default_root}

if [ ! -d "$root" ]; then
    printf 'oracle: no decompiled root at %s. Provision it or set UMPK_ORACLE_ROOT.\n' "$root" >&2
    exit 1
fi

case $version in
    *-decompiled) tree=$root/$version ;;
    *) tree=$root/$version-decompiled ;;
esac

if [ ! -d "$tree" ]; then
    printf 'oracle: no tree %s. Available:\n' "$tree" >&2
    (cd "$root" && ls -d -- *-decompiled 2>/dev/null | sed 's/^/  /') >&2
    exit 1
fi

file=${citation%%:*}
line=${citation#"$file"}
line=${line#:}
line=${line%%-*}

matches=()
if [ -f "$tree/$file" ]; then
    matches=("$tree/$file")
else
    # A citation names the class, not the path, so the basename is the only thing that always resolves. Elided paths (".../item/EnchantmentHelper.java") reduce to the same lookup.
    while IFS= read -r found; do
        matches+=("$found")
    done < <(find -P "$tree" -type f -name "$(basename -- "$file")" | sort)
fi

if [ ${#matches[@]} -eq 0 ]; then
    printf 'oracle: %s has no %s.\n' "$(basename -- "$tree")" "$file" >&2
    exit 1
fi

for match in "${matches[@]}"; do
    printf '%s%s\n' "$match" "${line:+:$line}"
done

target=${matches[0]}
editor=${VISUAL:-${EDITOR:-}}
if [ -n "$editor" ]; then
    if [ -n "$line" ]; then
        exec "$editor" "+$line" "$target"
    fi
    exec "$editor" "$target"
fi

if [ -n "${PAGER:-}" ] && [ -t 1 ]; then
    exec "$PAGER" "$target"
fi
