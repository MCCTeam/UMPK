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

## Entity geometry

`extract_entity_geometry.py` writes audited entity dimensions and metadata refresh rules to `entity-geometry.json`. It checks the native age/pose transition phases, report hashes and official client/server hashes before extracting the selected protocol representatives. Preserve the complete measurement roots outside Git.

Use the classic extractor for protocol 47. It verifies all ten 1.8 release aliases, their original client classes and artifact hashes before writing the signed-age selector and deferred living-tick contract. Metadata index 12 selects baby dimensions when its signed byte is negative. Villager adult dimensions are 0.6F by 1.8F; baby dimensions are 0.3F by 0.9F. Metadata updates and position reapplication keep cached dimensions until the eligible living phase runs. The original world source requires loaded columns across an inclusive 32-block horizontal radius.

```bash
python3 tools/extraction/extract_classic_entity_geometry.py \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/classic-villager-geometry-all-20261017-v1" \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --data data/java \
  --fixture tests/Umpk.Client.Tests/Fixtures/ClassicVillagerGeometry.native.json.gz \
  --roles "$MINECRAFT_OFFICIAL_ROOT/runs/classic-farm-geometry-all-20261017-v2" \
  --role-fixture tests/Umpk.Client.Tests/Fixtures/ClassicFarmGeometry.native.json.gz
```

Add `--check` to verify the dataset and both fixtures without rewriting them. The four farm-animal types require all ten release aliases. Their explicit `classic-role-v1` receipts retain canonical entity identity, original registry class and numeric ID, source hierarchy, method owners and helper source/class hashes. Cows, pigs, sheep and chickens use the same signed-age selector and deferred size phase, with their own measured dimensions. The separate role fixture contains 10,240 predicates and 3,000 size transitions in four deduplicated cases. The existing villager rule and fixture remain unchanged. These measurements cover the watcher predicate and size phases; complete mob movement and other dynamic geometry require their own evidence.

```bash
legacy_measurements="$MINECRAFT_OFFICIAL_ROOT/runs/legacy-entity-geometry-batch-20261017-v1"
early_measurements="$MINECRAFT_OFFICIAL_ROOT/runs/early-entity-geometry-batch-20261017"
pose_measurements="$MINECRAFT_OFFICIAL_ROOT/runs/early-pose-entity-geometry-batch-20261017-v5"
legacy_arguments=(
  --legacy "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-villager-geometry-20261017-v1"
  --legacy "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-villager-geometry-1.11-20261017-v1"
)
for release in 1.9 1.9.1 1.9.2; do
  legacy_arguments+=(--legacy "$early_measurements-v3/$release/geometry")
done
for release in 1.9.3 1.9.4 1.10 1.10.1; do
  legacy_arguments+=(--legacy "$early_measurements-v4/$release/geometry")
done
for release in 1.11.1 1.11.2 1.12 1.12.1 1.12.2 1.13 1.13.1 1.13.2; do
  legacy_arguments+=(--legacy "$legacy_measurements/$release/geometry")
done
for release in 1.14 1.14.1 1.14.2 1.14.3; do
  legacy_arguments+=(--legacy "$pose_measurements/$release/geometry")
done
role_arguments=()
for release in 1.14 1.14.1 1.14.2 1.14.3; do
  for entity in cat iron_golem; do
    role_arguments+=(--role "$MINECRAFT_OFFICIAL_ROOT/runs/entity-role-geometry-batch-20261017-v1/$release/$entity")
  done
  role_arguments+=(--role "$MINECRAFT_OFFICIAL_ROOT/runs/entity-horse-geometry-batch-20261017-v1/$release/horse")
done
mapped_arguments=()
for batch in mapped-entity-role-geometry-pilot-20261017-v1 mapped-entity-role-geometry-batch-20261017-v1; do
  measurement_root="$MINECRAFT_OFFICIAL_ROOT/runs/$batch"
  while IFS= read -r measurement; do
    mapped_arguments+=(--mapped-role "$measurement")
  done < <(python3 - "$measurement_root" <<'PY'
import json
from pathlib import Path
import sys
root = Path(sys.argv[1])
for row in json.loads((root / "results.json").read_text()):
    print(root / row["version"] / row["entity"].split(":")[1])
PY
  )
done
python3 tools/extraction/extract_entity_geometry.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-entity-native-all-20261017-v1" \
  --transitions "$MINECRAFT_OFFICIAL_ROOT/runs/villager-box-transitions-all-20261017-v1" \
  "${legacy_arguments[@]}" \
  "${role_arguments[@]}" \
  "${mapped_arguments[@]}" \
  --role-fixture tests/Umpk.Client.Tests/Fixtures/EntityRoleGeometry.native.json \
  --mapped-role-fixture tests/Umpk.Client.Tests/Fixtures/MappedEntityRoleGeometry.native.json.gz \
  --legacy-fixture tests/Umpk.Client.Tests/Fixtures/LegacyVillagerGeometry.native.json \
  --check
python3 -m unittest discover -s tools/extraction -p test_extract_entity_geometry.py
```

