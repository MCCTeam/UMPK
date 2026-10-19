# Legacy player packet runtime

This work follows the verified `dd14241` checkpoint and the completion ledger introduced in `9d08f91`. It connects the existing native lifecycle helpers to remote players identified by the spawn packet. Registration precedes initial metadata and spawn publication, including legacy datasets whose registry name does not identify a player. This report covers a production slice of milestone C; it does not close the milestone.

## Runtime ownership

`EntityGeometryState` owns `TrackedLegacyPlayerGeometry` alongside the tracked Shulker adapter. The adapter keeps cached bounds, observed position, dimension validity, metadata validity and bed context separate. Shared flags and 1.13 spin flags are observed without running the size phase. Explicit positioning reapplies the cached box even when the coordinates are unchanged. Removal, replacement IDs and world replacement use the existing geometry teardown.

`EntityApplier` routes UseBed, Animate action 2 and EntityEvent status 3 through this owner before publishing the associated move/status event. Known sleep clears current velocity through `EntityMotionState`; it does not create another velocity controller or discard a pending interpolation target. The status-3 integration currently applies death geometry; complete death health/velocity behavior and its prior-state dependencies remain acceptance work. Health metadata does not invoke the death callback.

Wake restores standing dimensions before the exit scan. The scan stops at its first unknown candidate instead of treating it as blocked and proving a later exit or fallback. Missing world context makes geometry unavailable. A known removed bed uses the measured no-position-reapplication path. An intact bed clears occupancy through the tracked world before the query. Legacy state IDs retain facing and part while clearing occupancy; flattened states select the same block with matching non-occupancy properties. Native float-before-double coordinate arithmetic, failed resize behavior and distinct bed-read/resize-query boundaries remain in the existing helpers.

## Original native observations

The oracle root is `/home/anon/Projects/MinecraftOfficial`. These run directories retain exact commands, verified official client/server hashes, source hashes, original-class equality checks, helper hashes, class origins, observations and harness logs. No game artifacts or raw logs are committed.

| Run under `runs/` | Executed result | Scope and limits |
| --- | --- | --- |
| `pre-pose-player-listener-all-20261019-v1` | 27 releases, 12,672 cases, 70,272 snapshots and 25,344 original packet dispatches | Original serialization, decoding, byte-identical packet round trips and typed sleep/wake/status listeners. Entity lookup and block inputs are controlled; Minecraft/world constructors and audio are excluded. The 35,136 direct-callback snapshots match retained lifecycle results; another 35,136 retain a pending interpolation target through those callbacks. |
| `pre-pose-player-native-world-all-20261019-v1` | 27 releases, 3,888 cases and 11,664 snapshots | Original client-world constructor, providers/chunks, per-entity World phase and full RemotePlayer tick methods. Initial membership/loading is controlled. This campaign excludes outer list traversal, chunk packet dispatch, mounts and the local player. |
| `pre-pose-bed-occupied-world-all-20261019-v2` | 27 releases and 1,152 original mutations, reduced to 944 canonical state inputs across 14 protocols | Original paired-bed storage and wake call through World.setBlock with flag 4. Every output has occupied=false and preserves every other decoded property. Initial paired-bed setup uses flag 20 to avoid neighbor propagation during setup. Minecraft/audio/world ticks are excluded. |
| `pre-pose-player-traversal-all-20261019-v1` | 27 releases, 864 scenarios and 14,688 phase snapshots | Original insertion, chunk serialization/decoding/listeners, complete entity-list traversal and full RemotePlayer ticks, followed by the original world reentry/provider phase in Minecraft source order. The original world constructor runs. Minecraft construction/audio and the ambient block/particle phase are excluded; an inert allocated local-player reference satisfies lookup. No vehicle or local-player ticks are claimed. |

The four-release boundary pilot covered 1.8.9, 1.9, 1.11 and 1.13.2 before each expanded campaign. Failed listener-library/audio setup, the initial unsafe-world setup and the missing local-player reference/chunk-constructor traversal attempts remain harness failures under their original run directories. The occupied-bed v1 run lacks the strict class-origin and method-owner receipt added in v2 and is not the fixture authority.

The traversal scenarios cover spawn without terrain, full terrain before/after insertion, empty chunks, delayed terrain, unload/reload, replacement IDs and crossing a chunk boundary, at positive/negative coordinates and low/high Y. They demonstrate that added-to-chunk state differs from current chunk availability: missing terrain can delay the first interpolation tick, while an unload/reentry can retain previously established membership. Protocol 47 differs from later clients. These facts must be integrated without a universal radius-32 player gate. They do not yet prove vehicle traversal, multi-actor reentry limits or session-owned tick eligibility.

## Reproduction and verification

The maintained occupied-bed importer verifies the 27-release cohort, official artifacts and source identities, original class origins, method owners, complete canonical state coverage, alias agreement and native/wire state consistency before producing `LegacyBedOccupied.native.json.gz`.

```bash
python3 tools/extraction/extract_legacy_bed_occupied.py --measurements /home/anon/Projects/MinecraftOfficial/runs/pre-pose-bed-occupied-world-all-20261019-v2 --official /home/anon/Projects/MinecraftOfficial --data data/java --out tests/Umpk.Client.Tests/Fixtures/LegacyBedOccupied.native.json.gz --check
python3 -m unittest discover -s tools/extraction
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore
```

The 75 new Client cases comprise 27 native-release sleep/removed-bed comparisons, 27 death-geometry/event-order comparisons, 14 occupied-bed protocol cases over all 944 inputs, four unavailable-world controls, two unknown-terrain/replacement/world-reset cases and one first-unknown scan control. Bound production codecs feed the applier tests; decoded positions, bounds, velocity and world state are asserted. These tests do not substitute for framed session-clock and live-server acceptance.

Initial runtime tests failed as expected before the adapter was connected. Existing lifecycle/wake tests passed after each production-file change. The occupied-state tests independently failed before the mutation resolver was added. Focused lifecycle/runtime tests then passed (173 cases), adjacent geometry/motion/applier tests passed (8,610 cases), and the full Release Client suite passed (21,502 cases, zero skips). The deliberate Client count change is +75. All 249 Python extraction tests, including seven importer rejection/coverage cases, passed before the later traversal importer was introduced. Formatting succeeded; an earlier sandboxed attempt could not connect to the build-host pipe and is a harness failure.

The complete repository gates, fresh consumers/AOT, packet-driven session ticks, physics-enabled live behavior, local-player sleep/wake, vehicle traversal and remaining lifecycle context have not passed for this production slice. Preserve the checkpoint's historical receipts under `dd14241`; do not relabel them as validation of the new runtime code.
