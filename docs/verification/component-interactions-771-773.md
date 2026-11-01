# Component-driven block interactions through protocol 773

The candidate lets the existing block-action owner read and apply Adventure predicates on protocols 771–773 (1.21.6–1.21.10). It also preserves mining progress when a new inventory packet carries an equivalent enchantment map on protocols 766–777. A changed key, final level, explicit-zero membership or applicable tooltip value still resets the target. Public item equality and action contracts remain unchanged.

This is a scoped implementation checkpoint. Specialized placement, richer predicates, later predicate wire forms and the complete native/live acceptance matrix remain open.

## Production ownership

| Owner | Change |
| --- | --- |
| `ItemComponentTable` | Bind the existing measured modern `can_break` and `can_place_on` codecs in the 1.21.6 and 1.21.9 factories. Other layout axes stay intact; factories for protocol 774 onward remain unbound. |
| `VanillaComponentMiningPermission` | Extend the existing component-owned permission/cache through protocol 773, with placement enabled for 770–773. Missing tags, native property/range information and full block-entity context remain unknown. |
| `VanillaMiningItemIdentity` | Extend measured predicate comparison through 773 and compare the two enchantment component forms as native last-wins maps through 777. Keep explicit zero entries, registry-handle identity, legacy tooltip semantics and pre-component NBT ordering. |

The caller remains `InteractionActions`: permission is checked on the session owner, placement still distinguishes pre-send rejection from a result after USE, and mining retains its existing target and cancellation owners. The comparator does not use DATA hashes as equality. It allocates two temporary dictionaries when it must compare separate nonempty enchantment lists; the unchanged fast path still applies first. Existing packet-allocation checks do not measure this new comparison cost directly.

## Native evidence

The 771–773 source audit compares all five named releases with the retained protocol-770 methods. The original-codec extension executes 33 cases across 1.21.6, 1.21.7 and 1.21.9, including full SetSlot vectors, recursive values and measured DATA hashes. All original game class entries in the unsigned runtime copies match their official JAR entries. ROOT receipt `adventure771-773-native33-root-audit-20261101-v1.json` has SHA-256 `71c313feccc4de964b9bbcde1ef63ec11ba8214f17bfe601a0e47f8630c4a3a5`.

The separate enchantment witness executes 190 checks on native protocols 766, 767, 770 and 777: 72 component/stack identity decisions, 112 final-map admission decisions and six original JSON/fixture-NBT codec checks. Independent decoding verifies last-wins map identity, explicit-zero membership, tooltip removal and final levels in 0–255. It verifies 137 executed original class origins; four conditional roles retain source evidence only. All seven owned host processes are absent after collection. ROOT receipt `mining-enchantment-native190-holderfix-root-audit-20261101-v1.json` has SHA-256 `bd492c27645da205fd778286d0c699f99440d14ab6eba902ba63b31ab5bc2a7f`.

The definition fixture contains two complete source-derived enchantment definitions and receives them through the bound configuration codec before play. The original native codec accepts both their JSON and fixture NBT forms. This does not claim that the fixture reproduces a full server registry or the exact NBT a native server emits. The witness does not execute original client input, a world tick, a server, effects or DATA-hash availability.

Both failed native setup attempts remain failed: the first mixed signed default-package classes with the owned helper; the second used the older Registry object as a HolderGetter receiver. The successful attempt reuses verified class-identical runtime JARs and changes only the helper lookup to the original older Registry method. No original method is replaced.

## Runtime evidence

The corrected baseline runs all 248 focused cases before the private owner change: 99 pass and 149 expose production gaps. The enchantment-only owner then makes the same 114 inventory/mining cases pass. It separately passes 1,064 existing regressions. ROOT receipts `corrected248-owner114-root-audit-20261101-v1.json` and `mining-owner-adjacent1064-root-audit-20261101-v1.json` retain the exact binaries and literal case Counters.

