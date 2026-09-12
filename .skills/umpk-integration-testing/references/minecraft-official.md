# Local vanilla artifacts and Java source

Keep official Minecraft artifacts below the repository-local, ignored `MinecraftOfficial/` directory:

```text
MinecraftOfficial/
├── downloads/<version>/server.jar
├── downloads/<version>/server_mappings.txt
├── downloads/<version>/client.jar
├── downloads/<version>/client_mappings.txt
├── <version>-decompiled/
└── <version>-client-decompiled/
```

Never commit jars, mappings, libraries, generated worlds, or decompiled Mojang source.

## Create and provision the directory

```bash
mkdir -p MinecraftOfficial/downloads
python3 .skills/umpk-integration-testing/scripts/provision_servers.py \
  --accept-eula --client 1.21.11
export UMPK_ORACLE_ROOT="$PWD/MinecraftOfficial"
export UMPK_SERVER_ROOT="$UMPK_ORACLE_ROOT/downloads"
```

The provisioner resolves Mojang's version manifest, downloads official artifacts, and verifies every download against the SHA-1 in the version metadata. Omit `--accept-eula` unless you accept Mojang's EULA.

## Override the artifact locations

Use an absolute oracle root when jars, mappings, and decompiled trees live outside this checkout:

```bash
export UMPK_ORACLE_ROOT=/absolute/path/to/MinecraftOfficial
python3 .skills/umpk-integration-testing/scripts/provision_servers.py \
  --root "$UMPK_ORACLE_ROOT" --accept-eula 26.2
```

The provisioner and extraction tools use `UMPK_ORACLE_ROOT`. The live Harness uses `UMPK_SERVER_ROOT`, whose default is `$UMPK_ORACLE_ROOT/downloads`. Override it independently when the runnable server directories are elsewhere:

```bash
export UMPK_SERVER_ROOT=/absolute/path/to/server-directories
```

Expected layouts are `$UMPK_ORACLE_ROOT/<version>-decompiled/` for source and `$UMPK_SERVER_ROOT/<version>/server.jar` for a runnable server.

## Decompile readable Java

Use a compatible JDK and Vineflower. Official Mojang mappings are available for modern releases. UMPK's remapper converts their named-to-obfuscated form before decompilation.

Place these untracked tools at:

```text
MinecraftOfficial/downloads/decompiler/vineflower.jar
MinecraftOfficial/downloads/libs/asm-9.6.jar
```

Then run the repository pipeline for the supported remap band:

```bash
export UMPK_ORACLE_ROOT="$PWD/MinecraftOfficial"
tools/extraction/decompile_1_16_to_1_18.sh 1.16.5 1.17.1 1.18.2
python3 tools/extraction/decompile.py 1.18.2
```

For another version with `server_mappings.txt`, follow the same stages:

1. If `server.jar` is a bundler, extract its inner server jar from `META-INF/versions/`.
2. Compile `tools/extraction/ProguardRemapper.java` with ASM and ASM Commons.
3. Remap the inner jar with that version's `server_mappings.txt`.
4. Run Vineflower on the remapped jar.
5. Store the result at `MinecraftOfficial/<version>-decompiled/`.
6. Run `python3 tools/extraction/decompile.py <version>` to verify discovery and mapping provenance.

For client-only packet behavior, repeat the process with `client.jar` and `client_mappings.txt`, then store the result at `MinecraftOfficial/<version>-client-decompiled/`.

Minecraft 1.8 through 1.14.3 require compatible community mappings because Mojang did not publish the modern mapping artifact for those releases. Protocol 47 expects the MCP 1.8.9 tree at `MinecraftOfficial/1.8.9/src/minecraft/`.

Use `tools/oracle.sh <version> <Class.java[:line]>` to resolve a source citation. Pass `--oracle "$UMPK_ORACLE_ROOT"` to DataGen layout comparisons.
