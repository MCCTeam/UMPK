# UMPK Java jar-driven extractors

These programs parse the official `server_mappings.txt` and reach Minecraft types by reflection, so an unmodified obfuscated server jar can be driven with no remap, ASM or compile-time game dependency. Unobfuscated jars use the same accessors directly.

- `ShapeDump.java` dumps per-state collision AABBs (the rest of this README).
- `CollisionGridDump.java` preserves exact default collision grids, occupied cells and the native full-block singleton identity. Use it when voxel intersection needs information that an AABB decomposition omits.
- `PushDump.java` dumps the four per-state values `PistonBaseBlock.isPushable` and `PistonStructureResolver.resolve` read: `getPistonPushReaction()`, `getDestroySpeed()`, `hasBlockEntity()` / `Block.isEntityBlock()`, and `isAir()`. `tools/extraction/extract_push_reactions.py` folds its output into `data/java/<protocol>/block-push.json`. The mapping parser is duplicated between the two on purpose: both are standalone single-file programs compiled and run by hand, and nothing in the gate would catch a refactor of the verified one.
- `MiningDump.java` reuses `PushDump`'s mapping resolver to measure every state's hardness and tool requirement, and every default item stack's mining speed and harvest capability. It binds the artifact's own vanilla block tags before measuring tools, including wool and leaves for early shears. Later jars also require initialization of delayed default item components. `tools/extraction/extract_mining.py` checks the complete state and item sets against the canonical dataset and refuses to collapse state-dependent answers.
- `EntityTeleportDump.java` reuses the same resolver to execute modern native position, rotation and velocity transforms. Compile it with `PushDump.java` and `MiningDump.java`; `measure_entity_teleport.py` supplies the isolated client classpath and verifies artifact, class-byte and class-origin evidence. All nine relative flags are measured in every combination. No entity or world is constructed by this dumper.

## Exact default collision grids

Compile `CollisionGridDump.java` with `PushDump.java` and `MiningDump.java`. Use the isolated unsigned classpath described below and the mappings for that exact client or server artifact; pass `none` for unobfuscated jars. Run Java from a temporary working directory so vanilla bootstrap logs stay outside Git.

```bash
javac --release 11 -d /tmp/umpk-collision-grid-classes tools/extraction/shape-extractor/PushDump.java tools/extraction/shape-extractor/MiningDump.java tools/extraction/shape-extractor/CollisionGridDump.java
java -Xmx1G -cp /tmp/umpk-collision-grid-classes:/tmp/vanilla-cp CollisionGridDump none /tmp/collision-grids-26.3.json
python3 tools/extraction/verify_collision_grids.py /tmp/collision-grids-26.3.json
python3 -m unittest discover -s tools/extraction -p test_verify_collision_grids.py
```

The schema-1 dump maps each native state id to a deduplicated group. Each group records the three coordinate-plane lists, occupied cell indices in `(x * y_size + y) * z_size + z` order, exact AABBs and whether the original shape is `Shapes.block()`. Coordinates retain their native doubles. The verifier checks complete state references, ordered finite planes, cell indices, box alignment and matching occupied volume. Legacy wall shapes can retain occupied cells with coincident planes and zero geometric width; the dump preserves those cells. The extractor refuses an existing output and publishes the JSON only after every native state succeeds.

These are default shapes measured with `EmptyBlockGetter` and `BlockPos.ZERO`. Dynamic block entities, neighbor state, entity collision context and position offsets still need their own inputs. Matching box volumes does not establish matching voxel intersections: retained grid planes can change epsilon comparisons. A full unit box also does not, by itself, prove the native singleton identity. Keep raw dumps outside Git and record artifact hashes and class origins before using them as evidence. This extractor does not modify canonical datasets or install runtime behavior.

Use Java 11 for older jars when required, or the existing logging shim with a recent JVM as described under mining measurements. The standalone extractor does not need a client/world adapter or a live server.

## Mining measurements

Compile `PushDump.java` and `MiningDump.java` together with `javac --release 11 -d /tmp/umpk-mining-classes`. Use the unsigned exploded classpath described below, including the artifact's `data/minecraft/tags/` resources. Keep all intermediate files outside the repository.

```bash
java -Xmx2G -cp /tmp/umpk-mining-classes:/tmp/vanilla-cp MiningDump none /tmp/vanilla-cp /tmp/mining-26.3.json
python3 tools/extraction/extract_mining.py 777 --version 26.3 --dump /tmp/mining-26.3.json --data data/java --official "$MINECRAFT_OFFICIAL_ROOT"
python3 -m unittest discover -s tools/extraction -p test_extract_mining.py
```

