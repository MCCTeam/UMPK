# Ignition tool use, 2026-10-24

UMPK now predicts the successful item-use result for flint and steel and fire charges on dry, unlit campfires and candles after secondary use bypasses block activation. The existing session owner checks cooldowns, item permission, received block tags, terrain membership and stack components before the new helper returns success. It sends the use packet before the swing packet; protocol 777 keeps the native local-only swing. The executed private producer is `c8492cb5c38b301b1095b0bf6ddb93adaab8753f` plus this ignition overlay. Combined producer checks remain pending.

## Scope and source

The implementation covers 28 protocol datasets, 735–777, and the source audit covers 40 named releases, 1.16 through 26.3. Campfires apply throughout this range; candles start at protocol 755. The two campfires and 17 candle blocks use received `campfires` and `candles` tags and explicit `lit=false` and `waterlogged=false` properties. Missing context remains unknown.

The native source index is `/home/anon/Projects/MinecraftOfficial/runs/ignition-use-runtime-20261023-v1/native-source-bindings.json`, SHA-256 `f2a7a35a68d14778e590e2c9349c40274c1e927abf25c9da123e81063682b05c`. It records artifact identity, per-release behavior and 475 source/completion/artifact-file pins. All 475 files matched again after validation. `FlintAndSteelItem.useOn`, `FireChargeItem.useOn`, `CampfireBlock.canLight`, `CandleBlock.canLight`, `ItemStack` permission handling, the client interaction caller and native swing methods supply the decisions. The 1.16 and 1.17 client methods use `SUCCESS.shouldSwing`; the 1.21.2 result uses `SwingSource.CLIENT`; 26.3 uses `SwingSource.PREDICTED`, whose inherited swing does not send the local-player animation packet.

Non-secondary activation, ordinary fire survival, portals, candle cakes, lit or waterlogged targets, earlier item methods, Adventure decisions and unclassified modern component patches remain outside this positive-result slice. It does not predict server-owned effects or mutate the local block or stack.

## Executed checks

The bounded job used SDK 10.0.401, Release, one MSBuild worker, two effective processors, disabled compiler/build daemons, a 10 GiB service limit and a 1,200-second deadline. It exited successfully in 52.05 seconds and peaked at 2,583,867,392 bytes. The owned service `umpk-ignition-runtime-20261024-v3.service`, invocation `59f4a5b1fdd941a395c569c4c30be071`, was stopped only after its successful exit and status capture. It started no native servers.

| Check | Executed result |
|---|---|
| Original production with new tests | 267 executed: 78 expected `Success` versus `Unknown` failures, 189 passing controls, zero skips |
| New helper without production caller | Same 267 executed: 78 expected failures and 189 passing controls, zero skips |
| Helper connected to existing interaction caller | 267 passes, zero skips |
| Adjacent existing block-use, axe, shovel, hoe, cooldown, animation and slab tests | 7,782 passes, zero skips; expected count derived from the completed c849 TRX |
| Scoped whitespace check | `dotnet format whitespace --folder --verify-no-changes` passed for the three changed/new source files |
| Actual compiler inputs | Nine frozen Client dependencies and eleven test dependencies, including Nbt, matched exact paths, bytes and SHA-256 values before each compilation |
| Frozen producer inputs | All 22 original DLL/PDB files unchanged |
| Source identity | 4,280 original source entries unchanged; two intentional existing-file changes and two new files accounted for |
| Portable PDB source identity | 515 documents passed, including the owner, new helper and new test; existing inspector DLL reused without compilation |
| Test output identity | Complete 124-file output manifest retained; all 22 copied dependency DLL/PDB comparisons passed against their appropriate frozen or newly built producer |

The framed tests use the bound codecs and the real connected session/action caller. They assert decoded tag membership and byte-identical codec round trips, packet-populated held items and durability, explicit state properties, both hands, every supported protocol dataset and all six faces. They verify the typed result, use/swing count and order, cursor floats, inside-block flag, acknowledgment lifetime and absence of invented local block/stack changes. Context controls cover missing/unloaded terrain, removed tags, component patches, Adventure, cooldown ordering, respawn and unsuppressed activation. On protocol 777 they assert local swing intent and one outbound use packet.

## Evidence and commands

All raw files remain outside Git under `/home/anon/Projects/MinecraftOfficial/runs/ignition-use-runtime-20261023-v1/delivery-20261024-v3`. `run-slice.py` and `commands/*.json` record the exact commands, cwd, bounded environment, durations, exit codes and log hashes. The sequence is restore, actual-reference inspection, focused RED; add only the helper, rebuild and repeat RED; connect the caller, rebuild and run GREEN; verify whitespace; build the broader test assembly and run the fixed adjacent selection. No assertion or expected count changed after execution.

| Receipt | SHA-256 |
|---|---|
| `completion.json` | `96aa12c60c985726f1d74cae843e285588bd81b4e193e96e69dc7fc4b162c6bb` |
| `delivery-audit.json` | `f9fee5cf7e4cfd0adeed70f1a86ed42da85ce68c89a0cd973c9f8163c9bf23e3` |
| `results/focused-green/focused-green.trx` | `8dec8348ce6bbb846167cb930a7caf3d77a26430f299fec37ca42e87831463fe` |
| `results/adjacent-green/adjacent-green.trx` | `6819f0f60f2e9f6916ab6beec929c157d538c14ae72ccedd54edd116a25f9fdb` |
| `pdb-documents.json` | `8725df41f166e4fe988493437871bbee23d803187aadb77ddbd4188be93aa99c` |
| `ignition-runtime-final.patch` | `beca8274d06d89aa62cfabd45c887d149757067bf1c022360bde204d975a1cfa` |
| `final-delivery.json` | `4622ba5159fbedd4a3f71408bdf8a46f3276cb8f546ea5ab0af98fd78e2cd2d9` |

The first attempt is preserved under `delivery-20261024-v2`: the reference guard passed, but test compilation found an omitted `Support/ScriptedServer.cs` input and a private `BlockUseSession.Send` helper in the c849 base. No tests executed and no production change was applied in that attempt. The retry included the unchanged support source and exposed the existing test helper internally, as the shared legacy tests already require. These preparation repairs changed no test assertion. A convenience shell print after the successful PDB audit raised a Python `TypeError` when it treated the numeric release count as a list; the inspector itself exited zero and wrote its complete successful receipt before that print.

The final three-file patch adds the helper and test and inserts seven lines at the existing interaction branch. It passed `git apply --check` against the shared checkout and preserves the separate legacy placement-obstruction edits. The patch excludes the support helper because the shared checkout already has the required internal visibility. This validation does not include the other uncommitted runtime changes. New native live acceptance, combined full-suite/repository gates, consumer checks and the strict Native AOT size/smoke proof remain to be run for the integrated producer.
