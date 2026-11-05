# Legacy resize clearance and early voxel grids

Protocols 393 through 490 now retain original voxel grids instead of answering collision queries with bounding boxes alone. Protocols 393, 401 and 404 also use the original strict plane comparison. The legacy player resize-query component matches the captured air/stone queries for every release from 1.9 through 1.13.2. Player lifecycle integration remains separate work.

Runtime commit: `7b30b507eae234f224c67801034862b6b7fdf9db`. The planning regression follow-up is `1958673acf8205a37a2583d1ebd10208d6d61a41`; its immutable archive supplies the final validation binaries. Both are on UMPK PR [#10](https://github.com/MCCTeam/UMPK/pull/10), targeting `master`.

## Native measurements

All runs below are under `/home/anon/Projects/MinecraftOfficial/runs/`. They preserve commands, original client/server hashes, source bindings, unchanged original class bytes, native class origins and run-owned probe sources. Java 11 executes the legacy clients. The owned adapters control world inputs; they do not replace the native clearance predicate.

| Run | Observations | Scope |
|---|---:|---|
| `pre-pose-player-client-clearance-20261018-v1` | 31,360 across fourteen releases | Original World bounding-box query, original client availability and loaded-block/validity methods; controlled air/stone lookup |
| `pre-pose-player-voxel-clearance-20261018-v1` | 6,720 across three releases | Original null-actor voxel query, original client availability and loaded-block/height guard; original border object |
| `pre-pose-player-size-phase-20261018-v3` | 19,968 across seventeen releases | Original Player size phase, original size/position/metadata methods and original world clearance; controlled starting dimensions, flags, sleeping/spin state and air/stone |
| `pre-pose-player-composite-clearance-20261018-v1` | 13,464 across three releases | Original null-actor query with eleven occupancy patterns over eight cells; exact faces, epsilon offsets and deterministic varied boxes |
| `pre-pose-collision-grids-20261018-v3` | 70,864 state grids across seven releases | Original default collision shapes at the origin; original empty reader in 1.14, owned air-only reader in 1.13 |
| `pre-pose-collision-grids-20261018-v4` | The same seven complete state registries | Finalized maintained runner, with each native working directory isolated inside its run |

The size-phase sweep controls inputs to the original operation. It does not execute complete player/world ticks or bed/death packet handlers. The clearance probes bypass the world constructor. Loaded-block samples are separate direct observations; the query traces record native block reads. They are not instrumented traces of every loaded-block call.

## Behavior boundaries

| Protocols | Original resize-query behavior |
|---|---|
| 107–109 | Loaded-block validity rejects out-of-range absolute X/Z before querying the client; bounding-box scan skips upper and lower side padding |
| 110–315 | Client allow-empty availability reaches the query's absolute-coordinate rejection; upper and lower side padding remain skipped |
| 316–340 | Lower side padding is queried; upper side padding remains skipped |
| 393 | Null-actor voxel query, X/Z/Y scan and strict query-first plane merging |
| 401, 404 | Z/Y/X scan, two-edge cells excluded and deferred full-cube results; strict query-first plane merging |

The original client availability returns true for the query's allow-empty request. A null actor contributes neither entity colliders nor a border wall. The runtime component follows those contracts; it does not use the local-player collision query's entity or border checks.

The original 1.13 merger keeps planes exactly one epsilon apart: its comparison is `previous > next - 1e-7`. All four early 1.14 releases use `>=`. The composite replay exposed eleven mismatches per 1.13 release before `QueryFirstStrict` was added. The corresponding original merger classes are `cee`, `ceo`, `cep`, `csi`, `csk`, `csk` and `cso` in release order. Their source and original class hashes are retained in `voxel-merger-source-audit.json` with the working evidence.

The player size-phase sweep also captures sleep-width growth at X=1.5: the proposed maximum becomes `2.000000022351742`. The original bounding-box eras reject a stone block at X=2; the original voxel eras accept the resize. Bounds remain float-derived, and resize candidates retain the current minimum. Position reapplication remains a separate operation.

## Data and runtime

The new canonical tables cover 8,582 states for 1.13, 8,599 each for 1.13.1 and 1.13.2, and 11,271 each for 1.14 through 1.14.3. All 39 grid tables now contain 797,628 state rows. DataGen writes the descriptors and packed tables; no generated C# was edited directly.

Native 1.13 represents its empty shape with three zero-length coordinate lists. The model, dataset validator and extraction validator now preserve that representation. Partial empty axes, occupied cells without planes, boxes without planes, false canonical identity and invalid offsets remain rejected.

`PhysicsCollisionQuery.IntersectsBlock` exposes the existing state-only grid/AABB intersection without owning block iteration or querying entities and borders. `LegacyPlayerResizeClearance` supplies the measured pre-pose client scan profiles. The normal physics and planning profiles select strict voxel queries from protocol 393 and ordinary query-first merging from 477. Later block-first and modern profiles retain their existing comparisons and full-cube shortcuts.

The single-block/resize fixture replays 39,508 queries across seventeen releases. The composite fixture replays another 13,464. All 52,972 comparisons pass. The generated-grid regression compares native coordinates, occupied cells, boxes, offsets and canonical identity for every supplied runtime grid; contextual exclusions remain explicit.

Default grid extraction does not establish neighbor, block-entity or actor-context variants. Entity-context substitutions and position-offset models retain their existing capability paths. These checks do not establish complete movement parity or connect pre-1.14 player geometry to the session loop.

## Reproduction

The maintained extraction commands and fixture reconstruction are in [the extraction guide](../../tools/extraction/README.md#early-voxel-grids-and-legacy-resize-queries). `extract_legacy_collision_grids.py --check` verifies the committed data against the recorded v3 run. `extract_legacy_player_clearance.py --check` reproduces both gzip fixtures from the original query and size-phase captures.

The focused .NET commands selected `LegacyPlayerClearanceTests`, `GeneratedCollisionGridCoverageTests`, `PhysicsProfileConformanceTests`, `PlanningCollisionNativeTests`, `WorldCollisionGridNativeTests`, `BlockCollisionGridTests`, `CollisionGridReaderTests`, `CollisionGridDatasetTests`, `BlockCollisionGridNativeTests` and `PhysicsCollisionQueryTests`: 310 cases passed. The planning follow-up passed twelve focused `PlanningCollisionGridTests` cases. All 211 Python extraction tests passed.

The corrected exact-commit full run passes 34,638 tests with seven opt-in skips. All seventeen suite totals match the baseline. The deliberate additions are 25 Client, six Game, five DataGen, one Data.Java, one Physics and three Pathfinding cases.

All eleven required exact-commit gates pass under `pre-pose-collision-validation-20261018-v2`, including build, format, data, language, extraction and a byte-for-byte comparison of all 61 generated outputs. All 4,044 archived source blobs and links remain unchanged.

DMCBK's 1,999 tests and MCC's 918 tests pass against that producer under `pre-pose-collision-consumers-20261018-v2`. All 33 dependency-binary comparisons match, and both sibling source trees and Git states remain unchanged. Their checkout heads remain `af22688e783fb2876ad55f06e0654a7a4c6c5641` and `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

Native AOT publish and smoke pass under `pre-pose-collision-aot-20261018-v1`. The executable is 24,727,168 bytes against the unchanged 25,165,824-byte limit; SHA-256 is `ef106284994be77d1296a8d62b92964212dca4145936f8b91c52676ffe00b570`. Producer binaries remain unchanged.

Two fresh vanilla `LiveServerTests.FullFeatureLeg` cases pass for 1.13.2 and 1.14.3 under `pre-pose-collision-live-20261018-v1`. They use the exact producer and isolated server copies, exercise real login/world/entity/inventory/mining/placement/lifecycle behavior and close owned ports 25599/25600. This harness disables local locomotion physics, so those cases do not establish voxel movement or the unconnected legacy player geometry phase.

The source archive, source checks, native audits and completed receipts are collected under `pre-pose-collision-proof-20261018-v1`. All seven jobs in [CI run `39159422715`](https://github.com/MCCTeam/UMPK/actions/runs/39159422715) pass: Linux, macOS, Windows, format, dataset/generated freshness, packages and AOT.

## Retained failures

The first exact full run at runtime commit `7b30b50` fails one Pathfinding test because it still treats newly defined enum value 4 as invalid. The follow-up tests value 5 as invalid and adds positive strict-profile cases. The count gate rejected the original failed run. No allocation budget was changed; the corrected unfiltered rerun passes.

The first single-block replay had an invalid-height cleanup error in the test harness; its corrected red run exposes seventeen behavior failures. The first grid campaign rejects the native 1.13 empty coordinate lists; later campaigns preserve them and pass. The first size-phase campaign fails because its actor adapter was absent from the runtime classpath; its corrected pilot and full sweep are separate runs. An initial source-archive setup also stopped on the archive's empty submodule directory before any gates executed. None of those interrupted or rejected runs is counted as a pass.

## Remaining player work

The original 1.8–1.13.2 sleep, wake and death lifetimes still need handler measurements and runtime ownership. Initial construction, metadata callbacks, eligible world ticks, resize attempts and position reapplication must remain distinct. The original world tick's added-to-chunk and player/area checks also need native client-world proof before selecting a loaded-area gate. This change supplies the collision-query foundation for that work.
