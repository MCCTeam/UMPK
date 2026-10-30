# Trapdoor and gate compatibility checkpoint

Immutable producer `2925b5180ce25b3fd50666ce569f4720be9934be` includes the [trapdoor/gate runtime](trapdoor-gate-activation.md) from `efad22a` and the existing connected interaction work. All repository, consumer, package and Native AOT checks pass for this exact producer. These compatibility results do not close native interaction acceptance or the wider [completion ledger](interaction-completion.md).

## Executed results

| Check | Observed result |
| --- | --- |
| Repository | All 25 retained gates pass, including Release restore/build, full tests, format, counts, dataset/language, reconstruction, conformance and existing allocation/performance limits. |
| Full .NET suite | 43,590 actual passing tests; exactly fifteen named normal opt-in skips; all seventeen totals match. The individual XML outcomes establish the skips; the TRX summary's zero `notExecuted` counter is not used for that purpose. |
| Python | 356 extraction and 26 official-tool cases pass, with no skips. |
| Generation and source | All 61 fresh generated files match; all 4,258 archived entries match the commit's Git blob IDs and modes; 53 retained native/fixture inputs remain unchanged. |
| DMCBK and MCC | 1,999 and 918 actual passing tests, zero skips; all 33 dependency DLL comparisons match this producer. Sibling HEADs, Git status and tracked/untracked source hashes remain unchanged. |
| Frozen MCC | All 576 output files have a complete fresh manifest. |
| Native AOT | MinimalBot is 25,132,640 bytes, strictly below 25,165,824; 33,184 bytes remain. Exactly one smoke test executes and passes, with zero skips. |
| Packages | Fourteen primary and thirteen symbol packages pass; all thirteen library DLLs, thirteen PDBs and thirteen isolated-consumer libraries match the tested producer. All 27 package commit/version bindings match this commit and unchanged `0.9.0-beta.7`. |
| Producer invariance | All 22 locked DLL/PDB files remain unchanged after consumer builds, publishing and package checks. |
| Published CI | All seven jobs pass on actual head `2925b51`, first attempt, run `39872164846`. PR #10 remains open and draft. Later heads require their own result. |

The original external DMCBK fourteen-case pose-fixture substitution remains explicit: the unchanged assertions receive a real full chunk through an external build override. No sibling source, expected count, dependency, package version or DI lifetime changes. Its reused receipt is verified again against this producer; it is not a silent fixture repair.

## Reproducibility and evidence

All paths in this section are under `/home/anon/Projects/MinecraftOfficial`. Raw evidence stays outside Git. The input request is `runs/trapdoor-gate-checkpoint-inputs-20261023-v1/request.json`, SHA-256 `8e386340ae8d28c56ffde336b6beafaed45466c167530bd024444587462a74f4`; its immutable archive has SHA-256 `791209f94cd0777b1c8a02d44bb46f627c8814833d7cbec00c3c41dfd9e48dae`. The complete input manifest has SHA-256 `f90c3827478be2732a63d11f937dc460771a6f9ea7da67c083652baaf6416502`.

The reviewed driver directory is `runs/trapdoor-gate-checkpoint-preparation-20261023-v1`, whose 26-file manifest has SHA-256 `152da40411764b6a839777765cb9e838e4d2004f860e4839078b99220b5de621`. Twenty-five files match the completed prior driver; the sole code difference identifies this checkpoint in the certificate scope. Prerequisite pins, skip accounting, assertions, counts and budgets are unchanged. The existing six skip-accounting rejection controls are reused with matching hashes.

