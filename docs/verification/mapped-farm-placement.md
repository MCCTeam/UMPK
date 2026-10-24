# Mapped farm placement verification

Producer `6098caadefc588dafca74acbfb34788a94a4929b` adds pig, sheep and chicken geometry to every stable release from 1.14.4 through 26.3. The 44 releases share 32 protocol datasets. Existing villager, cat, horse, cow and golem profiles and provenance remain unchanged. Earlier eras are recorded in [early 1.14 placement](pose-farm-placement.md), [legacy farm placement](legacy-farm-placement.md) and [classic farm placement](classic-farm-placement.md).

## Native measurements

Original client classes execute 335,772 age/pose transitions across 132 release/type records. Each record covers both Boolean ages, every native pose and three distinct positions. Measurements include cached dimensions before and after age/pose callbacks, explicit refresh and position reapplication. Original factories, actual entity types, numeric registry IDs/keys, strict child observations and complete class hierarchies are verified. Controlled world inputs and default attributes define the scope; complete entity/world ticks, nondefault scaling, riders and moving mobs remain separate work.

| Boundary | Observed behavior |
| --- | --- |
| 1.14.4 | Boolean baby metadata at index 14; pose metadata at 6; growth retains the minimum corner |
| 1.15 | Baby metadata moves to index 15 |
| 1.17 | Baby metadata moves to index 16; refresh recenters bounds |
| 1.20.5 | Geometry uses the scale attribute; this cohort measures its default value |
| 26.1 | Baby chickens change from .2F × .35F to .3F × .4F |
| 26.3 | The child predicate also checks `canBeABaby()`; the measured farm classes inherit its true default |

Adult dimensions outside sleeping remain .9F × .9F for pigs, .9F × 1.3F for sheep and .4F × .7F for chickens. Pigs and sheep use half-size baby dimensions. Chickens use the measured dimensions on either side of the 26.1 boundary. Sleeping dimensions remain fixed at .2F × .2F for both ages.

The chicken source confirms the boundary: `1.21.11-client-decompiled/net/minecraft/world/entity/animal/chicken/Chicken.java` scales the type dimensions by .5F; the matching 26.1 source declares `EntityDimensions.scalable(0.3F, 0.4F)`. Age and metadata callback ownership is in `AgeableMob` or the older `AgableMob`; pig overrides delegate to that base. Every callback chain is checked against the matching hashed source.

A separate post-measurement audit binds factory and registry source contracts to unchanged receipts, measurements, helper classes, client artifacts, mappings and decompiled-tree completion hashes. It covers factory declarations in both `EntityType.java` and the newer `EntityTypes.java`/`EntityTypeIds.java` form. Numeric lookup must resolve the source-named `getId`; the newly available `getIdOrThrow` cannot substitute for it. The first pilot stopped at that ambiguous numeric-method heuristic; its partial evidence remains retained. The corrected helper completes all 132 records.

The audit SHA-256 is `3e2c2c8bba4430330891912ffab142370f30bcbfca0ead465ce74b982c7dd401`. Input summaries have SHA-256 `9185181e9b2db9f712f8e4bea140188303f4e0a43e5e9d0e994c86de522377b1` and `3c5e42b9e828bec773d93c622b54c33cace3ccd56bc6fcc8088546e5df39f5c7`. The [extraction guide](../../tools/extraction/README.md) records the exact command. Its explicit `mapped-farm-role-v1` format requires the whole cohort, matching aliases and the hash-pinned source audit before any dataset write.

## Data and runtime checks

All 132 connected runtime cases and 27 source-derived data boundary cases fail at missing geometry before generation. The initial data-test attempt has a compile error from an incorrect enum member; it is retained separately and does not count as the expected failure. The corrected tests establish the missing-rule failures.

After generation, all 159 cases pass. The runtime cases check the native registry IDs and replay every row's metadata and position callbacks through packet application. Extraction also validates each explicit-refresh phase; the connected runtime test does not invoke that native refresh operation. The deterministic gzip fixture deduplicates 19 cases and retains every release's measurement, artifact and native-ID evidence. Its fields match all original measurements. The extractor's `--check` reproduces every dataset and the fixture.

The broader client geometry, placement-animation and tick regressions pass 11,143 cases; 425 geometry-table tests and all 192 Python extraction tests pass. Baselines deliberately add 132 client cases and 27 data cases. DataGen changes eight shared geometry tables; 362 shared tables remain identical. The old protocol-498 table now deduplicates to the unchanged protocol-477 table. Only `SharedTables.g.cs` and `V498/Descriptor.g.cs` change among generated files. The existing runtime geometry owner applies the new data; no runtime API, codec pin or allocation-limit change is needed.

The exact Release rebuild passes without warnings or errors. The full suite reports 31,510 passes, one existing citation-count guard failure and seven opt-in skips. All 17 suite totals match; the count gate fails solely because of the test failure. Format, dataset, language and generated-output gates pass, with all 61 generated files byte-identical to regeneration. The citation threshold remains unchanged.

