# Native entity placement from 1.9 through 1.10.1

Commit `995b11b754f66b26b02b28a38a6b1017b61e1c0f` adds measured villager geometry and item obstruction rules for protocols 107, 108, 109 and 110. The original 1.9, 1.9.1, 1.9.2, 1.9.3, 1.9.4, 1.10 and 1.10.1 clients each produce twelve age/box transition witnesses and two item-constructor witnesses. Both protocol-110 aliases and all three protocol-210 releases now have independent evidence, including the preceding 1.10.2 measurements.

The baby selector is Boolean metadata index 11 through 1.9.4 and index 12 from 1.10. Its native default is false. Adult dimensions are 0.6 × 1.95; baby dimensions are 0.3 × 0.975. These clients retain the box's minimum corner when shrinking or growing; explicit position reapplication recenters it. The maintained replay fixture retains artifact and measurement hashes for seventeen releases, with 260 transitions across four deduplicated cases.

Both original item constructors leave the building flag false. Each whole-client audit finds eight writes, all affecting `this` in unrelated entity classes. The probes preserve 2,905 original class files per 1.9-era release and 2,946 per 1.10-era release. Original spawn handlers establish the item class for object type 2. Source audits and class-origin checks accompany the native measurements. The probes use controlled world inputs and invoke original constructors, metadata callbacks and position methods; they do not execute native entity/world ticks.

Generated review confirms that only the four new protocols' entity-rule regions change; prior block, item, fixed-box and replacement bytes remain identical. Their geometry differs from the measured protocol-210 geometry only at the baby metadata index. All 46 other protocol descriptors' interaction and geometry bytes, and all 336 existing non-interaction shared-table members, remain identical. There are now 45 villager geometry datasets. Other unmeasured entity types remain unknown.

## Verification

The clean source snapshot matches all 3,944 files in the exact commit. Commands use .NET SDK 10.0.401. Deliberate additions are 116 client tests, eight Java data tests and one Python test. No wire codecs, conformance pins or allocation limits change.

| Check | Observed result |
|---|---|
| Release build | Passed, zero warnings |
| Full UMPK tests | 19,599 passed, one failed, seven skipped; 19,607 total |
| Test counts | All 17 suite totals match; the gate fails because one test fails |
| Existing failure | `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees`, line 138: the file-citation count guard requires more than 400 entries; the preceding resolution assertion passes |
| Focused client regressions | 939 passed; 80 failed before the new data |
| Focused Java data regressions | 96 passed; eight failed before the new data |
| Python extraction tests | 147 passed |
| Native extraction checks | All fourteen commands pass in check mode |
| Format, dataset and language | Passed |
| Generated outputs | All 61 files match |
| Native AOT | 24,481,040 bytes against the unchanged 25,165,824-byte limit; smoke test passed |
| DMCBK | 1,999 passed |
| MCC | 918 passed |

Native publishing preserves every tested producer hash. All 33 consumer DLL comparisons match. DMCBK remains at `af22688e783fb2876ad55f06e0654a7a4c6c5641`; MCC remains at `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

## Live acceptance

Each release passes five fresh survival actions through actual MCC commands: placement into water, lava and plants; direct plant replacement through `/useitem`; and attempted placement into an occupied bottom slab. Every positive action sends use → swing, changes the server block to stone and reduces the selected stack from 32 to 31. The negative control sends one use without a swing and preserves both slab and stack. Containing walls prevent fluid escape; independent server assertions verify the destination material immediately before each action.

| Release | Protocol | Passing run | Byte-roundtrip frames | Villagers before every use | Persistent item control |
|---|---|---|---:|---:|---:|
| 1.9 | 107 | Corrected matrix | 1,209 | 23 | 54 |
| 1.9.1 | 108 | Corrected matrix | 1,151 | 23 | 50 |
| 1.9.2 | 109 | Corrected matrix | 1,284 | 23 | 52 |
| 1.9.3 | 110 | RCON rerun | 1,224 | 23 | 54 |
| 1.9.4 | 110 | RCON rerun | 1,265 | 23 | 54 |
| 1.10 | 210 | Corrected matrix | 571 | 19 | 46 |
| 1.10.1 | 210 | RCON rerun | 604 | 19 | 46 |

Across the seven passing legs, 7,308 frames decode and re-encode byte-identically from 17,517 play frames. The decoder's thirteen UMPK DLLs match the tested producers. Frame-order reconstruction handles entity removal and item pickup, verifies controlled adult/baby villagers and their native selector indices, and identifies the persistent item from its original spawn packet before checking its presence at every use. This establishes packet target and metadata state, not native mob tick interpolation or placement against nearby changing boxes.

All passing legs use Java 11, a 1 GiB server heap and seed `4603516203263995078`, with private ports and fresh directories. The actual loaded-library maps match the tested producers. Clients and launchers exit with code zero, the servers observe disconnect, all owned PIDs disappear and both ports close. No movement warning occurs.

## Reproduction records

Evidence stays outside Git under `/home/anon/Projects/MinecraftOfficial/runs/`:

- `early-entity-geometry-batch-20261017-v3`: completed 1.9, 1.9.1 and 1.9.2 probes, plus the failed 1.9.3 resolver attempt.
- `early-entity-geometry-batch-20261017-v4`: completed 1.9.3, 1.9.4, 1.10 and 1.10.1 probes with source-resolved original world observer registration.
- `early-entity-rules-proof-20261017-v1`: exact source, commands, native source audit, extraction checks, gates, tested producers and trace tools.
- `early-entity-rules-consumers-20261017-v1`: consumer commands, results and all 33 DLL comparisons.
- `early-entity-matrix-20261017-v3`: corrected matrix with four passes and three RCON authentication failures, preserving all commands, captures and cleanup results.
- `early-entity-matrix-20261017-v4`: fresh passing 1.9.3, 1.9.4 and 1.10.1 reruns with the RCON authentication fix.

Earlier probe attempts remain recorded as harness failures: an uninitialized native navigation listener, an ambiguous listener resolver and the 1.9.3 observer implementation change. The successful probe initializes the original listener identified by the world's own observer registration and preserves original entity methods. The first live attempt stops before starting a server because it expects a lowercase language filename; the second fails before feature actions because these releases format time with `%d`. The corrected runner handles both `en_US.lang` and integer time responses. Three subsequent legs stop on RCON resets during authentication, before the command is sent; their cleanup checks pass. Commit `c5feac340b14bd4543686e488295e7a8d9bed8f1` adds three bounded authentication attempts while preserving single-shot command execution. Its six real-socket regressions cover recovery, exhaustion, lost command responses, rejected credentials and malformed/truncated frames; all fourteen artifact-tool tests pass in each of UMPK, DMCBK and MCC. Fresh reruns retain the same product DLLs and record the updated helper hash. Failed legs remain excluded from acceptance counts. A sandboxed focused-test attempt also fails at MSBuild IPC setup before tests run; the recorded red and green runs execute with local IPC available.

The proof directory retains each capture's SHA-256, decoder commands, source overrides and the actual loaded-library comparisons. Portable reruns must select their own artifact root and exact producer paths. Protocol 47 and protocols 477–490 still lack villager geometry datasets. Other dynamic geometry, scaling, riders, native mob interpolation and specialized placement remain unfinished.