For mapped jars, replace `none` with the version's `server_mappings.txt`. Use Java 11 for 1.14.4 through 1.16.5. With a recent JVM, 1.17 through 1.18.1 need the existing logging shim compiled against that artifact's exploded classpath and placed first on the run classpath. The shim changes logging only; game classes and their mining calculations remain unmodified.

The raw file includes every state and item. A missing sparse tool row means speed 1 and no special harvest capability. The canonical file records every block's hardness and harvest requirement, plus nondefault tool rules keyed by item and block names. It records the official server SHA-1, server SHA-256 and raw measurement SHA-256. Missing protocol measurements remain unknown in the generated API. These default facts must be combined with the player's tracked attributes, effects, equipment, environmental state, custom tool components and synchronized server tags when calculating mining progress.

For obfuscated 1.8–1.14.3 jars, resolve methods from that release's own decompiled source. The resolver requires unique mining-progress, hardness, stack-speed, harvest and registration bindings; it fails on ambiguous evidence. `LegacyMiningGeneric` measures the native state registry, including every registered legacy metadata variant. `LegacyTagBinding` loads and resolves vanilla block tags for 1.13–1.14.3 before measuring shears. Run Java from a temporary directory so vanilla bootstrap logs stay outside Git.

```sh
python3 tools/extraction/resolve_legacy_mining.py 1.8.9 --official "$MINECRAFT_OFFICIAL_ROOT" --out /tmp/mining-symbols-1.8.9.json
javac --release 11 -d /tmp/umpk-mining-classes tools/extraction/shape-extractor/LegacyMiningGeneric.java tools/extraction/shape-extractor/LegacyTagBinding.java
java -cp /tmp/umpk-mining-classes:"$MINECRAFT_OFFICIAL_ROOT/downloads/1.8.9/server.jar" LegacyMiningGeneric /tmp/mining-symbols-1.8.9.json /tmp/mining-1.8.9.json "$MINECRAFT_OFFICIAL_ROOT/downloads/1.8.9/server.jar"
python3 tools/extraction/extract_mining.py 47 --version 1.8.9 --dump /tmp/mining-1.8.9.json --symbols /tmp/mining-symbols-1.8.9.json --data data/java --official "$MINECRAFT_OFFICIAL_ROOT"
```

The legacy converter validates native block IDs and registry names before normalizing namespaces. It refuses missing blocks or items and state-dependent facts. `state_count` follows the canonical metadata-zero rows; `measured_state_count` records the actual registered vanilla states examined. Source binding hashes are retained in provenance and verified when converting.

`PushDump` needs two things `ShapeDump` does not:

- **1.14.4 and 1.15.2**: compile with `javac --release 11` as well as running under `openjdk@11`, or the class file is too new for that JVM.
- **1.17 and 1.17.1**: `log4j-shim/LogManager.java`, compiled into a directory placed AHEAD of the server classes. 1.17's server calls `LogManager.getLogger()` with no argument and every log4j-api resolves the caller by a stack walk that returns null when the class is initialized under a current JVM's reflective invoke, so `Bootstrap` throws before any registry exists. Substituting a newer log4j-api (2.25.2) does NOT fix it. The stub does, and neither dumper ever logs.

**Which versions can be driven at all**: Mojang publishes `server_mappings.txt` only from 1.14.4 on, and 26.1+ ship unobfuscated (pass `none` as the mappings argument). Of UMPK's 38 flattened protocols, 17 have a mappings file offline: 498, 578, 755, 756, 759, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775,
776. The other 21 need one download each before they can be measured.

# Block shape extractor

The current local oracle at `/home/anon/Projects/MinecraftOfficial` contains all 75 stable releases from 1.8 through 26.3. Availability notes and drift counts below describe earlier audits. `OutlineDump.java` measured all 44 mapped or unobfuscated releases, and `LegacyFlatOutlineDump.java` measured all seven earlier flattened releases. `LegacyOutlineDump.java` and `LegacyStateAliasDump.java` cover the 24 metadata-era releases. The resulting `block-targeting.json` files cover all 50 protocols, including measured door-neighbor rules on the 11 metadata-era protocols. Other legacy neighbor and block-entity variants and final interaction verification remain in progress.

`ItemInteractionDump.java` reads every item's default Tool component through vanilla `Tool.CODEC` with `RegistryOps<JsonOps>`. All 19 component-era releases were measured, and releases sharing a protocol were compared before generating the 12 protocol tables. Compile it alongside `PushDump.java` and `MiningDump.java`, then run `ItemInteractionDump <mappings-or-none> <exploded-classpath> <output.json>` on the version's unsigned vanilla classpath. Save each output as `<measurements>/<version>/item-interaction-raw.json`, then merge with `python3 tools/extraction/extract_item_interactions.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements <measurements>`.

