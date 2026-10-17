# Early 1.14 farm placement verification

The tested producer is `703400f16ab87fcc00aa81834ee5bf45880abf2f`. It adds measured cow, pig, sheep and chicken geometry to protocols 477, 480, 485 and 490, covering 1.14, 1.14.1, 1.14.2 and 1.14.3. The [legacy farm verification](legacy-farm-placement.md) records the preceding Boolean-age eras; the [classic farm verification](classic-farm-placement.md) covers protocol 47.

## Native behavior and identity

All four types use Boolean baby metadata at index 14 and pose metadata at index 6. Their original metadata callbacks resize immediately. Refresh recenters shrinking bounds and retains the minimum corner on growth; a later position callback recenters them. Sleeping dimensions are fixed at .2F by .2F for both ages. The other six measured poses retain the native age-scaled dimensions.

| Type | Adult width / height outside sleeping | Baby width / height outside sleeping | Native registry ID |
| --- | --- | --- | --- |
| Cow | .9F / 1.4F | .45F / .7F | 10 |
| Pig | .9F / .9F | .45F / .45F | 54 |
| Sheep | .9F / 1.3F | .45F / .65F | 61 |
| Chicken | .4F / .7F | .2F / .35F | 8 |

Sixteen original release/type measurements contain 9,408 transitions: all source/target pairs for two ages and seven poses at three positions. The helper invokes original constructors, age and pose metadata callbacks, cached dimensions, explicit refresh and position setters. Its controlled world inputs do not execute complete native entity/world ticks. Default attribute state is measured; scale sweeps, riders and moving mobs remain separate work.

Native registry lookups return the canonical IDs and names. The original entity-type getter must return the same type passed from its source-declared factory. Extraction also verifies every original class byte, official client/server SHA-1 and SHA-256, source and helper hashes, class origins, complete source/runtime inheritance, Boolean predicates, callback delegation and actual method owners. It accepts the explicit `pose-farm-role-v1` format only for the complete four-release, four-type cohort.

Representative original sources are `1.14-client-decompiled/aij.java` for typed factories, `aid.java` for the age predicate and callback, `aio.java` for the scale getter and `aif.java` for pose, type and bounds behavior. The later entity-type owners are `1.14.1-client-decompiled/aim.java`, `1.14.2-client-decompiled/aim.java` and `1.14.3-client-decompiled/aip.java`. Each source-derived type-key getter queries its native `fm.l` registry; the original entity-type getter returns the entity's stored type. Every measured source hash is retained in the receipts and dataset provenance.

The initial pilot lacks native registry identity. A second attempt measures its first actor but fails while initializing receipt metadata. The third cohort completes native identity but omits intermediate farm superclass origins and source hashes. The new validator rejects that provenance before writing data. The fourth cohort records the complete actual hierarchy, verifies its original origins and source, and reproduces every prior dimension and phase unchanged. Earlier cohorts remain available as incomplete evidence; extraction uses the complete fourth cohort.

## Data and regression results

The four datasets preserve their existing villager, cat, horse and iron golem profiles and provenance. DataGen changes `EntityGeometryV477`; the other 370 shared tables and all other generated files remain byte-identical. The new compressed fixture deduplicates four cases and retains every release's original registry ID, artifact and measurement hashes. Its gzip header has no filename or clock metadata. The [extraction guide](../../tools/extraction/README.md) records the complete regeneration command.

Before generation, all 224 new dimension cases and sixteen connected runtime cases fail at missing geometry. After generation, all 240 pass. The connected cases verify native registry IDs and replay every measured metadata and position phase through actual packet application. The data cases cover both ages and all seven native poses, including fixed sleeping bounds. The broader geometry, placement-animation and tick-control group passes 11,011 client cases; all 398 geometry-table cases and 180 extraction tests pass. Count baselines deliberately add sixteen client cases and 224 data cases. The runtime uses its existing geometry owner; no runtime API or codec pin changes.

## Live baseline and fixed acceptance

The baseline uses the previous immutable MCC build backed by UMPK `f021075d55bababf7ffadcd33284011dae1811a4`. A fresh vanilla 1.14.3 leg completes fifteen independent server checks: nine placements succeed and six reject occupied destinations. The capture contains fifteen use packets and no swings, with far farm controls still tracked. This reproduces the missing animation despite successful server placement.

