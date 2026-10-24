# Sequenced local block prediction

## Behavior and scope

The pending change connects local block prediction to the existing session, action, world, physics and targeting owners. On protocols 759–777, vanilla's current block state can differ from the last server observation while an action awaits acknowledgment. An early server update changes the retained server shadow. Acknowledgment restores that shadow before releasing action waiters. Internal interaction and physics queries read the current state; public received-world snapshots keep their existing meaning.

Filled-jukebox clearing is bounded to 759–761. Modern buttons also need local powered state, with their existing era-specific result and swing rules. Modern jukebox block-entity/cache context, empty-jukebox insertion and other block-use families remain separate work. Protocol 47 keeps its 1.8.9 client profile.

The implementation uses the existing sequence owner and captured-session packet group. Its internal ticket distinguishes world lifetime from sequence number, so cancellation from an old world cannot remove a newer same-number waiter. Pending predictions retain their original player position and latest sequence. Full-column observations replace current state while retaining the pending shadow. Unloading prevents state writes without proving that captured-player correction is impossible. World replacement, reconfiguration and disconnect clear the applicable state. Same-world respawn follows the measured native counter behavior.

The packed-key map preserves native mixing, linear probing, zero-key ordering, growth, descending rehash and wrapped iterator removal. The focused cases cover competing captured positions, growth, zero-key ordering and partial acknowledgment/reinsertion. The shrink branch has source comparison but no independently forced native shrink scenario.

Public signatures, dependencies, package versions, DI lifetimes, datasets and generated pins stay unchanged. Game adds an internal Client friend boundary and an internal state-reader path for the existing block raycast. These postimages remain private development inputs pending the open fixes and size check below.

## Native facts