`OutlineDump.java` measures separate outline and interaction shapes at `(0,0,0)`, `(17,63,-31)`, `(-30000000,-64,29999999)` and `(0,100,0)`. It measures an empty collision context and a context holding the item queried by the native shape implementation. A shape's coordinate dependence must be either absent or exactly the translation returned by native `getOffset`; other dependence aborts extraction. This distinguishes offset flowers from tall grass whose outline ignores its rendering offset. The dump retains held-light and held-scaffolding variants and interaction-shape face overrides. Compile it with the same helpers and run `OutlineDump <mappings-or-none> <output.json>`. Save each output as `<measurements>/<version>/outline-context.json`, then fold with `python3 tools/extraction/extract_outlines.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements <measurements>`.

## Block-use and placement contracts

`BlockInteractionDump.java` measures the declaring methods of each native block and item, retaining unknown specialized rules instead of inferring success from a packet write or server acknowledgment. It records passive block use and base nonreplacement flags, and recognizes ordinary full-cube block placement only when the native placement, survival, geometry, item-use, feature and default-component checks establish that contract. Runtime player, world, obstruction and stack preconditions still apply. Default 26.3 block-transformer components remain specialized rules rather than pass-through items.

Run the maintained workflow in a fresh external directory. It measures both actual client and server jars, verifies Mojang hashes and client class origins, preserves signed-original class bytes in its unsigned client classpath, and rejects unequal results. It reads embedded native protocol metadata to include patch aliases such as 26.1.1 and 26.1.2 even when the dataset catalog exposes their shared protocol under 26.1.

```bash
python3 tools/extraction/measure_block_interactions.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-vanilla-interactions
python3 tools/extraction/extract_block_interactions.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-vanilla-interactions
```

Use `--versions <release> ...` for a focused native reproduction. Dataset folding requires the completed full mapped range. The converter checks exact native state and item registries, equal protocol aliases, unchanged helper sources, client/server parity and recorded measurement hashes before writing any canonical tables. The current mapped range has 44 releases and 32 protocol datasets. The separate legacy and placement-context workflows below cover the earlier 31 releases and 18 protocols. Keep native logs, jars and measurements outside Git.

`PlacementIntersectionDump.java` and `PlacementIntersectionGridDump.java` measure native `Shapes.joinIsNotEmpty(AND)` for a full block cube against an entity box. Compile both with `PushDump.java` and `MiningDump.java` using `javac --release 11`. Run either helper as `<helper> <client-mappings-or-none> <exploded-server-library-classpath> <output.json>`, with the unsigned client jar before server libraries on the Java classpath. The first helper measures 294 contact-plane and thin-box cases at ordinary and world-border coordinates. The second adds 1,206 cases around dyadic grid coordinates and thin-box thresholds. Verify official artifact hashes, unchanged client class bytes, and the client origins of `Shapes`, `IndirectMerger`, `BooleanOp` and `AABB` before accepting output.

All 44 mapped or unobfuscated client releases from 1.14.4 through 26.3 passed these probes. Contact-plane outputs are equal across that range. Grid outputs split at 1.17, which changes the floor-based grid test to rounded coordinates with a scaled tolerance and explicitly discards boxes thinner than `1e-7`. The native fixture in `tests/Umpk.Client.Tests/Fixtures/PlacementCollision.native.json` retains both measured profiles, every release's artifact and measurement hashes, and helper source hashes. The client test exercises all 1,500 cases against each of the 44 release rows. This proof covers full-cube obstruction; it does not establish entity dimensions or arbitrary block collision shapes.

`EntityPlacementProbe.java`, `EntityWorldSource.java` and `EntityPlacementGeometry.java` construct native client entities in an owned world adapter, execute their native spawn handling, and measure their building flags, poses and metadata-dependent boxes. The adapter uses native overworld registry values and rejects unsupported queries. Its world constructor is never run; the owned survival-player subclass inherits native geometry. In particular, falling blocks become obstructing during spawn handling, and armor-stand markers change their building flag through metadata, so constructor-only measurements cannot establish permanent nonobstruction.

```bash
python3 tools/extraction/measure_entity_obstruction.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-vanilla-entity-obstruction
python3 tools/extraction/extract_entity_obstruction.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-vanilla-entity-obstruction
```

