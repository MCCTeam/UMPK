# Entity interaction wire and session ownership from protocol 775

The entity-interaction packet changes at protocol 775 (26.1). Protocol 774 (1.21.11) retains the action discriminator. Protocols 775–777 instead carry an interaction hand, a mandatory low-precision entity-relative hit location and the secondary-action flag. The official packet and vector classes are byte-identical across 26.1, 26.1.1, 26.1.2, 26.2 and 26.3. The earlier "Interact777" TODO therefore described the wrong starting boundary.

This slice supplies the real relative point through the existing entity-interaction caller. It also preserves the carried-slot synchronization and session lifetime needed to send an action against the same state that supplied its target. Mining and placement use the shared picker and geometry owners. This report covers the entity-interaction caller and the shared state exercised by these actions.

## Native facts

The original 26.1 `MultiPlayerGameMode.interact` at lines 435–439 synchronizes the carried index, subtracts the entity's current position from the selected hit, sends the interaction packet and only then handles the local result. The 26.3 equivalent is at lines 451–455. Spectator mode still sends the packet. `LocalPlayer` lines 1238–1288 uses the current eye, item-range context, block/entity precedence and strict reach filtering to select that hit.

Five original codec witnesses execute the same 32 vectors, producing 160 passing rows. The cases include zero, float-derived hit coordinates, rounding boundaries, scale continuations, clamp limits, infinities and NaN. This is native codec execution; it does not establish native input timing or live server acceptance. The later managed 96-row comparison reuses those 32 vectors across protocols 775–777.

An additional original read witness checks incoming hand values −1, 0, 1 and 2 on 26.1 and 26.3. Native `InteractionHand` uses the `ByIdMap` zero fallback: −1 and 2 decode as the main hand. Re-encoding those invalid input values produces canonical main-hand bytes. The typed outbound API rejects invalid hand values separately. Earlier valid-hand codec results remain valid within their recorded scope; they did not cover this reader fallback.

## Accepted private verification

These checks use immutable private libraries and source postimages. They have not yet been promoted into a final repository producer. The Protocol DLL after the hand fallback is `74be0113b6078b7b55bff81db3a9e323a5936bac459c821eb5736a10a7f402ef`; its PDB is `01621d01583cb0b8abc002edcd25987ee605b6c75cb0a0f56da381db33360f50`. The final private caller Client is `a2b9cbcc491178cce7d33394e240438d9886b0b5ef8e86370f012a694b9595ed`, with PDB `528b358087cddbf3ad89072e7c0f5df6af17fea3daead69fb183ee54ed4b6fa1`. Each receipt binds the exact commands, source checksums, compiler roles, binary manifests and cleanup for its own run.

| Check | Executed result |
|---|---|
| Initial wire boundary | Six failures against the prior codec; protocol 774 control passes. |
| Expanded native vectors | The same 39-case fixture has 38 failures before the wire change, including the helper-only stage, then 39 passes after the wire change. All 69 retained adjacent cases pass. |
| Context and truncation | Twenty-four genuine prior-codec failures become 63 passes when combined with the earlier 39 cases. |
| Shared decoder ownership | All 63 Protocol cases and 37 retained self/spawn velocity cases pass after the authored friend and Client wrapper stages. |
| Native hand fallback | Six prior-decoder failures become 69 passes, retaining the earlier 63 cases. |
| Caller baseline | All original 30 cases pass. The added 20 have 14 runtime/order/point failures, four exception-contract mismatches and two passing controls. This establishes the missing caller integration. |
| Caller production stages | Capabilities and picker stages retain the same 18 failures. The action stage passes all 50 cases through the same frozen Test pair, then all 37 velocity and 69 Protocol cases pass on the final private tuple. |
| Additional caller acceptance | The corrected complete-owner-fence fixture passes all 69 caller cases: the original 50 plus nineteen lifecycle, input and geometry cases. |

The shared LP value decoder preserves the earlier Client arithmetic and permissive captured-value fallback. Strict packet framing remains in the existing reader. The writer follows native float widening, clamping, scale selection, Java rounding and byte order. The Protocol 774 form and existing raw velocity capture contract remain unchanged.

## Caller design and remaining acceptance

The three reviewed caller postimages use the existing capability profile for hand availability and the 775 boundary. The picker retains the actual selected point and observed actor origin. The public method keeps its signature, enters the session owner, validates its hand and obtains a known matching view hit before synchronizing the carried slot on protocols 775–777. A miss, another entity, blocked view or unknown context rejects before the packet group. Earlier explicit-ID interaction semantics remain available. The committed group uses its captured session and sink; it does not initiate an attack, swing or server-owned item effect.

Root accepts the caller's three cumulative production stages, including the unchanged original 30 cases. Nineteen additional framed cases cover removal, ID replacement with a fresh origin, crouching eye geometry, secondary-action flags, exact reach, hand admission, cancellation, teardown, reconnect, spectator sending, world replacement and explicit unknown item-range context. The new crouching and replacement LP constants are independently source-derived; they are not additional native captures. The removal scenarios remove the actor before their first interaction and establish fresh admission after removal or replacement. Pending-hit replacement is outside these cases. The first executed version has seven passes and twelve fixture failures; the corrected complete-owner-fence fixture passes all nineteen, as recorded below.

