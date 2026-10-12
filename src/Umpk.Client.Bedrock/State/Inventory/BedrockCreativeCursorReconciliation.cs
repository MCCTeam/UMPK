using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Identifies the actual inventory, registry, catalog, and normal screen lifetime that own a retained creative operation.</summary>
/// <remarks>The screen token must belong to the actual open event and change after close or reopen, even when the server reuses the same window identifier.</remarks>
internal readonly record struct BedrockCreativeCursorOwnership(int InventoryGeneration, BedrockRegistrySnapshot Registry, BedrockCreativeContentPacket Catalog, BedrockOpenContainer Screen, object ScreenLifetime);

/// <summary>Separates an eligible current response with unavailable item data from a stale or unrelated response that cannot affect replacement state.</summary>
internal readonly record struct BedrockCreativeCursorReconciliationResult(bool IsEligible, ImmutableArray<BedrockInventoryReconciledSlot> Updates);

/// <summary>Retains the observed three-action creative-to-empty-cursor shape without building, emitting, or qualifying a generic maximum-stack policy.</summary>
internal sealed class BedrockCreativeCursorReconciliation
{
    private readonly BedrockInventorySnapshot _before;
    private readonly BedrockCreativeEntry _entry;
    private readonly BedrockItemState _registryItem;
    private readonly BedrockItemStackRequestPacket _packet;
    private readonly BedrockCreativeCursorOwnership _ownership;

    private BedrockCreativeCursorReconciliation(BedrockInventorySnapshot before, BedrockCreativeEntry entry, BedrockItemState registryItem, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership)
    {
        _before = Clone(before);
        _entry = Clone(entry);
        _registryItem = registryItem with { Components = registryItem.Components.Copy() };
        _packet = Clone(packet);
        _ownership = ownership;
    }

    /// <summary>Gets an isolated copy of the known empty cursor baseline used by the caller's atomic Store guard.</summary>
    internal BedrockInventorySnapshot Before => Clone(_before);

    /// <summary>Gets an isolated copy of the actual selected catalog entry.</summary>
    internal BedrockCreativeEntry CatalogEntry => Clone(_entry);

    /// <summary>Gets the matching registry identity with independently retained component data.</summary>
    internal BedrockItemState RegistryItem => _registryItem with { Components = _registryItem.Components.Copy() };

    /// <summary>Gets an isolated copy of the validated request, including its opaque result descriptor bytes.</summary>
    internal BedrockItemStackRequestPacket Packet => Clone(_packet);

    internal int RequestId => _packet.Requests[0].RequestId;
    internal int Generation => _ownership.InventoryGeneration;
    internal ImmutableArray<int> AffectedSlots => [0];

    /// <summary>Retains only the source-backed fixed Take64 creative request and its actual catalog, registry, pool, and screen identity.</summary>
    internal static BedrockCreativeCursorReconciliation Create(BedrockInventorySnapshot before, BedrockCreativeEntry entry, BedrockItemState registryItem, BedrockItemStackRequestPacket packet, BedrockCreativeCursorOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(registryItem);
        ArgumentNullException.ThrowIfNull(packet);
        ValidateOwnership(ownership);
        if (before.Address != new BedrockContainerAddress(124, 0, null) || before.Items.IsDefault || before.Items.Length != 54
            || (!before.KnownSlots.IsDefault && before.KnownSlots.Length != 54)
            || (!before.ResponseCorrections.IsDefault && before.ResponseCorrections.Length != 54)
            || !before.IsSlotKnown(0) || !before.Items[0].Equals(Air()))
            throw new InvalidOperationException("Creative cursor reconciliation requires the known empty cursor in the actual 54-slot UI pool.");

        BedrockCreativeItemInstance item = entry.Item;
        // Nonnegative catalog block values are a conservative supported representation subset, not a native signed-ID validity rule.
        if (item.NetworkId is 0 or < short.MinValue or > short.MaxValue || item.BlockRuntimeId < 0 || item.MetadataValue > int.MaxValue || item.Count != 1
            || registryItem.RuntimeId != item.NetworkId || string.IsNullOrEmpty(registryItem.Name) || registryItem.Name == "minecraft:air"
            || registryItem.Components is null || !ownership.Registry.Items.TryGetValue(registryItem.RuntimeId, out string? name)
            || !string.Equals(name, registryItem.Name, StringComparison.Ordinal)
            || ownership.Catalog.Entries.Count(candidate => candidate.EntryId == entry.EntryId) != 1
            || !ownership.Catalog.Entries.Any(candidate => ReferenceEquals(candidate, entry)))
            throw new InvalidOperationException("The retained creative entry does not have a matching current registry and qualified full-item representation.");

        if (packet.Requests.Length != 1)
            throw new InvalidOperationException("Creative cursor reconciliation retains exactly one request.");
        BedrockItemStackRequest request = packet.Requests[0];
        if (request.RequestId >= 0 || (request.RequestId & 1) == 0 || request.Cause != -1 || !request.CustomNames.IsEmpty || request.Actions.Length != 3)
            throw new InvalidOperationException("The creative request identity, cause, names, or action count is outside the retained shape.");
        if (request.Actions[0] is not { Type: BedrockItemStackActionType.CraftCreative, LegacyTypeId: 14, Payload: BedrockCraftCreativePayload creative }
            || creative.ItemId != entry.EntryId || creative.TimesCrafted != 1
            || request.Actions[1] is not { Type: BedrockItemStackActionType.ResultsDeprecated, LegacyTypeId: 19, Payload: BedrockResultsDeprecatedPayload results }
            || results.TimesCrafted != 1 || results.ResultItems.Length != 1
            || request.Actions[2] is not { Type: BedrockItemStackActionType.Take, LegacyTypeId: 0, Payload: BedrockTakePayload take }
            || take.Count != 64 || take.Source != new BedrockStackRequestSlot(new(60, null), 50, request.RequestId)
            || take.Destination != new BedrockStackRequestSlot(new(59, null), 0, 0))
            throw new InvalidOperationException("The retained creative request must create, declare one result, and take 64 from created output to the empty cursor in native order.");

        BedrockItemStackResultDescriptor expected = new(1, 1, registryItem.Name, checked((int)item.MetadataValue), 1, checked((uint)item.BlockRuntimeId), item.ExtraData);
        if (!expected.Equals(results.ResultItems[0]))
            throw new InvalidOperationException("The retained creative result descriptor does not match the actual catalog item and registry name.");
        return new(before, entry, registryItem, packet, ownership);
    }

