using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Text;

namespace Umpk.Client.State;

/// <summary>The tracked inventory state: the player inventory (window 0), the currently open container (if any), the cursor item, and the state-id bookkeeping the click pipeline uses for prediction and reconciliation. Mutated on the session loop by inventory appliers and the click pipeline.</summary>
public sealed class InventoryState
{
    /// <summary>The default player-inventory window id.</summary>
    public const int PlayerWindowId = 0;

    // AbstractContainerMenu revisions are incremented in a 15-bit ring. Comparing ordinary integers breaks at 32767 -> 0, so causal comparisons use the same ring and its unambiguous half-range.
    private const int StateIdMask = 0x7FFF;
    private const int StateIdHalfRange = 0x4000;

    private ItemStack[] _playerSlots = CreateEmpty(46);
    private ItemStack[]? _containerSlots;
    private PendingClickPrediction? _pending;
    private readonly Dictionary<int, int> _properties = [];

    /// <summary>The id of the currently open container, or 0 for the player inventory.</summary>
    public int OpenWindowId { get; internal set; }

    /// <summary>The open container's menu-type network registry id (the id-numbered <c>minecraft:menu</c> entry), or -1 when no non-player container is open OR when the version predates the menu registry: 1.8-1.13.2 name the window with a string instead, carried by <see cref="OpenContainerLegacyWindowType"/>. Prefer <see cref="OpenContainerMenuType"/>, which resolves both eras to a single handle.</summary>
    public int OpenContainerMenuTypeId { get; internal set; } = -1;

    /// <summary>The open container's raw pre-1.14 window-type string ("minecraft:chest", "EntityHorse", ...), or null on 1.14+ and when no container is open. It is the authoritative wire key on that era and stays available even when <see cref="OpenContainerMenuType"/> could not name the window.</summary>
    public string? OpenContainerLegacyWindowType { get; internal set; }

    /// <summary>The open container's resolved <c>minecraft:menu</c> entry, or null when no container is open or the kind could not be named. This is the semantic container type: its <see cref="Umpk.Game.Registries.RegistryEntry{T}.Id"/> is a stable identifier (<c>minecraft:furnace</c>, <c>minecraft:generic_9x3</c>, ...) meaning the same thing on every supported version, so a consumer can switch on the kind instead of inferring it from the slot count and the presence of properties. Resolved from the numeric menu id on 1.14+ and from the window-type string before that.</summary>
    public RegistryEntry<MenuTypeDefinition>? OpenContainerMenuType { get; internal set; }

    /// <summary>The open container's title component, or null when no non-player container is open.</summary>
    public Component? OpenContainerTitle { get; internal set; }

    /// <summary>Whether a non-player container is currently open.</summary>
    public bool HasOpenContainer => OpenWindowId != PlayerWindowId && _containerSlots is not null;

    /// <summary>The last authoritative state id for the open window (or player inventory).</summary>
    public int StateId { get; internal set; }

    /// <summary>The cursor (carried) item.</summary>
    public ItemStack Cursor { get; internal set; } = ItemStack.Empty;

    /// <summary>The number of slots in the currently active window.</summary>
    public int ActiveSlotCount => HasOpenContainer ? _containerSlots!.Length : _playerSlots.Length;

    /// <summary>The player inventory slot contents (46 slots on modern versions).</summary>
    public IReadOnlyList<ItemStack> PlayerSlots => _playerSlots;

    /// <summary>The open container's slot contents, when a container is open.</summary>
    public IReadOnlyList<ItemStack>? ContainerSlots => _containerSlots;

    /// <summary>Merchant/villager trade offers attached to the open window, when it is a merchant.</summary>
    public MerchantOffers? Trades { get; internal set; }

    /// <summary>The open window's typed properties (furnace progress, enchant levels, ...) keyed by id.</summary>
    public IReadOnlyDictionary<int, int> Properties => _properties;

    /// <summary>Whether an optimistic click prediction is awaiting authoritative reconciliation.</summary>
    internal bool HasPendingPrediction => _pending is not null;

