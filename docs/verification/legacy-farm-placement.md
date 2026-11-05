# Legacy farm animal placement verification

The tested producer is `f021075d55bababf7ffadcd33284011dae1811a4`. It adds measured cow, pig, sheep and chicken geometry to the thirteen protocols used by all seventeen releases from 1.9 through 1.13.2. The separately measured [classic farm rules](classic-farm-placement.md) remain in place for protocol 47. The [early 1.14 verification](pose-farm-placement.md) records the later extension through 1.14.3.

## Native behavior

An unmeasured entity can suppress a predicted placement swing even when it stands far from the target. These four types now use their original Boolean child accessor and metadata resize callback through the existing runtime geometry owner.

| Releases | Age accessor | Native refresh | Registry form |
| --- | --- | --- | --- |
| 1.9–1.9.4 | Boolean 11 | Retain the minimum corner | Entity list |
| 1.10–1.10.2 | Boolean 12 | Retain the minimum corner | Entity list |
| 1.11–1.12.2 | Boolean 12 | Recenter on shrink; retain minimum on growth | Class registry |
| 1.13–1.13.2 | Boolean 12 | Recenter on shrink; retain minimum on growth | Entity type registry |

Each measured metadata callback applies the new dimensions immediately. A later position callback recenters the bounds. The 1.8 signed-byte selector, deferred living phase and loaded-column radius do not apply to these profiles.

| Type | Adult width / height | Baby width / height | ID before / after 1.13 |
| --- | --- | --- | --- |
| Cow | .9F / 1.4F | .45F / .7F | 92 / 9 |
| Pig | .9F / .9F | .45F / .45F | 90 / 51 |
| Sheep | .9F / 1.3F | .45F / .65F | 91 / 58 |
| Chicken | .4F / .7F | .2F / .35F | 93 / 7 |

The 68 release/type measurements execute original constructors, registry lookups, Boolean child getters, metadata callbacks and position setters. They cover all four source/target age pairs at three positions, for 816 transitions. Controlled empty-world inputs keep those operations measurable; complete entity/world ticks, moving mobs and riders are outside this measurement. Native IDs and names agree with each release's canonical registry, including flattened IDs. Every protocol alias agrees on its profile.

The source review follows the original hierarchy and verifies callback delegation. Eight later pig measurements resolve the callback to a pig override, which invokes the ageable superclass once outside conditional branches. The other actors inherit the ageable callback. Representative original sources are `1.9.4-client-decompiled/ro.java`, `1.10.2-client-decompiled/rt.java`, `1.11.2-client-decompiled/sk.java` and `1.13.2-client-decompiled/aeo.java`; their matching entity bases are `rr.java`, `rw.java`, `sn.java` and `aer.java`. Every measured source hash is retained in the receipts and dataset provenance.

The explicit `legacy-ageable-role-v1` reader verifies official client/server SHA-1 and SHA-256, unchanged original class bytes, source and helper hashes, class origins, registry identity, selector type, callback owner and inheritance. It rejects missing or duplicate releases, mixed identities and altered transitions. The first cohort stops at 1.11's different registry syntax after 32 measurements; it is retained as an incomplete producer attempt. Extraction uses the complete 68-row second cohort and supports all three native registry forms.

## Data and regressions

All thirteen datasets preserve existing entity profiles and provenance. The generator changes `EntityGeometryV107`, `EntityGeometryV210` and `EntityGeometryV315`; the other 368 shared tables and all other generated files remain byte-identical. The compressed native fixture deduplicates twelve cases and retains every release's artifact and measurement hashes. Its gzip header contains no filename or clock metadata. The [extraction guide](../../tools/extraction/README.md) records the regeneration command.

Before generation, all 104 new dimension cases and all 68 connected runtime cases fail at missing geometry. After generation, all 172 pass. The connected cases replay every measured metadata and position phase through actual packet application. The broader geometry, placement-animation and tick-control group passes 10,995 client cases; all 174 geometry-table cases and 175 extraction tests also pass. Test-count baselines deliberately add 68 client cases and 104 data cases. The existing runtime owns geometry updates; this change adds no runtime API or codec pin.

## Live baseline and fixed runs

