# Creative mining item restrictions

This slice implements the observable input behavior when the held item prevents vanilla from removing a creative-mode target locally. START still goes out. The failed local removal leaves the target for the same input phase's held continuation, which emits another swing and reduces the destroy delay from five ticks to four. UMPK completes that captured packet group and its existing write/ack contract; a later explicit request uses the retained delay. The server owns block changes, item effects and the separate `DigOutcome`.

The private producer is `f0a84e3e1c1ff01b828c44bb81ae2fd46d0d8b8c` plus the two production files listed below. It is a tested source overlay, not an immutable Git commit. The source archive SHA-256 is `6bcdf1c11e92da632a2de543cc2f8030682a8e1521bfd17afae774100aa3df98`. Raw sources, logs, packet observations, libraries and receipts stay outside Git under `/home/anon/Projects/MinecraftOfficial/runs/creative-mining-runtime-20261022-v1`.

## Native boundaries and runtime owner

| Profiles | Native local-removal decision | Session behavior |
|---|---|---|
| 47–340 | The controller rejects swords in creative mode. Protocol 47 retains the 1.8.9 client profile. | The first input group is swing, START, swing; later profiles send START, swing, swing. |
| 393–765 | Sword and trident item overrides read the local player's own player-info creative state. | Controller mode and own player-info mode remain separate. Missing known-UUID info is the native false result; an unknown local UUID remains unknown. |
| 766–769 | Sword, trident and mace keep their class override. Effective Tool add/remove does not change it. The pre-770 Tool wire has no creative-breaking flag. | Experimental mace at 766 requires an enabled `update_1_21` feature for the initial click. The default-disabled native case still permits its held-only START/swing group, with delay five. |
| 770–777 | Effective Tool with `canDestroyBlocksInCreative=false` rejects instabuild. Removed/absent Tool and true permit that item predicate. | Abilities determine instant building. Tool patches on an ordinary item have the same predicate as patches on a sword. |
| 393–777 | DebugStick's override always rejects local destruction, including Tool-absent and Tool-true stacks. | Its server-owned debug state editing remains outside the local prediction. |
| 393–760 / 761–769 | Earlier clients look up own player info on each query; later clients retain a found PlayerInfo object. A missing entry is retried. | The cache retains object mutations and survives tab-map removal/replacement; player/controller/UUID lifetime replacement clears it. Completed survival removal attempts can prime the same cache. |
| 47–757 / 758–777 | The initial attack returns void before 758 and local-removal success from 758. | The original held method observes air in the earlier era; the input caller suppresses continuation after a successful local removal from 758. Known failed removal follows the held path in both eras. |

[`InteractionActions.Mining.cs`](../../src/Umpk.Client/Actions/InteractionActions.Mining.cs) owns the captured initial/continued group on the existing session tick. [`VanillaCreativeMiningRestriction.cs`](../../src/Umpk.Client/Actions/VanillaCreativeMiningRestriction.cs) identifies the held item's local rejection and owns the lazy own-player-info reference. Unknown custom item types and unknown local identity return an explicit unknown. The caller adds the extra continuation only for a proven failure whose initial item feature context is enabled. The existing charge/piercing, Adventure, targeting, speed, packet sequence, cancellation and server-outcome owners remain in the call path.

## Source and original-method evidence

The retained audit at `/home/anon/Projects/MinecraftOfficial/runs/creative-mining-audit-20261022-v1` verifies all 50 original client profiles, their artifact/decompilation receipts, and 250 original input/controller method bodies. Its manifest SHA-256 is `acff12309831a57a34c4d6ba80b78c536eac0555eaabf36c0613d35d79c23a2a`; `native-source-audit.json` is `c9033304f43859e65aff16b58c2af04bc9f88ad1e4beee7a6f7dc6b1d5b863f9`; `flow-audit.json` is `981cb1651a75a982527084170d548c13af48220394cd6dc3cc94c19301e6a93b`. A separate own-player-info audit covers every one of the 31 profiles from 393 through 769 (`native-player-info-audit.json`, SHA-256 `6452feceaee717f0568618858729992cd4ffcf57bc5f45d156522362fb3b64ea`).

`native/attempt-14` executes 200 original-method cases at 1.8.9/47, 1.13/393, 1.18.1/757, 1.18.2/758, 1.20.6/766, 1.21.4/769, 1.21.5/770 and 26.3/777. All eight profiles pass. Its immutable manifest SHA-256 is `c2b2ece0f6fd5b36d7ce26dcb3eca553ef3660240fd823c4c5f9b47365daf33c`; the owned helper SHA-256 is `a9e03bce88fe9842684e66d36c8652eab55b8808551c186d8b3213ad39c2de5c`. Native observations include actual packet class/order, START action, face and coordinates, initial return value, held argument, local writes and delay. The independent saved-phase validator also checks every later decrement and START-only retry phase (`native/attempt14-phase-verification/results.json`, SHA-256 `fd5b83f93e766c9796b80908943cc488dd6e776165a6351471d5b437141e12e6`).