The three Adventure production changes each run the same 29 codec, 105 action and 114 enchantment cases. Codec and enchantment cases pass at each stage. Adventure outcomes progress from 48 pass/57 fail to 81 pass/24 fail, then 105 pass/zero fail. The forecast of 87/18 for the intermediate stage was incorrect and remains distinguished from the observed result. ROOT receipt `adventure771-773-three-stages-root-audit-20261101-v1.json` has SHA-256 `48563fdf26bd63f9f8167a9b57194e48b1f58ffb0bfa098121cca79b544dcb7b`.

A subsequent regression run exposes an obsolete assertion that protocol 771 must throw as unsupported. It records 687 passes/one failure and leaves 376 cases unrun. The test moves that unchanged negative assertion to adjacent protocol 774 and retains protocol 777. A separate twelve-row table guard first records the three expected 24-versus-22 failures for 771–773, with nine passing controls. Its expected untyped counts then decrease by two because both measured codecs are now bound.

After those deliberate test updates, all 1,324 cases pass with zero skips: twelve table guards, the 248 focused cases and 1,064 existing regressions. Both normal test compilations retain their ordered source/reference/analyzer inputs, 195/363 portable-PDB documents and all 55 embedded resource payloads. Production binaries remain unchanged, and all 32 observed host processes are gone. ROOT receipt `adventure-test-refresh1324-root-audit-20261101-v1.json` has SHA-256 `46f4142e90b22c2b024b07746fab1ef27e855b84bd1f2cc08c581b528a0f9a01`.

The private Client DLL is `cc068a96416e060124a9381e6432880bc9011ff63db472879b67d0a9d6a9f3a6`; Protocol is `f46bf1150e8951eb86cbb23010a6434763a9ae0a60162ae16917f94f32f5dfb6`. Their metadata identifies D5; source equivalence to the reviewed postimages is recorded separately. New test DLLs are Client `3921eefd1d465f5a3eab5afb4b73d1db4b1c05c268de3192f62e373c2f98bdd0` and Protocol `4e874e246570c81d2bfebacf061680aa336737b65aead28b43e43e24cf2c16a0`. These hashes do not identify a build of a later commit.

## Full scoped checks and remaining acceptance

The complete Client suite passes all 36,131 cases with zero skips, 36,007 distinct display names and 124 duplicate occurrences. ROOT receipt `adventure-full-client36131-root-audit-20261101-v1.json` has SHA-256 `869f4138c57b0693a25dafe2b7dc71a7c1e9e9bcd520d0e6486493f9ed119c50`. The complete Protocol suite passes all 6,615 cases with zero skips. Its original wrapper stops on one updated display label after the successful test command: xUnit renders the codec's public Shape, whose protocol-773 component digest changes twice. A source-bound offline audit accepts that exact label change and the other 6,614 unchanged identities; no successful Protocol test is repeated. ROOT receipt `adventure-full-protocol6615-prefix-root-audit-20261101-v1.json` has SHA-256 `7c98e017e3be65b254b925b748b66700e3e7baf13a9d54ea3bffcde893a6ba26`. The earlier wrapper preflight-label failure starts no test. Both failed wrappers retain their original status.

Conformance first records three expected identity-pin failures and 98 passing controls. The documented `UMPK_UPDATE_CODEC_IDENTITY_PINS=1` gate then regenerates only `codec-identity/771.txt`, `772.txt` and `773.txt` in an isolated copy of the exact published `1c31d97` fixture tree. Actual digest values are `d07b0c41` for 771/772 and `55f3503d` for 773. Thirty-three shape lines contain 36 changed nested digest tokens; packet IDs, codec identities, probe fields and every other byte remain unchanged. All 404 other fixtures retain their hashes, including timelines and witnesses. With update switches absent, focused302 and the complete892 Conformance cases pass with zero skips. ROOT receipt `adventure771-conformance3-root-audit-20261101-v1.json` has SHA-256 `420e8aa52553e9db46234f18a05cd14a3b6d9f94a90a411359e4dce928b3bf9d`.

