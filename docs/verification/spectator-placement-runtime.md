# Spectator exclusion before unresolved rider geometry

Placement now ignores a player whose spectator state is already proven before it requires that actor's membership or rider geometry. A spectator cannot obstruct native placement. This fixes packet-only unknown predictions caused by a known spectator on an unsupported mount, while retaining unknown results for unproved actors and preserving the original lazy player-info lookup timing.

`EntityMotionState.IsKnownSpectator` reads the current connection-owned tab entry for the existing uncached profiles through protocol 760. From protocol 761 it reads only the already-attached native player-info object; it does not initialize that cache. The existing late `IsSpectator` query still runs only for a spatially relevant candidate and retains its original cache creation and detached-object lifetime. The placement caller changes no local mounting, picking, motion, geometry, public API, dataset, dependency, DI lifetime or packet layout.

## Source evidence and its limits

The earlier source audit covers all 31 supported named releases from 1.8 through 1.14.3, comprising 18 protocols from 47 through 490. Every original spectator predicate reads the connection's current UUID-to-player-info map, compares the game mode with native id 3/`spectator`, and bypasses the separate lazy skin/player-info cache. Each corresponding connection lookup is a pure map read. The audit retains exact class names, both method bodies, source hashes, enum identities and matching existing decompile/artifact receipts. This is source evidence; no additional native getter operation, packet handler or full client/world tick was executed.

The source bindings at 1.8.9/protocol 47 and 1.14.3/protocol 490 separately show the placement obstruction owner calling the default spatial entity query, which passes its spectator exclusion predicate. The former uses `adm.a` → `adm.b` → `po.d` and the player's `v` predicate. The latter uses `bhe.a` → `aio.f` and the actor's `t` predicate. Their full source paths, line locations, file hashes and six exact method/field bodies remain in the external filter-binding receipt. A positive spectator result therefore excludes that candidate independently of its geometry or membership. No protocol gate or new feature axis is needed to reuse the existing predicate.

Protocol 47 continues to select the existing 1.8.9 client profile. Earlier protocol 47 aliases in this audit are recorded source observations; the change does not select a different client profile by release name.

The separate, previously completed native getter campaign remains limited to the 44 releases from 1.14.4 through 26.3. Its 440 observations execute original spectator predicates, lazy lookups and connection map lookups with controlled allocated objects. The original fixture, measurement hashes and limitations remain indexed in [player-info-placement.md](player-info-placement.md). Those observations are reused; the 31-release source audit is not relabeled as another native campaign.

## Runtime verification

The private runtime is `c8492cb5c38b301b1095b0bf6ddb93adaab8753f` plus the reviewed legacy 47 v3 overlay (`22fcd5a128d8a9b706cf2e70692c257ed6d09985d584cab089723097532b0c3c`) and this spectator patch. It does not contain the later narrow membership-query refactor or ignition slice. A fresh combined producer must establish their integration separately.

| Check | Executed result |
|---|---|
| Focused regression before either production change | 38 executed; 10 expected prediction failures, 28 passing controls, no skips |
| New helper present without its placement caller | The same 38 executed; the same 10 expected failures and 28 controls |
| Connected placement caller | 38 passed; no failures or skips |
| Original adjacent regressions against the new Client DLL | 1,467 passed: 1,309 placement-animation cases, 143 player-info cases and 15 player-info-applier cases; no skips |
| Original spatial cache-timing regression | All three unchanged `PlacementQueriesPlayerInfoOnlyInsideTheNativeCandidateBox` rows executed and passed |
| Scoped formatting | Passed for the two production files and the new test file |
| Actual compiler references | Exact `ReferencePathWithRefAssemblies` identities, paths, lengths and hashes passed for nine Client dependencies and eleven test dependencies, including Nbt |
| Source and frozen inputs | All 4,284 private source entries verified; only the two reviewed production files changed during execution. All 146 frozen input files remain unchanged |
| Owned service | Completed in 33.2 seconds, exit 0; observed peak memory 2,385,231,872 bytes under the 10 GiB/two-CPU limit; main PID absent and completed service stopped |

The 38 framed-session cases cover 498, 760, 761, 764, 770 and 777. They verify known spectators, survival and missing-info controls, unsupported mounted geometry, retained detached cache objects and modern uncached entries. Every input uses the real bound codec with decoded-field assertions and byte-identical re-encoding. The tests call both the production predictor and public placement action, check use/swing order and acknowledgment behavior, and retain unchanged world/inventory observations. Protocol 777 preserves its local-only swing prediction. No synthetic rider position or geometry is supplied.

