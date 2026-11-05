# Trapdoor and fence-gate activation

This slice adds the native block-use result and swing decision for the registered trapdoor and fence-gate families through the existing `BlockUseActivationProfile` and `InteractionActions` owner. It starts from `651ddcbe377a86fe2d4e5f1acde028571cc0d71a`; its private source has two production overlays and one new session-test file. The scoped runtime checks pass. Native execution and live server acceptance for this slice remain open.

## Native facts and production decisions

The retained source audit covers 75 named releases and 50 protocol profiles. Its registrations contain 518 trapdoor and 419 fence-gate rows across protocol datasets, or 749 trapdoor and 626 fence-gate rows across named releases. The supplemental audit resolves the delegated toggle helpers and the inherited item-use/result methods needed by the production caller. It reuses official jars, decompile receipts, source and prior feature evidence without starting Java or downloading artifacts.

| Native family or boundary | Native result | Runtime behavior |
|---|---|---|
| Registered wood/copper trapdoors and all registered fence gates, protocols 47–777 | Success; consumes the action and requests a swing | Predict success before the existing item phase, subject to the caller's hand, feature, secondary-use and terrain rules. |
| Iron trapdoor, protocols 47–210 | Handled success without opening | Predict success and the era's swing policy. Do not reinterpret handled use as a visual opening. |
| Iron trapdoor, protocols 315–777 | Pass | Continue into the existing item phase. |
| Protocols 766–777 | Inherited item interaction reaches empty-hand activation only for the main hand | Keep offhand item continuation on the existing caller. |
| Protocol 777 | Existing local-only use swing | Keep the use packet and omit a swing packet. |

Native trapdoor toggles update the clicked state, fluid scheduling, sounds and game events. They do not inspect a door's lower cell. Fence-gate player direction can change the resulting facing without changing the success result. The predictor therefore needs no lower-cell or direction query for this bounded result decision. Exact native registration names define both families; production does not classify by suffix.

The existing caller still checks legacy hand availability before main-hand fallback, rejects disabled held items before sending, handles disabled targets after sending, and owns spectator, secondary-use, cooldown, Adventure and component behavior. Block activation can consume before item permissions, cooldown and custom item handling. A native Pass, modern offhand or suppressed activation reaches the existing item continuation.

The session's native-terrain guard now runs before those supported-family continuation paths. A block update can create cached column storage before a real chunk arrives or after unload. That storage does not prove the terrain needed by the prediction. The guard returns packet-only Unknown and preserves the earlier rejection/feature/spectator checks. It remains restricted to the existing `RequiresNativeTerrain` families.

The implementation predicts send/result/swing behavior. It does not mutate local blocks or inventory to imitate server-owned effects, and it keeps server-observed `UseBlockOutcome` separate. Protocol 47 retains the repository's 1.8.9 client profile; the audit records the other named 1.8 releases without introducing profile selection.

## Session evidence

`TrapdoorGateActivationSessionRuntimeTests` adds 459 deliberate test cases. It reuses the unchanged `Support/BlockUseSession.cs`: actual bound login/configuration, feature, chunk, block, inventory, mode, cooldown and acknowledgment packets reach the production session owner. Every packet fixture asserts decoded values and byte-identical round trips. The state-iteration helper also encodes, decodes and re-encodes each canonical block state before dispatch.

| Cases | Scenario |
|---|---|
| 99 | All 50 protocol profiles and available hands; iterate every registered trapdoor/gate and each canonical state ID; assert exact use/swing order, coordinates, face, cursor, held stack, hand and applicable sequence; preserve local blocks/stacks; observe peer EOF on disconnect. |
| 272 | Fourteen era boundaries with both families: activation, sneak with nonempty main/offhand, sneak with empty hands, Adventure, cooldown, offhand, spectator, unloaded cached storage and custom components. |
| 24 | Eight boundaries with wood trapdoor, iron trapdoor and gate: storage-only state, actual framed chunk arrival, unload, cached update and reload. |
| 16 | Enabled/disabled experimental or newly added target blocks at their feature boundaries. |
| 5 | Disabled held items reject before a use packet. |
| 8 | Iron Pass continues into ordinary stone-item use with physics enabled. |
| 8 | Modern offhand continues into stone-item use with physics enabled. |
| 12 | Secondary use reaches stone-item continuation with physics enabled. |
| 2 | Protocol-47 offhand rejection precedes the main-hand stack fallback. |
| 13 | Missing native terrain stays Unknown during item continuation, including sneak, iron Pass and modern offhand controls. |

