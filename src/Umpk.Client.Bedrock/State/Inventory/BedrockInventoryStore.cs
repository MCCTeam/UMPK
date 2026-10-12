using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Carries one authoritative point-in-time copy of a container with its items and storage item.</summary>
internal readonly record struct BedrockInventorySnapshot(BedrockContainerAddress Address, ImmutableArray<BedrockItemInstance> Items, BedrockItemInstance StorageItem)
{
    /// <summary>Gets which slots have usable authoritative item data; a default mask describes an entirely known retained snapshot.</summary>
    internal ImmutableArray<bool> KnownSlots { get; init; }

    /// <summary>Identifies the managed authority publication for each slot without representing a native epoch.</summary>
    internal ImmutableArray<object> AuthorityReceipts { get; init; }

    /// <summary>Gets the latest response corrections separately from opaque item data whose encoding has not been reconstructed.</summary>
    internal ImmutableArray<BedrockItemStackResponseSlot?> ResponseCorrections { get; init; }

    internal bool IsSlotKnown(int slot) => slot >= 0 && slot < Items.Length && (KnownSlots.IsDefault || KnownSlots[slot]);

    /// <summary>Qualifies identical retained item data only when both correction states are absent or carry matching semantic fields.</summary>
    internal bool HasCompatibleItemData(int source, int destination)
    {
        if (!IsSlotKnown(source) || !IsSlotKnown(destination)
            || (!ResponseCorrections.IsDefault && (source >= ResponseCorrections.Length || destination >= ResponseCorrections.Length)))
            return false;

        BedrockItemInstance first = Items[source];
        BedrockItemInstance second = Items[destination];
        if (first.NetworkId != second.NetworkId || first.MetadataValue != second.MetadataValue
            || first.BlockRuntimeId != second.BlockRuntimeId || !first.ExtraData.AsSpan().SequenceEqual(second.ExtraData))
            return false;

        BedrockItemStackResponseSlot? firstCorrection = ResponseCorrections.IsDefault ? null : ResponseCorrections[source];
        BedrockItemStackResponseSlot? secondCorrection = ResponseCorrections.IsDefault ? null : ResponseCorrections[destination];
        if (firstCorrection is null || secondCorrection is null)
            return firstCorrection is null && secondCorrection is null;

        return string.Equals(firstCorrection.CustomName, secondCorrection.CustomName, StringComparison.Ordinal)
            && string.Equals(firstCorrection.FilteredCustomName, secondCorrection.FilteredCustomName, StringComparison.Ordinal)
            && firstCorrection.DurabilityCorrection == secondCorrection.DurabilityCorrection;
    }
}

/// <summary>Holds the authoritative server-sent container content that every inventory submit validates against before building its request.</summary>
/// <remarks>Content and slot applies are generation-gated client support policy: stale generations are ignored, unknown containers and over-limit content are rejected, and only the stable player inventory survives a transient invalidation.</remarks>
internal sealed class BedrockInventoryStore
{
    /// <summary>Publishes separately retained tentative data only while one exact authoritative slot receipt remains current.</summary>
    internal bool TryPublishTentativeSlot(BedrockInventorySnapshot before, int slot, BedrockItemInstance tentativeItem, int legacyRequestId, int generation)
    {
        ArgumentNullException.ThrowIfNull(tentativeItem);
        lock (_gate)
        {
            if (generation != _generation || legacyRequestId > -4 || (legacyRequestId & 1) != 0
                || !ValidTentativeBaseline(before) || !_containers.TryGetValue(before.Address, out Entry? entry)
                || !MatchesTentativeReceipt(entry, before, slot, allowTentative: false)
                || entry.Tentative[slot].HasValue)
                return false;

            BedrockItemInstance authority = entry.Items[slot];
            if (authority.NetworkId == 0 || authority.Count == 0 || authority.StackNetworkId is not > 0
                || !ValidTentativeItem(authority, tentativeItem))
                return false;

            BedrockItemInstance[] items = [.. entry.Items];
            items[slot] = CloneItem(authority);
            bool[] known = [.. entry.Known];
            known[slot] = false;
            BedrockTentativeInventorySnapshot?[] tentative = [.. entry.Tentative];
            tentative[slot] = new(before.Address, slot, generation, legacyRequestId, entry.Receipts[slot], CloneItem(authority), CloneItem(tentativeItem), entry.Corrections[slot]);
            _containers[before.Address] = new Entry(items, entry.Storage, known, [.. entry.Corrections], [.. entry.Receipts], tentative);
            return true;
        }
    }

