# Immediate carried-item synchronization

The existing selected-slot cache now synchronizes before immediate block use, air use and explicit entity attack, without waiting for a session tick. The five-file implementation preserves input admission, native controller rejection, packet order and the captured session. Thirty focused framed-session cases pass, followed by a combined producer check with the separate [Light-outline change](light-outline-knowledge.md). This is bounded managed acceptance, followed by the matched full Client run below. Fresh Native AOT, native-server, consumer and final repository gates remain separate.

## Behavior and source boundaries

The existing `InventoryActions` cache remains the single owner of the last successfully sent carried index. `ClientSessionServices` connects immediate actions to that owner through `UmpkClient`, which captures a concrete packet sink and verifies the old session before and after synchronization. Block use, air use and attack finish an admitted packet group on that captured session. Caller cancellation before admission writes nothing; cancellation at the carried write does not omit the remaining committed action or swing. A failed carried write prevents the action. The queued reconnect case verifies that an old-session attack cannot reach the new peer.

Block input rejects a known disabled item or unsupported hand before synchronization. Controller border and legacy placement rejection follow synchronization and may therefore send only the carried index. Full prediction and the existing cake/candle mutation run after synchronization without an intervening await. Unknown prediction retains its existing packet-only behavior. Spectator air use returns before synchronization. Air use sends the native extra position/look report only on protocols 755–766, between carried synchronization and the use packet; this does not advance ordinary movement cadence. Protocol 767 and later retain look in the use packet.

Original client ordering is pinned at protocols 47/315/404/477/765/769/770/777 in `.scratch/carried-item-immediate-action-source-audit-20261029-v1/NATIVE-ENTRY-PHASES.json` (SHA-256 `1c14ff6b215f8ba4792266552207be5ef4746e4029d1c41caf9cf303cfc23625`). The independent `AIR-USE-POSITION-BAND-ALL75-SOURCE.json` in that directory covers all 75 named releases and establishes the 755–766 band (SHA-256 `a4cb5ae20392c3daa5175b633f3c1b136c055f57040b86fee07b1a5371e32b08`). Its jar, artifact metadata, completion-marker, class and source pins remain external. New framed cases execute protocols 47/754/755/766/767/770; source coverage is not additional native execution.

Explicit attacks retain their existing bound packet route and reset charge after a successful action send. This change adds no graphical input policy, hand fallback, target picking or combat simulation. `InteractEntityAsync` is unchanged. Protocol 777's mandatory relative interaction location and changed Interact wire shape remain a separate source-backed gap, recorded in `WIRE777-GAP-SOURCE-EVIDENCE.json` (SHA-256 `fab5de32744f8bfa27a9e61dc6d0a675c1fba5ec60d51751c938022320069733`). No public API, dataset, dependency, DI lifetime, timer or second cache changes.

## Executed results

| Producer step | Executed result | Meaning |
|---|---|---|
| Original first attempt | 6 causal failures, 2 controls, 2 fixture failures | Whole attempt failed; the fixture failures are not causal regression evidence. |
| Second first-RED attempt | 7 causal failures, 2 controls, 1 fixture failure | The disabled-input case incorrectly expected a controller tick while no world existed; whole attempt failed. |
| Corrected baseline | 7 causal failures, 3 controls | All ten intended rows execute with zero skips; the remaining twenty are not run at this stage. |
| Profile, services, then client wiring | Each retains 7 failures and 3 controls | Three cumulative single-file compiles do not yet connect the action callers. |
| Action caller | 3 failures, 7 passes | Only the three block-use failures remain. |
| Placement caller | 10 passes, then 20 further passes | All thirty focused cases pass on the completed five-file producer. |
| Joint immediate/Light producer | 30 immediate +9 Light +8 scaffolding +1,495 adjacent passes | Exactly 1,542 distinct passing displays; zero failures or skips. These are selected cases, not a full-suite total. |

The same focused Test DLL `e3d85848264b9b0c00b56a7e80c6447cdd18e3c44e4fd8ba686d21ed94c9fe19` and PDB `d97c40491b0b038c37251ee3d348980662ea52b7f996d261af9e14d9995c4bc2` are reused unchanged through the staged and joint runs. Its compiler used the historical Client `1ca23817`; runtime overlays are identified separately. Each Client compile verifies 221 authored sources, three generated sources, all 224 PDB documents and 182 references. The five stages change one production file each and retain all other 216 authored sources. All 44 observed owned PIDs are independently absent after the staged run.

The joint Client DLL is `eeeaaefce47801b47f89d07f9f9baf6579adc0b2d817f5061c6339436862a4d0`, with PDB `90990c78abae3fb7323f7c2ab5a87b7439d28ed5cf15250c02ac58e78bc7ae07`. It contains exactly the five immediate-action postimages and the Light Picker postimage; the other 215 authored Client sources remain unchanged. The 1,495 adjacent cases retain source-qualified selected-slot, automatic EOF, hand/cursor/packet-group, cake/candle, capability and lifecycle checks. All eighteen observed owned PIDs are independently absent. The private transport fault before buffer advance and externally supplied session scheduler are controlled test substitutions; these receipts do not establish native enqueue-failure behavior or a complete native client loop.

