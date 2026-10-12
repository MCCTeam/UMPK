using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Carries one authoritative point-in-time copy of a container with its items and storage item.</summary>
internal readonly record struct BedrockInventorySnapshot(BedrockContainerAddress Address, ImmutableArray<BedrockItemInstance> Items, BedrockItemInstance StorageItem)
{
    /// <summary>Gets which slots have usable authoritative item data; a default mask describes an entirely known retained snapshot.</summary>
    internal ImmutableArray<bool> KnownSlots { get; init; }

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
            _containers[effective] = new Entry(items, storage, known, corrections);
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
            return _containers.TryGetValue(address, out Entry? entry) ? new BedrockInventorySnapshot(address, [.. entry.Items], entry.Storage)
            {
                KnownSlots = [.. entry.Known],
                ResponseCorrections = [.. entry.Corrections],
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

            _containers[before.Address] = new Entry(items, entry.Storage, known, corrections);
        }
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
            _generation++;
        }
    }

    private static bool SameCorrection(Entry entry, BedrockInventorySnapshot before, int slot) =>
        Equals(entry.Corrections[slot], before.ResponseCorrections.IsDefault ? null : before.ResponseCorrections[slot]);

    private static bool IsStablePlayerAddress(BedrockContainerAddress address) =>
        address.WindowId == 0 && !address.DynamicContainerId.HasValue;

    private sealed class Entry(BedrockItemInstance[] items, BedrockItemInstance storage, bool[]? known = null, BedrockItemStackResponseSlot?[]? corrections = null)
    {
        internal BedrockItemInstance[] Items { get; } = items;

        internal BedrockItemInstance Storage { get; } = storage;

        internal bool[] Known { get; } = known ?? Enumerable.Repeat(true, items.Length).ToArray();

        internal BedrockItemStackResponseSlot?[] Corrections { get; } = corrections ?? new BedrockItemStackResponseSlot?[items.Length];
    }
}
