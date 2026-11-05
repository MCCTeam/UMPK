# Native shovel-on-block prediction

The existing session-owned block-use sender now has a shovel result profile for protocols 47–776. Successful path creation and eligible campfire extinguishing send use-on followed by the selected hand's swing. Native passes and unresolved context retain packet-only behavior. The server owns terrain changes, drops and durability. Protocol 777 remains an unknown data-driven transformer era.

## Behavior and ownership

| Native era and context | Tool result after the existing activation/permission phases |
| --- | --- |
| Protocol 47, using the 1.8.9 client profile | Inherited item-use no-op: pass |
| Protocols 107–754, grass block, non-bottom face and air above | Success and packet swing |
| Protocols 755–776, grass block, dirt, coarse dirt, podzol, mycelium or rooted dirt, non-bottom face and air above | Success and packet swing |
| Path target clicked from below or covered by a non-air block | Pass without swing |
| Protocols 573–776, a lit campfire, non-bottom face, with secondary use suppressing block activation | Success and packet swing, including when covered |
| Unlit campfire or bottom-face campfire use after activation is suppressed | Pass without swing |
| Standing campfire use | Unknown: block activation can consume a custom cooking-recipe input before shovel use |
| Required terrain not established by chunk packets, or an unclassified component patch | Unknown without inventing a swing |

`InteractionActions.PredictBlockUse` keeps its existing activation, inventory, Adventure and cooldown ordering. The narrow shovel branch follows the hoe branch and requires packet-established column availability. `VanillaShovelUse` owns the source-backed tool result. It never substitutes entity placement obstruction for native tool behavior. The legacy `grass` block name is scoped to pre-flattening states; the later grass plant remains a pass. Unused pre-flattening grass metadata stays unknown.

The campfire result is connected through the existing secondary-use owner. Runtime tests invoke `MovementActions.SetSneakingAsync`, assert its actual input packet and then exercise use-on. This bypasses the recipe-dependent block activation phase through the same production path used by the client. Standing campfires remain unfinished where the recipe context could change consume versus success. A successful helper measurement alone does not close that context.

Legacy damage and unrelated NBT do not affect the native shovel's client result. The bounded profile accepts their existing representations, together with modern Damage patches. Other modern patches remain unknown. Before flattening, slot damage uses the separate wire field; protocols 393–765 use NBT `Damage`; component-era stacks use the Damage component. These representations are asserted independently of the server-owned durability effect.

## Source evidence

The original-client audit verifies all 74 named releases through 26.2, covering 49 datasets. It checks each client jar's SHA-1/SHA-256, the matching decompile receipt, registered shovel materials, the exact path target map and campfire-tool boundary. Modern `ItemStack`, `MultiPlayerGameMode`, `InteractionResult`, `CampfireBlock` and block-registration sources are retained. The 26.2 block identifier indirection is resolved through the original `BlockItemIds` source. Legacy bindings retain their original obfuscated names and include each release's actual game-mode controller source.

The audit is `runs/shovel-focused-20261021-v1/native-source-audit.json` under the shared oracle, SHA-256 `3148d2854bc794b0b475eac9cfa23019a7b6549f9fa4a328896e51b5d01a1a70`. It establishes the production terrain/result decision and the recipe-dependent limit; it does not execute native client methods or establish live acceptance. Protocol 47 retains 1.8.9's profile, while the other named 1.8 sources remain recorded independently.

The measured source boundaries are original inherited item-use pass in 1.8.9, grass-path creation in 1.9, flattened registry identities in 1.13, campfire extinguishing in 1.15 and the six-soil path map in 1.17. The original protocol-777 `BlockTransformers` registry replaces `ShovelItem` and is not inferred from 776.

## Packet-runtime evidence and remaining failure

There are 1,955 new packet-driven cases. The original producer fails exactly 644 expected missing-swing assertions while 1,311 controls pass. Adding only the helper leaves the same failures, proving that the production caller is required. Connecting the sender passes 1,946 cases and exposes nine existing cooldown-owner failures on protocols 768–776. The adjacent run executes 3,115 cases with the same nine failures and 3,106 passes. Those assertions remain intact: a framed modern cooldown stores the group identifier's hash, while the old action precheck asks for the item network ID, so cooled shovels incorrectly swing. Exact group ownership and native expiry are a separate active repair. This slice is not a complete pass until that repair is integrated and the cases execute again.

