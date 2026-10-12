using System.Collections.Immutable;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Retains only the reviewed first legacy minus-four oak Drop-one packet projection and its original slot receipt.</summary>
/// <remarks>The Session owns actual admission, lifetime, registry definition resolution, transport and Store publication. This plan neither burns an opportunity nor changes authority.</remarks>
internal sealed class BedrockObservedOakDropPlan
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private readonly BedrockInventorySnapshot _before;
    private readonly BedrockCreativeCursorOwnership _ownership;

    private BedrockObservedOakDropPlan(BedrockInventorySnapshot before, BedrockCreativeCursorOwnership ownership)
    {
        _before = Clone(before);
        _ownership = ownership;
    }

    internal int LegacyRequestId => -4;
    internal int Generation => _ownership.InventoryGeneration;
    internal BedrockContainerAddress Address => s_player;
    internal int Slot => 0;
    internal object AuthorityReceipt => _before.AuthorityReceipts[0];
    internal BedrockInventorySnapshot Before => Clone(_before);
    internal BedrockItemInstance TentativeRemainder => Descriptor(_before.Items[0], 63);
    internal BedrockInventoryTransactionPacket Packet => BuildPacket(_before.Items[0]);

    /// <summary>Validates the fixed known source and the selected actual catalog object against the Session-resolved unique item definition.</summary>
    internal static BedrockObservedOakDropPlan Create(BedrockInventorySnapshot before, BedrockCreativeEntry entry, BedrockItemState item, BedrockCreativeCursorOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(item);
        if (ownership.InventoryGeneration <= 0 || ownership.Registry is null || ownership.Registry.Generation <= 0
            || ownership.Catalog is null || ownership.ScreenLifetime is null || ownership.Screen.WindowType != -1
            || ownership.Screen.WindowId is < 1 or > 99)
            throw new InvalidOperationException("The fixed oak Drop requires actual normal-screen and registry/catalog owners.");
        if (!ValidPlayerSnapshot(before) || !before.IsSlotKnown(0))
            throw new InvalidOperationException("The fixed oak Drop requires the known actual player slot-zero receipt.");

        BedrockCreativeItemInstance catalogItem = entry.Item;
        BedrockItemInstance source = before.Items[0];
        // Positive material IDs and nonnegative catalog block IDs bound this internal observed subset.
        if (item.Name != "minecraft:oak_planks" || item.ComponentBased || item.Version != 2
            || item.Components is not NbtCompound { IsEmpty: true } || item.RuntimeId != catalogItem.NetworkId
            || !ownership.Registry.Items.TryGetValue(item.RuntimeId, out string? name) || name != item.Name
            || ownership.Catalog.Entries.Count(candidate => candidate.EntryId == entry.EntryId) != 1
            || !ownership.Catalog.Entries.Any(candidate => ReferenceEquals(candidate, entry))
            || catalogItem.NetworkId is <= 0 or > short.MaxValue || catalogItem.Count != 1 || catalogItem.MetadataValue != 0
            || catalogItem.BlockRuntimeId < 0 || catalogItem.ExtraData.Length != 10 || catalogItem.ExtraData.Any(static value => value != 0)
            || source.NetworkId != catalogItem.NetworkId || source.Count != 64 || source.StackNetworkId is not > 0
            || source.MetadataValue != 0 || source.BlockRuntimeId != checked((uint)catalogItem.BlockRuntimeId)
            || !source.ExtraData.AsSpan().SequenceEqual(catalogItem.ExtraData) || !NeutralCorrection(before, source))
            throw new InvalidOperationException("Only the current vanilla oak-planks slot-zero source64 representation is qualified.");

        return new(before, ownership);
    }

    /// <summary>Checks retained owner identity; initial-life/world/runtime and admission eligibility remain separate Session checks.</summary>
    internal bool IsCurrent(BedrockCreativeCursorOwnership current) =>
        current.InventoryGeneration == _ownership.InventoryGeneration
        && ReferenceEquals(current.Registry, _ownership.Registry)
        && ReferenceEquals(current.Catalog, _ownership.Catalog)
        && current.Screen == _ownership.Screen
        && ReferenceEquals(current.ScreenLifetime, _ownership.ScreenLifetime);

    /// <summary>Checks the exact still-known admitted receipt before output admission; call under Session's gate using the current Store snapshot and generation.</summary>
    /// <remarks>A same-value actual publication has a different receipt. This predicate does not qualify carried-forward retired data for mutation.</remarks>
    internal bool IsSourceCurrent(BedrockInventorySnapshot current, int generation) =>
        generation == Generation && ValidPlayerSnapshot(current) && current.Items.Length == _before.Items.Length
        && current.IsSlotKnown(0) && ReferenceEquals(current.AuthorityReceipts[0], AuthorityReceipt)
        && current.Items[0].Equals(_before.Items[0])
        && Equals(Correction(current), Correction(_before));

    private static bool ValidPlayerSnapshot(BedrockInventorySnapshot snapshot) =>
        snapshot.Address == s_player && !snapshot.Items.IsDefault
        && snapshot.Items.Length is > 0 and <= BedrockInventoryStore.MaxSlotsPerContainer
        && snapshot.Items.All(static item => item is not null) && snapshot.StorageItem is not null
        && (snapshot.KnownSlots.IsDefault || snapshot.KnownSlots.Length == snapshot.Items.Length)
        && (snapshot.ResponseCorrections.IsDefault || snapshot.ResponseCorrections.Length == snapshot.Items.Length)
        && !snapshot.AuthorityReceipts.IsDefault && snapshot.AuthorityReceipts.Length == snapshot.Items.Length
        && snapshot.AuthorityReceipts.All(static receipt => receipt is not null);

    private static bool NeutralCorrection(BedrockInventorySnapshot before, BedrockItemInstance source) =>
        Correction(before) is not { } row || (row.Slot == 0 && row.HotbarSlot == 0 && row.Count == 64
            && row.StackId == source.StackNetworkId && row.CustomName.Length == 0 && row.FilteredCustomName.Length == 0
            && row.DurabilityCorrection == 0);

    private static BedrockItemStackResponseSlot? Correction(BedrockInventorySnapshot snapshot) =>
        snapshot.ResponseCorrections.IsDefault ? null : snapshot.ResponseCorrections[0];

    private static BedrockInventoryTransactionPacket BuildPacket(BedrockItemInstance source) => new(
        new BedrockTransactionLegacy(-4, [new BedrockTransactionLegacyEntry(29, [0])]),
        BedrockInventoryTransactionType.Normal,
        [
            new BedrockTransactionAction(0, true, 0, true, null, 0, Descriptor(source, 64), Descriptor(source, 63)),
            new BedrockTransactionAction(2, true, null, true, 0, 0, new(0, 0, 0, null, 0, []), Descriptor(source, 1)),
        ],
        null);

    private static BedrockItemInstance Descriptor(BedrockItemInstance source, ushort count) =>
        new(source.NetworkId, count, source.MetadataValue, null, source.BlockRuntimeId, source.ExtraData);

    private static BedrockItemInstance Clone(BedrockItemInstance item) =>
        new(item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, item.ExtraData);

    private static BedrockItemStackResponseSlot Clone(BedrockItemStackResponseSlot row) =>
        new(row.Slot, row.HotbarSlot, row.Count, row.StackId, row.CustomName, row.FilteredCustomName, row.DurabilityCorrection);

    private static BedrockInventorySnapshot Clone(BedrockInventorySnapshot before) => before with
    {
        Items = before.Items.Select(Clone).ToImmutableArray(),
        StorageItem = Clone(before.StorageItem),
        ResponseCorrections = before.ResponseCorrections.IsDefault ? default
            : before.ResponseCorrections.Select(row => row is null ? null : Clone(row)).ToImmutableArray(),
    };
}
