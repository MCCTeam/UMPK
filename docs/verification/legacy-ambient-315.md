# Protocol-315 bat and squid placement geometry

The protocol-315 adapter connects native bat and squid position, cached bounds, velocity, pending position interpolation and world-query membership to the existing placement caller. This is the bounded 1.11 client profile. Protocols 210 and 316 retain their previous unknown ambient geometry. No public API, dependency, version, DI lifetime or dataset changes are involved.

The original live 1.11 door failure remains open until a fresh exact-producer server run passes. Its native server placed stone and changed the stack from eight to seven, but UMPK omitted the expected swing after iron-door PASS. This change supplies the missing ambient geometry through production owners; it does not count a replay as a new native-server pass. Unclassified movement context can still make the prediction unknown.

## Ownership and behavior

`EntityApplier` registers the geometry, membership and motion adapters before applying initial metadata or publishing spawn. Protocol-315 mob spawn velocities use native float division by 8000. Packet-coordinate bases, observed position, pending interpolation target/count, velocity and cached bounds remain distinct. The existing `EntityMotionState` owns interpolation and velocity; the existing session tick remains the only locomotion clock.

The bat constructor uses width 0.5F, height 0.9F and health 6; squid uses width/height 0.8F and health 10. A hanging bat's direct Y assignment after the inherited phase preserves its earlier cached box. An actual position setter rebuilds that box. The adapter preserves native float-before-double arithmetic and does not rebuild the cache from the latest observed position during a query.

Squid travel uses the measured air, stone and liquid collision slice, native Y/X/Z clipping and velocity cancellation. Its inherited fluid-current acceleration is disabled. The bat's inherited push callbacks are disabled; nonhanging bat fluid sensing occurs before interpolation. Unclassified callbacks/hard peers, potentially better step alternatives, border transitions, unresolved local body/mount/push state and unsupported actor contexts remain unknown. This is a placement-position/bounds slice. Complete yaw/pitch parity, squid water-orientation updates and general AI are outside its evidence.

Membership keeps entity-list presence, Added, registered XYZ and actual section presence separate. The client `allowEmpty=true` override is permissive, and insertion into its EmptyChunk fallback is a no-op. A previously added actor can therefore continue ticking across an absent column with old registered coordinates and no actual section. Source-backed packet tests cover this transition and registration after terrain arrives. No universal radius-32 gate is added. The earlier proposal to clear Added at this boundary was withdrawn after following the client override; it was never implemented.

Living status 3 sets mob health zero without invoking the remote-player death launch or resize path. Default health, metadata kind validity, positive-health reentry and native nonpositive comparisons remain distinct, including NaN behavior. A known twentieth eligible death tick removes query participation. These death/metadata boundaries are source-backed runtime tests; the reused native campaign contains healthy actors only.

## Unknown movement and chunk transitions

Native living interpolation precedes virtual travel. If interpolation completes its deterministic phase but later travel becomes unknown, the stored target/count retain that completed phase; the focused proof observes count 3 becoming 2. The public complete position, cached box and velocity retain their last proven complete state. Subsequent ticks cannot treat that retained position as a proven interpolation starting point. Later target and packet-base updates do not restore full position knowledge.

Independent review found a real defect in candidate v2. After unknown travel crossed a column boundary, post-phase registration used the retained old public position. Unloading that old column then produced false query membership. The placement caller skipped the entity before consulting its unknown geometry and incorrectly sent use plus swing. A synchronized actual-session test failed on those two outgoing packets; the identical no-ambient control passed.

Candidate v3 invalidates registration confidence atomically with unknown ambient geometry. List/Added/registered/section values remain explicitly last-proven snapshots. Column load/unload, post-phase registration and reentry cannot use those snapshots as current registration evidence. Query membership remains unknown, and unproven tick eligibility cannot advance death lifetime. The regression preserves use-only behavior through unload/reload, later target/velocity/health/status packets and twenty session ticks. Explicit removal restores clear placement; a new actor with the same network ID receives known constructor geometry, health and lifetime. All healthy native replay rows still require known registration and exact measured values.

## Reused native and captured evidence

The retained native capture is `runs/mob315-native-measurement-20261023-v2/native.json`, SHA-256 `c6a2611c7895a5b96acc9d5016b3ef773b822371eaf732acc16e3057a113588e`. It contains 31 original healthy scenarios, 250 world ticks and 1,432 observations. The strict supplement at `/tmp/mob315-native-semantic-review-20261023-v2` has manifest SHA-256 `587f02230764222812c069c3dc2a59026abb130492ffd35bee20ccc99f01f0d6`; its 101 corruption controls reject. The original driver remains failed because of its incorrect skipped-entry assumption. Compiler/native exit zero does not change that validator result.

