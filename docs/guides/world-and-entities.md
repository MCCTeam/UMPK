---
title: World and entities
description: How world and entity state reaches the caller through appliers, client state and events, and what is tracked versus what is not.
sidebar:
  order: 4
---

A packet arrives, gets decoded, and then something has to happen to it. In UMPK that something is an applier: a small class that recognizes a packet type, mutates the session's state, and publishes an event. Your side of that is two things, `client.State` and `client.Events`, and nothing else.

## The pipeline

```text
frame -> codec -> applier chain -> ClientState mutation + event publish
```

The chain is assembled per session from the enabled features. There are ten appliers: connection, chat, self, UI, command tree, block acknowledgement, recipes, and then world, entity and inventory when their feature is on. A disabled feature registers no applier at all, so its packets are not handled rather than handled and discarded.

Every one of those classes is internal. There is no way to add your own applier, and no public interface to implement. If you need to see raw packets, two seams exist: the `PacketReceived` event, which carries the phase, the wire id, the decoded packet object and the payload length, and the `UmpkClient.PacketFrameObserved` event, an `Action<PacketObservation>` over the raw frame feed. Neither lets you change what the applier chain does with the packet.

For behavior rather than observation, the extension seam is `IClientPlugin`. A plugin gets a `ClientPluginContext` with the client, its actions, its events, a command registration scope, a tick-aware scheduler, plugin channel registration, and `TryAcquireMovement`.

## Threading

All mutation happens on the session loop. Reading state from another thread gives you an eventually consistent snapshot, which is usually fine and occasionally not. When you need a consistent read of two related things, marshal onto the loop:

```csharp
Vec3d position = await client.InvokeAsync(c => c.State.Self.Position, ct);
```

`PostAsync` is the same for work that returns nothing.

Event handlers already run on the loop, so a handler reads consistent state for free. The price is that a slow handler stalls packet processing for the whole session. Keep them short, or hand the work off.

## What is in ClientState

Always present, on every version and every feature configuration:

| Property | What it holds |
| --- | --- |
| `Self` | The local player: position, velocity, rotation, health, food, experience, game mode, abilities, effects, item cooldowns, held slot, view and simulation distance |
| `Server` | Brand, keep-alive id and turnaround, observed latency, measured ticks per second, world spawn |
| `Scoreboard` | Objectives and teams |
| `TabList` | The player list, plus header and footer |
| `BossBars` | Boss bars keyed by uuid |
| `Maps` | Map data by id |
| `Advancements` | Advancement progress |
| `ServerCommands` | The server's declared command tree |
| `Recipes` | The unlocked recipe book and the raw recipe registry |
| `Dialogs` | The dialog the server is currently showing (1.21.6+, empty elsewhere) |
| `Chat` | Global chat index bookkeeping and profile key rotation counters |
| `Registries` | The network-synced registries, populated during configuration |
| `Features` | The composed feature toggles for this session |

Three more are feature gated and throw `FeatureDisabledException` when their feature is off: `World` (Terrain), `Entities` (Entities), `Inventory` (Inventory).

`ClientFeatures` defaults every module to on, and the dependencies resolve themselves: pathfinding implies physics, physics implies terrain. So turning terrain off turns off three things, not one.

```csharp
await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    .UseProfile(profile)
    .ConfigureFeatures(features =>
    {
        features.Inventory = false;
        features.Pathfinding = false;
    })
    .Build();
```

`ClientState.World` also throws `InvalidOperationException` rather than `FeatureDisabledException` in one specific case: terrain is enabled but the session has not joined a dimension yet. The two are worth distinguishing, because one is a configuration mistake and the other is a timing one. `HasWorld` tells you which without throwing.

## The world

```csharp
BlockState state = client.State.World.GetBlock(new BlockPos(10, 64, -3));
int stateId = client.State.World.GetBlockStateId(pos);
LightLevels light = client.State.World.GetLight(pos);
BlockEntityData? sign = client.State.World.GetBlockEntity(pos);
ChunkColumn? column = client.State.World.GetColumn(new ChunkPos(0, 0));
```

Also on `World`: `LoadedColumns`, `Border`, `Dimension`, `TimeOfDay`, `WorldAge`, and `CopyRegion(min, max, includeBiomes)`, which takes a `RegionSnapshot` for planning or analysis without holding a reference to live chunks.

`World` has setters too, `SetBlock` and `SetBlockStateId` and the rest. Those exist because the appliers use them. Calling one yourself changes your local copy and nothing on the server, and the next chunk or block update will overwrite it. To actually change a block, use `client.Actions.Interaction`.

### IsLocalChunkLoaded

```csharp
public bool IsLocalChunkLoaded { get; }
```

False both before the first column of a freshly installed world arrives (a join, a respawn, or a dimension change) and whenever the player is outside every loaded column. UMPK combines that terrain signal with the first server placement in one session-owned readiness decision. Through protocol 768, and on protocol 769, movement follows current-column availability. Protocol 770 has vanilla's bounded 60-tick fallback and suppresses a late duplicate `player_loaded`; protocol 771+ becomes ready after the first usable terrain and stays ready across later unloads. Each supported modern session sends at most one `player_loaded` announcement.

Skip the check and the client integrates gravity against a world with no terrain in it, so every dimension change opens with a free fall from the arrival position that the server then teleports back, and those falling positions go out on the wire as real movement.

### World events