The cases cover every applicable dataset and registered shovel material, both supported hands, soil/face/cover boundaries, Creative mode, worn stacks, Adventure/cooldown guards, native column receipt/unload/reload with entity tracking enabled or disabled, campfire secondary-use ordering and explicit protocol-777/component unknowns. Full-column frames pass through the era's bound codec before the action; modern frames contain the full dimension's empty sections. Chunk structural re-encoding and inbound block/inventory/game-mode/cooldown decoded values and byte-identical round trips are asserted. These are constructed wire witnesses rather than new corpus captures. Outgoing use/swing/input packets resolve and execute their production bindings; protocol 47 uses the actual legacy placement packet.

The initial plant-name fixture fails 24 registry lookups because protocol 765 renamed the plant to `short_grass`. That failed attempt is retained separately. Correcting the fixture's registered name leaves the 644 genuine behavior failures above. The nine later cooldown failures are production defects, not harness failures.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore -m:2 -nodeReuse:false -p:SourceRevisionId=a72c573f84ac3221b1b45e46f404f5fc2cbbeb66 --filter FullyQualifiedName~ShovelUsePredictionRuntimeTests
```

Commands use `/tmp/umpk-shovel-20261021-v1/source`, an archive outside Git, with the official SDK as `DOTNET_ROOT` and first on `PATH`. The focused evidence directory retains exact source overlays, commands, logs, TRX results, native source bindings and their hashes. Its immutable failed-checkpoint manifest is `runs/shovel-focused-20261021-v1/manifest.json`, SHA-256 `b2692331272301460ecc01889064eff2760132680263b23d0736c551af31ddb1`, covering 610 files; 4,166 archived base blobs remain unchanged. These are overlay results; combined immutable producer, native-server, consumer, package, Native AOT and CI acceptance remain pending. Other tool/fluid families, complete standing-campfire recipe context and protocol 777 remain open.

## Native-server red and scoped green, 2026-10-21

The packet-runtime counts and failed checkpoint above remain historical results. Fresh native-server checks now establish the scoped shovel behavior below. They use an explicitly identified source overlay rather than claiming an immutable final producer, and they do not close the nine historical cooldown failures or the final combined-producer gates.

The valid red campaign used the exact eleven frozen runtime DLLs and matching PDBs from `3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb`, with separately hashed integration-test and launcher overlays. Exactly five theory rows executed: 1.8.9 passed its no-op/control pack, while 1.9, 1.15, 1.17 and 26.2 each failed `Expected: 2 / Actual: 1` at the use/swing assertion. Each failure followed a successful native grass-to-path transformation, native durability increment and independent server block/durability predicates. The missing predicted swing is therefore a runtime defect. Later soil/campfire cases and the final replay assertion did not execute on those four failed rows; their captures and that limit are retained. The earlier console-marker failures remain separate harness failures.

The green producer starts from a fresh archive of `3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb` and adds only `VanillaShovelUse.cs` plus the three-line shovel predictor call after the existing hoe branch. It excludes the active cooldown, falling-block and readiness changes. Its explicit identity is `3132e99+shovel-production-overlay-20261021-v1`, with `immutable_commit: false`; the base revision in assembly metadata does not identify the changed source by itself. The new `Umpk.Client.dll` has SHA-256 `59bd5ffd2532e17813c3a2fabf906769a63d1308d53c7a72b763e30c463be2af`. The producer binds all eleven newly built runtime DLLs and their eleven matching primary PDBs; it does not reuse the frozen 3132 Client DLL.

Both campaigns use the proven console-marker harness and raw-byte launcher repair described in [console forwarding](console-forwarding.md). The four recorded test-source hashes identify the harness, main partial class, shovel partial class and diagnostic source independently of the runtime. In the green archive, 4,165 other Git blobs match the base. Only the two stated production overlays and four stated harness/launcher overlays differ or are added. Neither campaign used the in-progress sibling or shared-checkout builds.

| Release | Protocol | Native live coverage | Final native damage | Relevant frame replays |
| --- | --- | --- | --- | --- |
| 1.8.9 | 47 | Shovel no-op, ordinary soil passes and face/cover controls through legacy block-place | 0 | 129 |
| 1.9 | 107 | Grass path creation, dirt/coarse-dirt passes and face/cover controls | 1 | 67 |
| 1.15 | 573 | Grass path plus covered lit-campfire extinguish, unlit pass and bottom-face pass under secondary use | 2 | 171 |
| 1.17 | 755 | Grass/dirt/coarse/rooted paths, face/cover controls and all three campfire cases | 5 | 269 |
| 26.2 | 776 | Expanded soil and campfire cases through the component-era inventory and use bindings | 5 | 304 |

All five green theory rows executed and passed with no skips. Each established usable terrain and a grounded survival player with physics enabled, asserted the actual native block and held-tool damage through independent server predicates, checked one-action use/swing order and decoded fields, then replayed relevant frames through the exact producer bindings with byte-identical re-encoding. The total is 940 relevant frame round trips. Podzol and mycelium are covered by source and packet-runtime cases; they were not separate targets in this live pack. The standing-campfire recipe path and unclassified modern component patches remain outside this scoped success.

Each campaign checked all 55 per-release runtime DLL comparisons, unchanged tested producer DLL/PDB bytes, unchanged original frozen 3132 artifacts, and unchanged DMCBK/MCC source hashes and Git snapshots. All ten native legs independently recorded the server-observed disconnect, a zero child/launcher exit, an absent owned child PID and both closed listeners. Seed files remained unchanged. Java 11.0.32.1 served the legacy releases and Java 25.0.4.1 served 1.17/26.2; their executable hashes, paths and version outputs are retained.

The native command was `/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.IntegrationTests/Umpk.IntegrationTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ShovelUseMatchesNativePathsCampfiresDurabilityAndSwing --logger trx;LogFileName=<campaign>/shovel-live.trx`, with the logger argument passed as one structured argument. Red ran in `/tmp/umpk-tools-live-3132-20261021-v2/source`; green ran in `/tmp/umpk-shovel-live-green-20261021-v1/source`. Both set `UMPK_NIGHTLY=1`, the explicit producer identity and fresh external evidence directory, the official oracle/download roots and private Java paths. Exact argument arrays, environment owners, timings and log hashes are recorded by the campaign drivers. The green build used the official .NET SDK 10.0.401, Release, `-m:2 -nr:false -p:SourceRevisionId=3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb`, and completed with zero warnings/errors.

Raw evidence remains under `/home/anon/Projects/MinecraftOfficial/runs/`. The valid red is `shovel-runtime-live-red-20261021-v2/manifest.json`, SHA-256 `6a5cf88d6c65b22abc8c25312e74712c69944800cc59170e4bc1b922ed7891b7`. The five-row green is `shovel-runtime-live-green-20261021-v2/manifest.json`, SHA-256 `9a595c8a1f71967f8e3088fd43f84bebfa5a320834c9928bfa0a80f4381decd7`. The complete executed green producer/harness output and reconstructible source overlays are frozen in `shovel-green-producer-overlay-20261021-v2/manifest.json`, SHA-256 `6838ed24eb6d434950892390eb694c992c802721fe02bf1351006a6b7a499993`.

The first private green driver stopped before starting a server because artifact discovery counted copied dependencies alongside each project's primary output. That harness prerequisite failure executed zero tests and is retained in `shovel-runtime-live-green-20261021-v1/`, manifest SHA-256 `7997aac4deb9a8e94bd94af66af4f47a42cf7bb089e1d5c4f13b642dce7f3efc`. Its exact private driver diff selects the owning project's DLL/PDB while preserving the strict eleven-library/twenty-two-primary-artifact checks. No C# harness or production assertion changed for green-v2.

The repaired cooldown owner still needs a fresh combined run of the unchanged 1,955 shovel packet-runtime cases. Full final-producer native acceptance, repository and count gates, package/consumer checks, Native AOT, and published-head CI were not executed by this shovel-only green campaign. Protocol 777, complete standing-campfire recipe context, unclassified component patches and the other interaction families remain open.

## Combined cooldown integration

The later root archive over `fb3a8e4` includes the repaired cooldown owner and the unchanged 1,955 shovel cases. All 3,326 focused interaction cases and all 26,050 Client cases pass without skips, closing the nine earlier modern cooldown failures without changing their assertions. Normal Integration passes 41 and skips ten opt-in cases; the single new shovel theory increases its normal discovery total from 50 to 51. All nineteen launcher/RCON cases and full solution formatting pass. `runs/interaction-combined-focused-20261021-v1/manifest.json` binds 44 evidence files, SHA-256 `d0fd5c8164e63a02977b4282f0f103dde998b27d02040dda8e2f3ddd4ba18dc6`, with 29 exact source overlays and 4,156 unchanged base blobs. The shovel commit deliberately adds 1,955 Client cases; the other combined additions retain their own intent and evidence.
