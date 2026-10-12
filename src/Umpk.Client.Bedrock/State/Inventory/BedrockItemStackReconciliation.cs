using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Carries one reconstructed item and the original authoritative response corrections for its physical player slot.</summary>
internal readonly record struct BedrockInventoryReconciledSlot(int Slot, BedrockItemInstance Item, BedrockItemStackResponseSlot Response);

/// <summary>Retains one supported player operation so an accepted response can reconcile known item data without another content packet.</summary>
internal sealed class BedrockItemStackReconciliation
{
    private readonly BedrockItemInstance _sourceResult;
    private readonly BedrockItemInstance _destinationResult;
    private readonly BedrockFullContainerName _sourceContainer;
    private readonly BedrockFullContainerName _destinationContainer;

    private BedrockItemStackReconciliation(BedrockInventorySnapshot before, int requestId, int generation, BedrockStackRequestSlot source, BedrockStackRequestSlot destination, bool swap)
    {
        Before = before with
        {
            Items = before.Items.Select(Clone).ToImmutableArray(),
            StorageItem = Clone(before.StorageItem),
        };
        RequestId = requestId;
        Generation = generation;
        AffectedSlots = [source.Slot, destination.Slot];
        _sourceContainer = source.Container;
        _destinationContainer = destination.Container;
        _sourceResult = swap ? Before.Items[destination.Slot] : Before.Items[source.Slot];
        _destinationResult = swap || IsEmpty(Before.Items[destination.Slot]) ? Before.Items[source.Slot] : Before.Items[destination.Slot];
    }

    /// <summary>Gets the retained content used to identify and guard the two affected items.</summary>
    internal BedrockInventorySnapshot Before { get; }

    /// <summary>Gets the sole request identity whose accepted response can reconcile this operation.</summary>
    internal int RequestId { get; }

    /// <summary>Gets the inventory generation under which this operation was admitted.</summary>
    internal int Generation { get; }

    /// <summary>Gets the source and destination physical player indices affected by this operation.</summary>
    internal ImmutableArray<int> AffectedSlots { get; }

    /// <summary>Retains one known single-action take, place, or swap after validating its player request addresses and original stack identities.</summary>
    internal static BedrockItemStackReconciliation Create(BedrockInventorySnapshot before, BedrockItemStackRequestPacket packet, int generation)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (before.Address != new BedrockContainerAddress(0, 0, null) || before.Items.IsDefault || before.Items.Length > BedrockInventoryStore.MaxSlotsPerContainer
            || before.Items.Any(item => item is null)
            || (!before.KnownSlots.IsDefault && before.KnownSlots.Length != before.Items.Length)
            || (!before.ResponseCorrections.IsDefault && before.ResponseCorrections.Length != before.Items.Length))
            throw new InvalidOperationException("The retained operation requires known stable player inventory content.");
        if (packet.Requests.Length != 1 || packet.Requests[0].Actions.Length != 1)
            throw new InvalidOperationException("Reconciliation supports exactly one request with one inventory action.");

        BedrockItemStackRequest request = packet.Requests[0];
        if (request.RequestId >= 0 || (request.RequestId & 1) == 0)
            throw new InvalidOperationException("The retained request identity must be odd and negative.");
        BedrockItemStackAction action = request.Actions[0];
        (BedrockStackRequestSlot source, BedrockStackRequestSlot destination, byte count, bool swap) = action switch
        {
            { Type: BedrockItemStackActionType.Take, LegacyTypeId: BedrockItemStackEgress.SupportedLegacyTakeId, Payload: BedrockTakePayload take } => (take.Source, take.Destination, take.Count, false),
            { Type: BedrockItemStackActionType.Place, LegacyTypeId: BedrockItemStackEgress.SupportedLegacyPlaceId, Payload: BedrockPlacePayload place } => (place.Source, place.Destination, place.Count, false),
            { Type: BedrockItemStackActionType.Swap, LegacyTypeId: BedrockItemStackEgress.SupportedLegacySwapId, Payload: BedrockSwapPayload exchange } => (exchange.Source, exchange.Destination, (byte)0, true),
            _ => throw new InvalidOperationException("The retained action is outside the supported take, place, and swap shapes."),
        };
        ValidateSlot(before, source);
        ValidateSlot(before, destination);
        if (source.Slot == destination.Slot)
            throw new InvalidOperationException("The retained source and destination slots must differ.");
        if (before.Items[source.Slot].Count > byte.MaxValue || before.Items[destination.Slot].Count > byte.MaxValue)
            throw new InvalidOperationException("The retained operation exceeds the supported response count range.");
        if (!swap)
        {
            BedrockItemInstance sourceItem = before.Items[source.Slot];
            BedrockItemInstance destinationItem = before.Items[destination.Slot];
            if (!IsNonempty(sourceItem) || count == 0 || count > sourceItem.Count)
                throw new InvalidOperationException("The retained transfer requires a known source item and an available positive count.");
            if (destinationItem.Count + count > byte.MaxValue)
                throw new InvalidOperationException("The retained merge exceeds the supported response count range.");
            if (!IsEmpty(destinationItem) && !before.HasCompatibleItemData(source.Slot, destination.Slot))
                throw new InvalidOperationException("The retained transfer cannot merge different item data.");
        }