    /// <summary>Checks actual owner identity; the caller must repeat this check under its owning gate before applying any Store update or unavailability.</summary>
    internal bool IsCurrent(BedrockCreativeCursorOwnership current) =>
        current.InventoryGeneration == _ownership.InventoryGeneration
        && ReferenceEquals(current.Registry, _ownership.Registry)
        && ReferenceEquals(current.Catalog, _ownership.Catalog)
        && current.Screen == _ownership.Screen
        && ReferenceEquals(current.ScreenLifetime, _ownership.ScreenLifetime);

    /// <summary>Materializes only one correlated accepted logical cursor row, retaining opaque item data and the original correction fields separately.</summary>
    internal BedrockCreativeCursorReconciliationResult Reconcile(BedrockItemStackResponse response, BedrockCreativeCursorOwnership current)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!IsCurrent(current) || response.RequestId != RequestId || !response.IsOk)
            return new(false, []);
        if (!response.ContainersPresent)
            return new(true, []);

        BedrockItemStackResponseSlot? selected = null;
        foreach (BedrockItemStackResponseContainer container in response.Containers)
        {
            if (container.SlotType != new BedrockFullContainerName(59, null))
                continue;
            foreach (BedrockItemStackResponseSlot row in container.Slots)
            {
                if (row.Slot != 0)
                    continue;
                if (selected is not null && !selected.Equals(row))
                    return new(true, []);
                selected = row;
            }
        }
        if (selected is null || selected.HotbarSlot != 0 || (selected.Count > 0 && selected.StackId is not > 0))
            return new(true, []);

        BedrockItemInstance item = selected.Count == 0 ? Air() : new(checked((short)_entry.Item.NetworkId), selected.Count, _entry.Item.MetadataValue, selected.StackId, checked((uint)_entry.Item.BlockRuntimeId), _entry.Item.ExtraData);
        return new(true, [new BedrockInventoryReconciledSlot(0, item, Clone(selected))]);
    }

    private static void ValidateOwnership(BedrockCreativeCursorOwnership ownership)
    {
        if (ownership.InventoryGeneration <= 0 || ownership.Registry is null || ownership.Registry.Generation <= 0
            || ownership.Catalog is null || ownership.ScreenLifetime is null || ownership.Screen.WindowType != -1
            || ownership.Screen.WindowId is < 1 or > 99)
            throw new InvalidOperationException("Creative cursor reconciliation requires actual current inventory, registry, catalog, and normal inventory screen owners.");
    }

    private static BedrockInventorySnapshot Clone(BedrockInventorySnapshot before) => before with
    {
        Items = before.Items.Select(Clone).ToImmutableArray(),
        StorageItem = Clone(before.StorageItem),
        ResponseCorrections = before.ResponseCorrections.IsDefault ? default : before.ResponseCorrections.Select(row => row is null ? null : Clone(row)).ToImmutableArray(),
    };

    private static BedrockCreativeEntry Clone(BedrockCreativeEntry entry) => new(entry.EntryId, new(entry.Item.NetworkId, entry.Item.Count, entry.Item.MetadataValue, entry.Item.BlockRuntimeId, entry.Item.ExtraData), entry.GroupIndex);
    private static BedrockItemInstance Clone(BedrockItemInstance item) => new(item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, item.ExtraData);
    private static BedrockItemStackResponseSlot Clone(BedrockItemStackResponseSlot row) => new(row.Slot, row.HotbarSlot, row.Count, row.StackId, row.CustomName, row.FilteredCustomName, row.DurabilityCorrection);

    private static BedrockItemStackRequestPacket Clone(BedrockItemStackRequestPacket packet)
    {
        BedrockItemStackRequest request = packet.Requests[0];
        var creative = (BedrockCraftCreativePayload)request.Actions[0].Payload;
        var results = (BedrockResultsDeprecatedPayload)request.Actions[1].Payload;
        var take = (BedrockTakePayload)request.Actions[2].Payload;
        BedrockItemStackResultDescriptor row = results.ResultItems[0];
        var descriptor = new BedrockItemStackResultDescriptor(row.Type, row.LegacyType, row.Name, row.Metadata, row.Count, row.BlockRuntimeId, row.ExtraData);
        return new([new(request.RequestId,
        [
            new(request.Actions[0].Type, request.Actions[0].LegacyTypeId, creative with { }),
            new(request.Actions[1].Type, request.Actions[1].LegacyTypeId, new BedrockResultsDeprecatedPayload([descriptor], results.TimesCrafted)),
            new(request.Actions[2].Type, request.Actions[2].LegacyTypeId, take with { }),
        ], request.CustomNames, request.Cause)]);
    }

    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
}