## Receipt and command index

Raw commands, arguments, compiler inputs, TRX files and cleanup stay under `/home/anon/Projects/MinecraftOfficial/runs/`. The staged run is `carried-item-immediate-action-staged-runtime-20261030-v1`; its `configuration.json` SHA-256 is `2f076c3918eeba0296cce708bbe1ae51220d1baac766dc03418492bf5ec1ad79`. The joint run is `carried-item-immediate-light-joint-runtime-20261030-v1`; its `configuration.json` SHA-256 is `4770ab39cd27f1c27752de365e48b79ec6222c5a2bf52aac4b47581f3fe2acbd`. Those configurations and the actual bindings retain each exact build/test command and selected test identity.

| Independent root receipt under `.scratch/` | SHA-256 |
|---|---|
| `immediate-carried-test30-firstred10-genuine-root-audit-20261030-v1.json` | `3c53061b811a04d1eeeaa63138eb7a4a0df5c7a96bade08f0e087b6db0d76377` |
| `carried-item-immediate-five-stages-actual-root-audit-20261030-v1.json` | `58dd3e0c17ffc7a3af659c326ed9667b400f6685e3e1455d74bbc60eef9ae0d6` |
| `carried-item-immediate-light-joint-actual-root-audit-20261030-v1.json` | `a9f865ee412faac90e67c0c850259fceec9e7cb23ac5d17dcec96954a77d3539` |
| `immediate-light-format9-root-audit-20261030-v1.json` | `61bf767372c4902071e747fd1d696ee5403d5dce09fad8e7c8efb331d97c5e9c` |

The nine promoted C# source/test/helper files pass the full-solution formatter selection with zero byte changes, using SDK 10.0.401 and `dotnet format UMPK.sln --no-restore --include <the nine files> --verbosity normal`; `.scratch/run-immediate-light-format9-20261030-v1.py` records the complete argument list. The original formatter audit's released-cgroup reader failed before sending any signal; the corrected reader accepts the same formatter execution without repeating it. The original user report and raw logs remain unchanged.

The source-derived Client baseline is deliberately 35,780: 35,741 preceding cases plus thirty immediate and nine Light cases; the seventeen-suite expected total is 49,649. The matched full Client continuation below now executes all 35,780 cases. Shared promotion and zero-change formatting preserve the accepted joint source bytes. Fresh AOT, native, consumer, package and final-producer CI acceptance remain separate. Earlier full 35,733/AOT and published-head CI receipts retain their original producer scope.

## Matched full Client acceptance, 2026-10-30

One fresh Test-only compile against the exact joint Client `eeeaaefc` passes all 35,780 Client cases, with zero failures, errors or skips. The literal result inventory has 35,656 distinct display names and preserves all 124 historical duplicate occurrences. The full run contains the thirty immediate, nine Light and eight scaffolding cases; their earlier focused totals are not added again. All 352 authored Test sources, the SDK entry, three generated sources, 356 PDB documents, 198 references including eleven producer DLLs, and all 54 compiler/embedded resources match their recorded inputs. Root independently compares the saved 352 Test and 221 Client sources with the shared working tree before launch. Canonical fixture checks cover all 127 paths and sidecars before compilation and execution.

The actual Test DLL SHA-256 is `9f183fe3e2fdd9fbc67f95ec27820a5991716a9040bf157b5ac0377844ceff44`; its PDB is `edd9f12221b33ebeb0ff8faaa16cb48c07d077593f03b4a50e980c3ea7094987`. The full suite takes 306.19 seconds, the whole job 321.94 seconds, and observed peak memory is 3,842,027,520 bytes under the unchanged 4 GiB cap. No OOM event occurs. All 126 runtime files remain unchanged, all ten observed owned PIDs are absent, and the owned unit is stopped. No producer, Protocol, dependency, helper, AOT, native-server or restore work runs. The final seventeen-suite log path remains untouched.

Raw commands, results and cleanup remain in `/home/anon/Projects/MinecraftOfficial/runs/inventory-client35780-full-noaot-runtime-20261030-v1/`. Actual binding `.scratch/carried-item-light-full35780-preparation-20261030-v1/ACTUAL-FULLCLIENT35780-NOAOT-BINDING.json` has SHA-256 `7c56a3e644422de45235ed6360a4a27f0977b8c44d0006e1b32cb278d341d424`. Independent root audit `.scratch/immediate-light-full35780-actual-root-audit-20261030-v1.json` has SHA-256 `ef38ee8e294616c4131e70c32cb15372739b88ac8a144c88976174c662e76017`; it rechecks every prerequisite pin, source/PDB/reference/resource closure, raw TRX count and literal inventory, runtime file and process absence. This is a full Client result, with final seventeen-suite, AOT, consumer and release acceptance still separate.
