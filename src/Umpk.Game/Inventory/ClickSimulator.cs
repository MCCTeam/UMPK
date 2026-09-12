using Umpk.Game.Items;

namespace Umpk.Game.Inventory;

/// <summary>The pure click-semantics function. Given a container state, a semantic <see cref="ClickAction"/>, and the <see cref="SlotLayout"/>, it returns the predicted next state, the changed-slot list a client sends, and the new cursor. It has no I/O or session coupling and is deterministic. The function mutates only its own scratch copy of the state.</summary>
public static class ClickSimulator
{
    // Drag types: 0 = left (even split), 1 = right (one each), 2 = middle (creative full).
    private const int DragTypeEven = 0;
    private const int DragTypeSingle = 1;
    private const int DragTypeClone = 2;

    /// <summary>Applies a click to a container snapshot and returns the prediction.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ClickResult Apply(ContainerSnapshot state, ClickAction action, SlotLayout layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(layout);

        var work = new Work(state, layout);

        switch (action)
        {
            case ClickAction.Pickup pickup:
                ApplyPickup(work, pickup);
                break;
            case ClickAction.QuickMove quickMove:
                ApplyQuickMove(work, quickMove);
                break;
            case ClickAction.Swap swap:
                ApplySwap(work, swap);
                break;
            case ClickAction.CloneSlot clone:
                ApplyClone(work, clone);
                break;
            case ClickAction.Throw t:
                ApplyThrow(work, t);
                break;
            case ClickAction.PickupAll pickupAll:
                ApplyPickupAll(work, pickupAll);
                break;
            case ClickAction.Drag drag:
                ApplyDrag(work, drag);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled click action.");
        }

