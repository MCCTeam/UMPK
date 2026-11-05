# Native entity placement from 1.11.1 through 1.13.2

Commit `d93d069fa1158f1247fb504b74f62c755e0ba46d` adds measured villager geometry and item obstruction rules for protocols 316, 335, 338, 340, 393, 401 and 404. Both protocol-316 releases are measured independently. Each original client produces twelve age/box transition witnesses and two item-constructor witnesses. Villager metadata index 12 is Boolean, defaults to false, and selects adult dimensions of 0.6 × 1.95 or baby dimensions of 0.3 × 0.975. Shrinking recenters the box; growth retains its minimum corner.

All eight original item classes leave the building flag false. Each whole-client bytecode audit finds seven writes, all targeting `this` in unrelated entity classes. The flag changes obfuscated name from `i` to `j` at 1.13; the extractor verifies this binding rather than assuming the name stays constant. Native spawn handlers establish the item class and object registry key. The probes preserve every original class file and verify its runtime origin. These are constructor, metadata callback and position-setter measurements with controlled world inputs; they do not execute native entity/world ticks.

The maintained extractors verify artifact hashes, source hashes, native receipts and equal profiles for protocol aliases. Generated review confirms that only the seven protocols' entity-rule regions change; prior block, item, fixed-box and replacement bytes remain identical. All 43 other protocol descriptors and 336 existing shared-table members remain identical. The geometry profiles reuse the protocol-315 blob. There are now 41 canonical geometry datasets. Unmeasured entity keys remain unknown.

## Verification

The exact commit is archived and built with .NET SDK 10.0.401. Its deliberate additions are 216 client tests, 14 Java data tests and five Python tests. No conformance pins or allocation limits change.

| Check | Observed result |
|---|---|
| Release build | Passed, zero warnings |
| Full UMPK tests | 19,475 passed, one failed, seven skipped; 19,483 total |
| Test counts | All 17 suite totals match; the gate fails because one test fails |
| Existing failure | `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees`, line 138: the file-citation count guard requires more than 400 entries; the preceding resolution assertion passes |
| Focused client regressions | 823 passed; the earlier run had 131 failures before implementation |
| Focused Java data regressions | 88 passed; the earlier run had 14 failures before implementation |
| Python extraction tests | 146 passed |
| Format, dataset and language | Passed |
| Generated outputs | All 61 files match |
| Native AOT | 24,481,040 bytes against the unchanged 25,165,824-byte limit; smoke test passed |
| DMCBK | 1,999 passed |
| MCC | 918 passed |

Native publishing preserves every tested producer hash. All 33 consumer DLL comparisons match, and the live process maps verify the loaded producers. DMCBK remains at `af22688e783fb2876ad55f06e0654a7a4c6c5641`; MCC remains at `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

## Live acceptance and harness correction

Each release passes five fresh survival actions through actual MCC commands: placement into water, lava and plants; direct plant replacement through `/useitem`; and attempted placement into an occupied bottom slab. Each positive action sends use → swing, changes the server block to stone and reduces the selected stack from 32 to 31. The negative control sends one use without a swing, preserving both slab and stack. Server observations establish these outcomes independently of command messages.

| Release | Protocol | Passing run | Byte-roundtrip frames | Villagers before every use | Persistent item control |
|---|---|---|---:|---:|---:|
| 1.11.1 | 316 | Original matrix | 512 | 19 | 47 |
| 1.11.2 | 316 | Original matrix | 587 | 19 | 47 |
| 1.12 | 335 | Original matrix | 573 | 19 | 47 |
| 1.12.1 | 338 | Original matrix | 585 | 19 | 47 |
| 1.12.2 | 340 | Original matrix | 562 | 19 | 47 |
| 1.13 | 393 | Contained rerun | 1,211 | 16 | 73 |
| 1.13.1 | 401 | Contained rerun | 1,188 | 17 | 72 |
| 1.13.2 | 404 | Contained rerun | 1,031 | 17 | 72 |

The original 1.13, 1.13.1 and 1.13.2 legs fail the lava case. Saved worlds contain obsidian at the intended destination and surrounding water. The 1.13 capture records lava state 50 at sequence 1395, followed by obsidian state 1129 at sequence 1408, before the action's start marker at sequence 1505. Water escaped the preceding case and contaminated the next setup. These original legs remain recorded as harness failures. Fresh reruns use containing side walls, wider cleanup and independent server assertions of the destination material immediately before each action. All three pass using the same product binaries.

Across the eight passing legs, 6,249 frames decode and re-encode byte-identically from 13,873 play frames. Frame-order reconstruction handles entity removal and item pickup. It verifies the controlled adult and baby villagers, the baby's Boolean selector, and the independently identified persistent item before every use. This establishes packet target and metadata state, not native mob tick interpolation or placement against nearby changing boxes.

Every leg uses Java 11, a 1 GiB server heap and seed `4603516203263995078`, with private ports and fresh directories. The client and launcher exit with code zero, the server observes disconnect, all owned PIDs disappear and both ports close. No movement warning occurs.

## Reproduction records

Evidence stays outside Git under `/home/anon/Projects/MinecraftOfficial/runs/`:

- `legacy-entity-geometry-batch-20261017-v1`: eight original-client probes, preserved-class audits, source bindings, artifact hashes and finalized extraction receipts.
- `legacy-entity-rules-proof-20261017-v1`: exact source, commands, test logs, AOT measurements, tested producers and trace tools.
- `legacy-entity-rules-consumers-20261017-v1`: consumer commands, results and all 33 DLL comparisons.
- `legacy-entity-matrix-20261017-v1`: original eight live legs, including the three contaminated failures.
- `legacy-entity-matrix-contained-20261017-v1`: fresh passing 1.13–1.13.2 reruns with the stronger setup assertions.

Each matrix preserves its executed `driver.py`, tool hashes, per-release ports, artifact hashes, process identities, commands, captured interactions and cleanup results. The proof directory preserves the trace summaries and each capture's SHA-256. Portable reruns must select their own artifact root and exact producer paths. Earlier legacy protocols, other dynamic geometry, scaling, riders, native mob interpolation and specialized placement remain unfinished.
