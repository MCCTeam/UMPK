using System.Collections.Immutable;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Separates stale responses from current accepted rows whose data may still be unavailable.</summary>
internal readonly record struct BedrockCursorPlaceReconciliationResult(bool IsEligible, ImmutableArray<BedrockInventoryReconciledSlot> CursorUpdates, ImmutableArray<BedrockInventoryReconciledSlot> PlayerUpdates);

/// <summary>Retains a fixed observed or explicit-amount cursor Place between the two qualified slot-zero Store owners.</summary>
internal sealed class BedrockCursorPlaceReconciliation
{
    private readonly BedrockInventorySnapshot _cursor;
    private readonly BedrockInventorySnapshot _player;
    private readonly BedrockItemInstance _source;
    private readonly BedrockItemStackRequestPacket _packet;
    private readonly BedrockCreativeCursorOwnership _ownership;

    private BedrockCursorPlaceReconciliation(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership)
    {
        _cursor = Clone(cursor);
        _player = Clone(player);
        _source = Clone(cursor.Items[0]);
        _packet = Clone(packet);
        _ownership = ownership;
    }

    internal BedrockInventorySnapshot CursorBefore => Clone(_cursor);
    internal BedrockInventorySnapshot PlayerBefore => Clone(_player);
    internal BedrockItemStackRequestPacket Packet => Clone(_packet);
    internal int RequestId => _packet.Requests[0].RequestId;
    internal int Generation => _ownership.InventoryGeneration;

    /// <summary>Retains the supplied positive amount up to the actual known cursor count without inferring a maximum item capacity.</summary>
    internal static BedrockCursorPlaceReconciliation CreateExplicitAmount(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, BedrockCreativeEntry entry, BedrockItemState item, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership) =>
        CreateCore(cursor, player, entry, item, packet, ownership, explicitAmount: true);

    /// <summary>Validates the fixed observed subset without inferring a generic maximum stack or a logical container from a screen window.</summary>
    internal static BedrockCursorPlaceReconciliation Create(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, BedrockCreativeEntry entry, BedrockItemState item, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership) =>
        CreateCore(cursor, player, entry, item, packet, ownership, explicitAmount: false);