    /// <summary>Returns a deep-owned copy of the separately tentative slot, never a usable authoritative source.</summary>
    internal BedrockTentativeInventorySnapshot? GetTentativeSnapshot(BedrockContainerAddress address, int slot)
    {
        lock (_gate)
        {
            if (!_containers.TryGetValue(address, out Entry? entry) || slot < 0 || slot >= entry.Items.Length
                || entry.Tentative[slot] is not { } tentative)
                return null;

            return tentative with { AuthorityItem = CloneItem(tentative.AuthorityItem), TentativeItem = CloneItem(tentative.TentativeItem) };
        }
    }

    /// <summary>Retires only the exact retained tentative receipt while leaving last authority unavailable until a genuine correction.</summary>
    internal bool TryRetireTentativeSlot(BedrockContainerAddress address, int slot, object authorityReceipt, int generation)
    {
        ArgumentNullException.ThrowIfNull(authorityReceipt);
        lock (_gate)
        {
            if (generation != _generation || !_containers.TryGetValue(address, out Entry? entry)
                || slot < 0 || slot >= entry.Items.Length || entry.Tentative[slot] is not { } tentative
                || !ReferenceEquals(tentative.AuthorityReceipt, authorityReceipt)
                || !ReferenceEquals(entry.Receipts[slot], authorityReceipt))
                return false;

            BedrockTentativeInventorySnapshot?[] remaining = [.. entry.Tentative];
            remaining[slot] = null;
            _containers[address] = new Entry([.. entry.Items], entry.Storage, [.. entry.Known], [.. entry.Corrections], [.. entry.Receipts], remaining);
            return true;
        }
    }

