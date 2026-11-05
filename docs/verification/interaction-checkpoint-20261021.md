# Combined interaction checkpoint, 2026-10-21

The tested immutable producer is `e65329e5021278771610755dccd75ad2d39d7cf6`. It combines the received-terrain hoe profile through protocols 47–776, native item/group cooldown expiry, shovel results through 776 and the bounded protocol-107 falling-block/liquid fix. This checkpoint passes the checks below. The [completion ledger](interaction-completion.md) still keeps the broader mining, targeting, geometry, specialized-use and final version acceptance work open.

All `runs/` paths are relative to `/home/anon/Projects/MinecraftOfficial`. Source archives, commands, binary manifests, official artifacts, captures, raw logs and failed attempts remain outside Git. `interaction-combined-validation-20261021-v1/checkpoint-receipt-index.json` binds the 34 selected receipt files and their hashes, including the retained native failure; its SHA-256 is `6998a32440888e32823bca6db8da6e038a75eb0e6caec39b94473d6b2a2db0f4`. Package version `0.9.0-beta.7`, dependencies, DI lifetimes and public signatures remain unchanged.

## Immutable repository and package checks

`runs/interaction-combined-source-20261021-v1` is outside every Git checkout. `interaction-combined-validation-20261021-v1/source.json` binds its 4,188 tracked source blobs and links to the producer. Its `results.json` records each exact command, exit code, duration and log hash; `finished.json` records no failed gates and unchanged source. No completed gate was rerun merely to write this report.

| Check | Executed result |
| --- | --- |
| Release restore/build | Passed |
| Full unfiltered .NET suite | 39,506 passed; ten normal opt-in cases skipped; no failures; output copied to `/tmp/umpk-test.log` |
| Count gate | All seventeen suite totals match, including 26,050 Client and 51 Integration cases |
| Release allocation/performance and conformance | Passed within the unfiltered suite; no update switches enabled |
| Full format verification | Passed |
| Canonical datasets and language | Passed |
| Python | 356 extraction and nineteen official-tool/RCON tests passed |
| Native reconstruction | All fifteen retained collision, clearance, lifecycle, bed, wake, traversal and context checks passed |
| Fresh generation | All 61 generated files match exactly |
| Source integrity | All 4,188 tracked blobs and links unchanged after validation |

Those rows comprise 25 recorded commands: restore, build, full-test, test-counts, format, dataset, language, python, official-tools, fifteen reconstruction checks and generate. The ten normal opt-in skips are separate from the required live and AOT executions below.

`runs/interaction-combined-packages-20261021-v1/results.json` records `dotnet pack UMPK.sln --no-build --no-restore -c Release` with both commit properties pinned to the producer, package verification and isolated consumer restore/build/run. All fourteen primary and thirteen symbol packages pass verification. The thirteen packaged library DLLs match the tested producer; a fresh-cache package consumer constructs protocols 47 and 777 and matches the same thirteen libraries. `proof.json` records package and DLL hashes. Tested producer DLLs remain unchanged. These are local package checks; no packages were published.

## DMCBK and MCC consumers

Accepted receipt `runs/interaction-combined-consumers-20261021-v3` records DMCBK 1,999 passing tests, MCC 918 passing tests and 33 matching dependency DLL comparisons. External `DirectoryBuildTargetsPath` local-project targets point at the immutable producer. `sibling-source-before.json` and `sibling-source-after.json` match source/Git state, including tracked and untracked source. The complete frozen MCC binary manifest is `client-manifest.json`.

The original consumer run is a retained failure, not an unmodified consumer pass. Seven DMCBK pose tests failed because their setup wrote synthetic terrain with `World.SetBlockStateId` and advanced one tick without receiving a native full chunk. Protocol 770's native readiness gate correctly kept the standing eye height. Production was not changed to treat synthetic terrain as received terrain.

The accepted run substitutes one external test source file through MSBuild, leaving sibling files untouched. `PoseTargetingTests.cs` gains a bound, structurally round-tripped full `level_chunk_with_light` frame and waits for `ChunkLoaded` before its existing synthetic terrain setup. Its fourteen focused cases then pass before the full suite. No original assertion was changed or removed, and no test was added or removed. The original source SHA-256 is `99f1ab40105bc86921b3d6677037b6a8460aa7c48413e28a5f1f3b201d44a9d9`; the external replacement is `6b3052ba4a1bda69fd00c8d8d8415df50e27d55a1695c350aa699c90bad49790`. `fixture-overrides.json` and `fixture-overrides.diff` record this substitution. The external source lives in `runs/interaction-consumer-fixture-20261021-v2`.

The original `interaction-combined-consumers-20261021-v1` preserves its 1,992 passes/seven fixture failures. Consumer attempt v2 preserves an external adapter compile error in tuple member names and executed zero tests. It is a harness failure. Accepted consumer v3 uses the corrected external adapter; it does not erase either attempt.

