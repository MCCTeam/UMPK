# Cake interaction runtime

The session action now predicts the registered cake's result and swing, then applies native local food, saturation and cake-bite changes before writing the use packet. This implementation covers empty hands and measured default pass-through items across the fifty protocol datasets, using the existing protocol-47 client profile from 1.8.9. Candle insertion, specialized items, custom components and exact native constructor/respawn defaults remain open.

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

## Retained failed attempts and remaining acceptance

The v3 trial completes formatting/test compilation, then fails copying a nested test-platform resource directory before executing tests or compiling Client. The v4 successor fixes that copy, but its protocol-47 health helper uses a wire alias instead of the canonical packet identity: 85 genuine behavior failures, eight setup failures and eighteen passing controls. Neither whole attempt passes. The v5 test changes only that locator to `EntityPackets.Clientbound.SetHealth.Id`; protocols, assertions, expected results and case counts remain unchanged. Earlier receipts remain under their original run directories.

A fresh test-only compile against the final Client, full 35,303-case run and strict Native AOT proof are still required. Native cake-use execution/live acceptance, missing item families, exact default-state parity, the final version matrix and final repository/consumer/package/CI gates remain unfinished. This report closes the bounded session-runtime change only.