The measurer supports `--versions <release> ...` for focused reproduction and checks exact entity coverage, failed factories, official hashes, unchanged client class bytes and six native client class origins. Folding requires the complete mapped range, equal protocol aliases, matching helper hashes and a source audit of every building-flag assignment before writing any dataset. `entity-obstruction.json` records every entity type: `1` means permanently nonobstructing under those native rules; `0` retains unknown or state-dependent obstruction unless `fixed_boxes` supplies separately audited dimensions. The fixed-box fold requires native constructor and spawn flags, root-owned dimension and box methods, no geometry selectors, complete equal pose boxes and a source audit excluding specialized dimension, box or interpolation methods. All 32 mapped protocol tables have one such contract: the end crystal's 2-by-2 box. The runtime consumes permanent traits and tests these fixed boxes against the destination using native voxel intersection. Riders and entities without audited fixed dimensions remain unknown. Native pose and metadata boxes are retained externally for the remaining dynamic-entity work. Reproductions can differ in incidental randomized horse health; the measured building rules, dimensions, pose boxes and geometry selectors reproduced on 1.14.4, 1.20.6 and 26.3.


`EntityBoxTransitionDump.java` measures every pair of native player poses at three coordinates, including a world-border position. It observes the box immediately after the native synchronized-data callback, after an explicit dimension refresh, and after reapplying position. `verify_entity_box_transitions.py` restores native float dimensions and compares every resulting double coordinate exactly; it rejects missing transitions, duplicate rows, nonfinite values and inconsistent phase results.

```bash
python3 tools/extraction/measure_entity_box_transitions.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-box-transitions
python3 tools/extraction/verify_entity_box_transitions.py /tmp/new-entity-box-transitions/1.14.4/entity-box-transitions.json
```

Use `--versions <release> ...` for focused reproduction. The maintained driver verifies original artifacts, unchanged client class bytes, eight native class origins and generated world/player adapters. All 44 mapped or unobfuscated releases passed 27,981 pose-transition witnesses. The ten releases through 1.16.5 retain the box's previous minimum corner when width grows; a subsequent position update recenters it. The 34 releases from 1.17 recenter during refresh itself. This is a lifecycle rule, so recomputing a box solely from current pose can lose native intermediate geometry. `VanillaEntityBounds` implements this arithmetic, and `EntityBounds.native.json` preserves every measured transition through deduplicated cases and per-release indices. Runtime placement uses its centered fixed-box construction; persistent dynamic bounds, mob age/scale behavior, interpolation and special bounding-box writers still require integration.

`EntityInterpolationDump.java` and `EntityInterpolationSource.java` measure the inherited native living interpolation controller on an owned survival-player subtype. Legacy `LivingEntity.aiStep` runs until its first velocity write after interpolation; an explicit sentinel in the owned subtype stops the remaining AI and movement work. The driver audits that boundary in each matching source tree. Modern handlers run their native interpolation methods directly. The world adapter supplies explicit collision answers and relative tick speed, and records every requested collision box. These are controller-input tests, not execution of vanilla world collision or complete entity ticks.

```bash
python3 tools/extraction/measure_entity_interpolation.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-interpolation
python3 tools/extraction/verify_entity_interpolation.py /tmp/new-entity-interpolation/26.3/entity-interpolation.json
python3 -m unittest discover -s tools/extraction -p 'test_verify_entity_*.py'
```

Use `--versions <release> ...` for a focused run. Each scenario records 12 ticks with state before and after interpolation: position, rotation, current box, target and active state. Controls include repeated and changed targets, zero/one/six-step lengths, cancellation, clear and blocked external movement, small deltas, world-border coordinates, wrapped rotation and relative tick speeds. Applicable releases also exercise partial position/rotation updates, stepped paths, queued paths and predicted movement. Length controls use the native argument or setter when available; 26.3's stepped controller uses an explicitly recorded protected-field input. The verifier rejects incomplete traces and stationary fake controllers, and checks native endpoint, cancellation, collision-answer and zero-speed controls. The catalog constructs every registered entity and records its effective controller or legacy interpolation method owner; owned player substitution and special constructors are labeled. Packet listeners, `RemotePlayer` overrides, other controller families, entity physics and full world lifecycle remain separate verification requirements. Official hashes, unchanged client bytes, native class origins, generated adapter hashes and matching source hashes accompany each release.

The `--probe edges` mode runs `EntityInterpolationEdgeDump.java` with the controller's unchanged default length where a handler exposes one, or an explicitly labeled legacy argument of three. It measures fractional rotation, externally applied rotation, synchronized sleeping/standing dimensions and a 320-tick stream with 200 successive targets. These traces include explicit input operations, so managed replay does not infer which inputs happened from the resulting state. This mode records its owned player actor rather than repeating the standard entity-type catalog.

```bash
python3 tools/extraction/measure_entity_interpolation.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-interpolation-edges --probe edges --versions 1.14.4 1.17 1.20.2 1.21.5 1.21.6 1.21.9 26.3
python3 tools/extraction/extract_entity_interpolation_fixture.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-interpolation --out tests/Umpk.Client.Tests/Fixtures/EntityInterpolation.native.json
python3 tools/extraction/extract_entity_interpolation_fixture.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-interpolation-edges --profile-matrix tests/Umpk.Client.Tests/Fixtures/EntityInterpolation.native.json --versions 1.14.4 1.17 1.20.2 1.21.5 1.21.6 1.21.9 26.3 --out tests/Umpk.Client.Tests/Fixtures/EntityInterpolationEdges.native.json
```