The state-iteration cases cover the registered family inventory rather than only a default open/closed state. These are runtime packet/session tests; they are not native execution measurements. At protocol 777 the wire assertions prove that no swing packet is emitted. They do not independently observe a graphical local swing.

## Retained failures and repair

| Attempt | Actual result | Disposition |
|---|---|---|
| Initial fixture build | `CS0246`, missing `Umpk.Game.Registries` import | Harness failure before any test ran; original fixture and log retained. Add the correct namespace import. |
| Unchanged production, first focused run | 446 executed: 180 pass, 266 fail, zero skipped | Genuine runtime red: expected use plus swing, observed use only. |
| Unchanged production, missing-terrain run | 13 executed: 7 pass, 6 fail, zero skipped | Genuine runtime red: expected packet-only Unknown, observed a predicted item swing from synthesized storage. |
| Profile change only | 459 executed: 451 pass, 8 fail, zero skipped | All original missing-swing failures repaired; six original terrain failures and two newly classified modern-offhand paths still failed. |
| Existing terrain guard moved before supported-family continuation | 459 executed, all pass, zero skipped | Smallest caller change; earlier permissions/feature checks remain in place. |
| First scoped-format driver | Build-host process selected system .NET 10.0.10 while the pinned tool needed 10.0.12 | Harness failure; no C# bytes changed. Correct `DOTNET_ROOT` and `PATH`; retain the failed log. |

Each production file change had an immediate focused build/run. The final formatting pass changed zero C# bytes. All raw failures remain under separate names rather than being overwritten by passing results.

## Scoped validation

| Check | Observed result |
|---|---|
| Release Client-test project build, warnings as errors | Pass. |
| Focused new runtime cases | 459 executed and passing; zero skips. |
| Adjacent activation/tool/component/placement/picking/lifecycle regressions | 6,601 executed and passing; zero skips. |
| Full unfiltered Client suite | 30,057 executed and passing; zero skips. This is the base 29,598 plus 459 deliberate additions. |
| Scoped `dotnet format --verify-no-changes` | Pass for the two production files and the new test. |
| Original archived source entries | 4,254 unchanged; exactly two reviewed production overlays among the original 4,256 entries. |
| Retained artifact/source bindings | All 749 pins match; all 75 named-release rows retained. |
| Physical and embedded PDB source identity | 1,610 documents match: 1,592 physical sources and 18 original embedded sources. Both deliberate corruption controls fail as required without modifying source/PDB files. |
| Sibling source and Git state | DMCBK and MCC HEAD, status and tracked/untracked source hashes match the accepted base-651 snapshots. |
| Strict Native AOT size | 25,132,640 bytes, 0 versus the certified base; strictly below 25,165,824, with 33,184 bytes remaining. |
| Native AOT smoke | Exactly one test executed and passed; zero skips. Tested normal DLL/PDB hashes remain unchanged. |

The repository baseline remains unchanged in this private delivery. Its owner must update the Client total by the deliberate 459 additions when applying the reviewed tests. This slice did not rerun the completed base-651 full repository, consumer, package or CI checkpoint, and it does not claim those gates for an uncommitted overlay.

## Commands and producer identity

Raw evidence is outside Git at `/home/anon/Projects/MinecraftOfficial/runs/trapdoor-gate-runtime-20261023-v1`. `red-commands.json`, `red-v2-commands.json`, `context-red-commands.json`, `profile-commands.json`, `placement-commands.json`, `final-validation-commands.json` and `final-validation-v2-commands.json` retain exact commands, paths, exit codes and log hashes for the separate attempts. `early-aot/commands.json`, `early-aot/source-overlay.json` and `early-aot/proof.json` identify the fresh publish source and smoke substitutions. The private full Client log is `full-client.log`; this slice did not overwrite the canonical `/tmp/umpk-test.log`.