    /// <summary>Publishes supplied correlated authority rows through exact affected receipt guards; missing rows remain unavailable.</summary>
    internal bool TryApplyTentativeAuthorityUpdates(BedrockInventorySnapshot before, IReadOnlyList<BedrockInventoryReconciledSlot> updates, IReadOnlyList<int> affectedSlots, int generation)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(affectedSlots);
        lock (_gate)
        {
            if (generation != _generation || !ValidTentativeBaseline(before)
                || !_containers.TryGetValue(before.Address, out Entry? entry)
                || affectedSlots.Count == 0 || updates.Count == 0
                || affectedSlots.Distinct().Count() != affectedSlots.Count
                || !affectedSlots.All(slot => MatchesTentativeReceipt(entry, before, slot, allowTentative: true))
                || !affectedSlots.Any(slot => entry.Tentative[slot].HasValue)
                || updates.Select(update => update.Slot).Distinct().Count() != updates.Count
                || updates.Any(update => !affectedSlots.Contains(update.Slot) || !ValidAuthorityUpdate(update)))
                return false;

            BedrockItemInstance[] items = [.. entry.Items];
            bool[] known = [.. entry.Known];
            BedrockItemStackResponseSlot?[] corrections = [.. entry.Corrections];
            object[] receipts = [.. entry.Receipts];
            BedrockTentativeInventorySnapshot?[] tentative = [.. entry.Tentative];
            foreach (int slot in affectedSlots)
                known[slot] = false;
            object publication = new();
            foreach (BedrockInventoryReconciledSlot update in updates)
            {
                items[update.Slot] = CloneItem(update.Item);
                known[update.Slot] = true;
                corrections[update.Slot] = update.Response;
                receipts[update.Slot] = publication;
                tentative[update.Slot] = null;
            }

            _containers[before.Address] = new Entry(items, entry.Storage, known, corrections, receipts, tentative);
            return true;
        }
    }

    private static bool ValidTentativeBaseline(BedrockInventorySnapshot before) =>
        !before.Items.IsDefault && before.Items.Length is > 0 and <= MaxSlotsPerContainer
        && before.Items.All(item => item is not null)
        && !before.AuthorityReceipts.IsDefault && before.AuthorityReceipts.Length == before.Items.Length
        && before.AuthorityReceipts.All(receipt => receipt is not null)
        && (before.KnownSlots.IsDefault || before.KnownSlots.Length == before.Items.Length)
        && (before.ResponseCorrections.IsDefault || before.ResponseCorrections.Length == before.Items.Length);

    private static bool MatchesTentativeReceipt(Entry entry, BedrockInventorySnapshot before, int slot, bool allowTentative) =>
        before.Items.Length == entry.Items.Length && slot >= 0 && slot < entry.Items.Length && before.IsSlotKnown(slot)
        && ReferenceEquals(entry.Receipts[slot], before.AuthorityReceipts[slot])
        && entry.Items[slot].Equals(before.Items[slot]) && SameCorrection(entry, before, slot)
        && (entry.Known[slot] || (allowTentative && entry.Tentative[slot] is { } tentative
            && ReferenceEquals(tentative.AuthorityReceipt, before.AuthorityReceipts[slot])
            && tentative.AuthorityItem.Equals(before.Items[slot])
            && Equals(tentative.AuthorityCorrection, before.ResponseCorrections.IsDefault ? null : before.ResponseCorrections[slot])));

    private static bool ValidTentativeItem(BedrockItemInstance authority, BedrockItemInstance tentative) =>
        IsCanonicalAir(tentative) || (tentative.StackNetworkId is null && tentative.Count is > 0
            && tentative.Count < authority.Count && tentative.NetworkId == authority.NetworkId
            && tentative.MetadataValue == authority.MetadataValue && tentative.BlockRuntimeId == authority.BlockRuntimeId
            && tentative.ExtraData.AsSpan().SequenceEqual(authority.ExtraData));

    private static bool ValidAuthorityUpdate(BedrockInventoryReconciledSlot update) =>
        update.Item is not null && update.Response is not null && update.Response.Slot == update.Slot
        && update.Response.HotbarSlot == update.Slot && update.Item.Count == update.Response.Count
        && (IsCanonicalAir(update.Item) ? update.Response.StackId == 0
            : update.Item.NetworkId != 0 && update.Item.Count > 0 && update.Item.StackNetworkId is > 0
                && update.Item.StackNetworkId == update.Response.StackId);

    private static bool IsCanonicalAir(BedrockItemInstance item) =>
        item.NetworkId == 0 && item.Count == 0 && item.MetadataValue == 0 && item.StackNetworkId is null
        && item.BlockRuntimeId == 0 && item.ExtraData.Length == 0;

    private static BedrockItemInstance CloneItem(BedrockItemInstance item) =>
        new(item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, item.ExtraData);

    /// <summary>Bounds how many containers the store holds before further authoritative content is rejected.</summary>
    internal const int MaxContainers = 64;
    /// <summary>Bounds how many slots one authoritative content packet may carry before it is rejected.</summary>
    internal const int MaxSlotsPerContainer = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<BedrockContainerAddress, Entry> _containers = new();
    private int _generation = 1;

    /// <summary>Gets the current content generation, advanced by every invalidation so stale packets can be recognized.</summary>
    internal int Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    /// <summary>Gets how many authoritative containers are currently stored.</summary>
    internal int ContainerCount
    {
        get
        {
            lock (_gate)
                return _containers.Count;
        }
    }

    /// <summary>Checks current retained slot availability before an unqualified raw request is admitted; it does not qualify the packet contents.</summary>
    internal bool CanAdmitRawItemStackRequest(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return false;
            foreach (Entry entry in _containers.Values)
            {
                foreach (bool known in entry.Known)
                {
                    if (!known)
                        return false;
                }
            }
            return true;
        }
    }

    /// <summary>Replaces the authoritative content of one container under the current generation and ignores packets from a superseded generation.</summary>
    internal void ApplyContent(BedrockInventoryContentPacket packet, int generation)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (generation != _generation)
                return;
            if (packet.Items.Length > MaxSlotsPerContainer)
                throw new InvalidOperationException($"The inventory content holds {packet.Items.Length} items and exceeds its slot limit.");
            if (!_containers.ContainsKey(packet.Container) && _containers.Count >= MaxContainers)
                throw new InvalidOperationException("The inventory store holds too many containers.");
            _containers[packet.Container] = new Entry([.. packet.Items], packet.StorageItem);
        }
    }

    /// <summary>Replaces one authoritative slot inside a known container under the current generation and ignores packets from a superseded generation.</summary>
    internal void ApplySlot(BedrockInventorySlotPacket packet, BedrockContainerAddress address, int generation)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (generation != _generation)
                return;
            BedrockContainerAddress effective = packet.Container ?? address;
            if (effective.WindowId != packet.WindowId)
                throw new InvalidOperationException("The inventory slot address does not match its window.");
            if (!_containers.TryGetValue(effective, out Entry? entry))
                throw new InvalidOperationException("The inventory slot targets an unknown container.");
            if (packet.Slot < 0 || packet.Slot >= entry.Items.Length)
                throw new InvalidOperationException($"The inventory slot index {packet.Slot} is outside its container.");
            BedrockItemInstance[] items = [.. entry.Items];
            items[packet.Slot] = packet.Item;
            BedrockItemInstance storage = packet.StorageItem ?? entry.Storage;
            bool[] known = [.. entry.Known];
            BedrockItemStackResponseSlot?[] corrections = [.. entry.Corrections];
            known[packet.Slot] = true;
            corrections[packet.Slot] = null;
            object[] receipts = [.. entry.Receipts];
            receipts[packet.Slot] = new object();
            BedrockTentativeInventorySnapshot?[] tentative = [.. entry.Tentative];
            tentative[packet.Slot] = null;
            _containers[effective] = new Entry(items, storage, known, corrections, receipts, tentative);
        }
    }

    /// <summary>Removes every stored container that belongs to one window and returns how many were removed.</summary>
    internal int RemoveWindow(int windowId)
    {
        lock (_gate)
        {
            List<BedrockContainerAddress> doomed = [];
            foreach (BedrockContainerAddress address in _containers.Keys)
            {
                if (address.WindowId == windowId)
                    doomed.Add(address);
            }

            foreach (BedrockContainerAddress address in doomed)
                _containers.Remove(address);
            return doomed.Count;
        }
    }

    /// <summary>Reads one authoritative point-in-time copy of a container, or null when its content is currently unknown.</summary>
    internal BedrockInventorySnapshot? GetSnapshot(BedrockContainerAddress address)
    {
        lock (_gate)
            return _containers.TryGetValue(address, out Entry? entry) ? new BedrockInventorySnapshot(address, [.. entry.Items.Select(CloneItem)], CloneItem(entry.Storage))
            {
                KnownSlots = [.. entry.Known],
                ResponseCorrections = [.. entry.Corrections],
                AuthorityReceipts = [.. entry.Receipts],
            } : null;
    }

    /// <summary>Checks that retained affected slots still describe usable current items before an operation is admitted.</summary>
    internal bool MatchesSnapshot(BedrockInventorySnapshot before, IReadOnlyList<int> affectedSlots, int generation)
    {
        lock (_gate)
            return generation == _generation && _containers.TryGetValue(before.Address, out Entry? entry)
                && affectedSlots.All(slot => slot >= 0 && slot < entry.Items.Length && before.IsSlotKnown(slot)
                    && entry.Known[slot] && entry.Items[slot].Equals(before.Items[slot]) && SameCorrection(entry, before, slot));
    }

    /// <summary>Applies a correlated accepted response atomically, retaining unrelated slots and making incomplete or superseded affected data unavailable.</summary>
    internal void ApplyResponseUpdates(BedrockInventorySnapshot before, IReadOnlyList<BedrockInventoryReconciledSlot> updates, IReadOnlyList<int> affectedSlots, int generation)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(affectedSlots);
        lock (_gate)
        {
            if (generation != _generation || !_containers.TryGetValue(before.Address, out Entry? entry))
                return;
            if (affectedSlots.Any(slot => slot < 0 || slot >= entry.Items.Length || slot >= before.Items.Length)
                || affectedSlots.Distinct().Count() != affectedSlots.Count
                || updates.Any(update => !affectedSlots.Contains(update.Slot))
                || updates.Select(update => update.Slot).Distinct().Count() != updates.Count)
                throw new InvalidOperationException("The response updates do not identify distinct retained inventory slots.");

            // Tentative authority requires the explicit receipt guard; generic accepted-response handling must not bypass it.
            if (affectedSlots.Any(slot => entry.Tentative[slot].HasValue))
                return;

            bool changed = affectedSlots.Any(slot => !before.IsSlotKnown(slot) || !entry.Known[slot] || !SameCorrection(entry, before, slot)
                || (!entry.Items[slot].Equals(before.Items[slot]) && !updates.Any(update => update.Slot == slot && entry.Items[slot].Equals(update.Item))));
            BedrockItemInstance[] items = [.. entry.Items];
            bool[] known = [.. entry.Known];
            BedrockItemStackResponseSlot?[] corrections = [.. entry.Corrections];
            foreach (int slot in affectedSlots)
            {
                known[slot] = false;
                corrections[slot] = null;
            }

            if (!changed)
            {
                foreach (BedrockInventoryReconciledSlot update in updates)
                {
                    items[update.Slot] = update.Item;
                    known[update.Slot] = true;
                    corrections[update.Slot] = update.Response;
                }
            }

            object[] receipts = [.. entry.Receipts];
            object publication = new();
            foreach (int slot in affectedSlots)
                receipts[slot] = publication;
            _containers[before.Address] = new Entry(items, entry.Storage, known, corrections, receipts, [.. entry.Tentative]);
        }
    }

    /// <summary>Qualifies both retained slot-zero receipts and data under one Store lock.</summary>
    internal bool MatchesCursorPlaceSnapshots(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, int generation)
    {
        lock (_gate)
            return generation == _generation && ValidCursorPlaceAddresses(cursor, player) && MatchesReceipt(cursor) && MatchesReceipt(player);
    }

    /// <summary>Publishes the two current transfer outcomes together, preserving all newer authority when either baseline is superseded.</summary>
    internal bool TryApplyCursorPlaceResponse(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, IReadOnlyList<BedrockInventoryReconciledSlot> cursorUpdates, IReadOnlyList<BedrockInventoryReconciledSlot> playerUpdates, int generation)
    {
        ArgumentNullException.ThrowIfNull(cursorUpdates);
        ArgumentNullException.ThrowIfNull(playerUpdates);
        lock (_gate)
        {
            if (cursorUpdates.Count > 1 || playerUpdates.Count > 1 || cursorUpdates.Any(update => update.Slot != 0) || playerUpdates.Any(update => update.Slot != 0))
                throw new InvalidOperationException("The cursor transfer updates must identify only the two retained slot-zero keys.");
            if (generation != _generation || !ValidCursorPlaceAddresses(cursor, player) || !MatchesReceipt(cursor) || !MatchesReceipt(player))
                return false;
            object publication = new();
            Entry cursorResult = PrepareSlotZero(_containers[cursor.Address], cursorUpdates, publication);
            Entry playerResult = PrepareSlotZero(_containers[player.Address], playerUpdates, publication);
            _containers[cursor.Address] = cursorResult;
            _containers[player.Address] = playerResult;
            return true;
        }
    }

    /// <summary>Retires only still-owned affected slots; an actual replacement receipt always retains its known data.</summary>
    internal void InvalidateCursorPlaceSlots(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || !ValidCursorPlaceAddresses(cursor, player))
                return;
            object publication = new();
            if (MatchesReceipt(cursor))
                _containers[cursor.Address] = PrepareSlotZero(_containers[cursor.Address], [], publication);
            if (MatchesReceipt(player))
                _containers[player.Address] = PrepareSlotZero(_containers[player.Address], [], publication);
        }
    }

    private static bool ValidCursorPlaceAddresses(BedrockInventorySnapshot cursor, BedrockInventorySnapshot player) =>
        cursor.Address == new BedrockContainerAddress(124, 0, null) && player.Address == new BedrockContainerAddress(0, 0, null)
        && !cursor.Items.IsDefault && !player.Items.IsDefault
        && cursor.Items.Length == 54 && player.Items.Length is > 0 and <= MaxSlotsPerContainer;

    private bool MatchesReceipt(BedrockInventorySnapshot before) =>
        !before.Items.IsDefault && before.Items.Length > 0
        && !before.AuthorityReceipts.IsDefault && before.AuthorityReceipts.Length == before.Items.Length
        && (before.KnownSlots.IsDefault || before.KnownSlots.Length == before.Items.Length)
        && (before.ResponseCorrections.IsDefault || before.ResponseCorrections.Length == before.Items.Length) && before.IsSlotKnown(0)
        && _containers.TryGetValue(before.Address, out Entry? entry) && entry.Items.Length > 0 && entry.Known[0]
        && ReferenceEquals(entry.Receipts[0], before.AuthorityReceipts[0])
        && entry.Items[0].Equals(before.Items[0]) && SameCorrection(entry, before, 0);

    private static Entry PrepareSlotZero(Entry entry, IReadOnlyList<BedrockInventoryReconciledSlot> updates, object publication)
    {
        BedrockItemInstance[] items = [.. entry.Items];
        bool[] known = [.. entry.Known];
        BedrockItemStackResponseSlot?[] corrections = [.. entry.Corrections];
        object[] receipts = [.. entry.Receipts];
        known[0] = updates.Count == 1;
        corrections[0] = updates.Count == 1 ? updates[0].Response : null;
        if (updates.Count == 1)
            items[0] = updates[0].Item;
        receipts[0] = publication;
        BedrockTentativeInventorySnapshot?[] tentative = [.. entry.Tentative];
        tentative[0] = null;
        return new Entry(items, entry.Storage, known, corrections, receipts, tentative);
    }

    /// <summary>Drops every stored container and advances the generation after a lifecycle boundary.</summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _containers.Clear();
            _generation++;
        }
    }

    /// <summary>Drops transient windows after a lifecycle boundary while keeping the stable player inventory readable under the next generation.</summary>
    /// <remarks>A stable player address has window identifier zero and no dynamic container identifier.</remarks>
    internal void InvalidateTransient()
    {
        lock (_gate)
        {
            List<BedrockContainerAddress> doomed = [];
            foreach (BedrockContainerAddress address in _containers.Keys)
            {
                if (!IsStablePlayerAddress(address))
                    doomed.Add(address);
            }

            foreach (BedrockContainerAddress address in doomed)
                _containers.Remove(address);
            foreach ((BedrockContainerAddress address, Entry entry) in _containers.ToArray())
            {
                if (entry.Tentative.Any(tentative => tentative.HasValue))
                    _containers[address] = new Entry([.. entry.Items], entry.Storage, [.. entry.Known], [.. entry.Corrections], [.. entry.Receipts]);
            }
            _generation++;
        }
    }

    private static bool SameCorrection(Entry entry, BedrockInventorySnapshot before, int slot) =>
        Equals(entry.Corrections[slot], before.ResponseCorrections.IsDefault ? null : before.ResponseCorrections[slot]);

    private static bool IsStablePlayerAddress(BedrockContainerAddress address) =>
        address.WindowId == 0 && !address.DynamicContainerId.HasValue;

    private sealed class Entry(BedrockItemInstance[] items, BedrockItemInstance storage, bool[]? known = null, BedrockItemStackResponseSlot?[]? corrections = null, object[]? receipts = null, BedrockTentativeInventorySnapshot?[]? tentative = null)
    {
        internal BedrockItemInstance[] Items { get; } = items;

        internal BedrockItemInstance Storage { get; } = storage;

        internal bool[] Known { get; } = known ?? Enumerable.Repeat(true, items.Length).ToArray();

        internal BedrockItemStackResponseSlot?[] Corrections { get; } = corrections ?? new BedrockItemStackResponseSlot?[items.Length];

        internal object[] Receipts { get; } = receipts ?? Enumerable.Repeat(new object(), items.Length).ToArray();

        internal BedrockTentativeInventorySnapshot?[] Tentative { get; } = tentative ?? new BedrockTentativeInventorySnapshot?[items.Length];
    }
}

/// <summary>Separates a tentative remainder and typed even legacy identity from the last server item, correction and managed receipt.</summary>
internal readonly record struct BedrockTentativeInventorySnapshot(BedrockContainerAddress Address, int Slot, int Generation, int LegacyRequestId, object AuthorityReceipt, BedrockItemInstance AuthorityItem, BedrockItemInstance TentativeItem, BedrockItemStackResponseSlot? AuthorityCorrection);