    private static BedrockCursorPlaceReconciliation CreateCore(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, BedrockCreativeEntry entry, BedrockItemState item, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership, bool explicitAmount)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(packet);
        if (ownership.InventoryGeneration <= 0 || ownership.Registry is null || ownership.Registry.Generation <= 0 || ownership.Catalog is null || ownership.ScreenLifetime is null
            || ownership.Screen.WindowType != -1 || ownership.Screen.WindowId is < 1 or > 99)
            throw new InvalidOperationException("The cursor transfer requires current normal-screen and registry/catalog owners.");
        if (cursor.Address != new BedrockContainerAddress(124, 0, null) || player.Address != new BedrockContainerAddress(0, 0, null)
            || !ValidSnapshot(cursor, 54) || !ValidSnapshot(player, null) || !cursor.IsSlotKnown(0) || !player.IsSlotKnown(0))
            throw new InvalidOperationException("The cursor transfer requires real known slot-zero baselines and receipt identities.");
        BedrockItemInstance source = cursor.Items[0];
        if (item.Name != "minecraft:oak_planks" || item.ComponentBased || item.Version != 2 || item.Components is not NbtCompound { IsEmpty: true }
            || item.RuntimeId != entry.Item.NetworkId || !ownership.Registry.Items.TryGetValue(item.RuntimeId, out string? name) || name != item.Name
            || ownership.Catalog.Entries.Count(candidate => candidate.EntryId == entry.EntryId) != 1
            || !ownership.Catalog.Entries.Any(candidate => ReferenceEquals(candidate, entry))
            || entry.Item.NetworkId is 0 or < short.MinValue or > short.MaxValue || entry.Item.Count != 1 || entry.Item.MetadataValue != 0
            || entry.Item.BlockRuntimeId < 0 || entry.Item.ExtraData.Length != 10 || entry.Item.ExtraData.Any(static value => value != 0)
            || source.NetworkId != entry.Item.NetworkId || (source.Count == 0 || (!explicitAmount && source.Count != 64)) || source.StackNetworkId is not > 0 || source.MetadataValue != 0
            || source.BlockRuntimeId != checked((uint)entry.Item.BlockRuntimeId) || !source.ExtraData.AsSpan().SequenceEqual(entry.Item.ExtraData)
            || !player.Items[0].Equals(Air()))
            throw new InvalidOperationException(explicitAmount
                ? "Only the known vanilla oak cursor and empty hotbar-zero representation are qualified."
                : "Only the known vanilla oak cursor64 and empty hotbar-zero representation are qualified.");
        if (packet.Requests.Length != 1)
            throw new InvalidOperationException("The cursor transfer retains exactly one request.");
        BedrockItemStackRequest request = packet.Requests[0];
        if (request.RequestId >= 0 || (request.RequestId & 1) == 0 || request.Cause != -1 || !request.CustomNames.IsEmpty || request.Actions.Length != 1
            || request.Actions[0] is not { Type: BedrockItemStackActionType.Place, LegacyTypeId: 1, Payload: BedrockPlacePayload place }
            || (explicitAmount ? place.Count == 0 || place.Count > source.Count : place.Count != 64) || place.Source != new BedrockStackRequestSlot(new(59, null), 0, source.StackNetworkId.Value)
            || place.Destination != new BedrockStackRequestSlot(new(28, null), 0, 0))
            throw new InvalidOperationException(explicitAmount
                ? "The supplied request is outside the qualified explicit cursor Place amount shape."
                : "The supplied request is outside the observed cursor Place64 shape.");
        return new(cursor, player, packet, ownership);
    }

    internal bool IsCurrent(BedrockCreativeCursorOwnership current) =>
        current.InventoryGeneration == _ownership.InventoryGeneration && ReferenceEquals(current.Registry, _ownership.Registry)
        && ReferenceEquals(current.Catalog, _ownership.Catalog) && current.Screen == _ownership.Screen
        && ReferenceEquals(current.ScreenLifetime, _ownership.ScreenLifetime);

    internal BedrockCursorPlaceReconciliationResult Reconcile(BedrockItemStackResponse response, BedrockCreativeCursorOwnership current)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!IsCurrent(current) || response.RequestId != RequestId || !response.IsOk)
            return new(false, [], []);
        if (!response.ContainersPresent)
            return new(true, [], []);
        return new(true, Select(response, 59), Select(response, 28));
    }

    private ImmutableArray<BedrockInventoryReconciledSlot> Select(BedrockItemStackResponse response, byte containerId)
    {
        BedrockItemStackResponseSlot? selected = null;
        foreach (BedrockItemStackResponseContainer container in response.Containers)
        {
            if (container.SlotType != new BedrockFullContainerName(containerId, null))
                continue;
            foreach (BedrockItemStackResponseSlot row in container.Slots)
            {
                if (row.Slot != 0)
                    continue;
                if (selected is not null && !selected.Equals(row))
                    return [];
                selected = row;
            }
        }
        if (selected is null || selected.HotbarSlot != 0 || (selected.Count > 0 && selected.StackId is not > 0))
            return [];
        BedrockItemInstance item = selected.Count == 0 ? Air() : new(_source.NetworkId, selected.Count, _source.MetadataValue, selected.StackId, _source.BlockRuntimeId, _source.ExtraData);
        return [new(0, item, Clone(selected))];
    }

    private static bool ValidSnapshot(BedrockInventorySnapshot snapshot, int? count) =>
        !snapshot.Items.IsDefault && snapshot.Items.Length is > 0 and <= BedrockInventoryStore.MaxSlotsPerContainer
        && (!count.HasValue || snapshot.Items.Length == count.Value)
        && (snapshot.KnownSlots.IsDefault || snapshot.KnownSlots.Length == snapshot.Items.Length)
        && (snapshot.ResponseCorrections.IsDefault || snapshot.ResponseCorrections.Length == snapshot.Items.Length)
        && !snapshot.AuthorityReceipts.IsDefault && snapshot.AuthorityReceipts.Length == snapshot.Items.Length && snapshot.AuthorityReceipts[0] is not null;

    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Clone(BedrockItemInstance item) => new(item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, item.ExtraData);
    private static BedrockItemStackResponseSlot Clone(BedrockItemStackResponseSlot row) => new(row.Slot, row.HotbarSlot, row.Count, row.StackId, row.CustomName, row.FilteredCustomName, row.DurabilityCorrection);
    private static BedrockInventorySnapshot Clone(BedrockInventorySnapshot before) => before with
    {
        Items = before.Items.Select(Clone).ToImmutableArray(),
        StorageItem = Clone(before.StorageItem),
        ResponseCorrections = before.ResponseCorrections.IsDefault ? default : before.ResponseCorrections.Select(row => row is null ? null : Clone(row)).ToImmutableArray(),
    };
    private static BedrockItemStackRequestPacket Clone(BedrockItemStackRequestPacket packet)
    {
        BedrockItemStackRequest request = packet.Requests[0];
        BedrockItemStackAction action = request.Actions[0];
        return new([new(request.RequestId, [new(action.Type, action.LegacyTypeId, ((BedrockPlacePayload)action.Payload) with { })], request.CustomNames, request.Cause)]);
    }
}