        return work.Build(state.StateId);
    }

    // PICKUP (mode 0): the doClick PICKUP branch, including the -999 outside-window drop.
    private static void ApplyPickup(Work work, ClickAction.Pickup pickup)
    {
        int slotIndex = pickup.Slot;
        bool primary = pickup.Button == MouseButton.Left;

        if (slotIndex == -999)
        {
            // Click outside the window: drop the carried stack (all on left, one on right).
            if (!work.Cursor.IsEmpty)
                if (primary)
                {
                    work.Drop(work.Cursor);
                    work.SetCursor(ItemStack.Empty);
                }
                else
                {
                    work.Drop(work.Cursor.WithCount(1));
                    work.SetCursor(work.Cursor.Shrink(1));
                }

            return;
        }

        if (slotIndex < 0 || slotIndex >= work.SlotCount)
            return;

        ItemStack clicked = work.Get(slotIndex);
        ItemStack carried = work.Cursor;

        // Server-managed output slots: takes are predicted, places refused (the server is authoritative here).
        bool output = work.Layout.IsOutputSlot(slotIndex);
        if (output)
            work.MarkServerAuthoritative();

        if (clicked.IsEmpty)
        {
            if (!carried.IsEmpty && !output)
            {
                int amount = primary ? carried.Count : 1;
                work.SetCursor(SafeInsert(work, slotIndex, carried, amount));
            }
        }
        else
        {
            // slot.mayPickup is always true for player-reachable slots here.
            if (carried.IsEmpty)
            {
                int amount = primary ? clicked.Count : (clicked.Count + 1) / 2;
                ItemStack taken = TryRemove(work, slotIndex, amount, int.MaxValue);
                work.SetCursor(taken);
            }
            else if (!output && carried.IsSameItemSameComponents(clicked))
            {
                // Same item: insert from cursor into the slot up to the max stack size.
                int amount = primary ? carried.Count : 1;
                work.SetCursor(SafeInsert(work, slotIndex, carried, amount));
            }
            else if (!output && carried.Count <= MaxStackSizeFor(work, slotIndex, carried))
            {
                // Different item, cursor fits: swap slot and cursor.
                work.Set(slotIndex, carried);
                work.SetCursor(clicked);
            }
            else if (output && carried.IsSameItemSameComponents(clicked))
            {
                // Output slot with matching cursor: grow the cursor by what the slot yields.
                int room = carried.MaxStackSize - carried.Count;
                ItemStack taken = TryRemove(work, slotIndex, clicked.Count, room);
                if (!taken.IsEmpty)
                    work.SetCursor(carried.Grow(taken.Count));

            }
        }
    }

    // QUICK_MOVE (mode 1, shift-click): repeat quickMoveStack until it stops moving, per doClick.
    private static void ApplyQuickMove(Work work, ClickAction.QuickMove quickMove)
    {
        int slotIndex = quickMove.Slot;
        if (slotIndex < 0 || slotIndex >= work.SlotCount)
            return;

        if (work.Layout.IsOutputSlot(slotIndex))
            work.MarkServerAuthoritative();

        // Vanilla loops quickMoveStack while the moved item stays the same; our QuickMoveStack already drains the whole source stack across its target ranges, matching the loop's fixed point.
        QuickMoveStack(work, slotIndex);
    }

    // SWAP (mode 2): exchange a slot with a hotbar (0..8) or offhand slot.
    private static void ApplySwap(Work work, ClickAction.Swap swap)
    {
        int target = swap.Slot;
        int hotbarButton = swap.HotbarButton;
        if (target < 0 || target >= work.SlotCount)
            return;

        if (!work.TryResolveInventorySlot(hotbarButton, out int sourceSlot))
        {
            // The player-inventory mirror slot for this hotbar/offhand button is not in this window.
            work.MarkServerAuthoritative();
            return;
        }

        bool output = work.Layout.IsOutputSlot(target);
        if (output)
            work.MarkServerAuthoritative();

        ItemStack source = work.Get(sourceSlot);
        ItemStack targetItem = work.Get(target);
        if (source.IsEmpty && targetItem.IsEmpty)
            return;

        if (source.IsEmpty)
        {
            // Take the target into the hotbar slot (may be an output slot; take is allowed).
            work.Set(sourceSlot, targetItem);
            work.Set(target, ItemStack.Empty);
        }
        else if (targetItem.IsEmpty)
        {
            if (output)
            {
                return; // cannot place into an output slot
            }

            int max = MaxStackSizeFor(work, target, source);
            if (source.Count > max)
            {
                work.Set(target, source.WithCount(max));
                work.Set(sourceSlot, source.Shrink(max));
            }
            else
            {
                work.Set(sourceSlot, ItemStack.Empty);
                work.Set(target, source);
            }
        }
        else if (!output)
        {
            int max = MaxStackSizeFor(work, target, source);
            if (source.Count > max)
            {
                // Vanilla: split into the target, drop/return the displaced target item to the inventory.
                work.Set(target, source.WithCount(max));
                work.Set(sourceSlot, source.Shrink(max));
                work.PlaceInInventoryOrDrop(targetItem);
            }
            else
            {
                work.Set(sourceSlot, targetItem);
                work.Set(target, source);
            }
        }
    }

    // CLONE (mode 3): creative middle-click. The simulator assumes creative (the codec only sends this in creative); it clones the slot into an empty cursor at full stack size.
    private static void ApplyClone(Work work, ClickAction.CloneSlot clone)
    {
        int slotIndex = clone.Slot;
        if (slotIndex < 0 || slotIndex >= work.SlotCount || !work.Cursor.IsEmpty)
            return;

        ItemStack slot = work.Get(slotIndex);
        if (!slot.IsEmpty)
            work.SetCursor(slot.WithCount(slot.MaxStackSize));

    }

    // THROW (mode 4): drop one (button 0) or the whole stack (button 1) from a slot when cursor is empty.
    private static void ApplyThrow(Work work, ClickAction.Throw t)
    {
        int slotIndex = t.Slot;
        if (slotIndex < 0 || slotIndex >= work.SlotCount || !work.Cursor.IsEmpty)
            return;

        if (work.Layout.IsOutputSlot(slotIndex))
            work.MarkServerAuthoritative();

        int amount = t.WholeStack ? work.Get(slotIndex).Count : 1;
        ItemStack removed = SafeTake(work, slotIndex, amount);
        if (!removed.IsEmpty)
            work.Drop(removed);

    }

    // PICKUP_ALL (mode 6, double-click): gather matching items from the whole window into the cursor.
    private static void ApplyPickupAll(Work work, ClickAction.PickupAll pickupAll)
    {
        int slotIndex = pickupAll.Slot;
        if (slotIndex < 0 || slotIndex >= work.SlotCount)
            return;

        ItemStack carried = work.Cursor;
        ItemStack clickedSlot = work.Get(slotIndex);

        // Vanilla gathers only with a non-empty cursor over an empty or unpickable clicked slot.
        if (carried.IsEmpty || (!clickedSlot.IsEmpty && CanPickup(work, slotIndex)))
            return;

        // Button 0 scans forward from slot 0; button 1 scans backward from the last slot. Two passes each: first skip already-full stacks, then take from them too.
        int start = pickupAll.Button == MouseButton.Right ? work.SlotCount - 1 : 0;
        int step = pickupAll.Button == MouseButton.Right ? -1 : 1;

        for (int pass = 0; pass < 2; pass++)
            for (int i = start; i >= 0 && i < work.SlotCount && carried.Count < carried.MaxStackSize; i += step)
            {
                if (work.Layout.IsOutputSlot(i))
                    continue;

                ItemStack candidate = work.Get(i);
                if (candidate.IsEmpty || !candidate.IsSameItemSameComponents(carried))
                    continue;

                if (pass == 0 && candidate.Count == candidate.MaxStackSize)
                    continue;

                int room = carried.MaxStackSize - carried.Count;
                ItemStack removed = SafeTake(work, i, Math.Min(candidate.Count, room));
                carried = carried.Grow(removed.Count);
            }

        work.SetCursor(carried);
    }

    // QUICK_CRAFT (mode 5, drag). Start/Add only preview in vanilla; the container state changes at End, which carries the accumulated slot set (the simulator is pure and does not retain cross-call state).
    private static void ApplyDrag(Work work, ClickAction.Drag drag)
    {
        switch (drag.Stage)
        {
            case DragStage.Start:
            case DragStage.Add:
                // No committed state change until release.
                break;

            case DragStage.End:
                EndDrag(work, DragTypeFor(drag.Button), drag.Slots ?? []);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(drag), drag.Stage, "Unhandled drag stage.");
        }
    }

    private static void EndDrag(Work work, int dragType, IReadOnlyList<int> requestedSlots)
    {
        ItemStack carried = work.Cursor;

        // Filter to slots the drag may actually place into (canItemQuickReplace), deduplicated in order.
        var slots = new List<int>(requestedSlots.Count);
        foreach (int slot in requestedSlots)
            if (!slots.Contains(slot) && CanDragInto(work, slot))
                slots.Add(slot);

        if (carried.IsEmpty || slots.Count == 0)
            return;

        // Single slot behaves like a plain pickup with the drag button (vanilla recurses into PICKUP).
        if (slots.Count == 1)
        {
            MouseButton button = dragType == DragTypeSingle ? MouseButton.Right : MouseButton.Left;
            ApplyPickup(work, new ClickAction.Pickup(slots[0], button));
            return;
        }

        int remaining = carried.Count;
        foreach (int slot in slots)
        {
            // Vanilla stops distributing once the source can no longer cover one item per remaining slot.
            if (dragType != DragTypeClone && carried.Count < slots.Count)
                continue;

            ItemStack existing = work.Get(slot);
            int have = existing.IsEmpty ? 0 : existing.Count;
            int max = MaxStackSizeFor(work, slot, carried);
            int place = Math.Min(QuickCraftPlaceCount(slots.Count, dragType, carried) + have, max);
            remaining -= place - have;
            work.Set(slot, carried.WithCount(place));
        }

        work.SetCursor(remaining > 0 ? carried.WithCount(remaining) : ItemStack.Empty);
    }

    // slot.safeInsert(carried, amount): put up to `amount` of `carried` into the slot, return leftover cursor.
    private static ItemStack SafeInsert(Work work, int slotIndex, ItemStack carried, int amount)
    {
        if (carried.IsEmpty)
            return carried;

        ItemStack existing = work.Get(slotIndex);
        int max = MaxStackSizeFor(work, slotIndex, carried);

        if (existing.IsEmpty)
        {
            int toPlace = Math.Min(amount, Math.Min(carried.Count, max));
            work.Set(slotIndex, carried.WithCount(toPlace));
            return carried.Shrink(toPlace);
        }

        if (!existing.IsSameItemSameComponents(carried))
            return carried;

        int room = max - existing.Count;
        if (room <= 0)
            return carried;

        int moved = Math.Min(amount, Math.Min(carried.Count, room));
        work.Set(slotIndex, existing.Grow(moved));
        return carried.Shrink(moved);
    }

    // slot.tryRemove(count, limit, player): take min(count, limit) from the slot, return what was taken.
    private static ItemStack TryRemove(Work work, int slotIndex, int count, int limit)
    {
        ItemStack existing = work.Get(slotIndex);
        if (existing.IsEmpty)
            return ItemStack.Empty;

        int take = Math.Min(count, Math.Min(limit, existing.Count));
        if (take <= 0)
            return ItemStack.Empty;

        work.Set(slotIndex, existing.Shrink(take));
        return existing.WithCount(take);
    }

    // slot.safeTake(amount, limit, player) collapses to tryRemove for the modeled slots.
    private static ItemStack SafeTake(Work work, int slotIndex, int amount) =>
        TryRemove(work, slotIndex, amount, int.MaxValue);

    private static int MaxStackSizeFor(Work work, int slotIndex, ItemStack stack) =>
        // The effective maximum is the lower of the slot limit and stack limit. Normal container slots allow 64 items, while special menus can set a per-slot limit. The layout carries that limit.
        Math.Min(work.Layout.SlotStackLimit(slotIndex), stack.MaxStackSize);

    private static bool CanPickup(Work work, int slotIndex) => !work.Layout.IsOutputSlot(slotIndex);

    private static bool CanDragInto(Work work, int slot)
    {
        if (slot < 0 || slot >= work.SlotCount || work.Layout.IsOutputSlot(slot))
            return false;

        // An empty slot or a slot with the same item can join the drag set. The inclusive size check means that a full same-item stack stays in the set and counts toward the even-split denominator; it then receives zero items in the placement pass because the place count is clamped to the max stack size.
        ItemStack existing = work.Get(slot);
        ItemStack carried = work.Cursor;
        if (existing.IsEmpty)
            return true;

        return existing.IsSameItemSameComponents(carried) && existing.Count <= carried.MaxStackSize;
    }

    private static int DragTypeFor(MouseButton button) => button switch
    {
        MouseButton.Left => DragTypeEven,
        MouseButton.Right => DragTypeSingle,
        MouseButton.Middle => DragTypeClone,
        _ => DragTypeEven,
    };

    // getQuickCraftPlaceCount: even split, one each, or full stack (creative).
    private static int QuickCraftPlaceCount(int slotCount, int dragType, ItemStack stack) => dragType switch
    {
        DragTypeEven => stack.Count / slotCount,
        DragTypeSingle => 1,
        DragTypeClone => stack.MaxStackSize,
        _ => stack.Count,
    };

    // moveItemStackTo across the source slot's target ranges: merge into matching stacks first, then fill the first empty slot, mirroring shift-click stack transfer for each range.
    private static void QuickMoveStack(Work work, int sourceSlot)
    {
        ItemStack moving = work.Get(sourceSlot);
        if (moving.IsEmpty)
            return;

        IReadOnlyList<SlotRange> ranges = work.Layout.QuickMoveTargets(sourceSlot);
        foreach (SlotRange range in ranges)
        {
            moving = MoveItemStackTo(work, moving, range);
            if (moving.IsEmpty)
                break;

        }

        work.Set(sourceSlot, moving);
    }

    private static ItemStack MoveItemStackTo(Work work, ItemStack moving, SlotRange range)
    {
        // Merge pass (only for stackable items): fill existing matching stacks.
        if (moving.MaxStackSize > 1)
            moving = ForEachInRange(work, range, moving, mergeOnly: true);

        // Placement pass: drop into the first empty slot.
        if (!moving.IsEmpty)
            moving = ForEachInRange(work, range, moving, mergeOnly: false);

        return moving;
    }

    private static ItemStack ForEachInRange(Work work, SlotRange range, ItemStack moving, bool mergeOnly)
    {
        int start = range.Reverse ? range.End - 1 : range.Start;
        int step = range.Reverse ? -1 : 1;
        for (int slot = start; range.Reverse ? slot >= range.Start : slot < range.End; slot += step)
        {
            if (moving.IsEmpty)
                break;

            if (slot < 0 || slot >= work.SlotCount || work.Layout.IsOutputSlot(slot))
                continue;

            ItemStack target = work.Get(slot);
            if (mergeOnly)
            {
                if (target.IsEmpty || !target.IsSameItemSameComponents(moving))
                    continue;

                int max = MaxStackSizeFor(work, slot, target);
                int total = target.Count + moving.Count;
                if (total <= max)
                {
                    work.Set(slot, target.WithCount(total));
                    moving = ItemStack.Empty;
                }
                else if (target.Count < max)
                {
                    work.Set(slot, target.WithCount(max));
                    moving = moving.Shrink(max - target.Count);
                }
            }
            else
            {
                if (!target.IsEmpty)
                    continue;

                int max = MaxStackSizeFor(work, slot, moving);
                int place = Math.Min(moving.Count, max);
                work.Set(slot, moving.WithCount(place));
                moving = moving.Shrink(place);
                break; // vanilla places into a single empty slot then stops this pass
            }
        }

        return moving;
    }

    /// <summary>The mutable scratch state the simulator works over; built from an immutable snapshot.</summary>
    private sealed class Work
    {
        private readonly ItemStack[] _slots;
        private readonly List<SlotChange> _changed = [];
        private readonly HashSet<int> _changedSet = [];
        private readonly List<ItemStack> _dropped = [];
        private ItemStack _cursor;
        private bool _serverAuthoritative;

        public Work(ContainerSnapshot snapshot, SlotLayout layout)
        {
            _slots = snapshot.CopySlots();
            _cursor = snapshot.Cursor;
            Layout = layout;
        }

        public SlotLayout Layout { get; }

        public int SlotCount => _slots.Length;

        public ItemStack Cursor => _cursor;

        public ItemStack Get(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot] : ItemStack.Empty;

        public void Set(int slot, ItemStack value)
        {
            if (slot < 0 || slot >= _slots.Length)
                return;

            ItemStack normalized = value.IsEmpty ? ItemStack.Empty : value;
            _slots[slot] = normalized;
            if (_changedSet.Add(slot))
                _changed.Add(new SlotChange(slot, normalized));

            else
                for (int i = 0; i < _changed.Count; i++)
                    if (_changed[i].Slot == slot)
                    {
                        _changed[i] = new SlotChange(slot, normalized);
                        break;
                    }

        }

        public void SetCursor(ItemStack value) => _cursor = value.IsEmpty ? ItemStack.Empty : value;

        public void Drop(ItemStack stack)
        {
            if (!stack.IsEmpty)
                _dropped.Add(stack);

        }

        public void MarkServerAuthoritative() => _serverAuthoritative = true;

        // Player-inventory mirror: hotbar buttons 0..8 map to the window's hotbar range, 40 to the offhand.
        public bool TryResolveInventorySlot(int hotbarButton, out int windowSlot)
        {
            windowSlot = -1;
            if (hotbarButton == 40)
            {
                for (int i = 0; i < _slots.Length; i++)
                    if (Layout.RoleOf(i) == SlotRole.Offhand)
                    {
                        windowSlot = i;
                        return true;
                    }

                return false;
            }

            if (hotbarButton is < 0 or > 8)
                return false;

            int seen = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (Layout.RoleOf(i) == SlotRole.PlayerHotbar)
                {
                    if (seen == hotbarButton)
                    {
                        windowSlot = i;
                        return true;
                    }

                    seen++;
                }

            return false;
        }

        // Vanilla inventory.add / placeItemBackInInventory: try to merge/place into player slots, else drop.
        public void PlaceInInventoryOrDrop(ItemStack stack)
        {
            if (stack.IsEmpty)
                return;

            ItemStack remaining = stack;
            // Merge into matching player slots first, then fill empties (hotbar + main).
            remaining = PlaceInto(remaining, SlotRole.PlayerHotbar, mergeOnly: true);
            remaining = PlaceInto(remaining, SlotRole.PlayerMain, mergeOnly: true);
            remaining = PlaceInto(remaining, SlotRole.PlayerHotbar, mergeOnly: false);
            remaining = PlaceInto(remaining, SlotRole.PlayerMain, mergeOnly: false);
            if (!remaining.IsEmpty)
                Drop(remaining);

        }

        private ItemStack PlaceInto(ItemStack stack, SlotRole role, bool mergeOnly)
        {
            for (int i = 0; i < _slots.Length && !stack.IsEmpty; i++)
            {
                if (Layout.RoleOf(i) != role)
                    continue;

                ItemStack existing = _slots[i];
                int max = Math.Min(64, stack.MaxStackSize);
                if (mergeOnly)
                {
                    if (existing.IsEmpty || !existing.IsSameItemSameComponents(stack) || existing.Count >= max)
                        continue;

                    int moved = Math.Min(stack.Count, max - existing.Count);
                    Set(i, existing.Grow(moved));
                    stack = stack.Shrink(moved);
                }
                else if (existing.IsEmpty)
                {
                    int place = Math.Min(stack.Count, max);
                    Set(i, stack.WithCount(place));
                    stack = stack.Shrink(place);
                }
            }

            return stack;
        }

        public ClickResult Build(int stateId)
        {
            var snapshot = new ContainerSnapshot(_slots, _cursor, stateId);
            return new ClickResult(snapshot, _changed, _serverAuthoritative, _dropped);
        }
    }
}
