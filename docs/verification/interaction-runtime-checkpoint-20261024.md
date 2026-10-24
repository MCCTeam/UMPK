# Combined interaction runtime development check, 2026-10-24

Exact producer `c977ff3d4dbd6ef7b3dcf318c8d36673d7ebba83` combines the [protocol-47 remote rider](legacy-remote-rider-runtime.md), [campfire/candle ignition](ignition-tool-use.md) and [spectator placement](spectator-placement-runtime.md) fixes. Its complete Client suite passes 33,149 tests with no skips. Native AOT publishes at 25,165,568 bytes, below the unchanged strict limit of 25,165,824, and exactly one smoke test executes and passes. The remaining size margin is 256 bytes.

This development check reuses nine unchanged dependency libraries from the completed `c8492cb` checkpoint. Their complete source/build inputs and binary identities are checked before reuse. Client and the full Client test assembly are compiled from the immutable new archive with the original project/package graph and explicit external references. They are not relabeled as a fresh full solution, consumer or package build.

## Executed checks

| Check | Result |
| --- | --- |
| Immutable producer | `git archive` of full `c977ff3`; 494,632,960 bytes; SHA-256 `206c63f90b093f72e29edc82bdae8d79744b29f0350278d72fbfa5e69f373adc` |
| Actual managed compiler inputs | Nine Client and eleven test references, including Nbt; exact paths, sizes and hashes checked at `ReferencePathWithRefAssemblies` |
| Complete Client run | 33,149 executed passes, zero skips; all 51 rider, 267 ignition and 38 spectator cases included |
| Portable-PDB source identity | All recorded documents match their actual archived source; required changed files are present |
| Native publication | Eleven actual ILC references match the tested Client, nine unchanged dependencies and unchanged Auth; all nine NuGet package/version/SHA-512 rows match |
| Strict executable size | 25,165,568 bytes; SHA-256 `08a99b42f34f718fbc6e7a8029ab9c4d0b4830d43d7c53479ca07b3c016dd18f` |
| Native smoke | Exactly one executed passing result, zero skips; the original frozen smoke harness checks the new native executable |
| Invariance | All 4,278 regular source files and twelve source links, seven fixture lookup links, 155 original prerequisite inputs, 24 frozen copies, 127 smoke-harness files and 117 tested output files remain unchanged |
| Independent root review | Actual XML, archive, source, native file, library inputs and package shape checked; 330 distinct file pins, 4,290 source entries and seven lookup links verify |
| Owned job | 183.23 seconds, exit zero; peak memory 7,899,664,384 bytes under 10 GiB/two CPUs; completed service stopped |

## Failed first attempt and exact retry

The first run builds Client and the full test assembly successfully, then fails because the separate output directory has no repository solution marker or committed data/corpus paths. Its TRX contains 30,592 discovered rows, 30,481 passes and 111 failures, all from unavailable fixture paths or corpus discovery. That reduced count is a failed harness attempt; it does not satisfy the 33,149 baseline. Native publication and smoke do not run in that attempt.

The corrected run copies all 117 compiled output files byte-for-byte into a fresh directory and adds seven task-owned lookup links to the same immutable source tree before testing. It recompiles neither Client nor tests. The new unfiltered run executes and passes all 33,149 rows. Every copied binary, original compile receipt, original failed receipt and lookup target remains protected during native publication and smoke. No assertion, test count, fixture contents, dependency, compiler setting or size budget changes to repair discovery.

Raw evidence is outside Git under `/home/anon/Projects/MinecraftOfficial/runs/interaction-fast-combined-c977ff3-20261024-v1` and `interaction-fast-combined-c977ff3-20261024-v2`. The latter `commands/` directory records exact argv, environment, PID, duration, exit and log hash for each executed command. `job-plan.json` and `HANDOVER.json` bind the immutable archive, frozen references, source and service limits before execution.

| Receipt | Location | SHA-256 |
| --- | --- | --- |
| Original failed completion | First run, `completion.json` | `05ecf72fd34282b9c56db1ea83399623e7ac4b69a017f0c420d9562cc6e8f1cc` |
| Original failed TRX | First run, `results/client-full/client-full.trx` | `0a055f771a16324d5b1a503cd32c1af87b3587aad30508d3801456815ada787e` |
| Corrected complete result | Second run, `completion.json` | `65008e0fb182fae692833400a780fcc5d940d356b93b71dd04b712ecc6b1cc02` |
| Complete Client TRX | Second run, `results/client-full/client-full.trx` | `2f324ab2ea102d8d5e6272454dd141b66e234d20bde47a8912d50212f1c5d7f5` |
| Executed smoke TRX | Second run, `results/native-smoke/native-smoke.trx` | `f96bc32e6affaca7b86e081c1295d6382ddb63f8d7f80c3c083adc9212892554` |
| Root actual-file review | `/tmp/umpk-c977-combined-independent-review-20261024-v1.json` | `05d7191d898e28a8550b3dcc76a40b9f4d0b3fdd3d8bcf276082faafb282c7f0` |

The root review's first attempt incorrectly expected a repeated path field in one manifest value; the actual format uses the absolute path as its map key. That failed review is retained at `/tmp/umpk-c977-root-review-attempt1-20261024-v1.json`. Correcting the reader changes no source, binary, test or result.

## Publication and remaining acceptance

All seven CI jobs pass on the runtime producer `c977ff3` in [run 40047637282](https://github.com/MCCTeam/UMPK/actions/runs/40047637282). Its completed receipt is `/tmp/umpk-c977ff3-completed-ci-20261024.json`, SHA-256 `d8cb6c4f96d5dc4107f148263631cce1ffaddcd06e155be4c1f9322387099c14`. All seven also pass on the later published fixture/documentation head `a932873` in [run 40049225194](https://github.com/MCCTeam/UMPK/actions/runs/40049225194); `/tmp/umpk-a932873-completed-ci-20261024.json` has SHA-256 `a986f6e733ce8f43f5a3e98b2017b317e1a4d75e80adc5e9549bc05b8eac6714`. These results retain their exact heads; later production changes require fresh CI.

Fresh final full-solution/25-core-gate, DMCBK/MCC, package/isolated-consumer, complete native/live/version-matrix and actual final-head CI acceptance remain required. The last full repository/consumer/package certificate remains exact `c8492cb`. The [completion ledger](interaction-completion.md) retains the unfinished behavior families and explicit unknowns. PR #10 stays open and draft.