The 49 datasets cover the measured releases from 1.9 through 1.14.3, and the measured protocol representatives from 1.14.4 through 26.3. The Boolean selector identifies the native baby metadata field: index 11 through 1.9.4, index 12 from 1.10 through 1.13.2, and the measured modern indices thereafter. Each variant contains `[selector, canonicalPose, width, height, fixed, blocksBuilding]`. Refresh rules distinguish retained minimum corners, retained minima only on growth, and recentering. Position reapplication always recenters. Native 1.11 already recenters shrinking dimensions, while 1.10.2 retains their minimum corner. `JavaGameData.EntityGeometryData` returns the geometry source independently; the interaction source retains its `IEntityGeometrySource` adapter. Unknown entity types and poses remain unknown. Attribute scaling beyond the default value, riders, specialized geometry and native mob interpolation still need implementation and live validation.

The four early 1.14 datasets also contain native cat, horse and iron golem geometry. Cats and horses retain the Boolean baby selector; iron golems use `[-1, "none", 0, 0]` and only selector-zero variants. Each type retains its measured pose transitions, including sleeping dimensions. The separate role fixture contains 5,292 transitions across twelve release/entity measurements and three deduplicated cases. `--role` checks the original class origins, method owners, age predicate, complete callbacks and artifact hashes before adding each role. Include these arguments when regenerating the datasets.

The 32 mapped protocol datasets also contain native cat, cow, horse and golem geometry. The 176 measurements cover 44 mapped releases and 363,753 native transition rows. The compressed replay fixture deduplicates 25 cases while retaining each release's artifact and measurement hashes. Gzip output uses no filename or clock metadata. The extractor checks the mapped method owners, original class preservation, official mappings and the native living-scale getter. Attribute scaling beyond the default value remains unmeasured; fixed dimensions retain their native behavior. Protocol aliases must agree on their geometry and scale contract. The official 26.1.1 and 26.1.2 clients use existing protocol 775; their release names share the same generated descriptor.

The extractor verifies original class preservation, source hashes and separate measurements for protocol aliases. Unequal aliases and duplicate release inputs fail. The native replay fixture deduplicates equal transition cases while retaining each release's artifact and measurement hashes; its five cases cover 2,612 transition replays across twenty-one unmapped releases.

`extract_legacy_item_obstruction.py` adds measured legacy item entities' nonblocking rules from 1.8 through 1.14.3. It verifies both native constructors, original client-class preservation and each version's audit of every write to the building flag. Protocol 47 requires separate measurements for all ten releases and retains their distinct obfuscated class bindings. Its native field is `k`; protocols 393–404 use `j`, and protocols 477–490 return to `i`. The remaining legacy entity keys retain unknown rules. Canonical identities join the native measurements to the registry while output keys preserve the registry's original spelling.

```bash
classic_items="$MINECRAFT_OFFICIAL_ROOT/runs/classic-item-obstruction-all-20261017-v1"
classic_aliases=()
for release in 1.8 1.8.1 1.8.2 1.8.3 1.8.4 1.8.5 1.8.6 1.8.7 1.8.8; do
  classic_aliases+=(--alias "$classic_items/$release")
done
python3 tools/extraction/extract_legacy_item_obstruction.py --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" --measurements "$classic_items/1.8.9" "${classic_aliases[@]}" --check
```

```bash
python3 tools/extraction/extract_legacy_item_obstruction.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-item-obstruction-20261017-v1" \
  --alias "$early_measurements-v4/1.10/item" \
  --alias "$early_measurements-v4/1.10.1/item" \
  --check
python3 tools/extraction/extract_legacy_item_obstruction.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/legacy-item-obstruction-1.11-20261017-v1" \
  --check
python3 tools/extraction/extract_legacy_item_obstruction.py \
  --data data/java \
  --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$legacy_measurements/1.11.2/item" \
  --alias "$legacy_measurements/1.11.1/item" \
  --check
for release in 1.12 1.12.1 1.12.2 1.13 1.13.1 1.13.2; do
  python3 tools/extraction/extract_legacy_item_obstruction.py \
    --data data/java \
    --official "$MINECRAFT_OFFICIAL_ROOT" \
    --measurements "$legacy_measurements/$release/item" \
    --check
done
```

For the earlier nonblocking item rules, check each 1.9 release and both protocol-110 aliases:

```bash
for release in 1.9 1.9.1 1.9.2; do
  python3 tools/extraction/extract_legacy_item_obstruction.py --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" --measurements "$early_measurements-v3/$release/item" --check
done
python3 tools/extraction/extract_legacy_item_obstruction.py --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" --measurements "$early_measurements-v4/1.9.4/item" --alias "$early_measurements-v4/1.9.3/item" --check
```

The `--legacy` inputs also accept the original pose-aware clients from 1.14 through 1.14.3. Their receipts verify native enum names, false baby defaults, pose metadata at index 6, building flags and a source-backed age-only scale method. The runtime name `SNEAKING` maps to canonical `CROUCHING`; obfuscated enum field names do not establish pose identities.

```bash
for release in 1.14 1.14.1 1.14.2 1.14.3; do
  python3 tools/extraction/extract_legacy_item_obstruction.py --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" --measurements "$pose_measurements/$release/item" --check
done
```
