# Filled lectern activation

Using a known filled lectern now follows the native client result and swing rules through the existing session-owned predictor and public `PlaceBlockAsync` action. The change requires the canonical `has_book=true` state. It does not predict empty-lectern or book-insertion behavior, simulate server menu effects, or infer a book from missing block-entity data.

## Native scope

The source audit covers 48 named releases and 36 supported protocols, from 1.14/477 through 26.3/777. The original 1.14 Boolean interaction returns true for a filled lectern. The 1.15 result method returns success. From 1.20.5, the item hook delegates to default block interaction, whose main-hand restriction remains owned by the existing controller. Later `TRY_WITH_EMPTY_HAND` handling and protocol 777's local-only swing policy retain their existing owners.

Empty lecterns differ by era: the early Boolean branch returns false, 1.15 adds item-sensitive results, and 1.20.5 splits item/default hooks. Those branches remain outside this slice. The implementation adds the lectern to the existing menu family and checks `has_book` before predicting success. It adds no public API, dataset, feature axis, dependency, package version or DI change.

The retained source bindings are `/tmp/umpk-lectern-activation-20261024-v1/native-source-bindings.json`, SHA-256 `14ed6c659125ce8d0ea98f3e0e68462cb69f07ed5a766a2aaee62b89877f1619`. They bind the original lectern methods, registration/property names, client controller and input branches, and canonical states. Retained jar hashes keep their original audit identity; this trial does not claim a fresh native-client execution.

## Executed runtime proof

The private trial uses the completed normal `8efa26f3ac2e8b1397928a7bc2c8ec530eb015ed` checkpoint with one exact `BlockUsePrediction.cs` overlay. Framed packets populate terrain, inventory, hand, sneaking, cooldown and Adventure context before the public action runs. The 72 protocol/hand cases each exercise all eight filled canonical states; eighty boundary/context cases retain terrain, empty/insertion and permission controls. Together they make 656 public block-use calls.

| Check | Actual result |
| --- | --- |
| Baseline behavior | All 152 cases execute: 104 expected failures and 48 controls pass. The failures comprise 87 missing packet-swing counts and seventeen typed `Unknown` results. Exact identities and assertion groups match. |
| One-file production change | The same 152 cases pass with zero skips. Actual decoded use/swing, hand, cursor, inside-block state and typed local swing assertions pass; observed world, inventory and block-entity state retain their tested values. |
| Existing regressions | All 5,152 original identities pass across menu activation, placement animation, doors/gates, spectator, decoded terrain and modern slabs. |
| Compiler/source proof | Actual reference guards verify nine Client dependencies and eleven test dependencies. Source guards and all 229 PDB documents pass, including the accepted embedded-source handling. Scoped whitespace passes. |
| Native AOT | The binary is 25,165,568 bytes, strictly below 25,165,824. Exactly one smoke test executes and passes, with zero skips. The eleven actual ILC library inputs and nine-package graph are verified. |
| Invariance | All 902 frozen inputs and 4,301 archived source entries match. The 279 tested output files, 250 protected stage outputs and changed native input pair remain unchanged. |
| Root integration | Root independently rehashes the delivery's 24 evidence files and reads the actual RED, GREEN, adjacent and smoke XML. After applying the exact production/test postimages, all 152 focused cases pass again against the proven matching compiled Client and test output. |

The production postimage is `ba9b97de89625edde0a3d4d036d5e216a0296c5ccd2579dfc22f36955fc2cf78`; the test source is `acec8e72d957a966598e5c007c88e7188b47d01cd1ea4f608dbdb62aaafa4ef7`. The tested Client DLL is `06c601f7699e3e97aacaef3969750ee6f29b000f276fd35dca4bfb3e6e09c727`; the focused test DLL is `5e2f510854dc0bedd56430141753913cf771caa2458039614a893b11b9ce9b17`. The native binary is `55f739337d72873d770a20974630db9122b4c07c6d890ad309cd16168a74ed37`.

The deliberate test-count change is Client 33,562 to 33,714, adding 152 cases. All other sixteen suite baselines stay unchanged. The expected seventeen-suite total becomes 47,500, including seventeen normal opt-in skips; this is an explained baseline update, not a new complete-suite execution claim.

## Commands, evidence and cleanup

The sealed trial runs `python3 /home/anon/Projects/MinecraftOfficial/runs/lectern-activation-runtime-8efa-20261024-v3/run.py --input-manifest-sha256 09a70d1fdccf3b61aa4b3d9a92b2db6ebbd83c14a0f3afcaa9c8986fa793b341` in its named retained systemd unit under 10 GiB, 200% CPU, a 1,800-second limit and `LimitCORE=0`. Its exact launch and per-command argv, working directories, actual reference/source receipts, logs and XML stay in that run directory. The completion is `54ec8e04fde677e99ba56e108c7bd49f3cf2d9d8b802f670bf2d5fdfd5e32b4e`.

The actual successful unit exits zero with a 2,310,385,664-byte peak and 70.673 CPU seconds. All twenty-nine observed owned PIDs are absent, and the retained unit is manually stopped after its final metadata is recorded. The final cgroup path is empty after clean exit; no final event counters are invented. The cleanup receipt is `69c19a632f7f1ad66b89d06a181392c635595542d2439009e10f0d184bf31f11`.

The required post-integration rerun uses the exact saved argv in `/tmp/lectern-root-focused-20261024-v1/grant-and-inputs.json`, including the public-session class filter and original compiled test assembly. It executes under a separate 3 GiB, 200% CPU, zero-core unit, passes all 152 cases, verifies unchanged DLL/PDB inputs, and records the actual successful unit before manual cleanup. Its peak is 290,865,152 bytes; completion SHA-256 is `4ad85ceb6affc60db206cab6e92e833c39c4e8854f9fab78692281f0ffa4876e`.

The first Python-only preparation failure is retained at `/tmp/lectern-bind-v1-failed-20261024.json`. The corrected binder verifies the actual seventeen named `NotExecuted` rows because the normal TRX aggregate skip counter is zero. That preparation failure launched no compiler or native process and does not count as behavior evidence.

## Remaining acceptance

Physics-enabled native acceptance is planned for seven result/hand/swing boundaries and 29 actions; none has executed for this lectern change. Empty lecterns, book insertion, unsupported components and the broader conditional interaction inventory remain open. Fresh final-producer repository, consumer, package, AOT and published-head CI acceptance remain required. The PR stays open and draft.
