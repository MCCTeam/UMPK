# Combined interaction checkpoint, 2026-10-23

This checkpoint freezes `c8492cb5c38b301b1095b0bf6ddb93adaab8753f`: modern slab placement/merging on 393–777, bounded 1.11 bat/squid placement bounds and membership, and canonical exact Adventure `can_break` properties on 766–769. It includes the earlier legacy pre-send and protocol-777 slab collision corrections. It does not close the remaining interaction inventory or native/version acceptance.

The exact producer archive is `/home/anon/Projects/MinecraftOfficial/runs/interaction-combined-checkpoint-inputs-20261023-v1/producer.tar`, 494,520,320 bytes, SHA-256 `c4f97e30cc46267b0fec0a1c0e74f359a8ec6b5cd582e9a0595487b18b63f492`. Git/archive binding identifies 4,282 entries: 4,270 regular files and twelve links. The source-tree hash is `df50b020faa884a3df7dbfb4fdeb59fcd15e85a2e336bbc0cb5cc2e717bf1498`; the baseline hash is `4b78ca7fceb36bc86acfb506cd372216502823da95a81ca9c9df26e1eaf6749d`. The original ten local report insertions and untracked `logs/` stay preserved outside the committed archive.

## Execution and results

The reviewed runner is `/tmp/combined-checkpoint-preparation-20261023-v2`, manifest `70181ec80598e1897384c38ff5c730f9762c4d6e607de76189ddabd7e05558fe`. Its six inherited drivers retain the 25 core gates, fifteen reconstruction commands, counts, pins and budgets. Preparation passes 38 offline tests, including twenty structural corruption controls; those controls are separate from the executed producer gates. Frozen v1 remains unchanged. Independent preparation approval is `7486c1116ab820211d3cf055813530ad8d0cfa3a37447dfa75177dcb56fe5f4b` at `/tmp/combined-checkpoint-v2-independent-review-20261023-nw782ewy/review.json`.

The actual request is `runs/interaction-combined-checkpoint-inputs-20261023-v1/request.json`, SHA-256 `69eb8f586f37fa7fe4d2caf61f90b60367c4a81b42f764bf089e47bc828cdf36`, under the shared oracle. `launch_pipeline.py` records the actual argv, explicit environment, output log hash and exit-zero result in that same directory. It uses a run-owned external `TMPDIR`; the unfiltered full test log still goes to `/tmp/umpk-test.log`. The previous canonical log, SHA-256 `caeda8e1f04b00ef67e85794cc2ba4eedf5aa4129a01280062ebb57294cbbd8b`, is backed up with its own receipt. The SDK is the retained .NET 10.0.401 toolchain.

```bash
python3 /home/anon/Projects/MinecraftOfficial/runs/interaction-combined-checkpoint-inputs-20261023-v1/launch_pipeline.py /tmp/combined-checkpoint-preparation-20261023-v2 69eb8f586f37fa7fe4d2caf61f90b60367c4a81b42f764bf089e47bc828cdf36
```

The exact restore/build/test/format/data/extraction/generation commands and individual log hashes are in `runs/interaction-combined-checkpoint-20261023-v1-validation/results.json`. Build uses `-m:2`; the full solution test has no explicit project-worker limit and no filter. The completed local test is not evidence for serialized scheduling. All subsequent stage commands and logs remain under the separately named `-consumers`, `-aot` and `-packages` output roots.

| Check | Executed result |
| --- | --- |
| Core repository gates | All 25 passed |
| Unfiltered .NET suite | 46,536 passed; seventeen named normal opt-in skips; 46,553 total |
| Baselines | All seventeen assembly totals match; Client 32,793; Integration 41 passed/seventeen named skips/58 total |
| Python | 368 extraction and 26 official-tool cases passed |
| Format, datasets and language | Passed |
| Fresh generation | All 61 generated files match |
| Archived source | All 4,282 entries remain unchanged after the complete pipeline |
| Producer lock | All 22 locked DLL/PDB files remain unchanged through consumers, publish/smoke and package checks |
| DMCBK / MCC | 1,999 / 918 passed; all 33 dependency DLL comparisons match |
| Sibling state | Before/after source and Git snapshots match |
| Frozen MCC output | 576 files, manifest `a14b0b018ba6677719799317db4767444ee5e36779befc9dc1e4a855db0005e0` |
| Native AOT | 25,157,328 bytes, strictly below 25,165,824; exactly one executed passing smoke |
| Packages | Fourteen primary/thirteen symbol packages verify; all thirteen producer, packaged and isolated-consumer libraries match |

Normal opt-in skips do not prove live or AOT execution. The separate AOT smoke executes and passes; its executable SHA-256 is `375d46134489ae5d5e4ca9b8be8b626f19a2364a0755a4f74bd92f42814db107`. Consumer compilation reuses the declared external real-chunk pose-fixture substitution with unchanged original assertions/counts; `fixture-reuse-current-producer.json` binds it to this producer. DMCBK and MCC source/Git remain unchanged. Their original historical failures retain their original receipts.

## Receipts and retained source evidence

The final cross-certificate is `runs/interaction-combined-checkpoint-20261023-v1-validation/cross-certificate.json`, SHA-256 `1a92bcd1f10c1fda6167fbebca2021f476e59e5624d3edc8f8299898e401eded`, under `/home/anon/Projects/MinecraftOfficial`. It checks current request/archive/source identity, actual XML, skip names, producer hashes, consumer equality, packages and the executed smoke. The completed pipeline exits zero. No Java compiler, JVM or native server starts in this pipeline.