## Consumer and live checks

The fresh 1.14.4 baseline uses immutable MCC output backed by producer `703400f16ab87fcc00aa81834ee5bf45880abf2f`. Its fifteen server checks observe nine successful placements and six occupied-target rejections, while the capture contains fifteen use packets and zero swings. Owned client/server/launcher processes exit, allocated ports close and the server observes the disconnect.

The first new consumer attempt stops before tests because MSBuild mixes symlink and physical snapshot paths. The failed attempt is retained. A corrected temporary project setup uses the physical path, after restoring all fifteen producer DLLs byte-identically to the full-tested build. No production source changes are made for this harness correction.

The initial 1.21.11 live leg stops during entity setup after five ordinary scenarios: vanilla reads the JSON-formatted `CustomName` argument as literal component text, so the name selector does not match. The retained capture decodes that literal JSON name. The source-backed harness correction chooses the native component form from `Entity.java`; two focused tests and eleven source-reader checks pass. A fresh 1.21.11 leg passes all fifteen scenarios with unchanged producer and consumer binaries. The partial attempt is retained and does not count as a passing leg.

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` are tested with temporary project references to the physical producer snapshot. Their suites pass 1,999 and 918 cases. All 33 producer/consumer DLL comparisons match. Live legs use an immutable copy of that verified MCC output. Sibling source files remain unchanged.

Each full leg performs fifteen ordinary survival actions: four fluid/plant replacements, an occupied slab, and five fresh adult/baby pairs for villagers and the four farm types. Server-side material, entity obstruction, resulting block and selected stack count are checked independently. Each entity initializes for twenty native server ticks. Persistent far controls remain tracked, captures verify one use per action, and accepted placements send a swing after use except on 26.3, whose native policy sends no client swing. Occupied targets produce no swing. All legs use seed `731015`, isolated directories/ports and a 1 GiB server heap. Java 11 runs through protocol 754; later legs use Java 25.

| Release | Protocol | Scenarios | Uses / swings | Selected byte-identical frames |
| --- | --- | --- | --- | --- |
| 1.14.4 | 498 | 15 | 15 / 9 | 1,088 |
| 1.15 | 573 | 15 | 15 / 9 | 1,090 |
| 1.16.5 | 754 | 15 | 15 / 9 | 1,085 |
| 1.17 | 755 | 15 | 15 / 9 | 214 |
| 1.20.4 | 765 | 15 | 15 / 9 | 256 |
| 1.20.5 | 766 | 15 | 15 / 9 | 255 |
| 1.21.11 | 774 | 15 | 15 / 9 | 995 |
| 26.1 | 775 | 15 | 15 / 9 | 995 |
| 26.3 | 777 | 15 | 15 / 0 | 988 |
| 1.14.3 | 490 | 15 | 15 / 9 | 1,087 |

The nine changed-protocol representatives pass 135 scenarios; the lower adjacent 1.14.3 leg passes fifteen more. Together, fixed and adjacent legs pass 150 scenarios. The baseline adds fifteen independently checked scenarios. These are selected behavior boundaries, not acceptance of every release/scenario combination.

The initial capture selection omitted 1.17's singular removal packet and the later idle-movement packet name. Its incomplete removal-coverage claim is retained and marked superseded. The corrected selection includes those forms and client player-position packets. The audit requires observed target removals, tracked far controls, initial player teleport and idle movement, and verifies every selected frame's byte-identical decode/encode round trip. It joins original registry IDs to named actors' spawn positions and age metadata; absent adult age metadata is explicitly distinguished from the source-measured false default. It does not pretend an omitted default value appeared on the wire.

Across the eleven passing baseline/fixed/adjacent legs, 9,136 selected frames round-trip from 20,245 play frames. The audit checks 110 named adult/baby actors and 66 persistent far controls. All owned processes exit, allocated ports close and every passing server leg observes the client disconnect. The failed name-setup attempt remains separate from these totals.

Native AOT publish and the smoke test pass. The binary is 24,526,400 bytes, below the unchanged 25,165,824-byte limit. Publishing leaves all fifteen tested producer DLLs unchanged. The earlier sound allocation failure does not recur in this full suite; its limit remains unchanged.

## Retained proof and remaining work

The immutable local proof is `/home/anon/Projects/MinecraftOfficial/runs/mapped-farm-proof-20261017-v1/proof.json`, SHA-256 `d838cb8ef49d4a3939c2b51dec646eebcf8f790605ac706a362fbd3b22d61503`. Its 558 files retain the exact producer source archive, original native measurements and receipts, source audits, gate results, both consumer attempts, name-format failure, captures, corrected frame audits and AOT binary. It links to the preceding early-1.14 proof. Official artifacts, decompiled trees, credentials, worlds and raw logs remain outside Git.

These results establish the measured default-attribute age/pose geometry and the listed stationary-entity stone-placement scenarios. Other entity types, nondefault scaling, riders, native mob interpolation, specialized placement and the remaining full version/scenario matrix still need acceptance. The UMPK PR remains draft.
