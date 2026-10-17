# Mapped entity placement verification

The native horse/cow data fix is `bd003cfa5c05b9fedb6bf06be2ce4667bd474672`. The tested snapshot is `eb52b8db1c129b66b3edc9c11560dea8bd4895bd`, which also makes pathfinding view comparisons use the existing virtual clock. The separate pending tick-control and shulker work is excluded from this snapshot.

## Implemented behavior

Horses and cows now have measured age/pose bounds in the 32 protocol datasets from 498 through 777. Before this fix, an unmeasured horse or cow could suppress a placement swing even when it was far from the destination. Existing villager, cat and golem profiles remain unchanged. The extractor verifies original client classes, official artifacts and mappings, source hashes, method owners and agreement between release aliases. All 357 other shared tables remain byte-equivalent.

The new measurements contain 223,848 native phase replays across 44 releases. Together with cat/golem evidence, the compressed fixture contains 176 release/type rows, 25 deduplicated cases and 363,753 phase replays. These execute original constructors and metadata/pose callbacks with controlled world inputs. They establish default-scale geometry phases; they do not execute complete native entity/world ticks.

## Executed live checks

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` were built against the tested UMPK snapshot through temporary targets outside the repositories. All 33 consumer/producer DLL comparisons match. DMCBK passes 1,999 tests and MCC passes 918.

Each fresh vanilla leg uses seed `4603516203263995078`, isolated ports/directories, a 1 GiB server heap and the same five scenarios: replace water, replace lava, replace a plant as the destination, click the plant directly, and reject placement into an occupied slab. Independent RCON queries verify each starting material, final block and selected stack count. A pre-action entity-volume query waits for an unobstructed destination without removing NPCs. Captures verify use-then-swing on successful pre-777 placements, no swing on the occupied slab, and protocol 777's native absence of a separate swing packet.

| Release | Protocol | Server and capture scenarios | Byte-identical selected frame round trips |
| --- | --- | --- | --- |
| 1.14.4 | 498 | 5/5 | 958 |
| 1.16.5 | 754 | 5/5 | 2,001 |
| 1.17 | 755 | 5/5 | 1,908 |
| 1.20.5 | 766 | 5/5 | 86 |
| 1.21.11 | 774 | 5/5 | 93 |
| 26.1.1 | 775 | 5/5 | 93 |
| 26.1.2 | 775 | 5/5 | 92 |
| 26.3 | 777 | 5/5 | 91 |

The audit round-trips 5,322 selected frames out of 15,938 play frames. Every leg also captures the persistent item and both adult/baby villager controls before its first use. Packet position reconstruction for the first three legs is separate from native mob interpolation. All owned processes exited and all allocated ports closed.

The earlier three 26.x attempts stopped before placement because the harness used the old time-query translation key. The corrected key comes from each matching `TimeCommand.java` and language resource. The earlier 1.17 timeout had packet-position evidence consistent with a villager obstructing the destination; that cause remains an inference because the old leg lacked the pre-action entity-volume query. Both failed attempts remain in their original run directories.

## Verification results

| Check | Observed result |
| --- | --- |
| New native RED witnesses | Both 1.15 horse/cow rows fail against the old datasets; the initial fixture-envelope setup fault is retained separately |
| Focused client geometry/placement | 8,851 passed |
| Java data suite before full run | 1,845 passed |
| Extraction tests | 161 passed |
| Planner view/timeout regressions | 11 passed |
| Full Release rebuild | Passed, no warnings or errors |
| Unfiltered full suite | 27,530 passed, 2 failed, 7 skipped; all 17 suite totals match the reviewed baseline |
| Test-count gate | Failed because the full run contains failures |
| Format, dataset and language checks | Passed |
| Generated output | All 61 generated files match regeneration |
| Native AOT publish and smoke | Passed; 24,493,360 bytes below the 25,165,824-byte budget |

The full-suite failures are the existing citation-count guard at `VanillaCitationTests.cs:138` and `SoundNameTests.LookupAllocatesOnlyTheRequestedName`, which measured 134 bytes per call against its 128-byte limit. Three subsequent fresh executions of the unchanged full data suite pass; a separate direct probe measures 88 bytes per call in all 40 batches. The intermittent failure remains unresolved. Neither allocation limits nor the citation guard were weakened. Seven environment-gated tests remain skipped in the unfiltered run; the AOT smoke was executed separately.

## Evidence and remaining scope

The complete source snapshot, commands, exact binaries, gate logs, generated-data review and decoded captures are preserved at `/home/anon/Projects/MinecraftOfficial/runs/mapped-herd-rules-proof-20261017-v1`. Consumer receipts are in `mapped-herd-rules-consumers-20261017-v1`; live artifacts are in `mapped-herd-placement-matrix-20261017-v1`. Native measurements are in `mapped-herd-geometry-batch-20261017-v1` and `mapped-herd-geometry-pilot-20261017-v1`, under the same `runs` directory. The retained earlier matrix is `mapped-entity-placement-matrix-20261017-v2`.

The exact build, test, format, extraction, generation, AOT and consumer commands are recorded in `manifest.json`, `focused.json`, `gates.json`, `consumer-live-status.json` and the consumer `results.json`. The live command and every server/client invocation, runtime, artifact hash and port are in `live-command.json` and the matrix `results.json`. `trace-summary.json` records capture hashes and frame counts. Raw logs, jars, worlds and credentials stay outside Git.

Protocol 47 geometry integration, additional entity types, non-default scaling, riders, native mob interpolation, tick-control integration, specialized block/item placement and the complete version matrix remain unfinished. Separate measurements now establish the signed-byte age predicate and deferred resize phase for all ten 1.8 releases; they do not yet constitute a protocol-47 runtime implementation.