The fixture extractor requires the exact expected release set, successful reports, unchanged measurement hashes, equal protocol aliases and complete traces before deduplicating cases. Standard fixtures derive trace groups from the common native scenarios; edge fixtures bind to the standard matrix. The managed `VanillaEntityInterpolation` component consumes explicit arithmetic profiles and caller-owned ticks. Its tests compare every before/after state and collision query, including pose refresh during interpolation and sustained queue consumption. The component is not yet connected to entity tracking or placement; per-type controller selection, current metadata/scale geometry, world lifetime, packet bases and tick-phase order must be implemented and verified before that integration is complete.

`EntityRemotePlayerSource.java` emits a separate owned client-level adapter and a subtype of the actual native `RemotePlayer`. It overrides only the player's survival mode queries. The player constructor executes unchanged; the existing controlled-world harness allocates the owned world without running its constructor. World callbacks return explicit spawn coordinates, empty peers, collision-free answers and relative tick speed one; unsupported callbacks fail. `RemotePlayerDump.java` executes the complete native `RemotePlayer.aiStep` for eight scenarios of 12 ticks. On 26.3 it advances the interpolation handler explicitly before that phase. This covers actual remote-player arithmetic and synchronized crouching under controlled inputs; it does not run the complete entity/world lifecycle, packet listener or player-pushing physics. The maintained driver and fixture commands are documented in [the extraction guide](../README.md#native-remote-player-measurements).

`EntityPoseWireDump.java` separately reads and writes every native pose through the actual metadata serializer, retaining its native serializer id, complete metadata frame and seven invalid-id probes. `measure_entity_pose_wire.py` verifies the original artifacts, unchanged client class bytes and four native client class origins. Its verifier rejects incomplete pose sets, incorrect payloads or envelopes and missing or contradictory invalid-id results. Before 1.20.5 those probes must reject; later serializers return standing. Codec construction selects profiles for those version boundaries without adding a protocol field to the decode context.

```bash
python3 tools/extraction/measure_entity_pose_wire.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-entity-pose-wire
PYTHONPATH=tools/extraction python3 -m unittest tools/extraction/test_verify_entity_pose_wire.py
```

Use `--versions <release> ...` for a focused reproduction. `LegacyPoseWireDump.java` covers unmapped 1.14 through 1.14.3 with an explicit binding JSON resolved from that release's source; its arguments are `<bindings.json> <output.json>`. Both helpers retain native enum names, including SNEAKING on the older releases. The protocol tests' `EntityPose.native.json` records all 48 applicable releases, 633 known poses and 336 invalid-id probes with original artifact and helper hashes. DYING maps from native id 6 through 1.16.5 and 7 thereafter; 1.19's missing SITTING entry also shifts later poses. Canonical UMPK poses remain stable, and the metadata value stores modern fallback wire ids for byte-identical re-encoding.

`LegacyInteractionDump.java` and `resolve_legacy_interactions.py` cover the argument-based placement path on all 24 releases from 1.8 through 1.12.2. The source resolver requires base passive-use and nonreplacement methods, placement into air, the side precheck, placement-state construction and the inherited collision path. The native probe checks every eligible state's actual box before recognizing a simple cube. It retains equal wire-state aliases and rejects conflicting names, flags or geometry. Client-only item prechecks are absent from server jars, so the server supplies libraries while game classes must load from the byte-preserving unsigned client copy. The origin validator accepts both raw stdout and Minecraft's logger wrapper.

```bash
python3 tools/extraction/measure_legacy_interactions.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-legacy-interactions
python3 tools/extraction/extract_legacy_interactions.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-legacy-interactions
```

Folding verifies complete block and item registrations, native unit-box evidence, source and artifact hashes, and reviewed protocol aliases before writing the 11 metadata-era tables. The canonical protocol-47 profile remains 1.8.9, matching targeting data. Original 1.8 has 1,391 registered wire states and reports air as nonreplaceable; 1.8.1–1.8.9 have 1,394 states and report air as replaceable. That exact earlier difference is retained in provenance, and every other unequal alias is rejected. Selecting the original 1.8 client behavior requires further release-profile support.

`LegacyContextInteractionDump.java` and `resolve_context_interactions.py` cover the placement-context path on the seven releases from 1.13 through 1.14.3. The resolver binds passive block use, material replacement, default placement state, unconditional survival, inherited shape and collision methods, and every block-item method accepting the use or placement context. The 1.14 food fallback also requires a nonedible native item. Every eligible registered state's actual voxel shape must be one unit cube before the item can be classified as ordinary placement. Other items remain explicit unknown or pass contracts. Native client classes load from a verified copy of the client jar with unchanged class bytes; the server jar supplies libraries only, and these measurements do not claim server semantic parity.