The compressed test fixture reuses those original rows, 900 native packet bodies, forty controlled block inputs and five frames from the original failed door session. Its SHA-256 is `8243dd1b00fb1847365e30de4207ab8cf94f5c85dbc841a81957bed92e993559`; the original door capture is `9697f79aec0e9c413fb193265ffaa7af2b1475b347f4bee80913aed36fb5f384`. No traffic was re-recorded and no generated pin was rewritten. Native block-state IDs are explicitly converted to the different legacy wire-state encoding before the controlled block updates are round-tripped.

The runtime replay checks raw64 position, velocity, cached bounds and pending target; exact interpolation count and packet base; list/Added/Removed; registered XYZ and actual section list; constructor health and on-ground at corresponding owner boundaries. Removed identities lose their owners. Original callback entry/exit and age evidence remains in the strict native reader; the C# replay does not claim native callback hooks or an entity-age API. Packet tests use bound codecs with decoded assertions and byte-identical round trips. Actual framed sessions exercise public placement, near/far geometry, explicit unknowns, removal, replacement, respawn and peer-observed disconnect.

## Candidate validation and limits

Frozen candidates v1 and v2 remain unchanged at `/tmp/mob315-runtime-implementation-20261023-v1` and `-v2`. Their successes, compile/import failures and failed attempts retain their original producer identities. V2's first full run exposed missing Spawn-to-Tick synchronization and an unsupported iron-door squid movement scene. The known near/far session proof now synchronizes the spawn before ticking in the measured stone scene. A separate synchronized iron-door case preserves unknown movement through the public caller; the original captured door cases retain their original swing expectations.

Candidate v3 is an exact `5fd6b08c518cb005faf89bfaecbf3ffa4a222a79` archive plus fifteen reviewed files, retained at `/tmp/mob315-runtime-implementation-20261023-v3`. Its full patch SHA-256 is `9f2231a51ac79ff78778c4fb6e73c436dbf61ab4653718c51ae3a4ee24299dfd`; the six-file v2-to-v3 delta is `7feb25018ef4241e095c2c0b3c2a975b675111c3db2d83ef50277c741105bdd5`. The 35-file frozen manifest is `3ad1d209031e7e346ac42b05c02a4328828b98660973e955bfae01c917b4bb24`; report is `34d54e5be99520af408804ec03792a11cec2fe7368102479cf2482c501a3aa43`; the eleven exact command records are `0a8335d383a440787f8c91cd3d3733e5fa903ad0840cf68220a4e18de1fa1392`. Source/decompile line hashes, raw TRX, logs, failed attempts and source/binary inventories remain outside Git.

| Private candidate check | Executed result |
| --- | --- |
| Captured caller, native replay, owner and real-session tests | 71 passed, zero skipped |
| Adjacent player, falling-block, teleport and lifetime tests | 816 passed, zero skipped |
| Complete Client suite on its exact private base | 30,301 passed, zero skipped |
| Normal Integration on its exact private base | 41 passed, sixteen named skips, 57 total |
| Scoped format | Exit zero |
| Isolated Native AOT | 25,153,152 bytes, strictly below 25,165,824 by 12,672 bytes |
| AOT smoke | Exactly one executed, passing test |

The v3 AOT executable SHA-256 is `63092874b0b06977ce6005b2e5f5a14fe6721aec2093edaa443666bd03b56b00`. Its isolated publish preserves all 224 then-protected normally tested managed artifacts; the final inventory separately contains 348 built DLL/PDB files. These private counts do not replace merged counts: the modern slab additions and newly maintained slab live theory have separate baselines. No new Java, server or native Minecraft measurement ran for this implementation.

Root applied the exact v3 source over `e6f5f9d`, including the separate slab fixture, and independently passed 2,415 focused and 32,645 complete Client cases with zero skips. The deliberate Client baseline increases by 71 from 32,574 to 32,645; Integration remains 58 with seventeen normal opt-in skips. Root rehashed all fifteen shared source files, all 35 private manifest entries and five external pins, then checked actual TRX outcomes and the read-only delivery verifier. That verifier confirms all 4,259 source pairs, 348 final managed artifacts, strict AOT, native/source pins and unchanged frozen v1/v2 evidence. Root results remain at `/tmp/umpk-ambient315-root-integration-20261023-v3`. Full immutable repository, consumers, package, AOT, actual-head CI and native Door315 acceptance still require the final merged producer.

Independent final review is `/tmp/mob315-runtime-v3-independent-review-20261023-pv1on4o3/review.json`, SHA-256 `5c3ebc8af94c9f5dee11d9717cd14ba5efbcf911c0cd693e968ac1cacc5dbb49`, with manifest `689eb13eb44bb1de27487829051b2d0338c4260c47a70e682da1c4d85b85bb04`. It verifies the frozen delivery, source and binary pins, exact private outcomes and named skips, resolves the reproduced false-clear blocker, and approves only the bounded placement slice described here.