    /// <summary>Reads a slot from the active window.</summary>
    public ItemStack GetSlot(int index)
    {
        ItemStack[] target = HasOpenContainer ? _containerSlots! : _playerSlots;
        return index >= 0 && index < target.Length ? target[index] : ItemStack.Empty;
    }

    /// <summary>The backing array a window id addresses, or null when that window is not tracked.</summary>
    private ItemStack[]? SlotsFor(int windowId)
    {
        if (windowId == PlayerWindowId)
            return _playerSlots;

        return windowId == OpenWindowId && _containerSlots is not null ? _containerSlots : null;
    }

    /// <summary>Writes one slot addressed by window id (the id-routed model). A player-window update always targets the player array, even while a container is open, so item pickups and off-screen inventory changes never clobber the open container view.</summary>
    internal void SetSlot(int windowId, int index, ItemStack item)
    {
        ItemStack[]? target = SlotsFor(windowId);
        if (target is not null && index >= 0 && index < target.Length)
        {
            target[index] = item;
            if (windowId == OpenWindowId)
                SyncPlayerSlotsFromOpenContainer();

        }
    }

    /// <summary>Replaces a window's full contents addressed by window id (the id-routed model).</summary>
    internal void ReplaceContents(int windowId, IReadOnlyList<ItemStack> items)
    {
        var next = new ItemStack[items.Count];
        for (int i = 0; i < items.Count; i++)
            next[i] = items[i];

        if (windowId == PlayerWindowId)
            _playerSlots = next;

        else if (windowId == OpenWindowId && _containerSlots is not null)
        {
            _containerSlots = next;
            SyncPlayerSlotsFromOpenContainer();
        }
    }

    /// <summary>Records or updates a container property. Properties reset when a window opens/closes.</summary>
    internal void SetProperty(int propertyId, int value) => _properties[propertyId] = value;

    internal void OpenContainer(
        int windowId,
        int slotCount,
        int menuTypeId = -1,
        Component? title = null,
        string? legacyWindowType = null,
        RegistryEntry<MenuTypeDefinition>? menuType = null)
    {
        OpenWindowId = windowId;
        OpenContainerMenuTypeId = menuTypeId;
        OpenContainerLegacyWindowType = legacyWindowType;
        OpenContainerMenuType = menuType;
        OpenContainerTitle = title;
        _containerSlots = CreateEmpty(slotCount);
        _pending = null;
        _properties.Clear();
        Trades = null;
    }

    internal void CloseContainer()
    {
        OpenWindowId = PlayerWindowId;
        OpenContainerMenuTypeId = -1;
        OpenContainerLegacyWindowType = null;
        OpenContainerMenuType = null;
        OpenContainerTitle = null;
        _containerSlots = null;
        Cursor = ItemStack.Empty;
        _pending = null;
        _properties.Clear();
        Trades = null;
    }

    /// <summary>Closes any open container and empties the player inventory, cursor, and state id. Login creates a fresh player inventory, which the server then refills. Keeping the old slots would leave items the player no longer holds visible until each slot happened to be overwritten.</summary>
    internal void Clear()
    {
        CloseContainer();
        _playerSlots = CreateEmpty(46);
        StateId = 0;
    }

    /// <summary>Builds a snapshot of the active window for the click simulator.</summary>
    internal ContainerSnapshot CaptureActiveSnapshot()
    {
        ItemStack[] target = HasOpenContainer ? _containerSlots! : _playerSlots;
        return new ContainerSnapshot([.. target], Cursor, StateId);
    }

    internal void ApplyPredictedSnapshot(ContainerSnapshot snapshot)
    {
        var next = new ItemStack[snapshot.SlotCount];
        for (int i = 0; i < snapshot.SlotCount; i++)
            next[i] = snapshot.GetSlot(i);

        if (HasOpenContainer)
        {
            _containerSlots = next;
            SyncPlayerSlotsFromOpenContainer();
        }
        else
            _playerSlots = next;

        Cursor = snapshot.Cursor;
    }

