# Interaction picking runtime

This first connected picker slice makes view-dependent block mining stop when an eligible cow/player intervenes or required context is unknown. It does not attack the entity. Explicit-coordinate mining and the public block raycast retain their existing contracts. Complete native entity picking remains open.

## Ownership and limits

`ClientSessionServices.ReadInteractionPick` combines the existing contextual block cast with tracked geometry on the session owner and returns block, entity, miss or unknown. Both initial view validation and held mining use it. The internal profile retains legacy survival/creative reach, the protocol-47 pick radius, the later zero base radius, the protocol-766 attribute boundary and explicit unknowns for unowned active-item attack ranges. The native box clip retains closed legacy faces and the modern open tolerance boundary.

The first classified participants are cows and tracked players. Item entities and experience orbs are source-proven nonparticipants independently of placement's `BlocksBuilding` flag. Other relevant families, missing geometry/membership, rider positioning and ambiguous native ordering remain unknown. This is incomplete picking coverage; a passing ordinary participant test does not classify the rest of the inventory.

The existing motion owner now tracks unresolved local mount context from bound legacy mount-versus-leash and later passenger packets. Self remains outside `EntityStore`. Observations run before endpoint lookup, and vehicle object identity prevents a removed/replaced numeric ID from falsely proving dismount. An explicit applicable dismount or lifetime reset can clear the context. Same-world player replacement resets only local mount state; world/session reset uses the existing owner cleanup. Native passenger positions, remote legacy relationships and full shared-vehicle filtering remain unfinished.

## Source and runtime evidence

`/tmp/umpk-picker-agent-20261019-v1/native/source-audit.json`, SHA-256 `9013c398e6623d03cfaf1158f3210d0bb44e92dc1063dd95e0c3383ef13e822f`, records verified original client jars, decompile receipts and source hashes for all 44 mapped releases plus 1.8.9, 1.9 and 1.13.2 boundaries. It covers renderer/LocalPlayer selection, Entity/LivingEntity predicates, projectile selection, packet listeners and AABB clipping. This is an original-source audit, not a complete native-client execution campaign. Representative witnesses are `1.8.9-client-decompiled/bfk.java:266`, `pk.java:1392`, `1.9-client-decompiled/bng.java:250`, `rr.java:1565`, `1.20.5-client-decompiled/net/minecraft/client/renderer/GameRenderer.java:827`, `1.21.11-client-decompiled/net/minecraft/client/player/LocalPlayer.java:1202` and `26.3-client-decompiled/net/minecraft/world/phys/AABB.java:393` under the shared oracle.

Thirty-five new cases cover cow/player intervention before and after START on protocols 47/107/766/777, explicit-coordinate controls, local mount packets, missing/replaced vehicles, leash separation, player/world reset and clip boundaries. The initial valid run fails 19 of 23 cases while all four explicit-coordinate controls pass. An unconnected helper retains those failures. A real bound passenger frame then exposes the missing same-world reset. Connecting the caller/reset passes the first 24 cases. The later source-backed clip boundary fails one case and passes three controls; correcting the open modern margin passes all four. Adjacent mining/player-info/view checks pass 544 cases with no skips.

The complete Client suite passes 22,548 tests with no failures or skips on the frozen overlay. Existing view/piercing fixtures initially placed the self player at Y=0 while asserting a hit at Y=64. The shared fixture now supplies a reachable self position; every existing assertion remains. The prior 48 view-fixture failures and later eight piercing-fixture failures remain recorded separately. Initial namespace compile errors also remain harness failures. Cancellation checks wait for the required canceled task with a bounded deadline, preserving the observable result without a 50-millisecond scheduling race.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test /tmp/umpk-picker-agent-20261019-v1/source/tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore -m:2 -nodeReuse:false
```

Both scoped formatter commands use the official SDK as `DOTNET_ROOT` and first on `PATH`. The source manifest records base `6299aad7f64d2e19d64ec92d995223cd33f39cf3`, eight exact overlays, 4,148 unchanged base blobs and the deliberate 35-case addition. Complete repository, consumer, AOT, live targeting and version-matrix acceptance for this later producer remain pending. The separate baseline 1.9 placement-swing discrepancy with distant falling-block entities remains unresolved; this picker change does not close it.