The previous immutable MCC build uses UMPK `fa9ac8b49be478cb472ff20c606f1f414e5c5d17`. On a fresh 1.10.2 server it sends fifteen use packets and no swings, although nine placements succeed and six reject occupied destinations. Four far farm controls remain tracked alongside two villagers and a dropped item. This reproduces the missing animation independently of a client success message.

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` were rebuilt against the tested UMPK snapshot through temporary project references. Their suites pass 1,999 and 918 cases. All 33 checked producer/consumer DLLs match. The fixed live runs use an immutable copy of this tested MCC build.

Each fresh survival leg covers water, lava, plant replacement, an occupied slab, and five adult/baby pairs for villagers and the four farm types. Adults obstruct placement while babies leave clearance; pigs and chickens stand on a bottom slab. RCON verifies starting material, occupancy, resulting blocks and selected stack counts. Packet captures verify one use per action, use-before-swing on all nine successful placements and no swing on all six rejected placements. Each leg uses seed `731015`, Java 11, an isolated directory and ports, and a 1 GiB server heap.

| Release | Protocol | Scenarios | Uses / swings | Selected byte-identical frame round trips |
| --- | --- | --- | --- | --- |
| 1.9.4 | 110 | 15/15 | 15 / 9 | 233 |
| 1.10.2 | 210 | 15/15 | 15 / 9 | 237 |
| 1.11.2 | 316 | 15/15 | 15 / 9 | 229 |
| 1.13.2 | 404 | 15/15 | 15 / 9 | 1,095 |

An earlier fixed run passes 1.9.4 and 1.10.2, then stops before its first 1.11.2 action because the private harness treats the native selector response `found nothing` as a positive `found` match. The corrected parser uses exact translations from the verified client jar. Its focused cases distinguish both legacy negative forms, a positive match and an invalid command. The corrected run passes 1.9.4, 1.10.2 and 1.11.2, then stops during 1.13.2 setup because the gamerule scanner omits its source-backed map registration form. After adding that form, a fresh 1.13.2 leg completes with unchanged producer binaries. Both setup failures remain retained with verified cleanup.

The four fixed legs pass all sixty scenarios. The baseline capture adds 225 byte-identical selected frame round trips; all five captures total 2,019 selected frames from 5,189 play frames. Decoding verifies fifty named actors' original type, spawn position and Boolean age at index 11 or 12, including 1.13.2's flattened IDs and optional-component custom names. Each leg also verifies that its six far animal/villager controls spawn before the first use and are never removed. Every owned process exits, every allocated port closes, and each server observes the client disconnect.

## Checks and retained evidence

| Check | Observed result |
| --- | --- |
| Exact Release rebuild | Passed, zero warnings or errors |
| Unfiltered full suite | 31,111 passed, one existing citation-count guard failure, seven opt-in skips |
| Test counts | All 17 totals match; the gate fails because the full suite contains a failure |
| Format, canonical data and language | Passed |
| Generated output | All 61 files match regeneration |
| DMCBK and MCC | 1,999 / 918 passed; 33 DLL comparisons match |
| Fixed live placement | 60/60 scenarios across four releases |
| Native AOT publish and smoke | Passed; 24,518,208 bytes below the 25,165,824-byte limit |

The existing citation test fails its greater-than-400 count guard after resolving its scanned citations; its source and threshold remain unchanged. The earlier intermittent sound allocation failure does not recur in this full run. Its cause remains unproven and the existing limit stays unchanged.

The immutable local proof is `/home/anon/Projects/MinecraftOfficial/runs/legacy-farm-proof-20261017-v1/proof.json`, SHA-256 `6ee7f7ac09bf4aca96e1826ca67fb533c4b060ce48451a1e2531c995153dcaed`. It retains the tested source archive, exact commands and results, native receipts, capture audits, hashes and AOT binary, and links to the earlier classic proof. Official jars, worlds and raw logs remain outside Git.

These results establish the listed stationary-entity and ordinary stone-placement paths. Further acceptance work includes farm profiles on later protocols, other entity types, scaling, riders, native mob interpolation, specialized placement and the remaining version/scenario matrix. The UMPK PR remains draft.
