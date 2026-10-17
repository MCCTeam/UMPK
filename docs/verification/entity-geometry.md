# Native entity placement verification

The 1.10.2 reproduction placed blocks successfully but omitted swings while villagers were tracked. Commit `9df820f5f1e61eee30bc38aaa00291b1441238ae` adds native villager geometry. A fresh capture then sent use → swing for the first three placements, but five newly tracked item drops suppressed the direct plant replacement swing. Commit `3528f9f7137cfc690f4fddfe77a1272787a923f4` adds the original item entity's nonblocking rule.

## Implemented behavior

The generated interaction source implements `IEntityGeometrySource`. Its 34 geometry datasets contain native age selectors, canonical poses and dimensions refresh rules for 1.10.2, 1.11 and the measured protocol representatives from 1.14.4 through 26.3. `JavaGameData.EntityGeometryData` loads geometry independently so entity tracking can trim unused block-interaction tables. The session owns current boxes and applies metadata callbacks in order. A position packet reapplies dimensions even when its coordinates are unchanged. World changes and entity removal clear owned geometry.

Native 1.10.2 retains a box's minimum corner when dimensions shrink or grow. Native 1.11 and the measured clients from 1.14.4 through 1.16.5 recenter on shrink and retain the minimum on growth; the measured clients from 1.17 recenter on refresh. Sleeping villager dimensions are fixed at 0.2 × 0.2. Baby standing dimensions change from 0.3 × 0.975 through 1.21.11 to 0.49 × 0.99 in 26.1 and 0.49 × 0.98 in 26.2/26.3.

Placement uses these owned boxes rather than the registry's placeholder dimensions. The original 1.10.2 and 1.11 item entities never block building; other unmeasured legacy entity keys remain unknown. Unknown poses, nondefault attribute scales, riders and specialized shapes still require implementation. Native mob tick interpolation and modern entity collision handlers remain unfinished.

The [AOT follow-up](entity-geometry-aot.md) records the trimming fix. The [1.11 follow-up](entity-geometry-1.11.md) records the next native boundary and its live acceptance.

## Native evidence

Artifacts and raw receipts remain outside Git under `/home/anon/Projects/MinecraftOfficial/runs/`:

- `placement-entity-native-all-20261017-v1`: original entity constructors and pose/metadata dimensions across 44 mapped releases.
- `villager-box-transitions-all-20261017-v1`: 111,924 original age/pose transition witnesses, including metadata callbacks, explicit refresh and position reapplication.
- `legacy-villager-geometry-20261017-v1`: 68 original 1.10.2 age/box witnesses at 17 captured packet-target positions.
- `legacy-item-obstruction-20261017-v1`: both original item constructors leave `Entity.i` false. All 2,946 original client class files are preserved. The bytecode audit finds eight writes to that field, each targeting `this` in an unrelated entity class. The native spawn handler constructs the measured item class for object type 2.

These probes use controlled world adapters and execute original model methods. They do not execute native entity/world ticks or establish full client behavior. The extractors verify original artifact hashes and native receipts; canonical provenance records their hashes.

## Verification for 3528f9f

The exact commit was archived into `villager-item-geometry-build-20261017-v1/source`. Build and test commands use the private .NET SDK 10.0.401. The full unfiltered output is captured in `/tmp/umpk-test.log` and preserved as the run's `full-test.log`.

| Check | Observed result |
|---|---|
| Release build | Passed, zero warnings |
| Full UMPK tests | 19,214 passed, 1 failed, 7 skipped |
| Test counts | All 17 suite totals match; the gate fails because one test fails |
| Existing failure | `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees`, line 138: the file-citation count guard requires more than 400 entries; the preceding resolution assertion passes |
| Focused client tests | 629 passed before the commit |
| Java data tests | 1,799 passed before the commit |
| Python extraction tests | 138 passed |
| Format, dataset, language | Passed |
| Generated outputs | All 61 files match |
| DMCBK | 1,999 passed against the exact UMPK producers |
| MCC | 918 passed on the full rerun; an earlier run hit an Avalonia pool `Stack empty` failure, and its two isolated rows passed |

The first fix deliberately adds 110 C# tests; the item fix adds three. No conformance pins or existing allocation limits change. Generated review confirms that the item fix changes only the entity-rule region of `BlockInteractionsV210`; its prior block, item, replacement and geometry fields remain identical.

## Live verification

The fresh 1.10.2 leg at `legacy-item-live-after-20261017-v1` passes all five scenarios with the exact binaries from `3528f9f`. Water, lava and plant destination replacement, plus direct plant replacement through `/useitem`, each produce one use packet immediately followed by one swing. Attempted placement into an occupied bottom slab produces one use packet and no swing. Every positive action changes the server block to stone and reduces the selected stack from 32 to 31; the slab and stack of 32 remain unchanged in the negative control.

The corrected harness uses a native spawn radius of zero, a cleared arena, and a normal one-block jump onto a pedestal. It preserves tracked villagers and summons one native item entity with a pickup delay of 32767. Decoding and byte-identical re-encoding of 566 captured frames confirms 17 villagers and one or more item drops are tracked before every use, including persistent item entity 46. This is a reconstruction of frame-ordered packet targets and metadata, not native tick interpolation.

The run uses `/home/anon/Projects/MinecraftOfficial/runtimes/java11/bin/java`, a 1 GiB server heap, and ports 56043/53773. Both client and launcher exit with code zero, the server observes disconnect, all three owned PIDs are absent and both ports close. No movement warning occurs. The capture is `1.10.2/configurations/logs/20261017-102020-127.0.0.1.zip` within the run. Producer comparisons cover 13 UMPK DLLs in DMCBK, 13 in MCC, and seven DMCBK DLLs in MCC; all 33 match. The live process maps independently verify the libraries actually loaded.

Reproduction command:

```bash
python3 /home/anon/Projects/MinecraftOfficial/runs/villager-item-geometry-proof-20261017-v1/live-item-control.py --out /absolute/fresh-run --client /home/anon/Projects/Minecraft-Console-Client/src/Mcc.Cli/bin/Release/net10.0/Mcc.Cli.dll --level-seed 4603516203263995078 1.10.2
```

The harness and its manifest, native server launcher, source overrides and trace decoder are preserved outside Git. Its snapshot uses the verified UMPK commit; DMCBK is `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC is `ea4d8f011608ad993808afa0e53ded40c0ac3f80`. Portable reruns must update the external artifact and producer paths before starting the runner.

Earlier invalid legs remain recorded as harness failures: random-spawn navigation failure; an unloaded pre-login arena; ground-level plant ray interception; and a pre-login RCON transport reset. None is an acceptance pass. The fourth leg independently verifies all server block/stack outcomes and the first three use → swing groups, then fails the missing direct-plant swing with item drops tracked.
