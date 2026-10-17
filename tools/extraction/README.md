# UMPK extraction tooling

These tools add data for current and future Minecraft Java protocol versions. Previously imported version-band builders are not kept here. Their outputs and provenance remain in `data/java/`, which is the canonical dataset.

## Requirements

- Python 3.10 or later. The Python tools use only the standard library.
- Java 17 or later for Mojang data reports and the jar-driven extractors.
- Official server jars under `MinecraftOfficial/downloads/<version>/server.jar` and decompiled trees under `MinecraftOfficial/<version>-decompiled/`, or an absolute external root selected with `UMPK_ORACLE_ROOT`.

Never commit official jars, mappings, libraries, or decompiled sources.

## Add a report-capable version

Run the server reports, extract one protocol dataset, and then review every changed file.

```bash
python3 run_reports.py <version> --out /tmp/umpk-reports-<protocol>
python3 extract_modern.py <version> <protocol> \
    --reports /tmp/umpk-reports-<protocol>/reports \
    --out ../../data/java/<protocol> \
    --codec-key <candidate-era>
```

`extract_modern.py` writes packet, item, component, menu, command argument, registry, block, entity, metadata, and collision-shape data. The codec key is candidate provenance for review, not proof that the previous wire layout still applies.

Add the release to `data/java/versions.json`, author or review `features.json`, and follow `docs/contributing/adding-a-version.md` before regenerating C# sources.

## Source and mapping check

`decompile.py` identifies the mapping source for a release and reports whether a usable decompiled tree already exists.

```bash
python3 decompile.py <version>
```

The script does not download or decompile artifacts. Use `.skills/umpk-integration-testing/references/minecraft-official.md` to provision official sources.

## Language data

`extract_lang.py` selects the representative release for each protocol from `data/java/versions.json`, extracts its vanilla `en_us` table, and can compare all committed language tables without rewriting them.

```bash
python3 extract_lang.py --data ../../data/java --check
```

## Piston push data

`shape-extractor/PushDump.java` measures push reaction, destroy speed, block-entity presence, and air state from the target server jar. Fold its JSON output into the protocol dataset with:

```bash
python3 extract_push_reactions.py <protocol> \
    --dump /tmp/push-<protocol>.json \
    --data ../../data/java \
    --version <version>
```

The fold step rejects a block when its states disagree instead of collapsing state-specific behavior into a false block-wide value.

## Collision shapes

`shape-extractor/ShapeDump.java` drives a target server jar and records vanilla collision shapes. See `shape-extractor/README.md` for its requirements and supported layouts.

When jar-driven extraction is unavailable, `shape_extractor.py` maps the report's block-state ordering onto the repository-local bootstrap asset and marks the result with `curated-shape-bootstrap` provenance. This fallback does not invent data for unresolved blocks.

## Entity and world lifetime source audit

From the repository root, audit the official client trees for same-dimension respawn retention and duplicate-ID entity replacement:

```bash
python3 tools/extraction/audit_entity_lifecycle.py --official /home/anon/Projects/MinecraftOfficial --out /tmp/entity-lifecycle-source.json
python3 -m unittest discover -s tools/extraction -p test_audit_entity_lifecycle.py
```

The auditor checks each official client jar's SHA-1, records jar and source SHA-256 hashes, and includes the resolved method evidence. It resolves both mapped and obfuscated releases and includes stable aliases from the official interaction catalogue. Missing or ambiguous source patterns fail the audit. This is source evidence; it does not execute a native world or replace live lifecycle tests.

`audit_entity_packet_bases.py` separately audits relative-coordinate storage, rotation-only handling, classic teleport cache replacement and modern position-sync ownership. Its legacy bindings include static cache setters resolved from the actual call site.

```bash
python3 tools/extraction/audit_entity_packet_bases.py --official /home/anon/Projects/MinecraftOfficial --out /tmp/entity-packet-bases-source.json
python3 -m unittest discover -s tools/extraction -p test_audit_entity_packet_bases.py
```

## Modern teleport measurements

