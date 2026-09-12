using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Tests.Items;
using Xunit;

namespace Umpk.Game.Tests.Inventory;

public class ClickSimulatorTests
{
    private static readonly SlotLayout Chest = TestLayouts.Chest9x3();

    // simple pickup / place

    [Fact]
    public void LeftClick_EmptyCursor_PicksUpWholeSlot()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 30).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), Chest);

        Assert.Equal(30, result.Cursor.Count);
        Assert.True(result.State.GetSlot(0).IsEmpty);
        Assert.Contains(result.ChangedSlots, c => c.Slot == 0 && c.Item.IsEmpty);
    }

    [Fact]
    public void LeftClick_CursorIntoEmptySlot_PlacesWholeStack()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 20).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(5, MouseButton.Left), Chest);

        Assert.True(result.Cursor.IsEmpty);
        Assert.Equal(20, result.State.GetSlot(5).Count);
    }

    // stack merge with overflow

    [Fact]
    public void LeftClick_MergeSameItem_OverflowStaysOnCursor()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 60).Cursor("stone", 20).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), Chest);

        Assert.Equal(64, result.State.GetSlot(0).Count);
        Assert.Equal(16, result.Cursor.Count); // 20 -
    }

    [Fact]
    public void LeftClick_DifferentItems_Swaps()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 10).Cursor("dirt", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), Chest);

        Assert.Equal(Identifier.Minecraft("dirt"), result.State.GetSlot(0).Item.Id);
        Assert.Equal(5, result.State.GetSlot(0).Count);
        Assert.Equal(Identifier.Minecraft("stone"), result.Cursor.Item.Id);
        Assert.Equal(10, result.Cursor.Count);
    }

    // right-click half / one

    [Fact]
    public void RightClick_EmptyCursor_TakesHalfRoundedUp()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 7).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Right), Chest);

        Assert.Equal(4, result.Cursor.Count); // ceil(7/2)
        Assert.Equal(3, result.State.GetSlot(0).Count);
    }

    [Fact]
    public void RightClick_CursorWithItems_DropsOne()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Right), Chest);

        Assert.Equal(1, result.State.GetSlot(0).Count);
        Assert.Equal(4, result.Cursor.Count);
    }

    [Fact]
    public void RightClick_CursorOntoMatchingStack_AddsOne()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 10).Cursor("stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Right), Chest);

        Assert.Equal(11, result.State.GetSlot(0).Count);
        Assert.Equal(4, result.Cursor.Count);
    }

    // shift-click distribution into ranges with partial fills

    [Fact]
    public void ShiftClick_FromStorage_MovesToEmptyPlayerSlot()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 40).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.QuickMove(0, MouseButton.Left), Chest);

        Assert.True(result.State.GetSlot(0).IsEmpty);
        // Player range is [27,63); first empty is slot 27.
        Assert.Equal(40, result.State.GetSlot(27).Count);
    }

    [Fact]
    public void ShiftClick_MergesIntoPartialThenOverflowsToEmpty()
    {
        var state = new SnapshotBuilder(63)
            .Slot(0, "stone", 40)
            .Slot(27, "stone", 40) // partial target: room for 24
            .Build();
        var result = ClickSimulator.Apply(state, new ClickAction.QuickMove(0, MouseButton.Left), Chest);

        Assert.Equal(64, result.State.GetSlot(27).Count);
        Assert.Equal(16, result.State.GetSlot(28).Count); // remaining 16 into next empty
        Assert.True(result.State.GetSlot(0).IsEmpty);
    }

    // drag: three modes incl. uneven division

    private static ClickResult EndDrag(ContainerSnapshot state, MouseButton button, params int[] slots) =>
        ClickSimulator.Apply(state, new ClickAction.Drag(DragStage.End, button, Slots: slots), Chest);

    [Fact]
    public void DragStart_And_Add_DoNotChangeStateBeforeRelease()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 6).Build();
        var afterStart = ClickSimulator.Apply(state, new ClickAction.Drag(DragStage.Start, MouseButton.Left), Chest);
        var afterAdd = ClickSimulator.Apply(afterStart.State, new ClickAction.Drag(DragStage.Add, MouseButton.Left, 0), Chest);

        Assert.Equal(6, afterAdd.State.Cursor.Count);
        Assert.True(afterAdd.State.GetSlot(0).IsEmpty);
        Assert.Empty(afterAdd.ChangedSlots);
    }

    [Fact]
    public void LeftDrag_DistributesEvenly()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 6).Build();
        var result = EndDrag(state, MouseButton.Left, 0, 1, 2);

        Assert.Equal(2, result.State.GetSlot(0).Count);
        Assert.Equal(2, result.State.GetSlot(1).Count);
        Assert.Equal(2, result.State.GetSlot(2).Count);
        Assert.True(result.Cursor.IsEmpty);
    }

    [Fact]
    public void LeftDrag_UnevenDivision_LeavesRemainderOnCursor()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = EndDrag(state, MouseButton.Left, 0, 1);

        // 5 / 2 = 2 each, remainder 1 on cursor.
        Assert.Equal(2, result.State.GetSlot(0).Count);
        Assert.Equal(2, result.State.GetSlot(1).Count);
        Assert.Equal(1, result.Cursor.Count);
    }

    [Fact]
    public void RightDrag_PlacesOneEach()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = EndDrag(state, MouseButton.Right, 0, 1);

        Assert.Equal(1, result.State.GetSlot(0).Count);
        Assert.Equal(1, result.State.GetSlot(1).Count);
        Assert.Equal(3, result.Cursor.Count);
    }

    [Fact]
    public void MiddleDrag_Creative_FillsEachToMax()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = EndDrag(state, MouseButton.Middle, 0, 1);

        Assert.Equal(64, result.State.GetSlot(0).Count);
        Assert.Equal(64, result.State.GetSlot(1).Count);
    }

    // drag over mixed occupancy: empty + same-item-partial + same-item-full (hardening) canItemQuickReplace(ignoreSize=true) keeps ALL same-item slots in the set, so the even-split denominator counts the full stack too; the full slot then receives zero items (clamped).

    [Fact]
    public void LeftDrag_MixedEmptyPartialFull_CountsFullSlotInDenominator()
    {
        var state = new SnapshotBuilder(63)
            .Slot(1, "stone", 62)  // partial: room for 2
            .Slot(2, "stone", 64)  // full: stays in the set, gets nothing
            .Cursor("stone", 10)
            .Build();
        var result = EndDrag(state, MouseButton.Left, 0, 1, 2);

        // size=3, placeCount = floor(10/3) = 3.
        Assert.Equal(3, result.State.GetSlot(0).Count);   // empty slot: +3
        Assert.Equal(64, result.State.GetSlot(1).Count);  // partial: min(3+62, 64) = 64 -> +2
        Assert.Equal(64, result.State.GetSlot(2).Count);  // full: min(3+64, 64) = 64 -> +0
        Assert.Equal(5, result.Cursor.Count);             // 10 - 3 - 2 - 0
    }

    [Fact]
    public void RightDrag_MixedEmptyPartialFull_OneEachExceptFull()
    {
        var state = new SnapshotBuilder(63)
            .Slot(1, "stone", 62)
            .Slot(2, "stone", 64)
            .Cursor("stone", 10)
            .Build();
        var result = EndDrag(state, MouseButton.Right, 0, 1, 2);

        Assert.Equal(1, result.State.GetSlot(0).Count);
        Assert.Equal(63, result.State.GetSlot(1).Count);
        Assert.Equal(64, result.State.GetSlot(2).Count); // full slot places zero
        Assert.Equal(8, result.Cursor.Count);            // 10 - 1 - 1 - 0
    }

    [Fact]
    public void LeftDrag_DifferentItemSlot_IsExcludedFromSet()
    {
        // A different-item slot fails canItemQuickReplace and drops out of the set entirely.
        var state = new SnapshotBuilder(63)
            .Slot(1, "dirt", 10)
            .Cursor("stone", 6)
            .Build();
        var result = EndDrag(state, MouseButton.Left, 0, 1, 2);

        // Surviving set = {0, 2}: placeCount = floor(6/2) = 3 each.
        Assert.Equal(3, result.State.GetSlot(0).Count);
        Assert.Equal(10, result.State.GetSlot(1).Count); // untouched
        Assert.Equal(3, result.State.GetSlot(2).Count);
        Assert.True(result.Cursor.IsEmpty);
    }

    // swap with hotbar and offhand

    [Fact]
    public void Swap_WithHotbar_ExchangesSlots()
    {
        var layout = TestLayouts.PlayerInventory();
        // Hotbar slots are 36..44; button 0 -> slot 36.
        var state = new SnapshotBuilder(46).Slot(9, "stone", 10).Slot(36, "dirt", 3).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Swap(9, 0), layout);

        Assert.Equal(Identifier.Minecraft("dirt"), result.State.GetSlot(9).Item.Id);
        Assert.Equal(Identifier.Minecraft("stone"), result.State.GetSlot(36).Item.Id);
    }

    [Fact]
    public void Swap_WithOffhand_UsesButton40()
    {
        var layout = TestLayouts.PlayerInventory();
        var state = new SnapshotBuilder(46).Slot(9, "stone", 10).Build(); // offhand empty
        var result = ClickSimulator.Apply(state, new ClickAction.Swap(9, 40), layout);

        Assert.True(result.State.GetSlot(9).IsEmpty);
        Assert.Equal(Identifier.Minecraft("stone"), result.State.GetSlot(45).Item.Id);
    }

    // clone (creative)

    [Fact]
    public void Clone_FillsCursorWithMaxStack()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.CloneSlot(0), Chest);

        Assert.Equal(64, result.Cursor.Count);
        Assert.Equal(5, result.State.GetSlot(0).Count); // source unchanged
    }

    [Fact]
    public void Clone_LimitedByItemMaxStack()
    {
        var state = new SnapshotBuilder(63).Slot(0, "ender_pearl", 1).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.CloneSlot(0), Chest);
        Assert.Equal(16, result.Cursor.Count);
    }

    // throw one / all

    [Fact]
    public void Throw_One_DropsSingleItem()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Throw(0, WholeStack: false), Chest);

        Assert.Equal(4, result.State.GetSlot(0).Count);
        Assert.Single(result.DroppedItems);
        Assert.Equal(1, result.DroppedItems[0].Count);
    }

    [Fact]
    public void Throw_All_DropsWholeStack()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Throw(0, WholeStack: true), Chest);

        Assert.True(result.State.GetSlot(0).IsEmpty);
        Assert.Equal(5, result.DroppedItems[0].Count);
    }

    [Fact]
    public void ClickOutsideWindow_DropsCursor()
    {
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(-999, MouseButton.Left), Chest);

        Assert.True(result.Cursor.IsEmpty);
        Assert.Equal(5, result.DroppedItems[0].Count);
    }

    // pickup_all gathering

    [Fact]
    public void PickupAll_GathersMatchingItemsIntoCursor()
    {
        var state = new SnapshotBuilder(63)
            .Slot(0, "stone", 10)
            .Slot(1, "stone", 20)
            .Slot(2, "dirt", 30)
            .Slot(3, "stone", 5)
            .Cursor("stone", 4)
            .Build();
        var result = ClickSimulator.Apply(state, new ClickAction.PickupAll(10), Chest); // slot 10 empty

        Assert.Equal(39, result.Cursor.Count); // 4 + 10 + 20 + 5
        Assert.True(result.State.GetSlot(0).IsEmpty);
        Assert.True(result.State.GetSlot(1).IsEmpty);
        Assert.Equal(30, result.State.GetSlot(2).Count); // dirt untouched
    }

    [Fact]
    public void PickupAll_StopsAtMaxStack()
    {
        var state = new SnapshotBuilder(63)
            .Slot(0, "stone", 40)
            .Slot(1, "stone", 40)
            .Cursor("stone", 10)
            .Build();
        var result = ClickSimulator.Apply(state, new ClickAction.PickupAll(10), Chest);

        Assert.Equal(64, result.Cursor.Count);
    }

    // Button 1 scans backward from the last slot, so a full cursor drains different slots from the forward pass.

    [Fact]
    public void PickupAll_Button1_GathersFromLastSlotFirst()
    {
        ContainerSnapshot BuildState() => new SnapshotBuilder(63)
            .Slot(0, "stone", 10)
            .Slot(50, "stone", 10)
            .Cursor("stone", 60) // room for 4 only
            .Build();

        var forward = ClickSimulator.Apply(BuildState(), new ClickAction.PickupAll(10, MouseButton.Left), Chest);
        var reverse = ClickSimulator.Apply(BuildState(), new ClickAction.PickupAll(10, MouseButton.Right), Chest);

        // Forward drains the low slot; reverse drains the high slot.
        Assert.Equal(64, forward.Cursor.Count);
        Assert.Equal(6, forward.State.GetSlot(0).Count);
        Assert.Equal(10, forward.State.GetSlot(50).Count);

        Assert.Equal(64, reverse.Cursor.Count);
        Assert.Equal(10, reverse.State.GetSlot(0).Count);
        Assert.Equal(6, reverse.State.GetSlot(50).Count);
    }

    [Fact]
    public void PickupAll_Button1_SecondPassTakesFullStacksBackward()
    {
        // Pass 0 skips full stacks in both directions; pass 1 takes from them, still backward.
        var state = new SnapshotBuilder(63)
            .Slot(0, "stone", 64)
            .Slot(1, "stone", 5)
            .Slot(2, "stone", 64)
            .Cursor("stone", 1)
            .Build();
        var result = ClickSimulator.Apply(state, new ClickAction.PickupAll(10, MouseButton.Right), Chest);

        // Pass 0 (backward): slot 1 gives 5 -> cursor 6. Pass 1 (backward): slot 2 (full) gives 58 -> cursor 64; slot 0 untouched.
        Assert.Equal(64, result.Cursor.Count);
        Assert.Equal(64, result.State.GetSlot(0).Count);
        Assert.True(result.State.GetSlot(1).IsEmpty);
        Assert.Equal(6, result.State.GetSlot(2).Count);
    }

    [Fact]
    public void Pickup_IntoCappedSlot_PlacesOnlyTheSlotLimit()
    {
        // A payment-style slot capped at 1 accepts one item and leaves the remainder on the cursor.
        var layout = TestLayouts.CappedFirstSlot();
        var state = new SnapshotBuilder(63).Cursor("stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), layout);

        Assert.Equal(1, result.State.GetSlot(0).Count);
        Assert.Equal(4, result.Cursor.Count);
    }

    [Fact]
    public void LeftDrag_IntoCappedSlot_ClampsToSlotLimit()
    {
        var layout = TestLayouts.CappedFirstSlot();
        var state = new SnapshotBuilder(63).Cursor("stone", 8).Build();
        var result = ClickSimulator.Apply(
            state,
            new ClickAction.Drag(DragStage.End, MouseButton.Left, Slots: new[] { 0, 1 }),
            layout);

        // placeCount = 4 each; slot 0 clamps to its limit of 1, slot 1 takes 4; remainder 3.
        Assert.Equal(1, result.State.GetSlot(0).Count);
        Assert.Equal(4, result.State.GetSlot(1).Count);
        Assert.Equal(3, result.Cursor.Count);
    }

    // server-managed output slot (crafting result)

    [Fact]
    public void ShiftClick_CraftingResult_IsFlaggedServerAuthoritative()
    {
        var layout = TestLayouts.CraftingResult();
        var state = new SnapshotBuilder(41).Slot(0, "oak_planks", 4).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.QuickMove(0, MouseButton.Left), layout);

        Assert.True(result.TouchedServerAuthoritativeSlot);
    }

    [Fact]
    public void LeftClick_PlacingIntoOutputSlot_IsRefused()
    {
        var layout = TestLayouts.CraftingResult();
        var state = new SnapshotBuilder(41).Cursor("stone", 5).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), layout);

        // Cursor unchanged, nothing placed into the output slot.
        Assert.Equal(5, result.Cursor.Count);
        Assert.True(result.State.GetSlot(0).IsEmpty);
        Assert.True(result.TouchedServerAuthoritativeSlot);
    }

    [Fact]
    public void LeftClick_TakingFromOutputSlot_IsPredictedAndFlagged()
    {
        var layout = TestLayouts.CraftingResult();
        var state = new SnapshotBuilder(41).Slot(0, "oak_planks", 4).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), layout);

        Assert.Equal(4, result.Cursor.Count);
        Assert.True(result.TouchedServerAuthoritativeSlot);
    }

    // state-id / revision bookkeeping

    [Fact]
    public void StateId_IsCarriedThroughUnchanged()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 5).StateId(17).Build();
        var result = ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), Chest);
        Assert.Equal(17, result.State.StateId);
    }

    // purity

    [Fact]
    public void Apply_DoesNotMutateInputSnapshot()
    {
        var state = new SnapshotBuilder(63).Slot(0, "stone", 30).Build();
        ClickSimulator.Apply(state, new ClickAction.Pickup(0, MouseButton.Left), Chest);
        Assert.Equal(30, state.GetSlot(0).Count); // original untouched
        Assert.True(state.Cursor.IsEmpty);
    }
}
