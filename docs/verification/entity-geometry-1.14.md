# Early 1.14 entity placement verification

Code commit: `a9d03fc5ea1d7b0258263494ff324ed9c0e86e2e`. DMCBK: `af22688e783fb2876ad55f06e0654a7a4c6c5641`. MCC: `ea4d8f011608ad993808afa0e53ded40c0ac3f80`. The tested archive matches all 3,955 tracked source files in the UMPK commit; pending tick and shulker work is excluded and preserved in the checkout.

UMPK suppressed placement swings whenever any tracked entity had unknown geometry, including distant animals. `6d65cf1` added native villager and item rules for protocols 477–490, but all four live legs still failed their swing assertions. The captures contained cats and iron golems. `3fbce47` added their measured bounds and a selector-free rule for golems; 1.14 passed, while 1.14.1–1.14.3 still failed around an unmeasured horse. The horse fix preserves native adult, baby and sleeping bounds without removing the animals or changing the seed.

## Native evidence

| Release | Protocol | Original client SHA-256 | Original server SHA-256 |
|---|---|---|---|
| 1.14 | 477 | `93907cbf0655aa5f10d049efa4d745f4250eecc5d0d5d1268046eec3144f323a` | `671e3d334dd601c520bf1aeb96e49038145172bef16bc6c418e969fd8bf8ff6c` |
| 1.14.1 | 480 | `7194d1326cf796f62ac55f3cb56f851c232895639ae27510f3207d5a73f91814` | `f822f0b730b7e1f05fca84248a6873400bac4ca449ff6762a55cab3d68b1f03e` |
| 1.14.2 | 485 | `9b740c9fd4955981125ebd3cd086baa8b4392963d043a3480b5b942a96637c25` | `b47fd85155ae77c2bc59e62a215310c4dce87c7dfdf7588385973fa20ff4655b` |
| 1.14.3 | 490 | `4063062677433988e012fba53e42e4c21adb6919ac7f061f19f3c6e75b334e07` | `942256f0bfec40f2331b1b0c55d7a683b86ee40e51fa500a2aa76cf1f1041b38` |

Java 11 executes the original client classes in controlled client-world adapters. Every original class byte is preserved in the unsigned probe jars. Receipts check source hashes, class origins and method owners. Each release contributes 588 cat, 588 horse and 147 golem transitions across every age/pose pair and three positions. The role fixture contains 5,292 replays, three deduplicated cases and all twelve release/entity receipts. Golems have no age selector; their tables use `[-1, "none", 0, 0]` and selector-zero variants. The measurements do not execute entity/world ticks or sweep attribute scale.

Native roots under `$MINECRAFT_OFFICIAL_ROOT/runs/`: `early-pose-entity-geometry-batch-20261017-v5`, `entity-role-geometry-batch-20261017-v1` and `entity-horse-geometry-batch-20261017-v1`. The [extraction commands](../../tools/extraction/README.md#entity-geometry) reproduce the datasets and fixtures. Generated review preserves all existing villager/cat/golem profiles, all 50 block-interaction tables, the other 46 geometry tables and 357 other shared tables.

## Executed checks

| Check | Result |
|---|---|
| Horse regression before extraction | 2,368 failed; 2,980 passed |
| Focused runtime and placement after extraction | 8,675 passed |
| Focused geometry table reader | 53 passed |
| Python extraction tests | 156 passed |
| Full Release build | Passed; zero warnings and errors |
| Unfiltered full suite | 27,351 passed; one failed; seven skipped; 27,359 total |
| Count gate | All 17 suite counts match; fails because conformance reports one failure |
| Format, dataset and language gates | Passed |
| Regenerated output equality | All 61 files match |
| DMCBK / MCC consumer tests | 1,999 / 918 passed |
| Consumer DLL comparison | All 33 comparisons match the tested producers |
| Native AOT | 24,485,168 bytes, below the unchanged 25,165,824-byte limit; smoke passes |

The full-suite failure is `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees`, at the existing `> 400` citation-count guard on line 138. Its preceding resolution assertion passes. The test and count baseline were not weakened. Seven environment-gated integration tests remain skipped in the full run; the Native AOT smoke is executed separately.

The exact commands, environment paths, source hashes, complete logs and native binary are preserved in `$MINECRAFT_OFFICIAL_ROOT/runs/horse-entity-rules-proof-20261017-v1`. `gates.json`, `consumer-live-status.json`, `trace-commands.json` and the consumer root's `results.json` record each executed command and exit code. The SDK is .NET 10.0.401. Consumer references use the recorded local-project targets and retain producer hashes through AOT publishing and consumer builds.

## Live acceptance

All four fresh vanilla server legs pass at the code commit. Each leg checks water and lava destination replacement, plant destination and clicked-plant replacement, and an occupied slab. The four successful placements send use then swing; the slab refusal sends use without a swing. Server block states and stack decrements are checked separately from the capture assertions.

The run uses seed `4603516203263995078`, fresh directories, isolated game/RCON ports and a 1 GiB server heap. Adult, baby and dropped-item controls remain tracked before all five uses. Natural villagers, cats, golems and the horses in 1.14.1–1.14.3 remain present. Every client and launcher exits with code zero. All 4,085 selected entity and interaction frames round-trip byte-for-byte out of 10,226 play frames. The trace reconstructs packet target positions, metadata and removals; it does not establish native mob interpolation.

The passing matrix is `$MINECRAFT_OFFICIAL_ROOT/runs/horse-entity-placement-matrix-20261017-v1`. The preceding failed matrices and traces remain in `early-pose-entity-matrix-20261017-v1`, `entity-role-placement-matrix-20261017-v1` and their proof roots. The consumer proof is `horse-entity-rules-consumers-20261017-v1`.

## Remaining work

Villager geometry covers 49 of 50 protocol datasets. Protocol 47 needs its signed-byte age selector and delayed resize phase. Other entity types, nondefault scaling, riders, native mob interpolation, pending tick integration, specialized placement and the remaining live matrix still need work. Separate cat/golem measurements now cover 44 mapped releases, 32 protocols and 139,905 native transitions; those additional datasets and runtime replays have not been integrated.