`EntityTeleportDump.java` executes the official client's `PositionMoveRotation.calculateAbsolute` for every combination of the nine relative flags and eight input pairs. The maintained driver checks official artifact hashes, unchanged client class bytes, native class origins, complete finite output and equal protocol aliases before emitting a deduplicated replay fixture.

```bash
python3 tools/extraction/measure_entity_teleport.py --official /home/anon/Projects/MinecraftOfficial --measurements /tmp/entity-teleport-measurements --out /tmp/EntityTeleport.native.json
python3 -m unittest discover -s tools/extraction -p test_measure_entity_teleport.py
```

The default matrix covers every available stable modern release from 1.21.2 through 26.3, including aliases from the official artifact catalogue. `--versions` selects a smaller explicit matrix. Each measurement includes the native sine-table hash and source hashes for position transforms, relative flags, vector rotation, math, entities and the client listener. Position-sync source must retain velocity. These measurements establish transform arithmetic; they do not execute the listener, select an entity's current or interpolation-target state, or implement vehicle authority.

## Entity motion catalogue proposals

`extract_entity_motion.py` folds a complete native interpolation factory catalogue into a fresh external proposal directory. It verifies raw measurement hashes, release coverage, registry coverage and equal protocol aliases. It preserves native controller owners, factory roles and measured network update intervals without inventing missing legacy intervals. A network update interval is not automatically the controller's interpolation length.

```bash
python3 tools/extraction/extract_entity_motion.py --measurements /absolute/native-interpolation-run --official /home/anon/Projects/MinecraftOfficial --out /tmp/entity-motion-proposal
python3 -m unittest discover -s tools/extraction -p test_extract_entity_motion.py
```

The owned survival-player row remains marked `client_player_measured: false`; it does not establish the actual remote-client player role. The extractor rejects relabeling that actor as a native player factory. Review the proposed files and obtain the dataset approval required by AGENTS.md before integrating them through DataGen and generated accessors. A constructor/controller catalogue does not establish full entity ticks, dynamic geometry or world physics.

## Native remote-player measurements

`measure_remote_player.py` constructs an owned subtype of the actual native `RemotePlayer`, overrides only survival game-mode queries and invokes the unchanged native `aiStep`. Its controlled client-level adapter supplies empty peer queries, collision-free answers and relative tick speed one. The player constructor runs normally; the owned world is allocated without executing its constructor. On 26.3 the driver explicitly advances the native interpolation handler before `aiStep`. This is a controlled phase measurement, not a complete entity or world tick or a packet-listener test.

```bash
python3 tools/extraction/measure_remote_player.py --official /home/anon/Projects/MinecraftOfficial --measurements /absolute/fresh-native-remote-player-run
python3 tools/extraction/verify_remote_player.py /absolute/fresh-native-remote-player-run/26.3/remote-ai.json
python3 tools/extraction/extract_remote_player_fixture.py --official /home/anon/Projects/MinecraftOfficial --measurements /absolute/fresh-native-remote-player-run --out /tmp/RemotePlayer.native.json
```

Use `--versions <release> ...` for a focused measurement. The fixture extractor requires the complete mapped release set, successful reports, exact hashes, equal protocol aliases and the previously verified position-controller profile for each release. Eight scenarios record 12 ticks each: linear, repeated and changed targets, velocity updates and retargeting, wrapped rotation, synchronized crouching and a stationary control. The sanity verifier checks completeness and controls; managed replay compares every recorded position, rotation, box, target, active state and velocity by exact numeric bits. The native measurements establish three position steps through 26.2 and two in 26.3, with immediate velocity updates through 1.19.3 and three-step velocity interpolation from 1.19.4. Network update intervals alone do not establish these durations. `VanillaEntityVelocity` and the existing position model remain caller-driven components pending runtime integration.

## Native item attack defaults

`measure_item_attack.py` measures the native piercing component's data, hash and wire codecs, plus piercing and minimum-charge defaults for every registered item from 1.21.11 through 26.3. It checks official artifact hashes, preserved client class bytes and native class origins. Set `MINECRAFT_OFFICIAL_ROOT` for another artifact location. The optional release arguments after the fresh output directory select a smaller measurement run.