The witness retains original Minecraft initial/held methods, game-mode methods, item predicates, LocalPlayer swing, registries and native inventory/abilities/attribute/equipment/fluid state. Its owned world provides one mutable native stone cell, native air and border, and no fire. It captures native packet objects through the listener's send boundary. It omits audio, particles, rendering and world-event output. The outer keyboard loop is source-bound; the helper invokes its original initial and held methods in the recorded order. Primary feature-bearing cases explicitly enable all native flags; separate 766 controls retain default vanilla flags. This is deterministic original-method evidence, with no display, live server or timing claim. All owned Java/compiler commands exited normally, and no seed or sibling checkout participates.

Native setup failures remain in attempts 01–12: missing matching client libraries, reflection ambiguity, adapter class-loader access, file-URI spelling, block-reader selection, an obfuscated package-name collision, and missing original prediction/inventory/audio/fluid/entity-ID constructor state. Attempt 13 passed the earlier 162-case inventory; attempt 14 adds the DebugStick override controls. Matching official client libraries were reused where hashes matched and otherwise provisioned into this private run from the release's pinned metadata. Original client class bytes were compared with every reused unsigned client jar. No failed attempt counts as acceptance.

## Framed runtime acceptance

The 335 cases below exercise the interaction owner with bound packet appliers and its scheduler. The separate [eight actual client-session cases](creative-mining-session.md) drive framed login/play and the real session clock; both layers retain their own red/green receipts.

[`CreativeMiningRestrictionsRuntimeTests.cs`](../../tests/Umpk.Client.Tests/CreativeMiningRestrictionsRuntimeTests.cs) adds 335 cases. Incoming mode, abilities, own player-info, inventory/components, feature flags and block updates use real bound codecs with asserted decoded values and byte-identical round trips. Outgoing actions/swings/punches are decoded and round-trip identically. The fixture supplies a known stone column and session-owned scheduler; it does not simulate local terrain removal.

| Cases | Observable acceptance |
|---|---|
| 140 | Sword across all 50 profiles, plus applicable trident, mace and DebugStick profiles; exact first packet group and no autonomous request after public completion. |
| 144 | Tool add/remove/false/true interpretation on ordinary, sword and DebugStick stacks at 766/769/770/777, crossed with controller mode and instabuild abilities. |
| 15 | Known own UUID with missing, survival or creative player info, independent of creative controller mode. |
| 7 | The next explicit survival request inherits four delay ticks after a failed creative local removal. |
| 8 | Direct versus cached own-info lookup, removal/replacement, completed-survival cache priming, and player lifetime replacement. |
| 4 | An unknown local UUID does not create a known creative-info result. |
| 10 | Caller cancellation after committed START finishes the captured group; session replacement cannot leak old-session writes. A scheduler barrier makes cancellation selection deterministic without changing its assertions. |
| 5 | An applied server block-update packet produces `NotBroken` independently of completed action writes/acks. |
| 2 | Default and packet-established vanilla-only feature context preserve the 766 disabled-mace held-only packet group. |

`focused-red-v9` executes all 335 cases against the original mining owner: 212 expected packet/delay/cache failures and 123 passing controls, with no skips or harness failures. The classifier alone also retained the session failures. The initial connected production run resolved the packet/delay failures; its remaining strict cancellation assertions exposed an existing fixture race. The final fixture waits until the public caller has queued cancellation cleanup before releasing the committed START write, so it retains the strict cancellation and exact packet assertions. Earlier compilation, identifier, teardown, feature-context and cancellation attempts remain separate raw receipts.

The focused final run passes all 335 cases with zero skips. The adjacent mining/charge/piercing/picking run passes 1,108 cases. The unfiltered Client run passes 27,071 cases with zero skips (26,736 original plus 335 deliberate additions). Final-source scoped format and full-Client receipt names are recorded in the delivery manifest. The first format check found only new-test import/indentation issues; dotnet format corrected that file. Separate checks use the owning Client and Client.Tests projects so referenced-project skipping cannot omit the production files.

Commands and exact source hashes are in each `*-command.json`. Tests use the retained .NET SDK 10.0.401 with Release, `-m:2 -nr:false --no-restore`, explicit focused/adjacent filters and an unfiltered full Client command. `archive-source-comparison.json` compares all 4,194 original regular archive entries: only the owned mining file differs, and 4,193 remain identical. Dataset, generated, fixture, API and baseline files remain unchanged. The final delivery records the two added code files, this report and the minimal mining-owner patch.

## Remaining combined-producer checks

This slice does not claim final live server, DMCBK/MCC, package, Native AOT, complete solution gates or published-head CI results. Those checks must use the merged immutable producer. Fresh consumer/sibling snapshots and the unchanged AOT budget remain required there. The 75-release basic acceptance pack and unrelated item/menu interaction families remain separate ledger work. No public API, dataset value, feature axis, dependency, DI lifetime, version or test baseline changes belong to this delivery.