The original-method comparison passes 96 observations on 1.19/759, 1.19.3/761, 26.1/775 and 26.3/777. Root audit `344f3bb36154895fd6b83cf59d65ed5214d648bd7d159c0d12c40e965a06cd78` verifies the observations and matching inputs. The [jukebox report](jukebox-activation.md#completed-original-method-comparison-2026-10-24) records the controlled bootstrap, renderer substitutions and limits.

The comparison establishes exact `VOID_AIR` for absent chunks, captured-player correction even when the state write fails, no correction for equal current/shadow states, the 775+ cumulative-acknowledgment teleport barrier, replacement/reload shadow retention and native first-cell acknowledgment order. Client button scheduled-tick lists are inert; they do not justify a reset timer. Initial survival mining emits START and two main-hand swings on its first tick, followed by the measured continuation and cleanup behavior.

## Runtime evidence before integration

All run paths below are under `/home/anon/Projects/MinecraftOfficial/runs/`. Each private producer records its compiler source, references and PDBs. This evidence does not replace fresh repository and consumer checks on the eventual Git commit. Exact command arguments and exits are retained in each run's `commands/*.json`, with output hashes and TRX results outside Git.

| Attempt | Executed result | Failure or limit |
| --- | --- | --- |
| `sequenced-block-prediction-red-recovery-20261024-v1` | 258 original framed cases: 45 expected failures, 213 controls, zero skips; root audit `379bbd93` | The original generated-output-pin harness failure remains failed; recovery does not rerun tests |
| `sequenced-block-prediction-staged-import-resume-20261024-v1` | All 21 production stages compile; 276 focused cases pass | Its older 6,593-case assembly has 1,170 failures across five fixture classes; Physics/AOT do not execute |
| `sequenced-block-prediction-full-current-map-20261024-v1` | Current 211-source Client and 316-source test assemblies compile; full 34,733 executes with 33,919 passes, 814 failures and zero skips | Six intended map failures, 799 ready-world membership setup failures, six pending-button acknowledgment fixture failures and three managed-memory failures |
| `sequenced-block-prediction-full-fixture-resume-20261024-v1` | Scoped formatter checks pass | Tests-only compilation fails CS0136 because a new loop local conflicts with a parameter; no tests execute |
| `sequenced-block-prediction-full-fixture-resume-20261024-v2` | 34,726 passes, seven failures, zero skips; all prior 805 fixture failures and three memory failures pass | Six complete map assertions and one direct-floor respawn setup failure; the whole run remains failed |
| `sequenced-block-prediction-full-fixture-resume-20261024-v3` | Four repaired world-rebuild cases and 298 focused cases pass; full suite has 34,732 passes and one failure | Managed memory exhaustion in the unchanged legacy JSON reader; Physics/AOT do not run |
| `sequenced-block-prediction-full-green-memory-resume-20261024-v1` | The same DLLs execute 34,733 cases under 4 GiB: 34,732 passes, one functional failure, zero skips | Exposes a real protocol-47 captured-session completion race; no managed-memory failure; Physics/AOT do not run |
| `sequenced-block-prediction-mining-race-runtime-20261024-v1` | Deterministic five-case RED has one expected failure/four controls; the one-line fix passes all five, 298 focused, all 34,733 Client and 715 Physics cases | Native AOT is 25,178,240 bytes, above the unchanged strict limit of 25,165,824; smoke does not execute |

The original 814-failure run has TRX `a88cddfc7261e8b0a286cb40126d1624df4f9bf8a58d8c4fd40bb33ef007f234` and completion `dc013c76dd25b35945120fbf24c0c39dc5af4221beb2df8ffb05ac2d2baeeac3`. Its memory errors occur while loading existing legacy-player context JSON. The unit reports no kernel OOM kill under its 3 GiB limit; its peak is 3,048,849,408 bytes. This does not establish the cause of the reported Codex app crash. Cleanup `38c418fcd480e98073c6096e2d883370198c6ba0fcb480dac6dd224765aaa89d` records all sixteen observed PIDs absent.

The tests-only repairs register explicitly constructed ready worlds with the membership owner and send a real bound acknowledgment between existing Axe fixture actions. Assertions and case counts stay unchanged. The 21-file patch is `43eb04e4ea6c3439e25c4bf013d89527b65347937b753c12987edae54f35c9eb`; independent review `bcc35cb838ec90196bbef6978166f4d3bf5474736a9bd71255f063a060f999ec` verifies all 5,776 pins, the reused Client pair and nine references, 295 unchanged test sources and 21 fixture postimages. Later narrow fixture corrections and failed attempts remain under their own run directories. Tests use explicit serial xUnit settings. Only the test service's memory cap increases from 3 to 4 GiB; assertions, counts, performance limits and AOT budget do not change.

## Captured-session mining race

On protocol 47, a START write can synchronously cancel or replace the session while the caller still awaits the queued-request result. The old mining request can complete before that await resumes. `WaitAsync` can return an already completed success despite its canceled token, and this older protocol has no subsequent acknowledgment wait to observe cancellation.

The repair checks the captured lifetime token immediately after the request-completion await. The existing five session-replacement cases gain a deterministic publication gate: real owner work and the START callback execute first, then the queued result reaches the caller. On the unchanged producer, protocol 47 fails with the expected missing cancellation while four adjacent controls pass. The single production-line change makes all five pass. Packet-order and no-new-session-write assertions remain intact; no test identity or count is added.

Root audit `02235218e96f5209f633142b784f5a64d92f29777c0e746544dd19bce659236a` rehashes all 5,935 inputs and checks the actual RED/GREEN, all 34,733 identities, the exact 6,593-case passing subset, 715 Physics identities and the failed AOT binary. It independently finds all 39 observed PIDs absent. Cleanup is `227aea35820f1915225c060dd01032d7fe7d5e2fe7bce570acbbf66a54c0cb9f`; completion is `4d041e1ea0114715a8a31ce7a49d6c31760d6b09e54494c20848eeb2c73ff13b`. The actual full-suite TRX is `94a079798618caf17c3d4991e669d3176500474c51b00b25535f497265e03cd4`. The retained unit's peak is 3,421,343,744 bytes. Final cgroup counters are unavailable because its exited ControlGroup is empty; the audit does not substitute root-cgroup counters.

The functional producer index is `sequenced-block-prediction-functional-producer-index-20261024-v3/REPORT.json` (`27dfcc4e492558940cbd35742ff32a2f1cc4b2a6115bd8c148c08ab6ee297dae`). Client DLL is `852ecc9701fa149bed9fc0011d0f606e225f776cda758f6966ea86dbf3a95332`; test DLL is `b68e235924fac0a08a1e9937c3d08dcbc48031ca7724f4c34f7d2585b52c671b`. The Game source map derives from actual compiler/PDB evidence, including the modified raycast, rather than the stale preparation map. The reused Game proof has exactly 1,032 passes. AOT failure and the unexecuted smoke remain explicit prerequisites for final acceptance.

## Contextual acknowledgment collision

The next private producer fixes captured-position restoration for powder snow and scaffolding. The supplied shadow now occupies only its own cell in the collision view. The existing physics query receives the player’s observed position, cached box, fall distance, boot context and applied sneak input. Applied navigation input can differ from the manually requested sneak state; the correction reads the physics owner’s input. Public collision APIs stay unchanged.

The original-method supplement executes thirteen observations on each of 761 and 777, including actual native fall-field values/raw bits and two observed-position versus cached-box controls. All 26 pass. The 24 framed cases reproduce twelve expected position failures and twelve controls on the prior producer. Replacing only `PhysicsEngineHolder.cs` makes the same 24 pass, followed by all 298 adjacent interaction and 715 Physics cases. Formatting produces no changes. The actual Client DLL is `90a0c9991ec061b0134bb58f2868917ccd66749ce23a5fcb7fb3ba6c3438db73`.

Run `contextual-prediction-ack-execution-preparation-20261024-v2` retains completion `f4a62a22719ad907267b1a911af588619bb80f888f83c381560957522e34108b` and cleanup `8ac3cb311923b8996191c749f8156f385284ee8f40db793371989c57db8df95a`. Root audit `936ab96db5590df31421c9f1da7076626a7696f61a01baf89ab913f7951e2194` rehashes 6,081 inputs and 73,397 native dependency entries, reruns the native-output verifier, checks actual TRX rows and compiler/PDB bindings, and finds all thirty owned PIDs absent. The retained unit exits zero under 3 GiB/200% CPU, with a 1,949,478,912-byte peak. Actual Type=exec, retention and invocation are checked before cleanup. Final cgroup counters are unavailable.

This scoped run does not repeat the full Client suite or publish AOT. It accepts the earlier functional producer as an experimental prerequisite while explicitly preserving its failed 25,178,240-byte AOT result and unexecuted smoke. Combined full-suite, strict AOT and final native acceptance remain required. The 24 new identities are separate from the earlier 34,733-case full result.

## Counts and remaining acceptance

The 284 prediction-focused identities include 206 existing jukebox cases. Deliberate additions are 78 identities: 68 acknowledgment/session cases, two lifetime-ticket contracts and eight map cases. Fourteen existing 1.11 peer cases bring the combined focus to 298. On integration, Client 34,655 becomes 34,733 and the seventeen-suite expected total 48,442 becomes 48,520. These count changes remain proposed until the tested postimages enter the shared checkout.

The contextual correction above now has native and framed runtime proof. Native inside-origin point/face/inside handling when targeting projected outlines remains pending. Independent owner review `03b06f83dfda32de0b7297c401f10ab3f5d43128a950890352c9d187fe7449b2` identifies both defects. The fourteen prepared projected-outline cases have not executed. Root static-outline audit `bc609963a1f5a713ee5a44b081842ae46c162aed7efaa1e3d466140ee9e747dd` checks 398 family registrations and 8,958 state IDs across all fifty datasets; that source audit does not prove the runtime fix.

The AOT binary must shrink below 25,165,824 bytes and execute exactly one passing smoke test. Fresh combined repository, package, consumer, DMCBK/MCC, live/version matrix and actual published-head CI checks remain required. The broader mining and placement objective is incomplete.