```bash
python3 tools/extraction/measure_context_interactions.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-context-interactions
python3 tools/extraction/extract_context_interactions.py --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-context-interactions
```

The measurer supports `--versions <release> ...` for focused reproduction. Folding requires the full seven-release catalog, exact native state and item coverage, complete unit-box evidence, matching helper and source hashes, six native class origins and verified official artifacts before writing any table. The measured range contains 70,864 registered states, 5,873 items and 1,208 eligible collision-shape measurements. Together the three workflows provide ordinary placement contracts for all 50 protocols; specialized use results and runtime conditions still require their own native rules.

## Entity packet coordinates

`EntityDeltaPositionDump.java` measures native packet-position decoding separately from visual interpolation. On 1.14.4 through 1.16.1 it invokes the native packet cache's encoder and vector decoder; from 1.16.2 it invokes the native packet's vector update, and from 1.19 it invokes `VecDeltaCodec`. Inputs include stationary axes, half-grid ties and their immediate double neighbors, negative coordinates, world-border positions and opposing sequential deltas. `LegacyEntityDeltaPositionDump.java` covers 1.8 through 1.14.3 using a uniquely source-resolved native long-floor method and the audited play listener's packet-coordinate units. The 1.8 cases start on the wire's 1/32-block grid. These earlier probes establish coordinate-cache arithmetic; they do not execute a complete play listener.

```bash
python3 tools/extraction/measure_entity_delta_position.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-mapped-entity-deltas
python3 tools/extraction/measure_legacy_entity_delta_position.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/new-legacy-entity-deltas
PYTHONPATH=tools/extraction python3 -m unittest tools/extraction/test_measure_legacy_entity_delta_position.py
```

Both drivers support `--versions <release> ...`, require a fresh external directory, verify official artifact hashes and unchanged client class bytes, and verify the native class origins. The older resolver rejects missing or ambiguous floor methods and requires source evidence for the play listener's coordinate units. The full 75-release run produced 5,040 coordinate sequences across all 50 protocols. `EntityPacketPosition.native.json` retains native outputs with artifact, measurement, source and tool hashes; the Client tests compare each decoded step exactly.

| Client releases | Units per block | Coordinate-cache rule |
| --- | ---: | --- |
| 1.8–1.8.9 | 32 | Integer cache; floor every axis |
| 1.9–1.16.1 | 4096 | Long cache; floor every axis, including rotation-only updates |
| 1.16.2–1.19.2 | 4096 | Floor moving axes and preserve zero-delta axes |
| 1.19.3–26.3 | 4096 | Java nearest rounding with ties toward positive infinity; preserve zero-delta axes |

26.3 stepped deltas update the temporary packet base after each step. Opposing steps can quantize an axis even when their sum is zero. This coordinate decoder does not implement interpolation timing, entity physics or dimensions; those need a separate current-position and bounds owner.

## Metadata-era targeting measurements

`resolve_legacy_outlines.py` binds native ray tracing, vector and box coordinates, mutable bounds, offset methods and exceptional trace overrides from each release's source. It refuses ambiguous bindings or an unclassified ray override. `LegacyWorldSource.java` generates an owned subclass of the native world. Measurement allocates that adapter without running a game-world constructor, sets the native client-side flag, supplies explicit state and block-entity maps, returns air and no block entity elsewhere, and rejects unsupported abstract world operations. The vanilla classes remain unmodified.

`LegacyOutlineDump.java` decodes registered state IDs through native metadata conversion, measures ordered selection boxes and records 95 native rays per distinct registered ID. It checks geometry at four coordinates, including the world border, and accepts coordinate dependence only when it exactly follows the native offset. Mutable 1.8 torch bounds must be obtained through the actual ray method; its bounds-update method alone is insufficient. Native 1.8 stairs use six ordered half-cubes, later stairs use a native list, and moving pistons have no ray hit. An unattached 1.8 vine's inverted bounds reject every native plane. Native neighbor and block-entity reads are retained in the raw measurement for the next extraction stage.

For example, with Java 11 and `MINECRAFT_OFFICIAL_ROOT` set:

```sh
export UMPK_LEGACY_VERSION=1.8.9
export UMPK_LEGACY_WORK=/tmp/umpk-legacy-selection-1.8.9
mkdir -p "$UMPK_LEGACY_WORK/classes"
python3 tools/extraction/resolve_legacy_mining.py "$UMPK_LEGACY_VERSION" --official "$MINECRAFT_OFFICIAL_ROOT" --out "$UMPK_LEGACY_WORK/mining-symbols.json"
python3 tools/extraction/resolve_legacy_outlines.py --official "$MINECRAFT_OFFICIAL_ROOT" --symbols "$UMPK_LEGACY_WORK/mining-symbols.json" --out "$UMPK_LEGACY_WORK/outline-symbols.json"
javac --release 11 -d "$UMPK_LEGACY_WORK/classes" tools/extraction/shape-extractor/LegacyMiningGeneric.java tools/extraction/shape-extractor/LegacyTagBinding.java tools/extraction/shape-extractor/LegacyWorldSource.java tools/extraction/shape-extractor/LegacyOutlineDump.java
(cd "$UMPK_LEGACY_WORK" && java -cp "$UMPK_LEGACY_WORK/classes:$MINECRAFT_OFFICIAL_ROOT/downloads/$UMPK_LEGACY_VERSION/server.jar" LegacyWorldSource "$UMPK_LEGACY_WORK/outline-symbols.json" "$UMPK_LEGACY_WORK/LegacyWorldAdapter.java")
javac --release 11 -cp "$MINECRAFT_OFFICIAL_ROOT/downloads/$UMPK_LEGACY_VERSION/server.jar" -d "$UMPK_LEGACY_WORK/classes" "$UMPK_LEGACY_WORK/LegacyWorldAdapter.java"
(cd "$UMPK_LEGACY_WORK" && java -Xmx512m -cp "$UMPK_LEGACY_WORK/classes:$MINECRAFT_OFFICIAL_ROOT/downloads/$UMPK_LEGACY_VERSION/server.jar" LegacyOutlineDump "$UMPK_LEGACY_WORK/outline-symbols.json" "$UMPK_LEGACY_WORK/outlines.json" "$UMPK_LEGACY_WORK/witnesses.json")
python3 tools/extraction/verify_legacy_outlines.py "$UMPK_LEGACY_WORK/outlines.json" "$UMPK_LEGACY_WORK/witnesses.json"
python3 -m unittest discover -s tools/extraction -p 'test_*legacy_outlines.py'
```

For client-artifact parity, put an unsigned copy of the matching client jar before the server jar on the run classpath. Remove signature metadata under `META-INF/` from that copy and verify every copied class's SHA-256 against the original signed jar; do not change class bytes. The server jar then supplies bundled libraries. The extractor prints the code-source location of the native block, state, world, position and vector classes so a client run must demonstrate that these classes actually came from the client copy. Verify the original client and server SHA-1 against `version.json`, verify native rays, and compare client and server output separately. A signature mismatch is a classpath setup failure, not a protocol result.

`LegacyStateAliasDump.java` decodes all 16 metadata values for every registered block through the same native client classes. It retains native decoder exceptions separately, checks block ownership and native state registration, and never invents an alias for rejected metadata. Compile it with `LegacyMiningGeneric.java`, then run `LegacyStateAliasDump <mining-symbols.json> <aliases.json>` with the same native classpath. `python3 tools/extraction/expand_legacy_outlines.py --blocks data/java/47/blocks.json --aliases <aliases.json> --outlines <outlines.json> --out <expanded.json>` validates complete wire metadata coverage and canonical registry names before expanding valid values. For example, torch metadata 0 decodes to metadata 5. Some invalid powered-rail and facing values throw in vanilla; these remain explicit rejected values. Releases sharing a protocol can have different intermediate native state IDs, so compare the expanded geometry as well as the aliases before pooling.

`LegacyDoorOutlineDump.java` measures each decoded door state against every decoded neighbor state, then populates the unused vertical neighbor as a negative control. Each distinct outcome also retains native ray witnesses. This preserves 1.8's use of unrelated neighboring metadata and the later matching-door check. `extract_legacy_targeting.py` folds static selection boxes, native metadata aliases and those neighbor lookups into the canonical targeting tables. The descriptor's existing final catalog release remains the canonical client profile for a shared protocol. Earlier differing client measurements are retained in provenance, including the 1.8 upper-door/double-plant difference.

The complete maintained workflow uses a fresh external measurement directory. It verifies both official artifact hashes, compiles owned Java helpers, preserves client class bytes in an unsigned copy, measures native server/client parity, verifies ray witnesses, decodes metadata, measures door contexts and writes the reports consumed by the converter:

```sh
python3 tools/extraction/measure_legacy_targeting.py --official "$MINECRAFT_OFFICIAL_ROOT" --measurements /tmp/umpk-legacy-targeting
python3 tools/extraction/extract_legacy_targeting.py --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" --measurements /tmp/umpk-legacy-targeting
dotnet run --project tools/Umpk.DataGen -- verify --data data/java
dotnet run --project tools/Umpk.DataGen -- generate --data data/java --out src/Umpk.Data.Java --out-lang src/Umpk.Data.Lang --protocol-out src/Umpk.Protocol.Java
```