## Native AOT

`runs/interaction-combined-aot-20261021-v1` records a Release `linux-x64` MinimalBot publish with `SourceRevisionId` pinned to the producer and map-file generation enabled. The binary is **25,017,504 bytes**, below the unchanged strict **25,165,824-byte** limit. SHA-256 is `499212663fc2ed15fafe912c2420b2146238c40d8333a4c57993761af161f1f1`.

The smoke runs with the `AotPublishSmoke` filter and an explicit TRX output. `aot-smoke-executed.json` confirms exactly one total, executed and passing test, with zero failures or skips. Publishing leaves the tested producer DLLs unchanged.

AOT ran independently while the consumer fixture was being repaired. Its `prerequisites.json` records the completed 25 repository gates, exact producer identity and the then-pending consumer correction. The accepted consumer `cross-check-certificate.json` subsequently verifies equality of all fifteen producer DLLs across the original baseline, accepted consumer run and immutable source, and links the completed consumer, package and AOT receipts. This is an explicit concurrent prerequisite substitution, not a claim that consumer v3 had completed before AOT started.

## Fresh native interaction checks

`runs/interaction-combined-native-acceptance-20261021-v2/producer.json` binds the frozen runtime DLL/PDB manifest to this producer. `plan.json` fixes the version/scenario lists. Each phase retains its exact command, TRX, assertions, captures, artifact hashes and cleanup. Required path preflights verify the source-root links, four harness source hashes, 1,133 dataset files, 407 fixture files and frozen binaries before executing the phase.

| Scenario | Versions | Executed result |
| --- | --- | --- |
| Physics-enabled hoe use | 1.8.9, 1.9, 1.13, 1.14.3, 1.14.4, 1.17, 26.2 | Seven passes, zero skips |
| Physics-enabled shovel use | 1.8.9, 1.9, 1.15, 1.17, 26.2 | Five passes, zero skips |
| Basic survival mining, placement, rejection and unbreakable cancellation | 1.8.9, 1.19.3, 26.3 | Three passes, zero skips |
| Controlled 268-falling-gravel placement reproduction | 1.9 | One pass, zero skips; all 268 bounds known; original retained world unchanged |

The fifteen interaction pilots use independent server block/stack/durability observations, exact action packet-order assertions, relevant frame replay, peer-observed disconnect and successful owned-process/port cleanup. The controlled falling-block repeat uses the separately hashed native fixture and twelve source-library DLL/PDB pairs; capture SHA-256 is `ff5b501c524916d7396037363bbe3ee988655e817e45e071716091c00dc3a4cf`, fixture manifest SHA-256 is `7de58d4c10a00dad68ca822ba44131e99a9927fd32e98688594ee05e1bb39cd0`. It repeats the controlled scenario from the [liquid report](falling-block-liquids.md), rather than replaying the incomplete original session.

Native attempt v1 failed before starting a JVM because the frozen test output lacked the source-root links required by `FixturePaths.RepoRoot`. Attempt v2 fixes only those links, pointing at the immutable source; the runtime DLLs and PDBs remain unchanged. The failed preflight stays separate from valid execution.

The first five-release basic batch passes 1.8, 1.8.1, 1.8.2 and 1.8.3, then fails exact placement packet order on 1.8.4: only use is sent, while native stone placement and stack 32→31 succeed. Fourteen distant falling blocks have unknown geometry, so the obstruction scan cannot prove the swing. Mining passes. All five servers exit successfully without forced cleanup and both ports close. `basic-01` retains the capture and assertions; later basic batches stop at this failure. Together with the three fresh pilots, seven distinct basic releases pass and one fails; the other 67 remain unexecuted. The protocol-107 fix does not close this protocol-47 case.

The fifty-protocol general smoke has not completed. General `FullFeatureLeg` smoke retains its creative/physics-disabled limits and skipped protocol-47 placement; it cannot replace the dedicated physics-enabled interaction pack. Native client-profile parity on protocol 47 remains the existing 1.8.9 profile; this server-version failure does not authorize release-specific profile selection.

## Publication and remaining work

The preceding published head `296cba6` has seven successful CI jobs in run `39634042389`. This report is a documentation-only follow-up to tested producer `e65329e`; its publication head requires a separate CI result. The PR remains open and draft. No merge, release, package publication or sibling PR is included.

Remaining acceptance includes axes and other specialized item/block families, full permission/component context, modern collision-aware motion and general dynamic geometry, riders/pushes, legacy/local-player gaps, contextual picking, the native 26.2 piston/network witness and the final version matrix. The cooldown readiness work also exposes an adjacent unresolved mining-charge distinction: `TickAttackChargeOnLoop` still uses movement eligibility, while native player ticks can advance attack strength before initial positioning on applicable modern versions. That gap is not closed by the cooldown tests.