The SDK is `/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet`. Builds use Release, `-m:2`, `-nodeReuse:false` and `-p:SourceRevisionId=651ddcbe377a86fe2d4e5f1acde028571cc0d71a`. Test commands use `--no-build --no-restore` and separate TRX paths. The focused filter is `FullyQualifiedName~TrapdoorGateActivationSessionRuntimeTests`; the complete adjacent filter is in `final-validation-v2-commands.json`; the full Client run has no filter.

The AOT driver extracts another base archive and overlays exactly the two reviewed production files. Its smoke helper comes from the completed base-651 Integration build; it loads the private tested runtime libraries, with unchanged Auth from the certified base because the Client test project does not build Auth. The receipt records every substituted runtime DLL/PDB and the original helper manifest. Publishing uses a separate source/output tree and checks that the normal tested artifacts remain byte-identical.

| Identity | SHA-256 |
|---|---|
| Base-651 source archive | `50db28a761cc45ee0f6a50276e258140272d1cf7fe227358e8df8b16916c5dae` |
| Original native source audit | `257fcc43ae07734cf76863a4c88fc14066f9e6a2e6502096db575c5868f69f31` |
| Original paused-audit manifest | `ff7a75e1368d9739b94c79f57b0ff424ef81a8583970b1abce960dbdab74682e` |
| Supplemental native helper/result audit | `a7fafdfa690870d073bcbec7878a00f21d7cf735aaa913f2050da80af49cfe7a` |
| Private Client DLL | `4f19f995ef6f11fe40b3fe916ee017cca814435b2e9dc80d7902d23328d72e24` |
| Private Client PDB | `f186bd67e73f4e715af7a569d18c613c999c1e2cf7eacaee2fab245d5aad7123` |
| Private test DLL | `a2b43ea8aef5492d27dc9fc8e2270c5e319289a04684116536743a8d65317c57` |
| Private test PDB | `0307e6d2acf0d87d8fbb33a5e0736db1047ba313f536000b81c9dfbf0ce4c1a3` |
| Native AOT binary | `5d55028e7db9a2e04b7dc5943c4116f5d89ebe728766880de823600485ea7649` |

The full source-binding index remains in the retained `trapdoor-gate-use-audit-20261023-v1/source-audit.json` and this run's `native-supplement.json`. `source-integrity.json` records the two production deltas, registration totals, archive pin and unchanged sibling proof. `pdb-proof.json` identifies all inspected PDBs and both negative controls. The frozen delivery manifest records exact hashes for the two source files, test, report, patches and raw receipts.

## Root integration

The reviewed two production files and new session-test file were applied byte-for-byte to published `4aa0368`, whose runtime source matches base `651ddcb`. A fresh Release build and all 459 focused cases pass with zero skips. The Client count baseline deliberately changes from 29,598 to 30,057; the other sixteen baselines stay unchanged. The root independently verifies all thirteen retained PDB hashes, all 1,592 physical PDB source documents and the actual private AOT executable's size/hash. This integration check does not replace the required immutable new-producer repository, consumer, package, AOT or native/live gates.

Exact root commands, source-overlay hashes, actual TRX outcomes and log hashes are in `runs/trapdoor-gate-root-integration-20261023-v1/root-review.json`, SHA-256 `b79894607ff7d02dbdc71fb50052c8ee774d6a57ad8d04feff14cb17ce5cc0e9`, under the shared oracle. The retained private delivery has SHA-256 `46c79d58f9dc784a8512606ef75d1a7411b712af67265c690488688bde66feaf`; its 97-file manifest has SHA-256 `5e4d5cbe0bee74ebaf2f37d8a516f8c6fae63d091fea7fe53134133a8867769a`.

## Remaining acceptance

Native executable observations and real vanilla server legs for this activation slice have not run. They must compare actual activation and native item continuation at the measured boundaries, with independent world/inventory observations and exact producer/capture pins. This source/runtime delivery does not close those ledger entries or the wider mining/placement objective. It also does not claim visual toggle prediction, rendered local swing, new attachment/multicell placement, full Adventure item predicates, server item effects, or other block-use families.

All Java/native/server process counts for this slice are zero. The private SDK runs use no external game session or authenticated account. Shared UMPK and sibling source edits, commits, pushes, baselines, package publication and CI changes belong to the parent review/application stage.
