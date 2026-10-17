# Classic entity placement verification

The classic villager fix is `ce87aa267a560db0826962496e92ba1d4028d5ff`. The dropped-item fix and tested producer snapshot are `cf3bf34e9115cac1a654f62540f1522e7a90c539`. These changes cover protocol 47, shared by all ten releases from 1.8 through 1.8.9.

## Implemented behavior

Villagers use the signed metadata byte at index 12: negative means baby. Native adult dimensions are .6F by 1.8F; baby dimensions are .3F by .9F. Metadata and position callbacks retain cached dimensions until the eligible living phase. That phase preserves the box minimum; a subsequent position update recenters it. The existing session tick invokes this deferred owner after its motion phase, with the native requirement that columns in an inclusive 32-block horizontal radius are loaded. Lifetimes without deferred rules skip the entity scan.

The model retains its four-argument geometry-rule constructor and adds explicit selector, refresh-timing and loaded-radius policies. Generated geometry schema 2 carries these policies; schema 1 and existing embedded geometry tables remain readable. Canonical geometry identities now join unqualified legacy registry names correctly.

Dropped items now have an audited permanently nonblocking placement rule. Previously, an item anywhere in the entity store could suppress the predicted placement swing, including items far from the destination. The extractor verifies each release's original classes, both item constructors and every write to the inherited building flag. Protocol 47 requires all ten release measurements with their distinct obfuscated bindings. Only the item rule is promoted; other unmeasured types remain unknown, and no item dimensions are invented.

The geometry change modifies only `EntityGeometryV47`; the item change modifies only `BlockInteractionsV47`. Each leaves the other 370 shared tables byte-identical. The decoded item-table review confirms unchanged block/item rules, fixed-dimension bytes and replacement bytes. Its 61 registry keys retain their original spelling, and only `item` has a nonblocking flag. Neither change moves protocol timelines or conformance pins.

## Native evidence and regressions

All ten original clients contribute 2,560 signed-byte age predicates and 750 native size transitions. Connected runtime tests cover deferred advancement, retained bounds and two missing-corner-column controls. The 22 new connected cases and the adjacent geometry/world-tick group pass; the latter contains 8,155 cases. Game model tests pass 1,026 cases, packed geometry tests 62, and generator geometry tests 30.

The dropped-item measurements execute both original constructors for every release. Their 20 observations all report a false building flag. Full class-file audits find 60 writes, six per release; all belong to unrelated entity classes and assign their own flag. Original Item and Entity contain no writer. The extractors verify official jar hashes, original-class preservation, source hashes and measurement receipts. These controlled constructor/callback measurements do not execute complete native entity/world ticks.

The new item regression first fails against the unchanged data: near and far items each produce one packet instead of placement followed by swing. After generation, all 734 placement-animation cases and all 55 block-interaction table cases pass. Extraction tests pass 166 cases. The 19 earlier native item measurements remain unchanged alongside the ten new measurements.

## Live baseline

The previous MCC snapshot, built with UMPK `82a8d9f69de29eb504f0073c40fc03a1afaef810`, reproduces the missing animation on a fresh vanilla 1.8.9 server. Four successful placements change the server block and decrement the selected stack, while an occupied slab rejects placement and preserves the stack. The capture contains five placement packets and no swing packets. The first successful placement therefore fails the animation assertion.

The first attempt stopped before any placement because the harness expected an error message for an empty legacy multi-entity selector. The corrected harness accepts the original dispatcher's empty result only after a positive selector control finds the villager controls. Both attempts retain their logs, captures and cleanup receipts. All owned processes exited and allocated ports closed.

## Fresh live checks

DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80` were built with temporary project references to the exact tested UMPK snapshot. Their source checkouts remain separate from this UMPK change. The live client uses an immutable copy of the verified output.

Each fresh leg uses seed `731015`, Java 11, isolated directories/ports, a 1 GiB server heap and seven normal MCC input actions. Four actions replace water, lava or plants. An occupied slab rejects placement. A named adult villager in a one-block pit obstructs the destination, while a fresh baby leaves sufficient clearance. Each geometry case waits twenty native server ticks before the action. RCON independently verifies starting material, destination occupancy, resulting block and selected stack count. Persistent dropped-item and adult/baby villager controls remain tracked.

| Release | Protocol | Scenarios | Byte-identical selected frame round trips |
| --- | --- | --- | --- |
| 1.8.9 | 47 | 7/7 passed | 467 |
| 1.9 | 107 | 7/7 passed | 102 |

Captures verify one use followed by one swing for each successful placement and no swing for either failure. The audit round-trips 569 selected frames from 1,088 play frames. It also verifies each named villager's original spawn position and age field: signed bytes `0` and `-1` at index 12 on protocol 47; Booleans `false` and `true` at index 11 on protocol 107. All owned clients and servers exited, all allocated ports closed, and each server observed the client disconnect. These legs cover stationary villagers and ordinary stone placement. Moving mobs and specialized block items still need live validation.

## Current checks

| Check | Observed result |
| --- | --- |
| Exact Release rebuild | Passed, zero warnings or errors |
| Unfiltered full suite | 30,851 passed, one existing citation-count guard failure, seven opt-in skips |
| Test counts | All 17 suite totals match; the gate fails because the full run contains a failure |
| Format, canonical datasets and language | Passed |
| Generated output | All 61 generated files match regeneration |
| DMCBK and MCC consumer suites | 1,999 and 918 passed; all 33 DLL comparisons match |
| Fresh live placement and obstruction checks | Seven scenarios each on 1.8.9 and 1.9 passed |
| Native AOT publish and smoke | Passed; 24,518,208 bytes below the 25,165,824-byte budget |

The full-suite failure remains `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees` at its existing count guard. The test and count gates exit 1. Their failure is not reported as a pass, and no guard or allocation limit was weakened.

## Evidence

The exact source archive, commands, gate logs, generated-table review, native receipts, consumer hashes, capture audit and Native AOT binary are preserved in `/home/anon/Projects/MinecraftOfficial/runs/classic-placement-proof-20261017-v1`. `proof.json` records their hashes and related run directories. The earlier classic geometry proof remains in `classic-geometry-core-proof-20261017-v1`; its controlled native measurements remain in `classic-villager-geometry-all-20261017-v1`. Dropped-item measurements remain in `classic-item-obstruction-all-20261017-v1`.

Consumer receipts are in `classic-placement-consumers-20261017-v1`, fresh live results in `classic-placement-live-fixed-20261017-v1`, and baseline attempts in `classic-placement-baseline-20261017-v1` and `classic-placement-baseline-20261017-v2`, under the same `runs` directory. `manifest.json`, `gates.json`, `extraction.json`, `aot-results.json` and `trace-summary.json` retain exact commands and producer identity. The live `results.json` retains server/client invocations, Java paths, artifact hashes, ports, input actions and cleanup results. Raw logs, worlds, credentials and downloaded artifacts remain outside Git. The seven environment-gated tests stay skipped in the unfiltered run; Native AOT smoke ran separately.

## Remaining scope

The later [classic farm animal fix](classic-farm-placement.md) adds cow, pig, sheep and chicken geometry for protocol 47, with separate native evidence and live acceptance.

Native interpolation for moving mobs, additional entity types, non-default scaling, riders, specialized block/item placement and the complete live version matrix remain unfinished. The separate shulker and trigonometry work is excluded from this tested snapshot. Existing mapped-era and tick-control evidence is recorded in [mapped entity placement](entity-geometry-mapped.md) and [native tick control](native-tick-control.md).
