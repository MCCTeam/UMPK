# Local vanilla artifacts and source

Use `/home/anon/Projects/MinecraftOfficial` on this workstation. The portable default is `~/Projects/MinecraftOfficial`. Read `tools/minecraft-official/README.md` from the repository root for hashes, mapping routes, private JVMs, decompiler warnings, and isolated server runs.

```bash
export UMPK_ORACLE_ROOT="$HOME/Projects/MinecraftOfficial"
export UMPK_SERVER_ROOT="$UMPK_ORACLE_ROOT/downloads"
export UMPK_JAVA11="$UMPK_ORACLE_ROOT/runtimes/java11/bin/java"
export UMPK_JAVA25="$UMPK_ORACLE_ROOT/runtimes/java25/bin/java"
bash tools/decompile.sh --version 1.21.11 --side both --accept-eula
```

Use `--accept-eula` only with EULA acceptance. Official jars and available mappings live in `downloads/<version>/`. Server source lives in `<version>-decompiled/`. Client source lives in `<version>-client-decompiled/` when requested. Check `artifacts.json` and `.complete.json` before citing evidence. Never commit official artifacts or source.

MinecraftDecompiler remaps official Proguard mappings. Vineflower decompiles the remapped jar or an already unobfuscated jar. Releases without official mappings retain obfuscated names and carry the `obfuscated-no-community-mappings` label. Do not use them as readable MCP source. Protocol 47's named legacy oracle still requires a compatible community-mapped 1.8.9 tree at `<root>/1.8.9/src/minecraft/`.

Use `tools/oracle.sh <version> <Class.java[:line]>` to resolve source citations. Pass `--oracle "$UMPK_ORACLE_ROOT"` to DataGen layout comparisons. The existing live harness copies `downloads/<version>` seeds into temporary directories. Do not boot or mutate the shared seeds during live tests.
