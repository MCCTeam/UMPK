# Legacy player lifecycle geometry

`VanillaLegacyPlayerGeometry` models explicit sleep, wake after bed removal, death-size and position callbacks from 1.8 through 1.13.2. It also applies the measured size phase through the existing block-clearance component. Runtime wiring remains unfinished: the session does not yet feed legacy bed/death packets or eligible player ticks into this component.

Implementation commit: `ea8ea06e98025fcb66da300acd3aceff98d0fad1`. Validation uses its immutable archive, with all 4,051 source blobs and links checked before and after the run. UMPK PR [#10](https://github.com/MCCTeam/UMPK/pull/10) remains a draft targeting `master`.

## Native evidence

The following runs live under `/home/anon/Projects/MinecraftOfficial/runs/`. Each retains exact commands, Java 11 probe sources, original client/server hashes, source bindings, unchanged original class bytes and native class origins. Owned adapters supply controlled world inputs and suppress audio side effects.

| Run | Coverage | Executed operation |
|---|---:|---|
| `pre-pose-player-lifecycle-1.8-20261018-v1` | Ten releases, 1,440 cases, 5,760 snapshots | Original sleep, wake after bed removal, status 3 death and position callbacks |
| `pre-pose-player-lifecycle-all-20261018-v1` | Seventeen releases, 4,896 cases, 29,376 snapshots | The same callbacks, plus original player size phases and native client-world clearance |
| `pre-pose-player-size-phase-20261018-v3` | Seventeen releases, 19,968 cases | Original size phase with controlled starting dimensions, shared flags, sleeping/spin state, irrelevant flag noise and air/stone context |
| `pre-pose-player-world-tick-all-20261018-v1` | Twenty-seven releases, 3,888 cases, 11,664 snapshots | Original World tick eligibility and native chunk registration, with an owned actor-tick counter |

The lifecycle sweep covers all four bed facings, standing/crouching/fall-flying flag combinations, six positions, death while sleeping and explicit position reapplication. The 1.8 family has no general player size phase. An independent model matches all 35,136 lifecycle snapshots, including 4,224 transient offsets after waking.

The world sweep covers no loaded chunks, the current column and a 32-block area; controlled or native initial registration; forced and unforced calls; negative/high Y; and three consecutive phases. In the empty-chunk case, 1.8 World assigns membership during its registration step. From 1.9, the native empty chunk leaves an unregistered player unregistered. This proves the measured World component with those inputs. It does not execute entity-list traversal, real chunk packets or complete actor ticks.

## Measured boundaries

| Protocols | Size and sleep behavior |
|---|---|
| 47 | Explicit callbacks resize the player; crouching metadata has no general size phase |
| 107–210 | Shrink and growth retain the cached minimum; sleep uses literal directional offsets |
| 315–340 | Shrinking recenters; sleep computes directional offsets through float multiplication and addition |
| 393, 401, 404 | Swimming and living spin flags join the size priority; original voxel clearance applies |

Fall flying takes priority over sleeping, followed by swimming/spin and crouching. A resize candidate starts at the current cached box minimum. A rejected attempt preserves both size and box and can be retried during a later phase. The unchanged clearance profiles retain their original absolute-coordinate and epsilon behavior.

Sleep converts the bed coordinate to float before adding its float offset. Near zero, the 1.11 expression produces `0.09999999403953552` for the north/west offset. Large odd coordinates can round by a whole block before widening back to double. The fixture preserves both cases.

Waking after bed removal restores standing dimensions without reapplying position, so the box can stay offset. Death shrinks and explicitly reapplies position while preserving the sleeping state. A later size phase can expand the box again even at zero health. The geometry component leaves health, horizontal/vertical velocity, sounds and other death effects to their owning runtime operations.

## Verification and limits

All 44 new .NET theory cases pass. They replay the lifecycle snapshots and size phases through the component and the measured clearance query. The focused run passes 8,923 cases across geometry, remote-player, player-info and clearance tests, including the additions. The fixture importer checks complete matrices, aliases, artifact/class/source hashes and native method owners; its ten new Python tests cover rejected incomplete or replaced observations.

All twelve exact-commit gates pass under `pre-pose-player-lifecycle-validation-20261018-v1`: restore, build, full tests, counts, format, dataset, language, Python, collision-grid reconstruction, clearance reconstruction, lifecycle reconstruction and generated output. The unfiltered full run passes 34,682 tests with seven opt-in skips. All seventeen suite totals match the baseline; Client deliberately adds 44 cases. All 221 Python tests pass, and all 61 generated files match the committed outputs.

The initial test build stopped on an unused theory parameter. After adding the release identity assertion, all 44 behavior cases failed against the initial implementation and passed after implementation. The independent lifecycle verifier also initially assumed that flag setup always resized the actor; the corrected model applies the measured boundary query during setup. Original native outputs remain unchanged. Neither rejected run is counted as a pass.

Fixture reconstruction is documented in [the extraction guide](../../tools/extraction/README.md#legacy-player-lifecycle-geometry). The lifecycle gzip SHA-256 is `e233bfaae66e28f82ffe59118ffce452f1b986cd1c7beb4c452deb4dac7583c1`; the size-phase gzip SHA-256 is `52feb55a00ef1b5f452ec3cab61d82aa4fe9031f8af8e15c15ee283e8229d335`. Every imported observation was compared with its original measurement field by field.

Intact-bed wake exits, unavailable-bed behavior, native packet-listener execution and complete player ticks remain unverified here. Bed-facing data, tracked runtime ownership and chunk-packet transitions are subsequent work. Consumer and live-server checks from earlier commits retain the scope recorded in [the clearance report](legacy-resize-clearance.md); this component has no live session caller yet.
