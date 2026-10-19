# Direct mining and placement acceptance

`VanillaInteractionAcceptanceLiveTests` exercises survival mining and ordinary placement against an isolated vanilla server with physics enabled. It complements the general feature smoke test. The complete 75-release acceptance pack has not run, and the retained 1.9 missing-swing scenario is a production failure.

## Required observations

Each leg waits for usable terrain, a grounded player, survival mode and server-provided inventory. A diamond pickaxe must break stone through START, seven animations and STOP, including the native initial ordering and the final animation after STOP. Independent server commands must observe air. Placement must use protocol 47's actual `block_place` packet or the applicable `use_item_on` form, produce stone on the server and reduce the stack from 32 to 31. Through protocol 776 the use packet must precede a swing; protocol 777 retains its local-only placement animation. Repeating placement into the occupied destination must retain the block and stack and send use without a predicted swing. Held survival mining against bedrock must stay pending until cancellation, finish with ABORT and leave server bedrock intact.

The leg captures at least 2.1 seconds after each completed action under the unchanged 20 MiB cap. Relevant captured frames must decode to the asserted values and re-encode byte for byte. Comparisons normalize only fields absent from that era's wire form, including protocol 47's absent hand field. The server must observe disconnect, the owned launcher and JVM must exit successfully, and both owned listener ports must close. Cleanup receipts retain forced termination and pipe errors separately.

The maintained vanilla launcher creates a new run directory and checks the server jar against `artifacts.json`; shared seeds remain unchanged. The harness requires an explicit immutable producer identity and records source and loaded-library hashes. World mutations run through the console's server-thread queue with completion markers. RCON supplies independent block and inventory observations. The diagnostic geometry snapshot is read-only and never substitutes for these assertions.

## Producer-matched pilot receipts

The first root pilot uses immutable producer `315e15337c9b7f002f40b880e9b4683cad55b628`. All eleven UMPK runtime dependencies loaded by the harness were copied from that producer's tested output and compared byte for byte. Matching PDBs accompany them. The test harness itself is a recorded three-file overlay; these results do not establish acceptance of the later AOT dependency correction or flattened Adventure implementation.

| Release and protocol | Executed result | Evidence under the shared oracle |
| --- | --- | --- |
| 1.8.9, 47 | Passed the complete pack with the final harness; 114 relevant frames replayed | `runs/connected-picker-interaction-live-20261019-v3/1.8.9-a192354e94b3497e956426d9f522997b` |
| 1.19.3, 761 | Passed the complete pack; 166 relevant frames replayed | `runs/connected-picker-interaction-live-20261019-v1/1.19.3-2a7c7e45b878471197b4ae7343b36c7a` |
| 26.3, 777 | Passed the complete pack with the final harness; 325 relevant frames replayed | `runs/connected-picker-interaction-live-20261019-v3/26.3-e617463e36264e72a6833714067619c2` |
| 1.9, 107 | Failed: server placement and the 32-to-31 count succeeded, but the required placement swing was absent | `runs/connected-picker-interaction-live-20261019-v1/1.9-2418e87a031241f785b95725d4d293ac` |

All runs in the first four-pilot group, the corrected protocol-47 leg and the final two-pilot group have successful cleanup receipts: no forced termination, launcher exit 0 and both ports closed. Passing legs also retain the peer-observed disconnect. An earlier protocol-47 run failed the harness's decoded-value comparison because the source packet's hand-presence default differed from the empty native wire form. Correcting that comparison preserved every packet-order and server-outcome assertion. The original failure remains in the v1 directory.

The failing 1.9 snapshot contains 269 falling-block actors below the placement platform whose geometry is unclassified. The current global obstruction scan returns unknown when it reaches one. Original 1.9 client sources show the falling-block coordinate constructor sets `blocksBuilding`, 0.98F dimensions and a float-derived vertical offset; the spawn listener does not overwrite that resulting position. Native movement, world membership and spatial relevance still require a connected implementation and native runtime acceptance. A static box at packet coordinates or an unconditional exclusion would not establish vanilla parity. Later passing pilots do not close this failure.

