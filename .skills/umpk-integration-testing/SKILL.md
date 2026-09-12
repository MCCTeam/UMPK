---
name: umpk-integration-testing
description: Run UMPK end-to-end tests against isolated local vanilla Minecraft Java servers. Use for live protocol, lifecycle, movement, inventory, registry, proxy, or version-matrix validation where unit tests are insufficient.
metadata:
  compatibility: Linux or macOS with .NET 10, Python 3, compatible JDKs, and local disk space for untracked vanilla artifacts.
---

# UMPK integration testing

Use the repository's `Umpk.IntegrationTests` live harness. It boots one isolated server per test, assigns private ports, caps the server heap at 1 GiB, waits for real listener readiness, and disposes the process after the leg. Do not substitute login success for feature validation.

## Read first

- Read `references/minecraft-official.md` before provisioning or decompiling game artifacts.
- Read `tests/Umpk.IntegrationTests/LiveMatrix.cs` for the 49 protocol representatives.
- Read the affected live test before running it. Its assertions define the claimed coverage.

## Workflow

1. Record `git rev-parse HEAD` and `git status --short`.
2. Run `scripts/preflight.sh`.
3. Provision the exact representative versions with `scripts/provision_servers.py`.
4. Build UMPK in Release mode.
5. Run the focused live leg with fresh output under `/tmp`.
6. Run the adjacent era boundary when the behavior is version-sensitive.
7. Run `scripts/run_live_matrix.sh` only after all 49 representatives are provisioned.
8. Confirm no server process or listener owned by the run remains.

Never use another checkout's binaries, server directory, Java source tree, or logs. Set `UMPK_SERVER_ROOT` and `UMPK_ORACLE_ROOT` explicitly when the repository-local defaults are not used.

## Location overrides

The official artifacts do not have to live inside this checkout:

```bash
export UMPK_ORACLE_ROOT=/absolute/path/to/MinecraftOfficial
export UMPK_SERVER_ROOT=/absolute/path/to/server-directories
python3 .skills/umpk-integration-testing/scripts/provision_servers.py \
  --root "$UMPK_ORACLE_ROOT" --accept-eula 26.2
```

`UMPK_ORACLE_ROOT` owns downloads, mappings, and `<version>-decompiled` source trees. `UMPK_SERVER_ROOT` owns the per-version directories consumed by the live Harness; omit it when those directories are at `$UMPK_ORACLE_ROOT/downloads`. `--root` overrides the provisioner's destination. Use absolute paths and keep both roots outside tracked repository content.

## Provisioning

```bash
python3 .skills/umpk-integration-testing/scripts/provision_servers.py \
  --accept-eula 1.8 1.12.2 1.16.5 1.20.4 1.21.11 26.2
```

The script downloads official artifacts, verifies Mojang's SHA-1, and writes them below the ignored `MinecraftOfficial/downloads/<version>/` tree. It never places artifacts in tracked paths.

## Running

```bash
dotnet build UMPK.sln -c Release
UMPK_NIGHTLY=1 \
UMPK_SERVER_ROOT="$PWD/MinecraftOfficial/downloads" \
dotnet test tests/Umpk.IntegrationTests/Umpk.IntegrationTests.csproj \
  -c Release --no-build --filter 'Category=Nightly'
```

The full feature theory requires every representative server directory. A missing directory is not a pass. For a focused test, use an xUnit fully-qualified-name filter and inspect which theory rows the runner actually executed.

## Evidence contract

Report these fields separately:

- **Executed:** commit, command, test name, versions, protocols, Java executables, and ports.
- **Observed:** assertions, client state, peer/server observations, and exit codes.
- **Inferred:** conclusions not directly demonstrated by that run.
- **Not run:** missing prerequisites, skipped rows, interrupted work, and adjacent versions.
- **Harness fault:** provisioning, Java, port, timeout, or cleanup failure.

A result is valid only when its assertion is caused by a fresh action in that leg. Prefer independent server-side observation for movement, inventory, entity, and disconnect behavior. Reproduce a failure in a fresh isolated run before attributing it to UMPK.

## Isolation rules

- Respect the system-wide process and memory limit declared for the run.
- Use unique directories and ports for concurrent work.
- Count foreign server processes toward the capacity limit, but never stop them.
- Do not modify a runner while it is active. Stop it, void the partial evidence, and rerun.
- Use bounded blocking waits. Do not poll long-running jobs at short intervals.
- Keep raw logs and downloaded artifacts outside Git.

## When not to use

Do not use this skill for build-only checks, static source comparison, documentation-only changes, or public-server testing. Public servers and authenticated accounts require explicit authorization.