The ten genuine failures comprise six mounted known-spectator cases and four retained cached-spectator cases. They fail with expected success versus actual unknown. Adding an unused helper preserves these failures; connecting the production caller resolves them. Modern uncached mounted spectators retain unknown predictions and do not populate the lazy cache early.

Only four unchanged focused sources are compiled: the new test plus `BlockUseSession`, `BoundDescriptorCodec` and `ScriptedServer`. The original full-v3 test assembly and its 124 output files are copied to a separate private directory for adjacent testing, and only Client DLL/PDB are replaced. The other 122 files remain byte-identical. This executes the unchanged regression methods against the new runtime without recompiling the full test suite or generated data.

## Exact external receipts

All paths below are outside Git under `/home/anon/Projects/MinecraftOfficial/runs/spectator-placement-runtime-20261023-v1`.

| Evidence | Location | SHA-256 |
|---|---|---|
| Earlier 31-release source audit |`scope-audit-20261024-v1/early-spectator-source-audit-20261024-v1.json` |`8b1c4f1b1b6c2d0565b1a842773ed3700d526a327b8d7748b89c3081ebc5ea4d` |
| Two explicit native placement-filter source boundaries |`scope-audit-20261024-v1/early-spectator-placement-filter-bindings-20261024-v1.json` |`7121856df04663b9a50eb8f8753a5cefc4ce683fc86c6891e3f73ee54d63f8c4` |
| Reviewed two-file production patch |`production-after-red.patch` |`e78265bb5ac3657cf24ab6bfbbaee1b3ae84354aae9e13471a147ce0da57d44b` |
| Production patch plus 38-case test |`delivery-20261024-v3/delivery.patch` |`fe39a2671279c6c388f2101382c801e7b6f3931fe035021ff1a25305a25ab766` |
| Initial RED TRX |`delivery-20261024-v3/results/focused-red/focused-red.trx` |`3a740cfe8a8e63e589fcfa26f12f1cedc8147ab1dfb4214066e0c638a6cc4035` |
| Unconnected-helper RED TRX |`delivery-20261024-v3/results/focused-helper-red/focused-helper-red.trx` |`7b234da00059b03ad32875d09d2ef0d6c95f4ae74609d1b444659b51f4824d6a` |
| Connected GREEN TRX |`delivery-20261024-v3/results/focused-green/focused-green.trx` |`770de1ac3ed0d35fa086040d500e8c700526971496640686058ca2c274939ba5` |
| 1,467 unchanged adjacent rows |`delivery-20261024-v3/results/adjacent-green/adjacent-green.trx` |`05db7919acb3249e74ce2fb9c878def160901708dea22c249008486043a03a77` |
| Complete command/result/source/binary receipt |`delivery-20261024-v3/delivery-receipt.json` |`89fc5b7f5e9967c37d57299733d2bec5b59e82fcc1b0f97a69e711856551cdf6` |
| Detailed evidence manifest |`delivery-20261024-v3/delivery-manifest.json` |`49fe41e0f0781bdfddb6fee3be7a3cf3f483d2c1e80b729e91dc1b06a91bff92` |

The command receipts preserve complete restore, actual-reference inspection, focused build/test, scoped-format and adjacent-test commands, logs, timestamps, exit codes and hashes. The earlier unexecuted target preparations remain separate. The source-audit matcher's initial enum-arity and optional raw-predicate discovery failures are retained separately; neither changed native source nor executed a native operation. The accepted early getter audit and two explicit filter bindings state their exact proof scope.

## Remaining acceptance

The 38-case runtime patch and source audit do not complete the overall interaction objective. Required remaining checks include the actual combined producer's focused/adjacent and full repository gates, deliberate test-count update, format/dataset/language/Python/reconstruction/generation/conformance/performance gates, packages and isolated package-consumer checks, fresh producer-matched DMCBK/MCC results and dependency hashes, strict Native AOT publication below 25,165,824 bytes with exactly one executed passing smoke, source/binary invariance, and all seven CI jobs on the actual published head.

No new live spectator witness was executed. A normal native spectator switch can remove the actor from a survival viewer's tracking set, so placement after that switch alone would not prove this predicate. General passenger geometry, local mounts, picking, pushes, contextual placement and the broader 50-protocol/75-release acceptance matrix retain their existing ledger entries and unknowns. PR #10 stays open and draft; this slice does not merge or publish a release.

The later [combined runtime development check](interaction-runtime-checkpoint-20261024.md) verifies exact `c977ff3` with all 33,149 Client cases, including this slice, and strict AOT at 25,165,568 bytes with exactly one executed passing smoke. It preserves source and tested library identities; the failed first fixture-discovery attempt remains separate. This does not replace the final repository, consumer, package, native/live or final-head CI requirements.
