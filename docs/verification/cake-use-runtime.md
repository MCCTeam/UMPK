# Cake interaction runtime

The session action now predicts the registered cake's result and swing, then applies native local food, saturation and cake-bite changes before writing the use packet. This implementation covers empty hands and measured default pass-through items across the fifty protocol datasets, using the existing protocol-47 client profile from 1.8.9. [Candle insertion](candle-cake-attachment-runtime.md) now has a separately accepted bounded runtime slice; specialized items, custom components and exact native constructor/respawn defaults remain open.

## Ownership and native boundaries

`InteractionActions.SendBlockUseOnLoopAsync` owns the committed action. `VanillaCakeUse` is a pure internal profile; previewing an action changes no state. The existing `SelfState` and block-prediction shadow receive the local mutation once, after cancellation checks and sequence capture. Server health packets and block updates/acknowledgments retain their separate correction paths. A committed use/swing group finishes within its captured session lifetime.

| Protocol band | Registered native behavior |
|---|---|
| 47–210 | Cake always reports handled; eating requires hunger and a player without invulnerability. |
| 315–498 | The Boolean result is eating success or an empty clicked stack. |
| 573–765 | Eating returns success; a full player with an empty hand gets consume without swing; a measured default nonempty item passes. Invulnerability permits eating. |
| 766–777 | Default cake interaction runs through the main-hand fallback. |
| 767–777 | Food and saturation also clamp at zero. |
| 777 | The existing placement policy uses a local swing with no swing packet. |

Food increases by two; saturation uses the original single-precision addition and clamping. Bites advance through six, then the cake becomes native air. Two internal observation markers prevent health/abilities retained from an earlier player lifetime from establishing current context. Missing observations retain unknown prediction and packet-only behavior. This conservative boundary does not establish exact native constructor-default parity.

The retained original-client audit binds seventy-five named releases, fifty datasets and 592 method bodies. Source evidence is `/home/anon/Projects/UMPK/.scratch/cake-use-source-preparation-20261026-v1/source-audit.json` (`2588a91228d8cbd4642dc9a789b4a88ae78dfdd444fddbc3c847a24020aa6cee`), its semantic matrix (`7e8ceb7d10f9e1cde6f6e358a734a86a08308cdf50757e2f86d2523a1785b199`), and the caller-order supplement `/home/anon/Projects/UMPK/.scratch/cake-runtime-preparation-20261028-v2/caller-order75.json` (`9bbc5fa1794e90144f365c4cdd90c2dddd8b1be0e426acd6544d4fc0812f29dc`). The independent source review is `/home/anon/Projects/UMPK/.scratch/cake-independent-review-20261028-v1.json` (`dd618d3d980381ff4d15631d57f5b67c1feafbff436ca7bcbebf3202a9266450`). No dataset, dependency, package version, DI lifetime or public API changes are involved.

## Executed staged runtime, 2026-10-28

The trial is `/home/anon/Projects/MinecraftOfficial/runs/cake111-staged-runtime-20261028-v5`, actual invocation `847c4f74a14243b4880f23277c9efe24`. Its exact runner command and all compiler/test commands are retained in the sealed configuration and job receipts. The root grant is `918817c9634277ad98d964d518d3ded631877dfa38b17c5ca0686c85e5386cb8`; the 12,805-input manifest is `d3ba9552016cabf94a0e0316b15bf0dd921fcbd5cfe4ffc63ecff81942642e42`. The job ran with a 4 GiB memory limit, 200% CPU limit, no swap/core dumps and a single-CPU compiler.

| Actual production stage | Executed | Passed | Expected behavior failures |
|---|---:|---:|---:|
| Unchanged Client, new tests only | 111 | 21 | 90 |
| Pure profile and internal state declarations | 111 | 21 | 90 |
| Packet-applier observation markers | 111 | 21 | 90 |
| Session placement caller connected | 111 | 111 | 0 |
| Adjacent interaction, nutrition and lifecycle regressions | 1,880 | 1,880 | 0 |

The same test DLL and exact 111 display-name identities run through all four stages. Each red stage fails at 73 packet-count, fifteen result and two write-fault nutrition assertions; all twenty-one controls pass. Final cases cover all fifty dataset profiles, native result boundaries, hand/mode/cooldown precedence, unknown context, float bits, repeated bites/removal, independent corrections, respawn/reconnect, cancellation and a real owned pipe-write fault. Packet-driven checks use bound codecs, asserted decoded values and byte-identical round trips. The deliberate Client baseline addition is 111, from 35,192 to 35,303; the other sixteen suite totals are unchanged.

The test compile has 334 PDB documents, 198 verified references and 54 byte-matched embedded resources. Each Client compile has 221 PDB documents and 182 verified references. The test compiled against baseline Client `6ea3ddb8ae4f605f6aa1fd6be8a80dd7b823e69c452dbbaf0de25432559b8243`; the final green runtime uses Client `ca4ae7b9f1fb3c5447a22edb44abddcaa16b6ae27cd3d75e96053a7724669758` / PDB `76a32c15fa0c4795e1e2979e058b6b1fe937b6d5d1570616b3cb2d08bfcbe633`. This intentional temporal red/green reuse is not a producer-matched final test compile. The test DLL is `d1505564d85714024b1201973fce28c5c46cc637ad8d2a96baf22aac1c777f34` / PDB `c91743332f08095ab075a7b37fe31208849a09f0b98c27562413f85f9c62735b`.

