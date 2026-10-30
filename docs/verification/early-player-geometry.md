# Early 1.14 player placement geometry

Placement could reach the vanilla server without a swing when another player was tracked: UMPK had no player geometry for protocols 477–490, so its local obstruction check remained unknown. The new rules use measured original-client dimensions and callbacks for 1.14 through 1.14.3. Existing entity geometry and pose codecs handle the change; no public API or dependency changed.

| Release | Protocol | Native pose/position transitions |
|---|---|---|
| 1.14 | 477 | 147 |
| 1.14.1 | 480 | 147 |
| 1.14.2 | 485 | 147 |
| 1.14.3 | 490 | 147 |

Each sweep covers all seven native poses, every ordered pose pair and three positions, including negative coordinates and the absolute world boundary. The separate snapshots verify constructor building flags, pose metadata index 6, dimensions and actual callback owners. Width growth retains the current minimum corner; explicit position reapplication recenters the box. Sleeping and dying use the original fixed float dimensions. Native `SNEAKING` maps to canonical crouching; native dying ordinal 6 passes through the existing codec as canonical dying 7.

The extractor checks official client/server hashes, unchanged original client classes, captured class origins, adapter hashes and each source owner. Its source checks require the original inheritance chain, first-call metadata delegation from LivingEntity to Entity, the pose setter, dimension getter, refresh callback and placement building predicate. The probe constructs an original RemotePlayer in an owned survival subtype with controlled empty-world inputs. It does not execute packet handlers, full player/world ticks, rider positioning or blocked resize queries.

## Local checks

Runtime commit `53df8815ecde5834c5dc759f8600d721ae0c5e34` adds four native replay cases and 36 placement cases. The Client baseline increases from 21,236 to 21,276. Before the datasets changed, the focused run failed 24 cases; afterward all 372 cases passed. The wider focused run passed 9,779 tests.

The complete Release run passes 34,597 .NET tests with seven opt-in skips, all 17 suite totals match, and 205 Python tests pass. Build, format, dataset, language, both player extractors, player-info extraction and all 61 generated outputs pass. The generator shares the early geometry table with 1.14.4 because their independently measured bytes match exactly; existing entity profiles remain unchanged.

The initial full run failed the unchanged sound-name allocation test once at 147 bytes per call against its 128-byte budget. The isolated test, complete data suite and repeated unfiltered full run passed with the same binaries and unchanged limits. The cause of that initial failure is not established. Both full logs and count-gate results remain in the evidence directory.

## Consumer and live checks

All 4,026 source blobs and links match the runtime commit. Fresh DMCBK and MCC runs pass 1,999 and 918 tests, and all 33 binary comparisons match the tested producer. Source hashes and Git states for both sibling checkouts remain unchanged.

Native AOT publication and the fake-server smoke pass. The executable is 24,534,656 bytes against the unchanged 25,165,824-byte limit, with SHA-256 `ba7b1f654e76161c3410c90f9cf328cd76e39f3efaaff67b062d0928e0d12fc8`.

Two actual MCC clients pass eight placement scenarios on each of the four vanilla releases: 20 accepted placements send 20 swings, and 12 obstructed placements send none. Scenarios include distant and overlapping peers, a crouching/standing peer on a lower half slab, and a peer-removed control. Independent server checks verify block placement, stack counts, occupancy and peer position. All 487 selected captured frames round-trip byte for byte, including real peer spawn, pose changes, removal and outbound use/swing order. Owned processes stop and their ports close.

The previous binaries reproduce four missing swings per leg on 1.14 and 1.14.3; the peer-removed controls still swing. All 241 selected baseline frames round-trip exactly. The first 1.14.3 baseline attempt stopped after an RCON authentication connection closed; its three completed scenarios are not counted as a passing leg. A fresh isolated run completed all eight scenarios. The failed run remains available.

## Evidence and remaining work

Use `/home/anon/Projects/MinecraftOfficial/runs` as the evidence root. Native measurements are in `legacy-player-geometry-20261018-v2`; the original narrower sweep is in `legacy-player-box-transitions-20261018-v1`. Initial gates and the unchanged full rerun are in `legacy-player-geometry-validation-20261018-v1` and `legacy-player-geometry-validation-recheck-20261018-v1`. Exact producer and consumer receipts use the `legacy-player-geometry-producer-20261018-v1` and `legacy-player-geometry-consumers-20261018-v1` directories.

Live runs are in `legacy-player-placement-live-20261018-v1`; both baseline attempts are in `legacy-player-placement-baseline-20261018-v1` and `legacy-player-placement-baseline-20261018-v2`. The source archive, commands, capture audits, AOT receipts and hash ledger are collected in `early-player-geometry-proof-20261018-v1`. The extraction command is documented in [the extraction guide](../../tools/extraction/README.md).

Pre-1.14 player sleep/wake/death and blocked resize behavior remain unfinished. Nondefault scaling, passengers and pushing, modern collision-aware interpolation, other dynamic geometry, specialized placement and the remaining input/version matrix also need implementation and live validation. These four live legs prove the listed placement scenarios, not that every server or player state is supported.
