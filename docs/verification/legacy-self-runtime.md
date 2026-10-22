# Legacy local-player body and eye runtime

The local player now owns legacy sleep, wake and death callbacks through the always-installed `SelfApplier`, using the existing cached-geometry helper and physics owner. This supports the measured legacy profiles from protocol 47 through 404. The player is never inserted into `EntityStore`. Correct eyes feed view mining; the actual cached body feeds placement obstruction. Unknown callback context remains unknown.

## Behavior and ownership

`TrackedLegacySelfGeometry` reuses `TrackedLegacyPlayerGeometry` for native lifecycle arithmetic, bed lookup, occupied-state mutation and resize queries. Local input owns sneak state; a peer metadata crouch bit cannot replace it. Sleeping suppresses local input while external velocity still reaches the existing physics step. Zero-health metadata and health packets remain distinct from status 3, which applies the measured death body and velocity. Self velocity packets now reach that same physics owner when optional remote entity tracking is disabled.

The optional `PhysicsBodyDimensions` value and `PlayerPhysics.ApplyBodyGeometry` bridge carry native float width, height and eye height with the existing cached `PhysicsState.BoundingBox`. Position, velocity and a noncentered wake box remain distinct. Null keeps the existing pose/scale path. Explicit position reapplication recenters the box even when coordinates are unchanged; ordinary movement derives position from the translated cached box. Snapshot/load and forward simulation preserve the override. Public additions are listed in `Umpk.Physics/PublicAPI.Unshipped.txt`.

The session physics step imports the original movement result before running the legacy size phase. Protocol 47 has no general size phase. Respawn, world replacement and disconnect clear the owner. When required bed, resize or metadata context is unknown, movement and current-body reporting stop, interaction picking returns unknown, and derived air prediction retains the last server observation. The last observed eye is retained without presenting it as a proven current pick origin.

## Native evidence

Source bindings cover the 27 supported legacy named releases and fourteen datasets. Original local-player execution covers the four boundary clients 1.8.9/47, 1.9/107, 1.11/315 and 1.13.2/404, with an adjacent 1.14/477 control. Evidence is retained in `/home/anon/Projects/MinecraftOfficial/runs/local-legacy-lifecycle-pilot-20261022-v1`.

| Original execution | Observations |
| --- | --- |
| Constructor, listeners, lifecycle, size and eye callbacks | 1,344 cases and 7,680 snapshots. |
| Health metadata and health/status packet comparison | 32 cases and 160 snapshots. |
| Inherited entity movement | 96 cases and 480 snapshots, including zero movement after a noncentered wake. |
| Inherited living movement with original local input and player travel | 128 loaded/unloaded cases and 1,088 snapshots. |
| Correctly populated protocol-404 chunk provider | Sixteen cases and 148 snapshots, retained separately from the earlier unpopulated-chunk trace. |
| Adjacent protocol 477 | Eleven constructor/pose/eye snapshots; no legacy UseBed packet. |

These runs retain original class origins and official artifact hashes. World, bed, chunk, lookup, survival and audio inputs are controlled. The original local-player constructor and typed packet body listeners run; the inherited living phase runs nonvirtually on that real player while its local input, player travel and entity movement methods remain original. This does not establish the complete native outer input/UI phase, outer player/entity tick, world traversal or network transport.

The committed gzip fixture contains 24 complete selected rows, including original packet bodies. Independent review compares every field with its retained native row and verifies the official client hashes. Legacy native block-state packing converts explicitly to the existing canonical state representation. Runtime full-column fixtures reuse retained native bodies with only the two fixed column coordinates changed for the declared empty neighborhood.

The first protocol-404 loaded comparison used a real inserted chunk whose native populated flag remained false. It correctly measured the unloaded branch. A separate retained original provider-payload operation establishes populated state; its trace supplies the full-column runtime expectation. The earlier two comparison failures remain recorded. No production float, tolerance or assertion was changed to hide them.

## Executed runtime checks

The private delivery is an explicit overlay on `e60c36047d168660b8ffd758f489e300e6836a91`, frozen in `runs/local-legacy-self-delivery-20261022-v1`. Its seventeen source/resource paths are independently checked against their before and after hashes. All 4,233 private source entries, including twelve original skill symlinks, verify. The delivery changes no dataset, dependency, DI lifetime, package version, generated source or existing test expectation.

| Check | Actual result |
| --- | --- |
| New framed-session Client cases | 85 executed passes, zero skips. Real `UmpkClient` login/play and bound codecs drive public placement, view mining, lifecycle and session ticks. |
| New Physics bridge cases | Nineteen executed passes, including independent position/cache/eye state, exact correction, collision, snapshot/simulation and default-path controls. |
| Full private Client suite | 28,921 executed passes, zero skips. |
| Full private Physics suite | 715 executed passes, zero skips. |
| Release solution build and scoped format verification | Both pass. |
| Private Native AOT | 25,091,376 bytes, strictly below 25,165,824; exactly one executed passing smoke, zero skips. |
| AOT integrity | Sixteen primary DLLs, sixteen PDBs, 74 tested-output copies and all source entries remain unchanged. |

Tests assert decoded values, byte-identical bound-codec round trips, exact position/body/eye/velocity values, use/swing ordering and peer-observed disconnect. They cover entity tracking enabled/disabled, callback eyes with physics disabled, held sneak, missing beds, unknown wake, server air retention, same-world respawn and teardown. The deliberate baseline changes are +85 Client and +19 Physics cases.