`ChunkLoaded` and `ChunkUnloaded` carry a `ChunkPos`. `BlockChanged` fires for single and multi-block updates. `ChunkBiomesUpdated` carries a count. `LightUpdated`, `WorldBorderChanged`, `TimeChanged`, `SpawnPositionChanged`, `DifficultyChanged` cover the rest of the level state, and `BlockEventOccurred`, `LevelEventOccurred`, `ExplosionOccurred`, `ParticleSpawned`, `SoundPlayed` and `SoundStopped` cover the effects.

One detail from the chunk path that is easy to get wrong and expensive to get wrong: a chunk packet that is not ground-up carries only the sections its bitmask names. Installing it as a whole-column replacement deletes every section it omits. Vanilla's server sends exactly that frame when 64 distinct block changes land in one chunk in one tick, for a column the client already holds. UMPK merges those rather than replacing, and both shapes publish the same `ChunkLoaded`, so a subscriber cannot tell how the column was framed.

## Entities

```csharp
EntityStore entities = client.State.Entities;

Entity? byId = entities.Get(entityId);
bool found = entities.TryGetByUuid(uuid, out Entity? byUuid);
IEnumerable<Entity> close = entities.Nearby(client.State.Self.Position, range: 16);
```

Also `All`, `Count`, `TryGet`, plus `Add`, `Remove`, `SetVehicle` and `SetPassengers`, which the applier uses.

An `Entity` carries `Id`, `Uuid`, `Type` (a `RegistryEntry<EntityTypeDefinition>`), `Position`, `Velocity`, `Yaw`, `Pitch`, `HeadYaw`, `OnGround`, `Pose`, `CustomName`, `Metadata`, `Attributes`, `Effects`, `Equipment`, `Passengers`, `Vehicle`, `LeashHolder`, and `PlayerProfile` for players.

The applier handles spawn and despawn, relative and absolute movement, head look, velocity, metadata, effects, attributes, passengers, equipment, item pickup and entity status. It also carries a decade of wire differences: `add_entity` is the spawn-object packet on every version below 1.14, with a type id in an id space disjoint from `add_mob`'s, and the two merged at 1.14. Getting that wrong does not produce a miss, it produces a wrong entity name.

### Entity events

`EntitySpawned` carries the `Entity` itself. Most of the others carry only an id:

```csharp
client.Events.Subscribe<EntityMoved>(moved =>
{
    if (client.State.Entities.TryGet(moved.EntityId, out Entity? entity))
    {
        Console.WriteLine($"{entity!.Type.Id} is at {entity.Position}");
    }
});
```

That is the pattern for the whole entity event family. The event tells you what changed; the store holds the value. `EntityMoved`, `EntityRemoved`, `EntityMetadataChanged`, `EntityStatusChanged` and `EntityPassengersChanged` all work this way. `EntityDamaged`, `EntityHurt`, `EntityEffectApplied`, `EntityEffectRemoved`, `EntityEquipmentChanged` and `ItemPickedUp` add their own fields.

## The local player

`Self` is not an `Entity`. It is a separate `SelfState` with its own shape, because the local player has state no other entity has: `Health`, `Food`, `Saturation`, `ExperienceLevel`, `GameMode`, `MayFly`, `Flying`, `InstantBuild`, `Invulnerable`, `WalkingSpeed`, `FlyingSpeed`, `HeldSlot`, `ViewDistance`, `SimulationDistance`, `LastDeathLocation`, `EnforcesSecureChat`, `CameraEntityId`, `ActiveEffects` and `ItemCooldowns`, alongside the usual `Position`, `Velocity`, `Yaw`, `Pitch`, `OnGround`, `Sneaking` and `Sprinting`.

Its events are `HealthChanged`, `ExperienceChanged`, `GameModeChanged`, `AbilitiesChanged`, `HeldSlotChanged`, `PositionCorrected`, `PredictionCorrected`, `Died`, `Respawned` and `ItemCooldownChanged`.

## What survives a phase change

From 1.20.2 a server can send the client back into the configuration phase mid-session. UMPK follows vanilla's own division of what dies and what does not, because it is a wire consequence rather than a judgement call.

Cleared: the world, all entities, the scoreboard, maps, the tab list with its header and footer, boss bars, advancements, the server command tree, recipes, the inventory, any open dialog, and the spawned flag.

Kept: the registries, the server state including the brand, the cookie store, and the local player's identity. The server does not resend any of it, because on re-entry it queues only the tasks that lead back into the world. Clearing the registries there would leave the client unable to resolve dimension types for the rest of the session.

A session end clears less: any open dialog, the per-connection server state, and the chat-signing counters, which belong to one connection's profile key.

## What is not tracked

- No prediction or interpolation of other entities. Positions change when a packet says so, and `EntityMoved` carries no position of its own.
- Terrain packets that arrive before the join packet are dropped, because there is no world to put them in yet.
- With `ClientFeatures.Entities` off, no entity is tracked at all, including the ones the local player is riding.
- No public applier seam. You can watch packets through `PacketReceived` or `PacketFrameObserved`, and you can act through a plugin, but you cannot replace how a packet updates state.

## Related reading

- [Packet pipeline](/concepts/packet-pipeline) for what happens before the applier.
- [Umpk.Game](/packages/umpk-game) for the world, block and entity model itself.
- [Movement and pathfinding](/guides/movement-and-pathfinding) for the physics that reads this state.
