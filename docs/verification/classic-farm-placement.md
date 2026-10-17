# Classic farm animal placement verification

The tested producer is `fa9ac8b49be478cb472ff20c606f1f414e5c5d17`, which adds measured cow, pig, sheep and chicken geometry to protocol 47. All ten releases from 1.8 through 1.8.9 share these rules. The earlier [classic villager and dropped-item verification](classic-entity-placement.md) remains separate.

## Behavior and source evidence

An unmeasured entity can suppress a predicted placement swing even when it is far from the target. The four farm-animal types now use the existing signed-age and deferred living-phase owner. Metadata index 12 is a signed byte; negative values select the baby dimensions. Metadata and position callbacks retain cached sizes until an eligible living phase runs. That phase preserves the box minimum, and a later position update recenters it. The original world gate requires loaded columns across an inclusive 32-block horizontal radius.

| Type | Adult width / height | Baby width / height | Original registry ID |
| --- | --- | --- | --- |
| Cow | .9F / 1.3F | .45F / .65F | 92 |
| Pig | .9F / .9F | .45F / .45F | 90 |
| Sheep | .9F / 1.3F | .45F / .65F | 91 |
| Chicken | .4F / .7F | .2F / .35F | 93 |

Forty original release/type measurements contain 10,240 signed-age predicates and 3,000 size transitions. The compressed role fixture retains four distinct cases and every release's artifact and measurement hashes. Each role agrees across all ten aliases. Original constructor, watcher and age-size methods run with controlled world inputs. These measurements do not execute a complete native entity/world tick.

The source review also checks all forty living-call chains. Each actor inherits the original ageable living method, or its override calls the superclass exactly once outside conditional branches. The ageable client branch invokes the measured age-size phase. The review retains original source hashes. Moving-mob interpolation and other runtime conditions need separate evidence.

The explicit `classic-role-v1` receipt format binds each measured actor to its canonical entity, original registry class and numeric ID. Extraction verifies official client/server SHA-1 and SHA-256, every preserved original class, source hierarchy, method owners, class origins and helper source/class hashes. It rejects incomplete or duplicate aliases, mixed identities and changed phases. The first measurement cohort used the older unlabelled helper output; the second adds entity identity fields while retaining the same original constructor and phase calls. Both cohorts remain available.

Only `EntityGeometryV47` changes. The other 370 shared tables, all other generated files, existing villager rule and villager fixture remain unchanged. The earlier nonblocking item table stays intact. The generated-table review decodes each new policy and variant. The runtime uses its existing geometry owner; this change adds no runtime API or protocol pin.

## Regression results

Before generation, eight data cases fail because the four age profiles are unavailable. Eighty connected runtime cases also fail at unavailable geometry; the existing twenty-two villager cases pass. After generation, all 102 connected cases and all 70 packed-geometry cases pass. The broader geometry, placement-animation and tick-control group passes 10,927 cases. Python extraction tests pass 170 cases.

The count baseline deliberately adds eighty connected theory rows and eight data rows. The connected tests replay the native signed predicates and every size transition through actual UMPK sessions and their existing tick source. They read expected dimensions from the native fixture. The old villager fixture remains byte-identical.

## Live baseline and harness correction

The previous immutable MCC build uses UMPK `cf3bf34e9115cac1a654f62540f1522e7a90c539`. On a fresh vanilla 1.8.9 server, all fifteen server-side placement and stack checks complete: nine placements succeed and six reject an occupied destination. With the four farm-animal controls present, the capture contains fifteen use packets and no swings. The first successful placement therefore reproduces the missing animation.

The first attempt stops before the first cow case because the inherited launcher sets `spawn-animals=false`. Original `WorldServer` removes animal instances when that flag is false, including summoned test animals. The corrected owned launcher keeps the native flag true; ordinary mob spawning is disabled through the gamerule after startup. The failed attempt is retained as a harness fault. Both runs stop their owned processes and close their allocated ports.

