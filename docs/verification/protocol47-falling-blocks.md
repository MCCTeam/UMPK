# Protocol-47 falling blocks and placement

The existing 1.8.9 client profile now supplies owned falling-block bounds, movement and chunk membership for protocol 47. This repairs a missing placement swing observed on the 1.8.4 server: distant falling gravel made the prediction unknown even though the server placed stone and consumed one item. The placement sender uses the resulting geometry through its existing prediction path.

## Production behavior

`EntityApplier` registers the falling block before publication. `EntityGeometryState` retains its cached box and validity; `EntityMotionState` owns velocity and movement on the existing session clock. `EntityMembershipState` keeps native entity-list participation separate from packet-established chunk availability and spatial-query membership. The source-aware world adapter selects the measured collision query for each supported falling-block profile.

The 1.8.9 constructor retains the exact packet Y. The existing 1.9 profile retains its float-derived offset. Both ignore spawn velocity and accept subsequent velocity packets through the motion owner. Protocol 47 inserts the actor before terrain and ticks against missing-column air; the different 1.9 insertion and reentry rules remain separate. Native movement uses the era's block-query grid and source-proven callbacks. Unsupported callbacks, unowned hard colliders, border context and mounts retain unknown geometry, including after later position or velocity packets.

The protocol-47 release-selection policy remains based on 1.8.9. The earlier 1.8 server observations do not introduce a release-specific client profile. Rider positioning, other falling-block eras and the remaining entity families need separate implementation and acceptance.

## Native and runtime evidence

| Check | Executed result |
| --- | --- |
| Valid focused red | 22 expected failures and eight passing controls on the unchanged base; retained spawn frames reproduce the missing swing |
| Focused final runtime | 34 passes, without skips |
| Adjacent runtime | 2,499 passes, without skips |
| Full private Client suite | 26,084 passes, without skips; 34 deliberate additions over the 26,050-case base |
| Original framed native comparison | Fifteen scenarios and 375 snapshots, including terrain transitions, ID replacement, high/negative Y, explicit teleport, velocity and wall collisions |
| Original liquid comparison | All 128 water/lava state/context combinations match air over twelve ticks; two controls and 3,250 snapshots |
| Original capability audit | All 198 registered blocks, 121 binary entity descendants and 62 registered entity IDs |
| Controlled constructor live pair | Old producer sends use only; patched producer sends use then swing with fourteen captured gravel positions |
| Controlled moving live pair | Fourteen spawns, eleven distinct native removals and three identified surviving actors; old producer misses the swing and patched producer sends it with known moving bounds |

Runtime tests decode real bound packet bodies, assert decoded values and byte-identical round trips, then exercise the packet appliers, entity phases and placement sender. They retain near/intersecting/removal controls, unknown-state persistence and removal, replacement, respawn, reconfiguration and disconnect coverage. Prediction leaves authoritative inventory and terrain unchanged.

The native probe retains the original actor, listener, chunk and world traversal methods. The controlled live pairs clone the preserved failed 1.8.4 world, use all fourteen retained gravel starting positions with new native IDs, and set Time 1/DropItem 0. They omit four dropped items whose nonparticipation was established separately. Terrain remains unchanged. These substitutions are recorded; the runs do not claim exact historical-session replay.

Both valid pairs independently observe native stone placement and stack 32 to 31, exact use/swing order, bounded traffic, byte-identical replay and peer-observed disconnect. All six native runs exit normally and close their owned ports. Client and RCON position samples occur at different phases; they do not establish simultaneous coordinate equality.

Two fixed-delay attempts failed their actor-count preconditions before placement and remain harness failures. The valid moving pair waits for the eleventh distinct native removal and verifies surviving IDs, starting coordinates and a native count. Earlier codec-fixture, empty legacy swing-body and compile failures remain separate from valid red/green results.

## Evidence identity and limits

The frozen private bundle is `runs/falling-block-protocol47-20261022-v1` under `/home/anon/Projects/MinecraftOfficial`, based on `e65329e5021278771610755dccd75ad2d39d7cf6`. Its manifest binds 1,036 files and has SHA-256 `c2f73a5400ae1dc11d49c8f2ff19e30a5c07835d7f9c2e08c656c41867d12482`. The minimal source manifest has SHA-256 `f24de4e6a1e3e65e4f41fd860b6a4937892115f5734355f59694666a86520568`; its patch has SHA-256 `5e7d70f477bba97e939e1c6a1c8112bf338d1b8789ce4374c74a085ef8cb30c9`. The eleven owned files preserve 4,179 other archived blobs. Exact commands, artifact/class integrity checks, binaries, TRX files, captures and cleanup receipts remain outside Git.

The fixture's final framed native trace has SHA-256 `41554514a2a37d2fd6d4e8add0546cf4d4095cad2580713ddd12493b0a2339a6`; the compressed fixture has SHA-256 `7b7eb43e65e61d96155184aafdb42b9860a8bf7c7503cdecd4f8188574da014e`. The initial `new-fixture.json` metadata predates that trace. The separate `runs/falling-block-protocol47-provenance-20261022-v2/verified` supplement corrects it without changing v1, verifies all scenes/snapshots and 33 packet bodies, and regenerates byte-identical gzip output. Its manifest has SHA-256 `55719f505c8b1fde473024b270543fcaf9da0803dfe8fe4c9f46b65309647daa`. A root preflight initially compared the fixture with the older direct-setter trace; the corrected comparison passes. Neither that source-selection error nor the supplement's first packet-index assumption counts as a runtime failure or requires repeating completed tests.

## Root integration

The combined source over `d7ba0c90e5c59dfe0626c88999fae346f5bf5f72` preserves the modern collision adapter while adding the legacy profile selection. All 232 focused modern/charge/falling regressions and all 26,246 Client cases execute and pass. Release solution build and full format verification pass; the existing workspace-loading warning remains recorded. The Client baseline increases by exactly 34.

`runs/falling47-modern-integrated-20261022-v1/manifest.json` has SHA-256 `97ab8dfc3a156fd243cfcf7094c6df9b5aa50211230760ededb7e910748b657c`. It records commands, logs, TRX results, the native fixture comparison and source/binary audit. The audit preserves 4,184 of 4,194 original blobs, records ten modified files and two new files, and identifies all sixteen own producer DLLs, including the meta project. Two audit preflights counted dependency copies or omitted the meta project; both remain harness failures with no source changes or test reruns.

Final combined-producer repository, consumer, package, AOT, CI and version-matrix acceptance remain separate. Unsupported collision context stays unknown. The broader completion ledger remains open.