The first baseline attempt stops during setup because the private gamerule scanner misses 1.14's static typed key initializers. The corrected parser recognizes that source form; four focused parser cases pass. A fresh baseline uses unchanged producer binaries. The setup failure remains retained with its cleanup results.

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` were rebuilt against the tested UMPK snapshot with temporary project references. Their suites pass 1,999 and 918 cases. All 33 checked producer/consumer DLLs match; live checks use an immutable copy of that tested MCC output. Sibling source and build files remain unchanged.

Each full farm leg uses fifteen ordinary survival actions: water, lava and plant replacement, an occupied slab, plus five named adult/baby pairs for villagers and the four farm types. RCON independently checks starting material, destination occupancy, resulting block and selected stack count. Adult bounds obstruct placement while babies leave clearance; pigs and chickens stand on a bottom slab. Each entity case waits twenty native server ticks. Captures verify one use per action, use-before-swing for each accepted placement and no swing for occupied destinations. All legs use seed `731015`, Java 11, isolated directories/ports and a 1 GiB server heap.

| Release | Protocol | Scenarios | Uses / swings | Selected byte-identical frame round trips |
| --- | --- | --- | --- | --- |
| 1.14 | 477 | 15 | 15 / 9 | 1,081 |
| 1.14.3 | 490 | 15 | 15 / 9 | 1,087 |
| 1.13.2 | 404 | 15 | 15 / 9 | 1,089 |
| 1.14.4 | 498 | 7 | 7 / 5 | 336 |

The two changed-protocol legs pass thirty scenarios. The lower adjacent protocol repeats all fifteen farm scenarios. The upper adjacent protocol repeats seven established villager/item and ordinary placement scenarios; it does not claim new farm profiles on protocol 498. Together, fixed and adjacent checks pass all 52 scenarios.

Including the baseline's 1,077 selected frames, the five captures round-trip 4,670 selected frames from 8,633 play frames. The audit verifies named actors' original types, positions and Boolean ages: index 14 in the pose eras and 12 on the lower neighbor. Each full leg checks six far animal/villager controls; the upper basic leg checks two villagers. Every control appears before the first use and is never removed. All owned processes exit, every allocated port closes, and each server observes the client disconnect.

## Full checks and retained proof

| Check | Observed result |
| --- | --- |
| Exact Release rebuild | Passed, zero warnings or errors |
| Unfiltered full suite | 31,351 passed, one existing citation-count guard failure, seven opt-in skips |
| Test counts | All 17 totals match; the gate fails because the full run contains a failure |
| Format, canonical data and language | Passed |
| Generated output | All 61 files match regeneration |
| DMCBK and MCC | 1,999 / 918 passed; all 33 DLL comparisons match |
| Fixed and adjacent live placement | 52/52 scenarios |
| Native AOT publish and smoke | Passed; 24,518,208 bytes below the 25,165,824-byte limit |

The existing citation test still fails its greater-than-400 comment-count guard after resolving the collected citations. Its threshold and source remain unchanged. The earlier sound allocation failure does not recur in this full run; its cause remains unproven and its limit is unchanged. The previous published snapshot also has one Windows DNS fake-server failure followed by a successful same-commit diagnostic rerun. All seven checks for that snapshot then pass without changing code, tests or timeouts. That DNS failure's cause remains unproven; both attempts are retained in its publication receipts.

The immutable local proof is `/home/anon/Projects/MinecraftOfficial/runs/pose-farm-proof-20261017-v1/proof.json`, SHA-256 `9b02de2f121bd498c3867b048c4621033d00d41785cc4a05a56ce76984346306`. It retains the tested source archive, exact commands/results, native receipts, source audits, capture checks and AOT binary, with links to the previous proof and publication. Official artifacts, worlds, credentials and raw logs remain outside Git.

These results establish the measured age/pose phases and listed stationary-entity stone-placement scenarios. Farm profiles on later protocols, other entity types, attribute scaling, riders, native mob interpolation, specialized placement and the remaining version/scenario matrix still need acceptance. The UMPK PR remains draft.
