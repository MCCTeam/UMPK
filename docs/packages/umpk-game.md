---
title: Umpk.Game
description: The game model: blocks, chunks, entities, items and components, containers, scoreboards and registries.
sidebar:
  order: 5
---

`Umpk.Game` is everything Minecraft has that is not a packet. Block states and chunk storage, entities and their metadata, item stacks and the 1.20.5+ data components, container layouts and click simulation, tab list, boss bars, maps, advancements, scoreboards, and the registry machinery that ties network ids to identifiers.

It is the largest public surface after [Umpk.Protocol.Java](/packages/umpk-protocol-java), and it is deliberately passive. Nothing here sends or receives anything. The client applies decoded packets into these types; you read them.

## Its place in the stack

`Umpk.Game` depends on [Umpk.Core](/packages/umpk-core), [Umpk.Nbt](/packages/umpk-nbt) and [Umpk.Text](/packages/umpk-text). [Umpk.Protocol.Java](/packages/umpk-protocol-java) uses it as the decode target, [Umpk.Physics](/packages/umpk-physics) reads block states and shapes out of it, [Umpk.Pathfinding](/packages/umpk-pathfinding) plans over a snapshot of it, and [Umpk.Client](/packages/umpk-client) owns the live instances.

## Main entry points

Registries first, because everything else refers to them. `Registry<T>` maps a network id and an `Identifier` to a value, in both directions, and hands back a `RegistryEntry<T>` that carries all three. `RegistryAccess` groups the registries a session needs: `Blocks`, `Items`, `EntityTypes`, `MenuTypes`, `Biomes`, `DimensionTypes`, `Enchantments`, `MobEffects`, `Attributes` and `ChatTypes`. `RegistryBuilder<T>` and `RegistrySnapshotBuilder` build them; `RegistryIds` has the well-known registry identifiers as constants.

`Umpk.Game.Blocks` has `BlockState`, a readonly struct that pairs a state id with an `IBlockDataSource` and answers `IsAir`, `IsSolid`, `IsFluid`, `IsClimbable`, `IsWaterlogged`, `BlocksMotion`, `Friction`, `SpeedFactor`, `JumpFactor` and the block's property names and values. `BlockFlags` is the underlying bitmask.

`Umpk.Game.World` has `World` (chunk columns keyed by `ChunkPos`, block entities, the world border and the dimension), `ChunkColumn`, `ChunkSection`, `DimensionState` (height, minimum Y, section count, skylight), and `RegionSnapshot`, a detached copy of a box of blocks that [Umpk.Pathfinding](/packages/umpk-pathfinding) plans against. `Raycast.CastBlock` and `Raycast.CastEntities` do line-of-sight queries.

`Umpk.Game.Entities` has `Entity` (position, rotation, velocity, pose, effects, attributes, equipment, passengers and vehicle) and `EntityStore`, which indexes entities by id and by UUID and can list `Nearby`. `EntityMetadata` holds the raw indexed metadata; `EntityMetadataKeys` gives you the semantic handles (`Health`, `CustomName`, `Pose`, `SharedFlags`, `AirSupply` and more) that resolve to the right index for whichever version you are on.

`Umpk.Game.Items` has `ItemStack` (immutable, with `WithCount`, `Grow`, `Shrink`, `With` and `WithComponents`), `DataComponentMap`, and `DataComponents`, a static table of every modelled component type. `ItemStack.Empty` is the empty stack.

`Umpk.Game.Inventory` has `ContainerView` and `ContainerSnapshot` for container contents, `SlotLayout` and `SlotRole` for what each slot means, and `ClickSimulator.Apply`, which predicts what a click does before the server confirms it. `MerchantOffers` covers villager trades.

`Umpk.Game.Players` and `Umpk.Game.Scoreboard` cover the tab list, boss bars, maps, advancements, objectives and teams.

## Example

```csharp
using Umpk;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Data.Java;

// The version's static registries. See the Umpk.Data.Java page for what is and is not in here.
RegistryAccess registries = JavaGameData.Registries(protocol: 770);

// Look up an item by identifier, then build a stack of it.
if (!registries.Items.TryGet(Identifier.Minecraft("diamond"), out RegistryEntry<ItemDefinition> diamond))
{
    throw new InvalidOperationException("This protocol has no minecraft:diamond.");
}

var stack = new ItemStack(diamond, count: 16);
Console.WriteLine($"{stack.Item.Id} x{stack.Count}, network id {stack.Item.NetworkId}");

// Predict a shift-click without touching the network. A two-slot toy container:
// slot 0 is storage, slot 1 is the player hotbar, and shift-click moves between them.
var snapshot = new ContainerSnapshot([stack, ItemStack.Empty], ItemStack.Empty);
var layout = new SlotLayout(
    roles: [SlotRole.Storage, SlotRole.PlayerHotbar],
    quickMoveTargets: [[new SlotRange(1, 2)], [new SlotRange(0, 1)]]);

ClickResult result = ClickSimulator.Apply(snapshot, new ClickAction.QuickMove(0, MouseButton.Left), layout);
foreach (SlotChange change in result.ChangedSlots)
{
    Console.WriteLine($"slot {change.Slot} -> {change.Item.Item.Id} x{change.Item.Count}");
}
```