The executed commands are `/usr/bin/python3 -u <driver-directory>/<stage>` in this order: `validate.py`, `build-lock.py`, `consumers.py`, `aot.py`, `packages.py`, `certificate.py`. The environment binds `UMPK_CHECKPOINT_SPEC` to the exact request above, `UMPK_CHECKPOINT_SPEC_SHA256` to its hash, `UMPK_VALIDATION_COMMIT` to the full producer commit and `PYTHONDONTWRITEBYTECODE=1`. `runs/trapdoor-gate-checkpoint-inputs-20261023-v1/stage-commands.json` records full working directories and arguments; the two actual `stage-results.json` and `post-repository-stage-results.json` files in `runs/trapdoor-gate-checkpoint-root-start-20261023-v1` record exits and log hashes. The outputs are the corresponding `runs/trapdoor-gate-checkpoint-{source,validation,consumers,aot,packages}-20261023-v1` directories. Their command receipts retain the exact pinned .NET SDK 10.0.401 invocations. The completed stages must not be rerun merely to resume work.

| Receipt | SHA-256 |
| --- | --- |
| `validation-20261023-v1/cross-certificate.json` | `fd4dac331511ec0de58a32480e4a7b9130d8dc351382dcd9910122f8ca0a4af6` |
| Tested producer lock | `6e4dd769541dc4b939d2144c9612d66e9dc9c9f8baca1bbe37977eec872b7697` |
| Consumer completion | `e988f3418d1ec10a3fb800ecac8f1677b522583c13c71c1d21f62de48339a13d` |
| External DMCBK fixture reuse | `9159c512b9e66293328c41bf19a07db30eae8cbc264a7bc117a41fe6903d820d` |
| Frozen MCC manifest | `8f5223627c5cc91214356ce4df9a979516827306c58e61782102cdc962becc74` |
| Actual MinimalBot binary | `e375ca97b798881e7231d51a6bae26291e1e7bf32b1b9fb42a005b2b27d65986` |
| AOT completion | `8a7684eb21ef7b389705b3b02d68a8021e3eddfb5fa8942af81daa28d8cde2ea` |
| Executed AOT smoke guard | `3a56ec286fe0813981f3d2fc3bd3dd2e9bcbd6dab0cebdab90a899580b2b1cbe` |
| Package proof | `a9865d858d95277433075715d6f6afe2eb166e9f4c71c075e7af7bb7a85494b5` |
| `trapdoor-gate-publication-20261023-v1/completed-ci.json` | `2b79d983b206ab0bbac78a764726c7409270742b9972859f5b8eb70c620bf025` |

The previous canonical full-test log was backed up before `/tmp/umpk-test.log` received this producer's complete unfiltered Release output. Its retained backup matches the previous `651ddcb` run. Consumer builds started only after the earlier native run had stopped normally; current sibling output DLLs deliberately belong to `2925b51`. Frozen `651ddcb` libraries, native captures and their original receipts remain unchanged.

An independent read-only audit verifies actual XML outcomes, all nine cross-certificate pins, source/archive modes, binaries, package contents and the exact completed CI API receipt. Its directory is `runs/trapdoor-gate-checkpoint-independent-review-20261023-v1`: `review.json` has SHA-256 `9b613ce88a2d3c99e7be1304b85591a21eb78d365aacc21ea3d676a5b591fdef`; `report.md` has SHA-256 `ed24647bef94235f9ce478bdd09d7b58a7bd780a84ba20063611a0869f9e0694`; its ten-file manifest has SHA-256 `14a74bb4ab5186f7e2d331733c51b18289d9b7a8ee1299f3cf7d717de8b2221a`. Root checked every manifest entry again without rerunning a gate. Remote CI raw logs/artifacts were not downloaded by that audit.

## Remaining acceptance

The fifteen normal skips are not native/live passes. Trapdoor/gate native execution remains unrun. [Crystal 769/777 acceptance](end-crystal-picking.md#native-263-interaction-acceptance) and [door 47 acceptance and the 315 failure](door-activation-runtime.md#native-189-door-acceptance) retain producer `651ddcb`; they are not relabeled as `2925b51` results. The 1.11 missing-swing defect, broader participating geometry/motion, riders/pushes, specialized block/item families and final fifty-protocol/seventy-five-release acceptance remain open. No required unknown is changed to a proven success or clear space by this checkpoint.