```bash
python3 tools/extraction/measure_item_attack.py /absolute/fresh-item-attack-run
python3 tools/extraction/extract_item_attack.py --measurements /absolute/fresh-item-attack-run --data data/java --out data/java
```

The extractor verifies the maintained helper hash and report hashes, reads protocol identifiers from the verified client jars, requires every registered item and rejects differing protocol aliases. Canonical nulls mean measured component absence. Generated prototypes preserve the native sound holders and minimum charge while allowing stack patches to remove or override them. These measurements do not execute input handling or establish attack-charge tick order.

## Validation

```bash
dotnet run --project ../Umpk.DataGen -- verify --data ../../data/java
dotnet run --project ../Umpk.DataGen -- diff --data ../../data/java <previous-protocol> <protocol>
```

After extraction, regenerate all affected C# outputs through `Umpk.DataGen` and review the dataset and generated diffs together.
## Ordinary cube replacement

`extract_block_replacement.py` validates completed native client measurements and writes `block-replacement.json` through the canonical dataset pipeline. Pass each measurement root with a separate `--measurements` argument. The roots must retain their results, tool receipts, source snapshots, adapters and class-origin logs. Artifact jars and decompiled client trees remain under the shared official-artifact root.

```bash
python3 tools/extraction/extract_block_replacement.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-replacement-modern-all-20261016-v2" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-replacement-legacy-all-20261016-v1" \
  --out data/java
python3 -m unittest discover -s tools/extraction -p test_extract_block_replacement.py
```

The extractor checks official artifact hashes, preserved original client-class hashes, predicate origins, source bindings, exact registry and ordinary-cube item coverage, and equal-protocol release differences before writing any dataset files. It rejects per-item or per-face rules that the compact schema cannot represent. Protocol aliases select the latest measured vanilla client profile; measured earlier differences remain in provenance.

Range rows contain `[firstStateId, lastStateId, flags]`. Flag 1 means the clicked block is replaced; flag 2 means the resolved destination accepts an ordinary cube item. These predicates are distinct in metadata-era clients. `below_overrides` retains the measured dependency of upper double plants on the lower cell. Gaps remain unknown state identifiers. These tables cover ordinary cube items; item components, block use, player state, survival, obstruction and other runtime placement conditions still require evaluation.

## Villager geometry

`extract_entity_geometry.py` writes audited villager dimensions and metadata refresh rules to `entity-geometry.json`. It checks the native age/pose transition phases, report hashes and official client/server hashes before extracting the selected protocol representatives. Preserve the complete measurement roots outside Git.

```bash
python3 tools/extraction/extract_entity_geometry.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-entity-native-all-20261017-v1" \
  --transitions "$MINECRAFT_OFFICIAL_ROOT/runs/villager-box-transitions-all-20261017-v1" \
  --legacy "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-villager-geometry-20261017-v1" \
  --check
python3 -m unittest discover -s tools/extraction -p test_extract_entity_geometry.py
```

The 33 datasets cover 1.10.2 and the measured protocol representatives from 1.14.4 through 26.3. The Boolean selector identifies the native baby metadata field. Each variant contains `[selector, canonicalPose, width, height, fixed, blocksBuilding]`. Refresh rules distinguish retained minimum corners, retained minima only on growth, and recentering. Position reapplication always recenters. The generated interaction source also implements `IEntityGeometrySource`; unknown entity types and poses remain unknown. Attribute scaling beyond the default value, riders, specialized geometry and native mob interpolation still need implementation and live validation.

`extract_legacy_item_obstruction.py` adds the original 1.10.2 item entity's nonblocking rule. It verifies both native constructors, original client-class preservation and an audit of every write to the building flag. The remaining legacy entity keys retain unknown rules.

```bash
python3 tools/extraction/extract_legacy_item_obstruction.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-item-obstruction-20261017-v1" \
  --check
```