## Fixed live checks

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` were built against the tested UMPK snapshot through temporary project references. Their source and build files remain unchanged. The immutable client copy matches all 33 checked producer/consumer DLLs.

The fresh 1.8.9 leg uses fifteen normal survival input actions. Four replace water, lava or plants. An occupied slab rejects placement. Five named adult/baby pairs cover villagers and all four farm types: adults obstruct the destination, while babies leave sufficient clearance. Pigs and chickens stand on a bottom slab; other types stand one block below the destination. Each entity case waits twenty native server ticks. Four additional farm controls, two villager controls and a dropped item remain tracked away from the target. RCON independently verifies starting material, destination occupancy, final block and selected stack count.

A separate fresh 1.9 leg repeats the seven existing villager/item and ordinary-placement scenarios with the same new client. It checks the adjacent protocol boundary without claiming farm geometry on protocol 107. Both legs use seed `731015`, Java 11, isolated ports/directories and a 1 GiB server heap.

| Release | Protocol | Scenarios | Byte-identical selected frame round trips |
| --- | --- | --- | --- |
| 1.8.9 | 47 | 15/15 passed | 948 |
| 1.9 | 107 | 7/7 passed | 102 |

Captures verify use-then-swing on every successful placement and no swing on each rejected placement. The audit round-trips 1,050 selected frames from 2,197 play frames. It also checks all ten named 1.8.9 entities' type, original spawn position and signed age bytes `0`/`-1` at index 12, plus the adjacent villagers' Boolean values at index 11. Entity-destroy packets verify that all four far farm controls and both far villagers were tracked before the first use and were never removed during the run. All owned processes exit, all allocated ports close, and each server observes the client disconnect. This establishes the listed stationary-entity and ordinary stone scenarios; moving mobs and specialized items remain outside these legs.

The first fixed leg completed all fifteen independent server checks, but its capture checker matched marker `1` inside markers `10`–`14`. It is retained as a harness failure. The final fresh leg uses delimited markers and passes the complete checker. The fix changes only the private harness; producer binaries remain unchanged.

## Current checks

| Check | Observed result |
| --- | --- |
| Exact Release rebuild | Passed, zero warnings or errors |
| Unfiltered full suite | 30,939 passed, one existing citation-count guard failure, seven opt-in skips |
| Test counts | All 17 totals match; the gate fails because the full run contains a failure |
| Format, canonical datasets and language | Passed |
| Generated output | All 61 generated files match regeneration |
| DMCBK and MCC consumer suites | 1,999 and 918 passed; all 33 DLL comparisons match |
| Fixed live checks | Fifteen scenarios on 1.8.9 and seven adjacent scenarios on 1.9 passed |
| Native AOT publish and smoke | Passed; 24,518,208 bytes below the 25,165,824-byte budget |

The full-test and count gates exit 1 because of the existing `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees` count guard. No guard, expected count or allocation limit was relaxed to hide a failure. Seven environment-gated cases remain skipped in the unfiltered run; Native AOT smoke ran separately.

## Evidence and remaining scope

Native measurements remain under `/home/anon/Projects/MinecraftOfficial/runs/classic-farm-geometry-all-20261017-v1` and `classic-farm-geometry-all-20261017-v2`. Baseline runs are `classic-farm-placement-baseline-20261017-v1` and `classic-farm-placement-baseline-20261017-v2`, under the same `runs` directory. The immutable proof is `/home/anon/Projects/MinecraftOfficial/runs/classic-farm-proof-20261017-v1`. Its `proof.json` records exact source, commands, gate logs, binary hashes, native receipts, source-call-chain review, capture audits and the Native AOT binary. Consumer receipts remain in `classic-farm-consumers-20261017-v1`; the final live roots are `classic-farm-placement-fixed-20261017-v2` and `classic-farm-adjacent-20261017-v1`. The earlier fixed harness attempt remains in `classic-farm-placement-fixed-20261017-v1`. Raw logs, worlds, credentials and downloaded artifacts stay outside Git.

This change supplies the four farm profiles for protocol 47. It does not establish farm-animal geometry on other protocols. Other entity types, non-default scaling, riders, native mob interpolation, specialized block/item placement and the complete version/scenario matrix remain unfinished. Separate shulker and trigonometry work is excluded from this tested snapshot.
