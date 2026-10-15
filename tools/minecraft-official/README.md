# Shared vanilla artifacts

These helpers adapt the former MCC `tools/decompile.sh` and local server workflow for UMPK, DMCBK, and MCC 2.0. The scripts in each checkout are standalone. They use downloaded artifacts rather than sibling source projects. Keep the copies of `tools/minecraft-official/`, `tools/decompile.sh`, and `tools/start-server.sh` synchronized when changing their common behavior.

Use Python 3.12 or later. Run the server launcher on Linux or macOS, or use WSL on Windows. Automatic Temurin installation supports Linux x64 and ARM64. Other platforms can supply a compatible Java executable with `--java`.

## Artifact root

The default is `~/Projects/MinecraftOfficial`. This workstation uses `/home/anon/Projects/MinecraftOfficial`. Set `MINECRAFT_OFFICIAL_ROOT` or pass `--root` to select another location. `UMPK_ORACLE_ROOT` is also accepted. Keep this directory outside tracked repository content.

```text
MinecraftOfficial/
├── MinecraftDecompiler.jar
├── downloads/<version>/
│   ├── version.json
│   ├── artifacts.json
│   ├── server.jar
│   ├── client.jar
│   ├── server_mappings.txt   # when Mojang publishes them
│   ├── client_mappings.txt   # when Mojang publishes them
│   ├── eula.txt
│   └── server.properties
├── runtimes/java11/bin/java
├── runtimes/java25/bin/java
├── <version>-decompiled/
├── <version>-client-decompiled/
└── logs/
```

`downloads/<version>` is a reusable server seed. Start each live run in a fresh directory. Do not boot a server inside a shared seed or alter another run's world, lock, process, or port.

## Download and decompile

Download every stable release from 1.8 through 26.3, including patches. Snapshots and prereleases are excluded.

```bash
bash tools/decompile.sh --all-releases --through 26.3 --side both --download-only --install-runtimes --accept-eula
bash tools/decompile.sh --all-releases --through 26.3
bash tools/decompile.sh --version 1.21.11 --side client
bash tools/decompile.sh --all-releases --through 26.3 --verify
```

Use `--accept-eula` only after accepting the Minecraft EULA. Without that flag, provisioning preserves an existing acceptance and writes `eula=false` for a new seed.

The provisioner checks Mojang's SHA-1 for metadata, jars, and available mappings. `artifacts.json` records URLs, sizes, SHA-1, and SHA-256. The verification command checks both sides and all available mappings, so download both sides before using it. Private Temurin runtimes use the publisher's SHA-256. Tool downloads record their source release and SHA-256. MinecraftDecompiler v3.3.2 does not publish an asset digest, so its recorded SHA-256 detects later local changes without independently authenticating the initial download.

MinecraftDecompiler remaps releases with official Proguard mappings. Vineflower 1.12.0 decompiles Minecraft and Mojang classes from the remapped jar. Embedded third-party libraries remain in the original artifacts and provide classpath information. They are excluded from source generation. Releases from 26.1 use Vineflower directly on unobfuscated jars. Earlier releases without official mappings also use Vineflower directly, but their source retains obfuscated names. Their completion records say `obfuscated-no-community-mappings`. A readable MCP or community-mapped legacy tree remains a separate prerequisite for checks that require named classes. Do not treat an unmapped tree as that prerequisite.

Each completed source tree has `.complete.json` with the input hash, mapping route, tool versions, Java file count, and log path. Existing directories without valid completion evidence cause an error. Inspect incomplete trees before moving them aside. A decompiler heap failure prevents completion even if its process reports success. Other decompiler warnings remain in the logs. Decompiled source supports inspection and does not prove protocol conformance.

## Run a local server

```bash
bash tools/start-server.sh 1.21.11 --port 26480 --rcon-port 26481
bash tools/start-server.sh 26.3 --port 26482 --rcon-port 26483 --smoke
```

The first command stays in the foreground and accepts server console commands on stdin. Send `stop` or press Ctrl+C to stop its owned process. The second command waits for the readiness message, a TCP connection, and an authenticated RCON `list` response, then stops. This proves server infrastructure, not client behavior.

The launcher uses a new `/tmp/minecraft-<version>-*` directory by default. `--run-dir /absolute/new/directory` selects a fresh explicit directory and fails if it already exists. Logs, worlds, `ready.json`, and `stopped.json` remain there for inspection. The server listens on loopback, uses offline authentication, and caps its heap at 1 GiB. RCON receives a random password in `rcon-password.txt`. Select unused, distinct game and RCON ports. The launcher refuses occupied ports and does not stop their owners.

```bash
python3 tools/minecraft-official/server.py --rcon-port 26481 --password-file /absolute/run/rcon-password.txt --rcon-command 'say local test'
```

The launcher selects Java 11 through 1.16.5 and Java 25 from 1.17 onward. `--java`, `UMPK_JAVA11`, and `UMPK_JAVA25` can override the selected executables. `UMPK_JAVA` overrides both eras. Use version-compatible installed runtimes when this workstation's private runtimes are unavailable.

## UMPK live harness

```bash
export UMPK_ORACLE_ROOT="$HOME/Projects/MinecraftOfficial"
export UMPK_SERVER_ROOT="$UMPK_ORACLE_ROOT/downloads"
export UMPK_JAVA11="$UMPK_ORACLE_ROOT/runtimes/java11/bin/java"
export UMPK_JAVA25="$UMPK_ORACLE_ROOT/runtimes/java25/bin/java"
```

UMPK's live harness copies these seeds into its own temporary directories and owns its server processes. DMCBK and MCC can use the same artifacts, but each consumer must verify its own packages and binaries.

## Offline checks

```bash
python3 -m unittest discover -s tools/minecraft-official -p 'test_*.py'
bash -n tools/decompile.sh tools/start-server.sh
```

The tests cover failed-download preservation, artifact hashes, release selection, bundled jar extraction, and truncated RCON frames. The socket tests require local socket access.