The first extra-acceptance wrapper stopped at formatter CLI admission because folder mode rejects the `--no-restore` option. It compiled no Test assembly and executed no cases. The exact failed invocation, unchanged inputs and both owned host-process exits are retained in `interact-caller69-format-admission-failed-binding-20261101-v1.json`, SHA-256 `3b49252d58ec0865b16aacdffc396767c6d5fe389dcca81a9cd11eb050a4d98c`. The separate wrapper successfully runs that formatter, compiles the ten-document Test fixture with all 54 resource payloads intact, and executes all 69 cases. The original 50 pass. Seven of the additional nineteen pass; twelve stop at an inconsistent test capture count, expected two raw frames but observed three, after the two typed action-packet count passes. The test kept its raw-write hook active through its own keepalive fence. This attribution follows the retained failure location and fixture source; the third raw frame bytes were not retained. The failed wrapper and all seven owned host-process exits remain in `interact-caller69-fixture-window-failed-binding-20261101-v2.json`, SHA-256 `f1a29ea8e015240ede4782e7224940beefac6a8192160925ef1a05e6fe56cc66`. A test capture-window correction keeps recording through the complete action and a non-network owner fence, then removes the hook before the network keepalive fence. Every assertion, expected body and case count remains. The corrected fixture executes all 69 cases with zero skips on the same private Client/Protocol tuple; all original 50 and all additional nineteen pass. Its actual binding is `interact-caller69-actual-binding-20261101-v4.json`, SHA-256 `f3cd465e12272213a1e8ebd65d9368d522b9dc519829aedaba0f98ea71393c66`; Root independently accepts the exact 69-case result in `interact-caller69-root-audit-20261101-v4.json`, SHA-256 `d49c7092f372a33b63ea14f86491085425c31b11c17cbcd49f397d9c51cde6d8`. The failed sneak cases stopped at their first standing helper, before sneak input and eye assertions; later LP/body assertions and some recovery/tick assertions in the other failures were also not reached. The passing typed packet count alone does not prove those behaviors.

The corrected run reaches the later assertions: standing/crouching/standing eye and exact LP bodies on 775 and 777; strict framed reach boundaries; removal, fresh ID/origin and world recovery; both unknown item-range inputs and restoration; complete-group cancellation; and teardown, reconnect, no-session and hand checks. Removal remains a before-first-interaction scenario. The test assembly is `a014d39c3ea2e9629d0251e80baa576b400d3fc27d8e3b377e5279ffbbdcc44e`, with PDB `50f3fd129278227f770653b2448749539aafb54bca149f93efd835d97a56da81`. Its ten PDB documents match the actual sources, all 54 resource payloads match, and the original 198 references, twelve analyzers and three configurations remain. The formatter produces zero changes to the extra source; non-whitespace content and all nineteen quoted literals match. All 892 immutable inputs and the 126-file runtime remain unchanged. All seven owned host processes are gone. This is framed session acceptance, with native codec evidence retained separately; live server behavior and native input timing remain unproven by these cases.

Final formatting, aggregate suites and test counts, conformance and generated pins, package checks, producer-matched consumers, AOT, live acceptance and final-head CI remain required. The packet record's XML-summary postimage is also awaiting the final source build. Prior AOT and consumer receipts for other producer tuples do not validate these private libraries.

## Evidence index

Root receipts are under `/home/anon/Projects/UMPK/.scratch/`; raw outputs are under `/home/anon/Projects/MinecraftOfficial/runs/`. The named JSON receipts contain the exact commands and hashes without placing raw logs or official artifacts in Git.

| Root receipt | SHA-256 |
|---|---|
| `interact775-native-root-audit-20261101-v1.json` | `3d362a64b1a1b4170872b1352bbe048827bdc741ad5c1a2dd95cb35278086c8d` |
| `interact775-codec-red7-root-audit-20261101-v1.json` | `8255824aead87b84dd623681359d46e4e984b07717d545081e5c26aa3389b36d` |
| `interact775-staged39-root-audit-20261101-v1.json` | `8c3517a32164b115f5d4d683156c05a97314ba529ced91e7dafaa19b8829f9c3` |
| `interact775-context63-root-audit-20261101-v1.json` | `c6cbd929f285edcc711e3822fc180df15850b1b6aa1847c28327998995e33209` |
| `interact775-friend-wrapper-root-audit-20261101-v1.json` | `4a78d27cfdb53355ba8dc5625759f9c4c1eff2e4336eb786928a91cb532e6c5b` |
| `interact775-hand-read-root-audit-20261101-v1.json` | `df36f458e9c3faefc6b6aa132630f712f843a147ec0e845b34d9a85c2e68e8e6` |
| `interact775-hand69-root-audit-20261101-v1.json` | `0acfa5152a7031226b994863229ee522dc95644f58167b86576e7b523336d1f4` |
| `interact-caller50-red-root-audit-20261101-v1.json` | `c666140a88ef11dc9863d88ee8b1f4864ca2b57fdbe86d76c611286a2781a0c7` |
| `interact-caller3-root-audit-20261101-v1.json` | `4fc96d6c8fbcc890c75069b1465a306a5a278693449d713c5cf90882d930cfee` |
| `interact-caller69-root-audit-20261101-v4.json` | `d49c7092f372a33b63ea14f86491085425c31b11c17cbcd49f397d9c51cde6d8` |