Independent integration checks in `runs/local-self-root-integration-20261022-v1` build a fresh archive of `ec4cf085109e4639ff6b58268739b6ec4e4639d8` with exactly the reviewed seventeen-path overlay and deliberate count changes. Release solution restore/build, all 28,921 Client tests, all 715 Physics tests and scoped format verification pass; both suites execute every case with zero skips. The command/result arrays, actual TRX files and `finished.json` identify this archive and overlay explicitly. Builds run outside the shared checkout; the current committed-producer final gates remain separate.

Earlier harness failures and valid red tests remain separate. The first full private Client run has 28,893 passes and 28 existing pose-report failures across fourteen legacy datasets. The body override had left the public input-derived pose at standing. A focused bridge test reproduced that defect; the fix retains the existing input-derived pose report without running modern resize queries against the authoritative legacy body. All 28 original assertions then pass unchanged in the final full run.

## Evidence and remaining work

The frozen delivery manifest has SHA-256 `322809a202fa2d2ed05aaae8f96330e8d129b2bdf445665c4eb1ab819655e96f`; the exact native-fixture supplement is `6718d78384b63e0053719b2503c404894eb2c8ebecc4ebcca3914ed2a69d8e8d`. Independent root review verifies all 29 delivery/index files, 159 runtime evidence files, actual TRX results and the AOT binary in `runs/scale-creative-root-integration-review-20261022-v1/local-self-delivery-root-review.json`, SHA-256 `3256ff1cfc517fd954dce183189f813edc7982574514f2db15bf27fe9f86c5d1`. Exact commands, SDK 10.0.401 paths, logs, hashes and failed attempts are indexed by `report.txt` and `runtime-evidence-manifest.json` in the delivery.

The private proof does not replace checks on a committed merged producer; those fresh receipts follow below. Full native local-player outer tick/network/live acceptance, modern local pose lifecycle, special glide/spin travel and local riders remain open. This bounded runtime change does not close the full local-player acceptance row in the [completion ledger](interaction-completion.md).

## Merged immutable checkpoint

Producer `7ace56988430cf7650659a95813b2218fa2afb03` now passes its fresh checks independently of the earlier private overlay. Its archive is `runs/legacy-self-checkpoint-source-20261022-v1`. All 4,235 committed source entries, including symlink targets, match before and after validation, consumers, AOT and packaging. The ten pre-existing uncommitted report insertions and untracked logs are preserved outside this archive.

| Fresh check | Executed result |
| --- | --- |
| Repository and reconstruction | All 25 gates pass, including Release restore/build, full formatting, dataset/language checks and fifteen retained native reconstruction checks. |
| Full unfiltered .NET suite | 42,454 actual passes; fourteen exact named normal opt-in skips; zero failures. All seventeen baselines match, including Client 28,921 and Physics 715. The complete output is retained in `/tmp/umpk-test.log` and the immutable validation directory. |
| Python and generation | 356 extraction plus 23 artifact-tool tests pass; all 61 independently generated files match exactly. |
| Fresh consumers | DMCBK 1,999 and MCC 918 passes; all 33 dependency DLL comparisons match. Sibling HEADs, Git status and tracked/untracked source hashes remain unchanged. |
| Frozen MCC | All 576 files are pinned; manifest SHA-256 `f9ad7f256ffb3170efc2f4cdc4d0c84987f1e482c8905f23f2ee5489fdf94b26`. |
| Native AOT | 25,091,376 bytes, strictly below 25,165,824, leaving 74,448 bytes. Exactly one smoke test executes and passes, with zero skips. Binary SHA-256 `6c8e8e6759fea79310adb812d6edac4bd1922bdaf41f3501937d9e50e22eac6a`. |
| Local packages | Fourteen primary and thirteen symbol packages verify; all thirteen packaged libraries and thirteen fresh-cache consumer libraries match the tested producer. The isolated consumer constructs protocols 47 and 777. Nothing is published. |
| Final integrity | All 22 locked runtime DLL/PDB files remain unchanged after every check. |

Exact command arrays, working directories, timings, logs and hashes are retained in `runs/legacy-self-checkpoint-preparation-20261022-v1` and `runs/legacy-self-checkpoint-{validation,consumers,aot,packages}-20261022-v1`. Validation uses SDK 10.0.401 with `SourceRevisionId=7ace56988430cf7650659a95813b2218fa2afb03`, an unfiltered Release `dotnet test UMPK.sln --no-build --no-restore`, the documented seventeen-suite count gate and fresh generation into separate directories. No conformance update switch is enabled; existing allocation/performance checks and budgets remain unchanged.

The previously approved external consumer pose-fixture adapter still receives a real full chunk before its original assertions. Its unchanged approval metadata retains the original `e65329e` provenance. `fixture-reuse-current-producer.json`, SHA-256 `7c241b3673b5c849e47c5f222c32d2ebccc020afabb7049bd28bb15c2526cce6`, explicitly binds that adapter to the current fourteen focused pose passes and full 1,999-case DMCBK result. Sibling HEADs remain DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

The strict producer lock is `legacy-self-checkpoint-validation-20261022-v1/producer-lock.json`, SHA-256 `0c4b9c841c633b89d31b6db8bf1919638b5f6e6c4355fbd6c7663e98dfbf5274`. The independent cross-certificate in that directory has SHA-256 `cfa1098622991cc33ac74b0763e042783754d63a8ae1bac05c4e6e66e5a229eb` and verifies the receipts, actual TRX results, frozen consumer files, sibling snapshots, source and all 22 runtime hashes. Native menu and earlier motion/scale live evidence retain their separately identified producers; this checkpoint does not relabel them as local-player live proof. Published-head CI and the remaining behavioral/version acceptance require their own evidence.
