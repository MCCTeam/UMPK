---
title: Inventory and resource packs
description: Read inventory state safely, send container actions, and choose an explicit resource-pack policy.
sidebar:
  order: 4
---

UMPK tracks inventory and containers as packets arrive. It also sends container actions through a local prediction model. Read state through snapshots when code runs outside the session loop. Make a resource-pack decision before a server asks for one.

## Read inventory state

`client.Snapshots` runs the read on the session loop and returns an immutable value. Use it from UI, background work, or an event handler that hands work to another task.

```csharp
using Umpk.Client.Snapshots;

PlayerInventorySnapshot inventory = await client.Snapshots.PlayerInventoryAsync(ct);
Console.WriteLine($"Held slot: {inventory.HeldSlot}");
Console.WriteLine($"Held item: {inventory.HeldItem}");

OpenContainerSnapshot? container = await client.Snapshots.OpenContainerAsync(ct);
if (container is not null)
{
    Console.WriteLine($"Open window: {container.WindowId}");
    Console.WriteLine($"Slots: {container.Slots.Count}");
}
```

`OpenContainerAsync` returns `null` when only the player inventory is open. `PlayerInventoryAsync`, `OpenContainerAsync`, `CursorAsync`, and `HeldItemAsync` need the inventory feature. They throw `FeatureDisabledException` if you disabled it in `ConfigureFeatures`.

Do not enumerate `client.State.Inventory` from another thread when you need a consistent view. Packet application changes that state on the session loop.

## Respond to container changes

Subscribe before the action that can open a container. Keep the handler short because event handlers run on the session loop.

```csharp
using Umpk.Client.Events;

using IDisposable opened = client.Events.Subscribe<ContainerOpened>(opened =>
    Console.WriteLine($"Container {opened.WindowId} opened."));

using IDisposable corrected = client.Events.Subscribe<PredictionCorrected>(corrected =>
    Console.WriteLine($"Server corrected window {corrected.WindowId} at state {corrected.StateId}."));
```

`ContainerContentChanged` reports a slot or full-content update. `ContainerClosed` reports a close. `PredictionCorrected` means an authoritative server update contradicted an optimistic click. Read a new snapshot after that event. Do not keep the old predicted result as your source of truth.

## Select and move items

Hotbar slots use indices 0 through 8. Container-click slots use the active window's menu indices. Those index spaces are different.

```csharp
using Umpk.Game.Inventory;

await client.Actions.Inventory.SelectHeldSlotAsync(2, ct);

// Shift-click slot 13 in the active container.
ClickResult result = await client.Actions.Inventory.QuickMoveAsync(13, ct);
Console.WriteLine($"Changed slots: {result.ChangedSlots.Count}");

// Pick up or place with the primary button.
await client.Actions.Inventory.ClickAsync(
    new ClickAction.Pickup(Slot: 13, Button: MouseButton.Left), ct);
```

`ClickAsync` predicts the cursor and changed slots, then sends them with the action. The server remains the authority. It can accept the prediction, replace it, or reject it. Use a new snapshot after a click when later decisions depend on the result.

For the player window, slots 36 through 44 are the hotbar and slot 45 is the offhand. Do not pass a hotbar index to `CreativeSetSlotAsync`. That method takes player-window indices, not hotbar indices.

## Choose version-dependent actions

Some actions use different packet forms on different Java versions. Check `client.Capabilities` before you send an optional action.

```csharp
if (client.Capabilities.CanRenameItem)
{
    await client.Actions.Inventory.RenameItemAsync("Survey kit", ct);
}

if (client.Capabilities.CanPlaceRecipeByName)
{
    await client.Actions.Inventory.PlaceRecipeByNameAsync(
        Umpk.Identifier.Minecraft("stick"), ct: ct);
}
```

`CanRenameItem` covers both the legacy plugin-channel route and the modern packet route. Recipe placement has two separate capability properties because the server uses an identifier form on older versions and a network-id form on newer versions. Use the matching action. Do not branch on a protocol number in application code.

## Close the container

Call `CloseAsync` after you finish with an open container. The method sends the close packet and clears the local open-container state.

```csharp
await client.Actions.Inventory.CloseAsync(ct);
```

This does not disconnect the client. Use `DisconnectAsync(CancellationToken.None)` for a clean session shutdown.

## Handle resource packs deliberately

The default policy is `ResourcePackPolicy.Decline`. It declines every server resource pack and avoids downloading network content without a host decision.

You can construct `ResourcePackPolicy` with a request callback when your application processes packs itself. The callback runs on the session loop. It must return promptly. Return a response only when your host can meet the claim it reports.

To download and retain verified bytes, opt in to the built-in bounded downloader:

```csharp
.ConfigurePolicies(p => p.ResourcePack = ResourcePackPolicy.Configure(new()
{
    Accept = true,
    Download = true,
    Cache = true,
    CacheDirectory = "/var/lib/my-bot/resource-packs",
    MaxDownloadBytes = 64L * 1024 * 1024,
}))
```

The downloader accepts only HTTP or HTTPS URLs. It enforces `MaxDownloadBytes`, checks a supplied SHA-1 hash, and writes a verified cache file atomically. It reports `Downloaded`. It never reports `SuccessfullyLoaded` because UMPK does not render textures, sounds, or other visual assets.

`ResourcePackPolicy.Accept` reports `SuccessfullyLoaded` without downloading or applying a pack. Use it only when your application itself loads the pack and can make that claim truthfully.

## Next

- [A minimal bot](/getting-started/minimal-bot) shows the connection lifecycle around these APIs.
- [Movement and pathfinding](/guides/movement-and-pathfinding) covers navigation after the player has spawned.
- [Limitations](/reference/limitations#resource-packs-are-downloaded-not-rendered) states the headless-client boundary in full.