All eleven other producer pairs and the original source/build tree remain unchanged. Four complete runtime closures are checked. Full formatting of the four production files and the corrected test produces zero changes. All 47 observed owned processes are independently absent after cleanup; this trial starts no native server. Root audit `/home/anon/Projects/UMPK/.scratch/cake111-actual-runtime-root-audit-20261028-v5.json` (`350c948c8e6cb2e0904c58562b8938abbdb5dffca6100be19a61c4af7f1461f1`) physically verifies 13,344 unique files, exact test counters, causal assertion locations, compiler/reference/PDB evidence and runtime pairs. Completion is `48b06fc33863ffffd5ba7c5ef14daa46a072f1bfb182041a87e0d0ce978c5874`; cleanup is `3e2ade3bdf6323be3eb3d41ee3d50a52cc1b716b1467ef628ad145d4f64406f0`.

## Matched full Client acceptance, 2026-10-28

A fresh test-only compile captures the actual final Client `ca4ae7b9` in all 198 compiler references, preserving 334 PDB documents and 54 identical Csc/PE resources. Its test DLL is `989adc425352eae1f742061b39d427bfe0573a663030f9846a4a2ec185e20aa3` / PDB `94bc4a1c30a31218c820f2b6292c99814e350f26c7b68edccb45f1bf5bb6de4d`. Root prefix audit `.scratch/cake-matched-prefix-root-actual-audit-20261028-v1.json` (`d5a078540b4b3a30a71f9bc4d8d8713aed645f2323e461cd112f1795a9ebf978`) verifies the completed build and nine absent processes. Its enclosing v1 attempt remains failed: two receipt fields passed strings to a helper requiring `Path`, after successful compilation/PDB/resource checks and before full tests or AOT. The completed build is reused.

The no-recompile v2 continuation executes all 35,303 Client cases with zero failures/skips in 299.11 seconds. All 35,179 unique display-name keys and multiplicities match the source-derived current-directory inventory, including exact 6,593/1,880/111-case subsets. The enclosing job stops before AOT at a stale literal display-name guard: three corpus theory names contain the old run-directory prefix. The original expected/current Counters and failed receipt remain preserved.

The correction derives exactly three literal keys, with multiplicities 121/3/3, from the unchanged theory/discovery sources, complete capture-input paths, old/current owned solution roots and the same pinned xUnit formatter. It verifies all 121 capture/sidecar identities, three command captures and six ancestor links. The 127 complete input roles resolve to identical files. Actual names, assertions, fixtures and counts are unchanged; no broad normalization is used. All other 35,176 unique keys are identical. Root audit `.scratch/cake-full35303-root-actual-audit-20261028-v2.json` (`c3b2403fd9eb1b5868af78ac12550905ce66d8244dfb39ea0238f1f27a3de9df`) independently streams the raw TRX, derives the literal role bindings and verifies four absent owned PIDs. The 548 authored Client/test inputs match the shared implementation; the retained `8efa26f` source-revision metadata is not a final Git/archive certificate.

Published `1ce8c35` separately passes all seven [CI jobs](interaction-ci-fixtures-20261025.md#published-cake-and-acceptance-fixture-checkpoint-2026-10-28), including strict 24,688,080-byte Native AOT/exactly one smoke. Local AOT against the same matched producer now also passes in `/home/anon/Projects/MinecraftOfficial/runs/cake-matched-aot-only-runtime-20261028-v3`, actual invocation `370dda8cdae94ced9572ede753dacf8f`. The ELF is 24,688,128 bytes, SHA-256 `25b8981d9bf1d8a01e01af5afcb5af094b6d7a2e2bb693217dd5991dbcc0333c`, leaving 477,696 bytes under the strict limit. Exactly one smoke test executes and passes, followed by the actual CI XML guard. All eleven ILC producer references match; twelve producer DLL/PDB pairs and the matched test pair remain unchanged. All four commands exit zero and are reaped; all fourteen observed processes are absent after cleanup. Root audit `.scratch/cake-aot-only-root-actual-audit-20261028-v3.json` (`77d45e5944028c9ce61b861342d83723c6523daa495a16c1fa8990c6c24729ca`) verifies the actual ELF, single size switch, raw one-test TRX, producer hashes and cleanup. The 25.53-second continuation reuses the accepted full result and compiles only the sample, with no repeated full suite, Test/Client/dependency build or formatter.

## Retained failed attempts and remaining acceptance

The v3 trial completes formatting/test compilation, then fails copying a nested test-platform resource directory before executing tests or compiling Client. The v4 successor fixes that copy, but its protocol-47 health helper uses a wire alias instead of the canonical packet identity: 85 genuine behavior failures, eight setup failures and eighteen passing controls. Neither whole attempt passes. The v5 test changes only that locator to `EntityPackets.Clientbound.SetHealth.Id`; protocols, assertions, expected results and case counts remain unchanged. Earlier receipts remain under their original run directories.

The matched test compile, full 35,303-case run and strict local Native AOT are accepted above. Native cake-use execution/live acceptance, missing item families, exact default-state parity, the final version matrix and final repository/consumer/package/CI gates remain unfinished. This report closes the bounded session-runtime change only.