An isolated causal regression now reproduces this without a server or entity tick. Bound protocol-107 spawn/removal frames feed the real applier and placement owner: the distant falling block fails with one packet instead of the required two; the no-actor, intersecting-actor and removed-actor controls pass. The four-case result is one failure and three passes. `/tmp/umpk-falling-placement-20261019-v1/controlled-red-command.json`, `controlled-red.trx` and the retained test source identify base `765a27561803927d85002f90b67d544a21f4c2c8`. The failing experiment stays outside the normal suite until its production fix is ready; no expected assertion was weakened.

The exact commands, TRX files, final source manifest and eleven-library comparisons are retained in `/tmp/umpk-direct-live-producer-20261019-v1`. The final invocation was:

```bash
UMPK_NIGHTLY=1 UMPK_INTERACTION_RELEASES=1.8.9,26.3 UMPK_INTERACTION_PRODUCER=315e15337c9b7f002f40b880e9b4683cad55b628 UMPK_ORACLE_ROOT=/home/anon/Projects/MinecraftOfficial MINECRAFT_OFFICIAL_ROOT=/home/anon/Projects/MinecraftOfficial UMPK_SERVER_ROOT=/home/anon/Projects/MinecraftOfficial/downloads UMPK_JAVA11=/home/anon/Projects/MinecraftOfficial/runtimes/java11/bin/java UMPK_JAVA25=/home/anon/Projects/MinecraftOfficial/runtimes/java25/bin/java /home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.IntegrationTests/Umpk.IntegrationTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~VanillaInteractionAcceptanceLiveTests --logger 'trx;LogFileName=/tmp/umpk-direct-live-producer-20261019-v1/final-pilots.trx'
```

The command runs from `/tmp/umpk-direct-live-producer-20261019-v1/source` with the official SDK configured as `DOTNET_ROOT` and first on `PATH`; its evidence destination is `runs/connected-picker-interaction-live-20261019-v3` under the oracle. `final-pilots-command.json`, `frozen-runtime-final.json` and each run's `producer.json` retain the complete identity and hashes. The final two legs execute and pass without skips.

## Matrix and remaining acceptance

Fresh pilots on immutable producer `765a27561803927d85002f90b67d544a21f4c2c8` pass on 1.8.9/47, 1.19.3/761 and 26.3/777. `runs/interaction-runtime-live-20261019-v1` retains the exact command, TRX, before/after producer comparisons and each leg's source, binary, acceptance and cleanup receipts. All three execute without skips, observe disconnect on the server, exit without forced termination and close both ports. These checks include the AOT dependency correction and bounded flattened Adventure code, but the survival pack does not exercise Adventure predicates or view picking. The 1.9 defect remains open.

`UMPK_INTERACTION_RELEASES=all` enumerates all 75 supported named releases from the maintained catalog and resolves each release through the existing generated version lookup. Discovery confirms exactly 75 distinct cases; this is discovery evidence, not 75 passing live legs. The default selects 1.8.9 and 26.3. With nightly tests disabled, the ordinary suite retains one skipped theory row: the deliberate Integration baseline changes from 48 to 49, with 41 executed passes and eight total opt-in skips. Scoped formatting passes.

The remaining named releases, specialized permission/geometry/use scenarios, the separate 50-protocol general smoke matrix and acceptance on a final completed-runtime producer remain unexecuted. Explicit-coordinate mining in this pack does not prove entity picking or view-dependent target selection. Protocol 47 retains the 1.8.9 client profile; running another named protocol-47 server does not establish exact parity with that release's native client.

The focused picker/Adventure evidence, final initial harness manifests and causal falling-block regression are also retained in `runs/interaction-runtime-focused-20261019-v1/manifest.json`, SHA-256 `3aed2af99d47dfb3ccdd135818ddfd8aaf7c34e84866a0a103fa3eaa64c27cf9`. This preserves 76 files outside Git, including failed attempts and the uncommitted failing experiment source. Its focused scope does not replace the separate immutable acceptance receipts.