## Things that catch people out

`Umpk.Game` declares `IBlockDataSource` but ships no public implementation of it. The one the client uses is internal and is built from the version's block registry. [Umpk.Data.Java](/packages/umpk-data-java) does not supply one either. So you cannot construct a standalone `World` without writing your own `IBlockDataSource` (the test suites each have one). If you want a world, take the one on a live session at `client.State.World`.

`RegistryAccess.Attributes` is deliberately empty on every protocol, and so are `Biomes` and `DimensionTypes` when they come from `JavaGameData.Registries`. Biomes and dimension types arrive as config-phase registry data at runtime. Attributes stay empty because `AttributeDefinition` needs default, minimum and maximum values that no vanilla registries report carries, and a zero-filled definition would clamp every attribute to zero. An empty registry means "not resolvable here", never "does not exist".

`ChatTypes` and `LegacyObjectTypes` on `RegistryAccess` are nullable. `LegacyObjectTypes` is present only on pre-1.14 protocols, where `add_entity` resolves against a second, disjoint id space. Its presence is the signal, which is what `RegistryAccess.HasSplitEntityIdSpaces` reports. Do not assume an entity type id means the same thing in both spaces, because it does not.

`BlockState` is a struct that holds a reference to its `IBlockDataSource`. A `default(BlockState)` has no source, and `IsValid` is how you tell. Do not compare block states across two different sources: the state ids only mean something relative to the version they came from.

`ItemStack` is immutable. `WithCount`, `Grow` and `Shrink` return new stacks; they do not mutate. And mutating anything in this package does not change the server's mind about it. Container contents in particular are a local model of what the server last told you. To move an item for real:

1. Send the action through [Umpk.Client](/packages/umpk-client).
2. Wait for the server to confirm the change.

`ClickSimulator.Apply` is a prediction, not a command. `ClickResult.TouchedServerAuthoritativeSlot` tells you when the prediction covered a slot whose contents only the server can decide (a crafting output, for example), which is your cue to trust the server's next update over your own arithmetic.

On 1.17.1 and later, container state ids establish causality rather than acceptance. A server update at the same or an older revision than a pending click can have been queued before the click; UMPK rebases untouched fields from it and preserves the optimistic delta until a newer revision confirms or corrects the action. Several rapid clicks at one revision compose into one bounded prediction. A newer per-slot correction replaces only that slot, while a newer full-content packet replaces slots and cursor atomically. `PredictionCorrected` therefore means a causally newer server result differed from the prediction, not merely that an old packet arrived late.

`MerchantOffers` is the last offers packet the server sent. Vanilla 1.21.5 sends it when the trade screen opens and when offers are explicitly resent (for example after a restock), but a normal completed trade only increments the server-side offer and synchronizes container slots. Do not use `MerchantOffer.Uses` as an immediate per-trade acknowledgement unless a later `TradeOffersReceived` event delivered a fresh offers packet; use the container result/input updates as the live transaction state.

Entity metadata has two tiers, and they behave differently. The raw tier is indexed by integer and is always populated. The semantic tier, reached through `MetadataKey`, needs an `IMetadataKeySource` to map a key to the index that version uses. `EntityMetadata.HasKeySource` tells you whether you have one. Without it, `TryGet` on a typed key always misses, even though the value was decoded and stored, and typed properties such as `Entity.CustomName` stay empty. Get the source from `JavaGameData.EntityMetadataKeys(protocol)`.

`ClickAction` is an abstract record with nested subtypes: `ClickAction.Pickup`, `.QuickMove`, `.Swap`, `.Throw`, `.Drag`, `.CloneSlot` and `.PickupAll`. Write `new ClickAction.QuickMove(0, MouseButton.Left)`, not `new QuickMove(...)`.

There are two unrelated `ProfileProperty` types and two unrelated `ResolvableProfile` types in this package (one pair under `Umpk.Game.Entities`, one under `Umpk.Game.Items.Components`), plus `Umpk.ProfileProperty` in [Umpk.Core](/packages/umpk-core). They are not interchangeable. Watch your `using` directives.