| Receipt | SHA-256 |
| --- | --- |
| Repository finished | `d781ed7380115b6424fbd0626763c0b869be618394b9625d37ef507861c64169` |
| Producer DLL/PDB lock | `f27f883ae156478e221c5d8e7ec073f3365437824ac4dc8d694c6ec0713089d1` |
| Consumer finished | `56be41ca45f8ac2fd92e1ce4d1ea3bab05d5cf847c371e0a502de23bfbe0edac` |
| Reused consumer fixture bound to producer | `902c25097f4209d374bd648004ae2dc3956a16404c29fa27b6a2396f2b2e0ccb` |
| AOT finished | `39bd4fb146c80ca3ac626f0598dd330a5787502be0189c324ee25ad160fa9a63` |
| Executed AOT smoke | `b40e76a9123f95132dda4d3a54bce729904bf656c9e981fbb4d64b619c88544b` |
| Packages finished | `4bbbbdee361b80db58f159cd182e9bc2f02bceec69e942f3459b458906a00b98` |
| Package/isolated-consumer proof | `3a0bb6d654d17ecce9533a254332a82d0f32dcb6141f3ba9a2a5d3c6cc2cf8b6` |

The separate slab reconstruction receipt is `02494ee13518e2bc6254bc8e3f058ea7d123ea56d1a467ae9b79caab10627e6a`: twelve current source inputs and eleven retained native inputs match. The separate 315 fixture-integrity receipt is `8191eeb940396846f0ff2ba89702e3c4f78eb370e815e11bc9fe9bd5b1df0554`: the current committed GZip reproduces all 31 ordered native rows and five exact captured-frame projections. That integrity check preserves the original failed native-validator status. It does not rerun native semantics, reconstruct the whole failed live tick sequence or prove rotations/AI. Neither extra check replaces a core gate or a required native leg.

Read-only independent approval is `/tmp/combined-checkpoint-actual-independent-review-20261023-7bb1g0bk/review.json`, SHA-256 `474592f9221071cdfa5f6d0cfae7219b02c2c4fbdff30c3fa7cc9a694457b2a4`, with manifest `deaa91982c71abcbef95916513924882a1c035b0f16fb257551b0bd52db21536`. It verifies actual source modes, archive, all 25 command/log pairs, XML, current binaries, consumer snapshots, finite reconstructions and package contents without rerunning gates. Raw logs, binaries, source inventories and artifacts stay outside Git.

## CI and unexecuted acceptance

Published `c8492cb` runs all seven jobs in [39956127323](https://github.com/MCCTeam/UMPK/actions/runs/39956127323): six pass, while Windows fails `ConcurrentWholeSection_ReadsStayConsistentPerCell` because it completes two of three required writer sweeps. The log establishes overlapping test-project lifecycles; it does not establish the cause of the failure. The test retains its two dedicated threads, start barrier, two-second stress interval, thirty-second waits, 50,000-read floor and three-sweep floor.

Commit `8b86c3c` changes only PR test-project scheduling to `-m:1` and passes all seven jobs on its first attempt in [39959850911](https://github.com/MCCTeam/UMPK/actions/runs/39959850911). The completed receipt is `661cee902c75e12825bc418f01f697fafe9ee8285c144f51d63610e22fea1f0b`, under `runs/pr-test-project-scheduling-publication-20261023-v1`. [The exact SDK source](https://raw.githubusercontent.com/dotnet/sdk/v10.0.401/src/Cli/Microsoft.DotNet.Cli.Utils/MSBuildForwardingAppWithoutLogging.cs) injects parallel project scheduling by default, and [MSBuild documents the worker-node limit](https://learn.microsoft.com/en-us/visualstudio/msbuild/building-multiple-projects-in-parallel-with-msbuild). The scheduling correction removes known competing project work while keeping test assertions, counts, fixtures and time/size budgets unchanged. The bounded review is `/tmp/umpk-windows-mutation-scheduling-review-20261023-v2-f4tmufht/review.json`, SHA-256 `72c205119b53cb9dad42e6f0159baba25543ef9c96cc4014144dac104bced9be`. The fresh pass validates this invocation without proving universal elimination or relabeling the older failure. The Windows log has all seventeen matching assembly totals, 46,533 passes and twenty named skips: the seventeen normal Integration opt-ins, two existing Linux-only symlink cases and one existing oracle-availability case. All 1,032 Game tests pass without skips. The observed test-project lifecycles now have a maximum overlap of one. `windows-scheduling-verification.json` has SHA-256 `dcfa624d13d69b11202599aed8f8f6d48990c6f4bba8666133f8cbf32862d9fe`. The first audit wrongly assumed Linux skip totals for Windows; its failed assertion is preserved separately, and no test result changed. The earlier `e6f5f9d` mining-cancellation timeout remains unexplained and separately retained.

The documentation head `43ae3f9` also passes all seven jobs on its first attempt in [39963892130](https://github.com/MCCTeam/UMPK/actions/runs/39963892130). Its completed receipt is `runs/combined-checkpoint-publication-20261023-v1/completed-ci.json`, SHA-256 `a194eeb7ad755ad7ac493e35986fb23e73f3145ace93aa7f740b59ae437fb869`. This is CI for the actual published head; the immutable local compatibility receipts above still identify the production commit `c8492cb`.

The five-version slab campaign, the original Door315 retry, new exact-property native acceptance, the remaining geometry/rider/interaction inventory and final 50-protocol/75-release acceptance remain unfinished. Current slab attempts are indexed in the [slab report](modern-slab-placement.md). Older native passes keep their original producers and scope. PR #10 remains open and draft; no merge, release or package publication is included.
