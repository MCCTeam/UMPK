# Attack-charge player-tick eligibility

Protocols 774–777 now advance the existing attack-charge owner when the native local-player tick is eligible, including eligible ticks before the initial position correction. Movement and position publication retain their existing placement requirement. This matters when `DigBlockAsync` reaches the native charged/piercing input path: loading ticks can charge its first click instead of incorrectly suppressing it. The change uses the same session clock and readiness owner as item cooldowns; it adds no timer, public API or physics bypass.

## Native evidence

Verified original client artifacts and source for 1.21.11, 26.1, 26.1.1, 26.1.2, 26.2 and 26.3 retain `LocalPlayer.tick`'s `connection.hasClientLoaded()` gate. Its inherited `Player.tick` increments attack strength, performs native held-item reset handling and then ticks cooldowns. The existing readiness profile already distinguishes that player phase from movement after initial positioning; this change connects the attack-charge caller to that existing distinction. Older protocol counter/readiness algorithms are unchanged.

`/home/anon/Projects/MinecraftOfficial/runs/attack-charge-readiness-20261022-v1/native-source-facts.json` records all six artifact SHA-1/SHA-256 checks, matching `.complete.json` markers, source hashes and control-flow lines. Its SHA-256 is `79e4e738189e0e64c519cde113c83cb9312573612ecfe4c6ffd915de1d66305a`. The existing original attack-counter fixture remains unchanged. This is source control-flow evidence and actual UMPK session acceptance; it does not claim a fresh complete native client/network run before positioning.

## Runtime acceptance

The durable private archive starts from `53c6f97418d5f377fee5f03338149dee62fc46ec`. Thirty-two new real-session cases cover every protocol 774–777, received/missing terrain, enabled/disabled physics and running/frozen worlds at 40 TPS. Login, selected inventory, full chunk, block updates, ticking state and position correction pass through the bound production codecs. Relevant decoded values and byte-identical re-encodings are asserted, as are emitted action fields and packet order.

The test receives a spear, advances six session ticks before positioning, then invokes the existing mining action. Received terrain permits native charge: the initial item-change phase resets the first eligible tick and the next five charge the action. Missing terrain is the negative control and cannot invent charge; its terrain arrives only after those ticks. The initial action remains charged in frozen worlds because the native player phase is exempt from world freeze. Protocol 777 emits the action with its native local-only swing; 774–776 emit action then swing. Held requests remain active until caller cancellation.

The untouched production code fails sixteen cases with a missing action and passes sixteen controls. The single caller-line correction passes all 32 unchanged cases. Exact commands and logs are `red-commands.json`, `red.trx`, `green-command.json` and `green.trx` in the durable run. The complete Client suite passes all 26,082 cases with no skips; the deliberate baseline increase is 32. Scoped format application and verification pass. `regression-commands.json` records these commands; final immutable repository, consumer, package, AOT and live acceptance of the later combined runtime remains separate.

This closes only pre-position eligibility for the already owned charged-input path. Broader permissions, components, creative restrictions, geometry and the complete mining/version matrix remain in the [completion ledger](interaction-completion.md).

The frozen evidence/source index is `manifest.json`, SHA-256 `218f5b6aa6aa5cff4f6afa46b3a968aba0bf41f0cfa7ddb19a52029cd456dafd`; `binary-manifest.json` binds all 124 test-output files. The source archive and tested output remain durable and unchanged after this freeze.