    /// <summary>Records the pre-click and predicted snapshots of the active window and the state id the click was sent with. Rapid clicks sent at the same revision compose into the same bounded prediction: the original baseline is retained and the expected snapshot advances, so a later pre-click update cannot erase an earlier optimistic click.</summary>
    internal void RecordPrediction(
        int windowId,
        int sentStateId,
        ContainerSnapshot before,
        bool usesStateIds = true)
    {
        ContainerSnapshot predicted = CaptureActiveSnapshot();
        if (_pending is { } pending &&
            pending.WindowId == windowId &&
            pending.SentStateId == sentStateId &&
            pending.UsesStateIds == usesStateIds)
        {
            _pending = pending with { Predicted = predicted };
            return;
        }

        _pending = new PendingClickPrediction(windowId, sentStateId, before, predicted, usesStateIds);
    }

    /// <summary>Discards the pending prediction without rolling back (the prediction was confirmed).</summary>
    internal void ClearPrediction() => _pending = null;

    /// <summary>Atomically applies an authoritative per-slot update and reconciles it with a pending prediction. Same/older modern revisions predate the click: unchanged fields are rebased, while predicted fields and the pending record are preserved. A newer revision acknowledges the click and corrects only the addressed slot; rolling back the whole window for one delta would discard accepted changes for which the server has no reason to send another packet.</summary>
    internal bool ApplyAuthoritativeSlot(int windowId, int slotIndex, ItemStack item, int authoritativeStateId)
    {
        if (_pending is not { } pending || pending.WindowId != windowId)
        {
            SetSlot(windowId, slotIndex, item);
            if (windowId == OpenWindowId)
                StateId = authoritativeStateId;
            return false;
        }

        if (pending.UsesStateIds && !IsCausallyNewer(authoritativeStateId, pending.SentStateId))
        {
            // Apply a pre-click field only when the click did not change it. The packet remains useful as a rebase, but it cannot reject the later local action or move the click-facing revision back.
            if (!SlotChangedByPrediction(pending, slotIndex))
            {
                SetSlot(windowId, slotIndex, item);
                _pending = RebaseUnchangedSlot(pending, slotIndex, item);
            }
            return false;
        }

        ItemStack predicted = pending.Predicted.GetSlot(slotIndex);
        bool contentMismatch = !predicted.Equals(item);

        _pending = null;
        SetSlot(windowId, slotIndex, item);
        if (windowId == OpenWindowId)
            StateId = authoritativeStateId;
        return contentMismatch;
    }

    /// <summary>Atomically applies an authoritative full-content sync. Same/older modern revisions are rebased under the prediction and keep it pending. A causally newer full snapshot is authoritative, consumes the prediction and reports whether either slots or cursor corrected the expected result.</summary>
    internal bool ApplyAuthoritativeContent(
        int windowId,
        IReadOnlyList<ItemStack> items,
        ItemStack carriedItem,
        int authoritativeStateId)
    {
        if (_pending is not { } pending || pending.WindowId != windowId)
        {
            ReplaceContents(windowId, items);
            if (windowId == OpenWindowId)
            {
                Cursor = carriedItem;
                StateId = authoritativeStateId;
            }
            return false;
        }

        if (pending.UsesStateIds && !IsCausallyNewer(authoritativeStateId, pending.SentStateId))
        {
            var rebased = new ItemStack[items.Count];
            for (int i = 0; i < items.Count; i++)
                rebased[i] = SlotChangedByPrediction(pending, i)
                    ? pending.Predicted.GetSlot(i)
                    : items[i];

            ReplaceContents(windowId, rebased);
            ItemStack rebasedCursor = pending.Before.Cursor.Equals(pending.Predicted.Cursor)
                ? carriedItem
                : pending.Predicted.Cursor;
            if (windowId == OpenWindowId)
                Cursor = rebasedCursor;

            _pending = pending with
            {
                Before = new ContainerSnapshot(items, carriedItem, pending.Before.StateId),
                Predicted = new ContainerSnapshot(rebased, rebasedCursor, pending.Predicted.StateId),
            };
            return false;
        }

        _pending = null;
        bool contradiction = !SnapshotMatches(pending.Predicted, items, carriedItem);

        ReplaceContents(windowId, items);
        if (windowId == OpenWindowId)
        {
            Cursor = carriedItem;
            StateId = authoritativeStateId;
        }
        return contradiction;
    }

