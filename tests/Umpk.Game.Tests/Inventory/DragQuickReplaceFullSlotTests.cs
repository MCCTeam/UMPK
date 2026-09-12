using Umpk.Game.Inventory;
using Umpk.Game.Tests.Items;
using Xunit;

namespace Umpk.Game.Tests.Inventory;

// Guards vanilla-accurate left-drag distribution when the drag set includes a slot already full of the same item: canItemQuickReplace(ignoreSize=true) keeps the full slot in the even-split denominator so it must not collapse to the single-slot fallback.
public class DragQuickReplaceFullSlotTests
{
    private static readonly SlotLayout Chest = TestLayouts.Chest9x3();

    // left-drag distribution over a set that includes a slot already full of the same item. Vanilla canItemQuickReplace(slot, carried, ignoreSize=true) returns true when slotCount <= maxStackSize, so the full slot is part of the drag set and counts toward quickcraftSlots.size() (the even-split denominator). UMPK's ClickSimulator.CanDragInto uses a strict "existing.Count < carried.MaxStackSize", so it DROPS the full slot from the set, shrinking the denominator and (here) collapsing to the single-slot pickup fallback.
    //
    // Cursor = 6 stone, drag Left over [slot0 empty, slot1 = 64 stone (full)]. Vanilla: size=2, placeCount=floor(6/2)=3 -> slot0=3, slot1 unchanged, cursor=3. UMPK: slot1 filtered out, slots=[0], single-slot fallback places all 6 into slot0, cursor empty.
    [Fact]
    public void LeftDrag_OverFullSameItemSlot_MatchesVanillaDistribution()
    {
        var state = new SnapshotBuilder(63)
            .Slot(1, "stone", 64) // already a full stack of the same item
            .Cursor("stone", 6)
            .Build();

        var result = ClickSimulator.Apply(
            state,
            new ClickAction.Drag(DragStage.End, MouseButton.Left, Slots: new[] { 0, 1 }),
            Chest);

        // Expected quick-craft behavior:
        Assert.Equal(3, result.State.GetSlot(0).Count);   // even split over 2 slots
        Assert.Equal(64, result.State.GetSlot(1).Count);  // full slot unchanged but still in the set
        Assert.Equal(3, result.Cursor.Count);             // remainder returns to cursor
    }
}