The measurement command defaults to the shared Java 11 runtime. Override it with `--java` and `--javac`. Use `--versions 1.8.9 1.9 1.12.2` for a focused measurement check; the converter's normal full-catalog run requires complete reports. The maintained command reproduced the canonical geometry and door rules on those three era representatives.

Packed targeting schema 3 extends the previous format with optional per-shape stair-list clipping and bounded neighbor lookups. Context roots select a node for a center state. Each node reads one immediate neighbor and maps its raw state ID to an outline shape or a later node; negative targets encode later node indexes with bitwise complement, and backward edges are rejected. Current door tables use terminal shape targets. Missing chunks read as air, while metadata rejected by the native decoder remains unselectable. Other legacy neighbor-derived shapes and block-entity variants currently retain their measured empty-world boxes. Those cases still require extraction and integration; static ray agreement does not establish them.

The Mojang-derived shape extractor: a small Java program run against the official server jar that walks the server's own block-state registry and dumps, per block state, the collision-shape AABB list.

This replaces third-party collision-shape snapshots with a direct vanilla-derived measurement.

## Status

`ShapeDump.java` parses `server_mappings.txt` and reaches each required type by reflection. It needs no remap, ASM pass, or compile-time binding, and one source file covers 1.14.4 through 26.x.

What it does NOT yet dump: `friction`, `speedFactor`, `jumpFactor`, `blocksMotion` and fluid state per state. Those still live as material-level toggles in `shared/block-attributes.json`. Adding them is more reflection in the same loop, not a new mechanism.

## Requirements

- A compatible JDK with `java` and `javac` on `PATH`.
- The version's `server.jar` and its `server_mappings.txt`. Mojang publishes mappings from 1.14.4 on, so 1.13 - 1.14.3 cannot be driven this way.
- The server classes and their libraries on the classpath, UNSIGNED. Some shipped library jars are signed and the JVM refuses to mix them with the unsigned server classes ("signer information does not match"), so explode everything into one directory and drop `META-INF/*.SF|DSA|RSA`.

## Running it

```bash
JAVA=$(command -v java)
DL=<...>/MinecraftOfficial/downloads/1.21.4

# 1. explode server + libraries into one unsigned classpath directory.
#    1.18+ jars are bundlers: the real jar and the libs are inside META-INF.
mkdir -p /tmp/cp && cd /tmp/cp
unzip -qo "$DL/server.jar" 'META-INF/versions/*' 'META-INF/libraries/*' -d /tmp/_b
for j in $(find /tmp/_b -name '*.jar'); do
  unzip -qo "$j" -x 'META-INF/*.SF' 'META-INF/*.DSA' 'META-INF/*.RSA'
done

# 2. dump
"$JAVA" -cp <this-dir>/classes:/tmp/cp ShapeDump "$DL/server_mappings.txt" shapes-1.21.4.json
```

Pre-1.18 server jars are already fat, so step 1 is a single `unzip` of `server.jar`. 1.14.4 and 1.15.2 need an older runtime (openjdk@11). For 1.17.x, compile `log4j-shim/LogManager.java` and place its output ahead of the server classes. Replacing the shipped log4j jars does not solve the reflective caller lookup failure described above.

## What it is for

Cross-checking the committed shape dataset against vanilla. The committed modern tables are a `curated-shape-bootstrap`: one repository-local modern snapshot re-indexed positionally onto each band's own state order. That is exact while a block's state schema has not changed since the snapshot, and silently wrong where it has. Measured against this dumper:

| protocol | version | states | wrong | share |
| --- | --- | --- | --- | --- |
| 498 | 1.14.4 | 11271 | 875 | 7.76% |
| 578 | 1.15.2 | 11337 | 861 | 7.59% |
| 756 | 1.17.1 | 20342 | 25 | 0.12% |
| 765 | 1.20.4 | 26644 | 7 | 0.03% |
| 766 | 1.20.6 | 26684 | 7 | 0.03% |
| 769 | 1.21.4 | 27866 | 7 | 0.03% |
| 770 | 1.21.5 | 27914 | 6 | 0.02% |
| 773 | 1.21.9 | 29671 | 0 | 0.00% |

(Those seven and six are the renamed blocks, since fixed. The 1.14/1.15 mass is schema drift: walls carried 64 states then and 324 from 1.16, so the positional read takes the first 64 of the wrong list. The 1.17 residue is the wall skulls, which gained `powered` at 1.20.3.)

The remaining drift is only fixable by dumping each band from its own jar, which is what this program is for. It was not done in the run that measured the table above because only eight of the flattened bands have `server_mappings.txt` present offline.
