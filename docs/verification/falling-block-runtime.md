# Falling-block placement geometry and motion

The protocol-107 placement caller now uses an owned 1.9 falling-block box, movement and chunk membership. A proven distant actor no longer forces the block-use result to unknown merely because the generic geometry table lacks its moving box. An intersecting actor still prevents a predicted successful placement swing. Unknown motion or collision context stays unknown. Other falling-block eras remain outside this bounded implementation.

## Production path

`EntityApplier` forwards the spawn object's block data before membership registration and spawn publication. `EntityMotionState` owns the native constructor velocity and subsequent movement; `EntityGeometryState` owns the cached box and its validity. `ClientState.TickEntities` supplies the existing world, block shapes, membership and entity store on the existing session clock. The placement obstruction scan consumes those owned bounds and the independent native world-query membership result.

The measured constructor uses `.98F` dimensions and float arithmetic for its initial Y offset, and ignores the spawn packet's velocity. Later velocity packets feed the same motion owner. Each eligible tick applies native `.04F` gravity, Y/X/Z clipping and `.98F` drag, preserving cached-box translations separately from explicit position setters. The first tick removes a matching source block as the client does. Server-owned drops and item effects remain outside the implementation.

Membership uses received columns, native insertion and the existing reentry owner. A loaded center column is sufficient for this actor; no universal radius-32 gate is imposed. An actor that crosses into absent terrain after being added keeps ticking but leaves the spatial query until native membership is restored. Chunk replacement, unload/reload, removal, replacement IDs and world/session teardown retain separate paths. Direct changes to the public observed position refresh the box without reviving previously unknown motion.

## Native and runtime evidence

| Check | Executed result and scope |
| --- | --- |
| Original 1.9 client trajectories | Fifteen scenarios and 375 snapshots, including all movement axes, floor/walls, late/missing/unloaded terrain, crossing columns, initial block removal and negative/high Y |
| Original reentry traversal | Thirteen actors across two world phases; native HashSet bucket order, growth and ten-entry phase cap |
| Block-class audit | All 214 registered block classes examined; 81 inherit the proven fixed cube and relevant callbacks |
| Original class integrity | All 2,905 examined client classes match the retained unsigned jar byte for byte |
| Valid received-terrain red | Seven expected failures and three passing controls on untouched producer `412e29d` |
| Final focused runtime | All 32 cases executed and passed, without skips |
| Complete isolated Client suite | 23,398 executed passes on the `412e29d` overlay; full solution format verification exits zero |

The packet-runtime cases execute real bound codecs, assert decoded values and byte-identical round trips, then exercise the production packet appliers, entity phase and placement sender. Placement cases independently assert the use/swing group, unchanged predicted world/inventory and near/far/removal controls. Additional cases preserve unknown contexts after later position/velocity packets and verify removal, replacement, respawn, reconfiguration and disconnect cleanup.

The native harness retains the actor, listener, chunk, collision, world traversal and reentry methods. It suppresses the ambient block phase, allocates a minimal Minecraft placeholder and excludes local-player ticks/audio. Those substitutions prove the stated remote actor path; they do not establish local-player physics. Earlier invalid block-update-only placement fixtures, compile failures and a first connected query-phase expectation failure remain recorded separately from valid red/green results.

## Collision limits

The source-aware world adapter supplies the measured 1.9 block query and proves nonparticipation for the audited soft peer types. Placement obstruction, picking geometry and native hard collision remain separate predicates. Unowned boat/Shulker bounds, custom types, mounts, unsupported block callbacks or shapes, border transitions and invalid/extreme coordinates retain unknown state. This slice does not enable modern entity collision or modern position controllers, and its adapter must not be cited as complete modern collision-context acceptance.

The [hard-collision classification](hard-entity-collision-classification.md) records the separate source audit. Native 1.9 source-null queries inspect blocks only; source-aware peer predicates differ. The implementation does not substitute a block-building flag for a hard-collision predicate.

## Evidence and remaining acceptance

The frozen implementation evidence is `runs/falling-block-runtime-20261021-v1` under `/home/anon/Projects/MinecraftOfficial`. Its 302-file manifest has SHA-256 `4cd7a68f58d6ec0f83e1caeeb80eb2169ba942cf9598dbfbd3a7d8ed2803a033`. It retains native bindings/probes/observations, valid and failed runtime attempts, exact commands/TRX results, reviewed source and binary hashes. `source-overlay.json` binds six modified and four new files over `412e29d5fa1802205b80ed83661ca1634d9715b6`; 4,148 base regular files and twelve symlinks remain unchanged. Integration changes only the context-argument call in `ClientState`, preserving the separate local-player lifetime hook.

The combined root archive over `3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb` passes the same 32 focused cases and all 23,871 Client cases, without skips. Full solution format verification exits zero with the existing workspace-load warning. The deliberate Client baseline increases by exactly 32. `runs/falling-integrated-20261021-v1/manifest.json` retains eleven evidence files, SHA-256 `29ed955753c49e291b6556bf2290d65891ca608598f6e86940fac1c13335f118`, including exact commands, source/binary hashes and TRX results; 4,162 other base blobs remain unchanged. These results cover the combined legacy hoe and bounded falling-block implementation, not later cooldown, shovel or liquid-context changes.

The original live failure contains 268 falling blocks and one item. Its server observed successful placement and a stack decrease from 32 to 31, while the client omitted the required swing. The retained capture starts after initial terrain and omits the initial chunk prefix and exact client tick phase. A reconstructed actor replay alone cannot establish exact historical-session parity. A controlled repeat using a private clone of the preserved world remains required, together with combined producer, consumer, package, Native AOT and published-head CI acceptance. The original failure remains open until that comparison executes.
