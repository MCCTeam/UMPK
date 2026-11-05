# Legacy lever and button activation

## Runtime change and native scope

The existing public block-use caller now predicts native lever/button success on all eighteen datasets 47–490. The native source inventory binds 31 named releases and 128 registrations, with 282 pinned inputs. Protocol 47 keeps the 1.8.9 client profile and rejects offhand before reading a main-hand fallback. Older button metadata has no flattened `powered` property map, so these clients use the original unconditional successful activation result. Powered buttons retain the native consume/no-swing distinction from 1.15/573; main-hand-only fallback begins at 1.20.5/766, and 26.3/777 retains local-only placement swings.

The change extends the existing `BlockUseActivationProfile`, recognizes the legacy `wooden_button` key and uses the existing session-owned typed result/send/swing pipeline. It also applies the existing packet-established-terrain guard to levers/buttons. A block update that synthesizes a storage column before chunk arrival or after unload cannot prove native activation. That context remains Unknown and sends only the existing use packet. Native activation still precedes item cooldown and Adventure item continuation; sneaking with a nonempty supported hand suppresses it.

No public signature, dataset, generated file, pin, dependency, package version, DI lifetime or clock changes. Native redstone, projectile triggering and server-owned effects are outside this activation-result slice.

## Evidence and producer

Evidence paths are relative to `/home/anon/Projects/MinecraftOfficial/runs/`; raw artifacts remain outside Git.

| Evidence | Location | SHA-256 |
| --- | --- | --- |
| Rebound native source audit, 31 names/18 protocols/282 inputs | `legacy-switch-runtime-20261024-v3/native-source-bindings.json` | `b062c65a532feef72f39bcb44bdacb3d1e44f51f2a49fce3d83000befaa9c1fb` |
| Published producer/source binding | `legacy-switch-runtime-20261024-v3/published-producer-binding.json` | `7c5c9142e009bdc7c6273e5b42d44db8263517d58f5d497896d363a48d94a0b9` |
| Independent original red review | `legacy-switch-runtime-20261024-v3/root-valid-red-review.json` | `91c33e4476832de33ae279d37a54123a0de03209081d2c41e98a4c7c52a06d58` |
| Final runtime/native completion | `legacy-switch-runtime-20261024-v5/completion.json` | `3250f726d42240569150baff81f15462941c4beb85366ad4c291ec41eb326796` |
| Retained actual unit/cleanup receipt | `legacy-switch-runtime-20261024-v5/cleanup.json` | `5cb76b3a6e5c0bad5c47f50ba29e9476b7f34dd651e02e3b2926a58e5ce56ee5` |
| Three reviewed source postimages | `legacy-switch-final-delivery-20261024-v1/REPORT.json` | `84e1f2308aeb257d86fcf4561bc1090a1ac0b7d236c4b88b6753f89a313d7f75` |

All 211 Client production source files in the input binding match published `bfffe9e`; the trial changes one production file. The remaining libraries use the completed normal `8efa26f` producer. This is an explicitly matched development producer, not a fresh later-head repository/consumer certificate. Actual nine-library Client compiler references, all eleven native compiler inputs, complete PDB/source checks and protected-output invariance are recorded. The tested Client DLL is `340898e66d7b60fa51a8765a99ba688f794313a298496f1a1969c9ca4e4c50c6`; its PDB is `9acd66ee4012ae131351d0a00b43adb7af6de4c948bcd2535faf32bcbc5cc47a`.

## Executed checks

| Check | Actual result |
| --- | --- |
| New framed-session inventory | 264 cases: 175 family/hand cases, 32 modern controls, 38 context cases, sixteen terrain lifecycle cases and three protocol-47 offhand rejections; family cases exercise both powered states |
| Original red | All 264 execute: 221 expected failures and 43 passing controls; 213 missing wire swings, six invented wire swings and two protocol-777 typed Unknown/Success mismatches |
| One-file production correction | The same 264 cases pass with zero skips |
| Existing block-use fixture | All 368 original identities/assertions pass before and after the profile change; one explicit complete-column setup line establishes its intended world context |
| Other adjacent cases | All 6,225 original distinct identities pass; combined with the 368 above, all 6,593 existing regressions are preserved |
| Source and format | All 242 portable-PDB documents match; scoped format passes; all 4,301 immutable normal source entries and 453 original failed-attempt files remain unchanged |
| Strict Native AOT | 25,157,376 bytes, strictly below 25,165,824; margin 8,448 bytes; exactly one test executes and passes |

The native executable has SHA-256 `b2e243565074fb896c042a2d7181a53eb51d00086e15ed886a9009c3a3ae30f7`. Its nine-package graph remains unchanged. Native publishing does not alter the tested producer DLLs or protected test outputs. Equal whole-binary size to the preceding private producer does not establish zero code cost or a throughput claim.