Normal repository formatting selects the eleven reviewed files by workspace-relative paths, runs both analyzer phases on all four affected projects, reports no workspace errors or warnings, and changes zero files. All eleven source hashes therefore still match the validated PDB documents. ROOT receipt `adventure11-normal-format-root-audit-20261101-v1.json` has SHA-256 `ce39d3b849f145069b98d83529b32dfc429eb9ba52e027c461f6c9bb976b34c0`. It verifies all four owned processes absent, peak 3,751,116,800 bytes under the unchanged 4 GiB limit, requested OOM score zero and observed nonnegative scores. This selected check is separate from a new full-solution CI format result.

Implementation commit `b03301b` records the exact reviewed source, tests, three generated pins and deliberate baseline changes: Client +190 to 36,131, Protocol +19 to 6,615, expected seventeen-suite total 50,118. This is not a fresh local seventeen-suite result. The private libraries retain D5 compiler metadata; their source equivalence to the committed eleven-file checkpoint is explicit. Fresh producer-matched consumers, AOT, package runtime and native/live acceptance remain separate, as does CI on the new published head. The final 50-protocol/75-release pack has not run for this candidate.

Final-map enchantment level admission is a separate measured decode defect with an unexecuted 344-case proposal; the mining comparator does not validate the packet stream. Wire-level zero remains distinct from the positive-level DATA codec, whose hash availability needs its own check. Protocols 774–777 add a different partial-predicate type discriminator and AnyValue identity behavior; those Adventure paths are not enabled by this change.

Raw commands, inputs, TRX, original-method outputs, failed attempts and resource/cleanup receipts remain outside Git under the shared `MinecraftOfficial/runs` directories. The ROOT receipts above live under the checkout’s ignored `.scratch/` directory. No downloaded game artifact, raw log, dataset, public API, dependency, DI lifetime or package version changes in this slice.

## Command and artifact index

Exact arguments, environments, inputs and raw outputs are retained in these owned runs; each ROOT receipt above identifies their hashes and accepted scope.

| Check | Evidence directory or command file |
| --- | --- |
| Native Adventure (33) | `/home/anon/Projects/MinecraftOfficial/runs/adventure771-773-wire-hash33-continuation-native-20261101-v1/commands.json` |
| Native enchantments (190) | `/home/anon/Projects/MinecraftOfficial/runs/mining-enchantment-native190-holderfix-20261101-v1/commands.json` |
| Three production stages | `/home/anon/Projects/MinecraftOfficial/runs/adventure771-773-three-stages-runtime-20261101-v1/commands.json` |
| Test-boundary refresh | `/home/anon/Projects/MinecraftOfficial/runs/adventure-enchantment-test-refresh-runtime-20261101-v1/commands.json` |
| Protocol full-suite prefix | `/home/anon/Projects/MinecraftOfficial/runs/adventure-enchantment-full42746-runtime-20261101-v2/commands.json` |
| Client full suite | `/home/anon/Projects/MinecraftOfficial/runs/adventure-enchantment-remaining-client36131-runtime-20261101-v1/commands.json` |
| Identity generation and strict conformance | `/home/anon/Projects/MinecraftOfficial/runs/modern771-adventure-conformance3-pins-20261101-v1/commands.json` |
| Normal selected formatting | `/tmp/umpk-adventure11-normal-format-20261101-v1/command.json` |

All test and formatter runs use SDK 10.0.401. Managed jobs retain the 4 GiB limit; the native enchantment run uses 2 GiB with one 128 MiB compiler and four serial 512 MiB JVMs. CPU, swap, core, observed OOM scores, exact invocations and host-PID cleanup are retained in the ROOT and per-run receipts. No inherited GC tuning or additional server process is used by these checks.