    /// <summary>Applies an unversioned cursor update. While a modern state-bearing click is pending, an update that merely repeats the pre-click cursor can have been queued before that click; preserve the predicted cursor until a causally newer container revision settles the operation.</summary>
    internal void ApplyAuthoritativeCursor(ItemStack item)
    {
        if (_pending is { UsesStateIds: true } pending &&
            !pending.Before.Cursor.Equals(pending.Predicted.Cursor) &&
            item.Equals(pending.Before.Cursor))
            return;

        Cursor = item;
    }

    private static bool SnapshotMatches(
        ContainerSnapshot predicted,
        IReadOnlyList<ItemStack> items,
        ItemStack carriedItem)
    {
        if (predicted.SlotCount != items.Count || !predicted.Cursor.Equals(carriedItem))
            return false;

        for (int i = 0; i < items.Count; i++)
            if (!predicted.GetSlot(i).Equals(items[i]))
                return false;

        return true;
    }

    private static bool SlotChangedByPrediction(PendingClickPrediction pending, int slotIndex)
    {
        if (slotIndex < 0 ||
            slotIndex >= pending.Before.SlotCount ||
            slotIndex >= pending.Predicted.SlotCount)
            return false;

        return !pending.Before.GetSlot(slotIndex).Equals(pending.Predicted.GetSlot(slotIndex));
    }

    private static PendingClickPrediction RebaseUnchangedSlot(
        PendingClickPrediction pending,
        int slotIndex,
        ItemStack item)
    {
        if (slotIndex < 0 ||
            slotIndex >= pending.Before.SlotCount ||
            slotIndex >= pending.Predicted.SlotCount)
            return pending;

        return pending with
        {
            Before = SnapshotWithSlot(pending.Before, slotIndex, item),
            Predicted = SnapshotWithSlot(pending.Predicted, slotIndex, item),
        };
    }

    private static ContainerSnapshot SnapshotWithSlot(ContainerSnapshot source, int slotIndex, ItemStack item)
    {
        var slots = new ItemStack[source.SlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i == slotIndex ? item : source.GetSlot(i);
        return new ContainerSnapshot(slots, source.Cursor, source.StateId);
    }

    private static bool IsCausallyNewer(int candidate, int reference)
    {
        int distance = (candidate - reference) & StateIdMask;
        return distance is > 0 and < StateIdHalfRange;
    }

    /// <summary>Mirrors the 36 player-inventory slots at the end of a generic open-container snapshot into the player window. The server sends those slots in the container's content and slot updates while a chest is open; without this mirror, consumers observe a stale player inventory until the container closes and can incorrectly treat a successful quick-move as a rejected one.</summary>
    private void SyncPlayerSlotsFromOpenContainer()
    {
        if (!HasOpenContainer || _containerSlots is not { Length: >= 36 } container)
            return;

        int playerStart = container.Length - 36;
        for (int i = 0; i < 36; i++)
            _playerSlots[9 + i] = container[playerStart + i];

    }

    private static ItemStack[] CreateEmpty(int count)
    {
        var slots = new ItemStack[count];
        Array.Fill(slots, ItemStack.Empty);
        return slots;
    }

    /// <summary>An in-flight optimistic click prediction awaiting authoritative reconciliation.</summary>
    private readonly record struct PendingClickPrediction(
        int WindowId,
        int SentStateId,
        ContainerSnapshot Before,
        ContainerSnapshot Predicted,
        bool UsesStateIds);
}