        return new BedrockItemStackReconciliation(before, request.RequestId, generation, source, destination, swap);
    }

    /// <summary>Reconstructs only correlated accepted rows whose slots, retained item types, and returned identities are usable.</summary>
    internal IReadOnlyList<BedrockInventoryReconciledSlot> Reconcile(BedrockItemStackResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.RequestId != RequestId || !response.IsOk || !response.ContainersPresent)
            return [];

        var rows = new BedrockItemStackResponseSlot?[2];
        var conflicts = new bool[2];
        bool hasRemapping = false;
        foreach (BedrockItemStackResponseContainer container in response.Containers)
        {
            foreach (BedrockItemStackResponseSlot row in container.Slots)
            {
                int index = row.Slot == AffectedSlots[0] ? 0 : row.Slot == AffectedSlots[1] ? 1 : -1;
                if (index < 0 || container.SlotType != (index == 0 ? _sourceContainer : _destinationContainer))
                    continue;
                hasRemapping |= row.Slot != row.HotbarSlot;
                if (rows[index] is { } previous && !previous.Equals(row))
                    conflicts[index] = true;
                else
                    rows[index] = row;
            }
        }

        var updates = ImmutableArray.CreateBuilder<BedrockInventoryReconciledSlot>(2);
        for (int index = 0; index < rows.Length; index++)
        {
            BedrockItemStackResponseSlot? row = rows[index];
            if (row is null || conflicts[index]
                || !BedrockPlayerInventoryAddressing.IsSupportedPhysicalSlot(row.HotbarSlot, Before.Items.Length)
                || !AffectedSlots.Contains(row.HotbarSlot))
                continue;
            BedrockItemInstance retained = index == 0 ? _sourceResult : _destinationResult;
            BedrockItemInstance item;
            if (row.Count == 0)
                item = new BedrockItemInstance(0, 0, 0, null, 0, []);
            else if (row.StackId is > 0 && IsNonempty(retained))
                item = new BedrockItemInstance(retained.NetworkId, row.Count, retained.MetadataValue, row.StackId, retained.BlockRuntimeId, retained.ExtraData);
            else
                continue;
            updates.Add(new BedrockInventoryReconciledSlot(row.HotbarSlot, item, row));
        }

        // A nonidentity projection cannot borrow an uncaptured receipt or partially publish an ambiguous pair.
        if (hasRemapping && (updates.Count != 2 || updates[0].Slot == updates[1].Slot))
            return [];
        return updates.ToImmutable();
    }

    private static void ValidateSlot(BedrockInventorySnapshot before, BedrockStackRequestSlot slot)
    {
        if (!BedrockPlayerInventoryAddressing.TryGetRequestContainer(slot.Slot, out BedrockFullContainerName name)
            || slot.Container != name
            || !BedrockPlayerInventoryAddressing.IsSupportedPhysicalSlot(slot.Slot, before.Items.Length)
            || !before.IsSlotKnown(slot.Slot))
            throw new InvalidOperationException("The retained operation targets an unknown or unsupported player slot.");
        BedrockItemInstance item = before.Items[slot.Slot];
        if (IsEmpty(item))
        {
            if (slot.StackId != 0)
                throw new InvalidOperationException("The retained empty slot must use the empty request identity.");
        }
        else if (!IsNonempty(item) || item.StackNetworkId is not > 0 || slot.StackId != item.StackNetworkId.Value)
            throw new InvalidOperationException("The retained operation does not match the known item stack identity.");
    }

    private static bool IsEmpty(BedrockItemInstance item) => item.NetworkId == 0 && item.Count == 0;

    private static bool IsNonempty(BedrockItemInstance item) => item.NetworkId != 0 && item.Count > 0;

    private static BedrockItemInstance Clone(BedrockItemInstance item) => new(item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, item.ExtraData);
}