Exact argv, environment, working directory, exit codes and hashes are in the final job's `commands/` receipts. The sealed continuation command is `/usr/bin/python3 -B /home/anon/Projects/MinecraftOfficial/runs/legacy-switch-runtime-20261024-v5/run.py --input-manifest-sha256 a66f41f2d787f549dda46651a486576159fc467abf7ba4a9619fdaddb30889ee`. It runs in retained unit `umpk-legacy-switch-runtime-20261024-v5.service` with a 3 GiB memory cap, 200% CPU, 1,200-second limit and no core dumps. Actual unit exit is zero; peak memory is 2,387,881,984 bytes and all thirty observed owned PIDs are absent. All 75 active cgroup samples match the actual limits and report zero OOM/kill events. Final owned cgroup counters are unavailable; the cleanup-scope correction below excludes the mistakenly captured root-cgroup snapshot. No vanilla server starts in this private runtime/AOT trial.

The deliberate baseline addition is 264 Client cases, from 34,377 to 34,641. Integration remains 59. The seventeen-suite expected total becomes 48,428; no new full-suite execution is claimed here.

## Preserved verifier failure and continuation

The first durable `legacy-switch-runtime-20261024-v3` attempt passes the original 368 cases and executes the expected 221-red/43-control inventory. Its verifier then fails because it expected a wire-count mismatch for all eight modern terrain cases. Protocol 777 uses a local swing, so its two cases correctly fail the typed result assertion while both packet counts remain one. The original overall failure, actual XML and cleanup remain unchanged. No production compile or native publish runs in that attempt.

The separate v5 continuation changes only the verifier classification and receipt-gated reuse of completed work. Read-only negative controls reject a wrong typed result, wrong protocol and missing result. It requires exactly 213 missing wire swings, six invented wire swings and two protocol-777 typed failures, retains every test assertion and failed identity, and does not rerun the completed original 368/red tests. It then executes the missing context, production, green, adjacent, PDB, format, native and smoke phases. An unlaunched v4 copy-setup error is preserved separately and is not a runtime failure or pass. The earlier temporary preparation disappeared; its reviewed 264-test bytes were recovered exactly, and the new source audit has its own hash rather than recreating the old receipt.

## Cleanup scope and post-integration proof

`legacy-switch-cleanup-scope-addendum-20261024-v1/scope-correction.json`, SHA-256 `fe3403e238f6603f8378bc1494082a28aa2e738748c285597729ba09621cfb1d`, preserves both original cleanup receipts and excludes their final root-cgroup snapshots. An empty unit `ControlGroup` was mistakenly resolved to `/sys/fs/cgroup`; its foreign process list and counters are not owned-run evidence. Final owned counters are unavailable. The addendum independently reconstructs the eleven/thirty owned PID unions from the service, commands and observed descendants, and checks the nine/seventy-five valid active samples. Only the exact named units were stopped. Future collectors reject empty/root/foreign paths before any read.

Independent actual-file review verifies 1,516 pins, all 4,301 immutable source entries, actual passing XML, the native ELF and its eleven real compiler inputs; `legacy-switch-runtime-20261024-v5/root-actual-review.json` has SHA-256 `4b5b1e67f9b985fcfe44029928644f4ee542381b6894d79595ee88dba98de392`.

After integrating the three exact postimages, a separate source-bound rerun passes all 264 focused cases again, with zero skips and no rebuild. `legacy-switch-post-integration-focused-20261024-v2/completion.json` is `4398e3ff6e191a24faf5faf9128bf1a739bca75735ff2f120dd602c1bb927a27`; cleanup is `708596ae796436b4bef72f598257f2f304321235cad64fed02296f8ec1639e74`. Actual exit is zero, peak memory is 244,084,736 bytes and all four observed owned PIDs are absent. The corrected guard records final owned counters as unavailable and reads no root cgroup. Its independent `root-actual-review.json` is `3f514241d72f05050e421c6219681660858ca1144f89459b5dbb20228089ce6b`.

## Remaining acceptance

Native-server lever/button acceptance across the measured boundaries has not run for this slice. Modern repeated-button local state is a separate gap: the inspected 1.15 and 26.3 clients set `powered=true` locally, while the current predictor only observes the received state. Its repeated-use and release/invalidation rules require their own source-bound runtime proof; the one-action modern controls above do not close it. This proof covers activation result/send/swing, not specialized lever/button placement, support/orientation or other hand-activated families. Broader interactions, the final 50-protocol/75-release matrix and a fresh final committed-producer repository/consumer/package/AOT/CI certificate remain open.
