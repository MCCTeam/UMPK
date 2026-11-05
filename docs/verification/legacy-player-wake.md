# Legacy intact-bed wake geometry

`VanillaLegacyPlayerGeometry.WakeFromIntactBed` now matches the original wake geometry, integer exit and candidate order across all 27 releases from 1.8 through 1.13.2. Implementation commit: `9295d408fdbf4db71ee6564d0b847244fa0afe6d`. The caller still owns the occupied-state update, support/material predicate and eligible runtime callback. The session has no caller for this operation yet.

The original Player wake callback restores standing dimensions before querying two overlapping 3×3 areas, scanning X then Z and shifting the second area backward along the bed facing. The first eligible cell wins. If none qualifies, the original fallback is the cell above the bed. The position uses float X/Z plus 0.5 and Y plus 0.1 before widening to double; only then does wake clear the sleeping flag.

## Native evidence

`/home/anon/Projects/MinecraftOfficial/runs/pre-pose-player-intact-wake-all-20261018-v1` contains 31,680 controlled cases and 175,680 snapshots. An independent model checks all 21,120 intact-bed wake callbacks, including the original integer exit, final position/box/size, sleeping state and occupied-state write arguments. The wake-specific verifier does not claim the other phases in those snapshots.

Five native neighborhoods cover no supporting floor, a complete stone floor, obstructed body cells, a single supported exit only in the second search area and an obstructed first candidate. Four bed facings, six positions, shared flags and death while sleeping remain distinct inputs. The original exit finder is also executed directly on the unchanged neighborhood after wake, so float rounding cannot hide a different integer selection.

Owned adapters supply bed/stone/air states and record the occupied-state update with flag 4. Original sleep/wake, size/position methods, support/material predicates and exit selection execute from unchanged vanilla classes. World construction, native World.setBlock implementation/notifications, audio side effects, full actor/world ticks and packet listeners remain excluded. The run records exact commands, artifact hashes, source bindings, original class-byte equality and native class origins.

The wider sweep establishes the direct block-read boundary at protocol 110, Minecraft 1.9.3: earlier reads validate absolute X/Z and height, while later reads use the height guard. This differs from the separate legacy resize query, which retains its own absolute-coordinate rejection through protocol 340. A limited three-version pilot initially suggested a later boundary; the all-release verifier rejected that assumption, and source inspection corrected the model without changing native observations.

## Checks and remaining ownership

All 27 new .NET theory cases pass, replaying 21,120 native wake results and candidate sequences. The combined wake/lifecycle/clearance run passes 91 cases. Six new Python tests reject incomplete matrices, duplicate cases, missing callbacks, altered occupied-state flags and overlong scans. The fixture importer checks all artifact/class/source hashes and callback owners before writing deterministic gzip output. Every imported row and candidate sequence was compared with its original measurement.

All fourteen exact-commit gates pass under `pre-pose-bed-wake-validation-20261018-v1`: 34,759 .NET tests, seven opt-in skips, 234 Python tests, matching totals for all seventeen suites and identical output for all 61 generated files. All 4,076 immutable source blobs and links remain unchanged. Client deliberately adds 27 cases to the baseline. The first test build stopped on a missing version-table import; the corrected red run exposes 27 behavior failures before implementation, and the green run passes.

Reconstruction is documented in [the extraction guide](../../tools/extraction/README.md#legacy-player-lifecycle-geometry). The original wake fixture stores three deduplicated observation profiles and all 27 release proofs. Existing consumer and AOT results retain the exact bed-data producer commit recorded in [the bed-facing report](legacy-bed-facings.md); this internal component has no live runtime caller.

Per-state support/material data and packet/tick ownership remain unfinished. Collision boxes cannot replace the original support and material predicates. Native rejected legacy metadata must remain unknown rather than receiving a guessed cell rule.
