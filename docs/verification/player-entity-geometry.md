# Player placement geometry

Player entities previously had no canonical geometry entry, so placement prediction suppressed a swing whenever another player was tracked, even if that player was far from the destination. Native player pose rules now cover 44 releases from 1.14.4 through 26.3 and 32 protocols, 498–777. Attribute scale remains at its measured default.

The original client probes record 27,981 complete pose-pair and position transitions at three positions. Separate entity snapshots establish constructor building flags, pose metadata, dimensions and fixed sleeping/dying sizes. Protocol aliases must agree. Older clients retain the minimum corner when dimensions grow; 1.17 and later recenter. The extractor verifies every phase, original class hashes and captured class origins, official client/server hashes, adapter source hashes and callback owners before merging player rules into the existing datasets. All pre-existing entity entries and provenance remain unchanged.

The actor is a survival Player adapter that inherits the original geometry methods. A separate source audit covers RemotePlayer and AbstractClientPlayer in all 44 releases and finds no overrides of the measured geometry callbacks. These probes exercise constructors and individual pose/position callbacks with controlled client-world inputs. They do not execute complete entity/world ticks, spectator lookup, attribute-scale transitions or rider positioning.

| Check | Observed result |
|---|---|
| Placement regression before extraction | 155 of 288 cases missed a required swing; the other cases retained their expected no-swing result |
| Native replay before generation | All 44 release cases failed because player geometry was unknown |
| Generated geometry | All 288 placement cases and 44 release replays pass; each release replay checks every native transition through the real spawn and metadata appliers |
| Session interpolation regression | Two protocol boundaries initially retained a stale box after an active interpolation step with unchanged coordinates; both now pass |
| Full solution tests | 34,275 passed, zero failures, seven opt-in skips |
| Test-count baseline | Client increases from 20,620 to 20,954 for 288 placement cases, 44 native replays and two session-loop cases |

The session fix reapplies tracked dimensions whenever the existing remote-player interpolation controller calls its position setter, including equal-coordinate steps. It leaves an idle player's retained box intact. The packet coordinate base and movement events retain their existing owners. The tests for protocols 498 and 754 use framed packets and the real client session loop with physics disabled; the native source calls `setPos` on each active interpolation step in those eras. Modern collision-aware controllers remain disconnected until their world query is implemented.

The larger replay fixture uses real add-player packets before 1.20.2 and generic player spawn packets thereafter. Pose metadata is encoded and decoded with the matching protocol codec. The replay explicitly invokes the geometry owner's position callback to match the native fixture's separate position phase; it does not claim that a zero-delta network packet applies position immediately. The two session-loop cases verify the actual deferred interpolation behavior separately.

Use `/home/anon/Projects/MinecraftOfficial` as the evidence root. `runs/player-box-transitions-all-20261017-v1` contains the new original-client callback sweep, helper hashes, class-origin logs and verification receipts. `runs/placement-entity-native-all-20261017-v1` contains the independent snapshots. `runs/player-geometry-validation-20261017-v2` records exact commands, source hashes, exit codes, test output and generated comparisons. The completed proof is preserved in `runs/player-entity-geometry-20261017-v1`. The reproducible commands are in [the extraction guide](../../tools/extraction/README.md).

Earlier test attempts included sandbox-blocked MSBuild pipes, a missing test packet constructor argument and a missing legacy join-fields block. These harness faults are retained separately from the reproduced runtime failures. The first full validation passed every gate except extraction reproducibility: source-provenance keys depended on Python set iteration. A regression reproduced the different output under Python hash seeds 1, 7 and 19. Source hashes are now emitted in sorted order; all six focused extraction tests pass. The first validation remains recorded in `runs/player-geometry-validation-20261017-v1`. The focused runtime run passes all 334 cases. No public API, conformance pin, allocation limit or sibling repository source changes are part of this batch.

Fresh DMCBK/MCC consumer tests, Native AOT and live multiplayer checks for this batch remain pending. The earlier Shulker consumer/live/AOT proof belongs to its recorded commit. Older player eras, nondefault scale, spectator query/cache behavior, riders, modern collision-aware interpolation and specialized placements remain unfinished.
