using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class InventoryDecodeAvailabilityTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(32767)]
    public void AppliedHeldSlotRestoresKnowledgeWithoutNumericRevisionFence(int receivedRevision)
    {
        var inventory = new InventoryState();
        inventory.ApplyAuthoritativeSlot(0, 36, TestItems.Stone(), 7);
        inventory.MarkSlotUnavailable(0, 36);

        Assert.Equal(TestItems.Stone(), inventory.GetSlot(36));
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        inventory.ApplyAuthoritativeSlot(0, 9, TestItems.Stone(2), receivedRevision);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsPlayerSlotAvailable(9));

        inventory.ApplyAuthoritativeSlot(0, 36, TestItems.DiamondSword(), receivedRevision);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(TestItems.DiamondSword(), inventory.GetSlot(36));
        Assert.Equal(receivedRevision, inventory.StateId);
    }

    [Fact]
    public void LostContentKeepsCachedValuesButRequiresEachAcceptedField()
    {
        var inventory = new InventoryState();
        ItemStack[] contents = Empty(46);
        contents[36] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(0, contents, TestItems.DiamondSword(), 7);
        inventory.MarkContentsUnavailable(0);

        Assert.Equal(TestItems.Stone(), inventory.GetSlot(36));
        Assert.Equal(TestItems.DiamondSword(), inventory.Cursor);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.False(inventory.IsCursorAvailable);
        Assert.All(Enumerable.Range(0, 46), slot => Assert.False(inventory.IsPlayerSlotAvailable(slot)));

        inventory.ApplyAuthoritativeSlot(0, 36, TestItems.Stone(3), 7);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsPlayerSlotAvailable(9));
        Assert.False(inventory.IsCursorAvailable);
        inventory.ApplyAuthoritativeContent(0, contents, ItemStack.Empty, 7);
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.True(inventory.IsCursorAvailable);
        Assert.All(Enumerable.Range(0, 46), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
    }

    [Theory]
    [InlineData(0, 36)]
    [InlineData(3, 54)]
    public void PlayerTailLossAndRecoveryCoverBothAliasesOnly(int window, int slot)
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        ItemStack[] contents = Empty(63);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 7);
        inventory.MarkSlotUnavailable(window, slot);

        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsSlotAvailable(3, 54));
        inventory.ApplyAuthoritativeSlot(3, 28, TestItems.Stone(3), 7);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsSlotAvailable(3, 54));
        Assert.True(inventory.IsPlayerSlotAvailable(10));

        inventory.SetAuthoritativeSlot(0, 36, TestItems.DiamondSword());
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(3, 54));
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.GetSlot(54));

        inventory.ApplyAuthoritativeSlot(3, 28, TestItems.Stone(4), 7);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(3, 54));
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.GetSlot(54));
    }

    [Fact]
    public void LostContainerSnapshotDoesNotInvalidateArmorOrOffhand()
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        inventory.ApplyAuthoritativeContent(3, Empty(63), ItemStack.Empty, 7);
        inventory.MarkContentsUnavailable(3);

        Assert.All(Enumerable.Range(9, 36), slot => Assert.False(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsPlayerSlotAvailable(5));
        Assert.True(inventory.IsPlayerSlotAvailable(45));
        Assert.False(inventory.IsCursorAvailable);

        inventory.CloseContainer();
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsCursorAvailable);
        inventory.Clear();
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.True(inventory.IsCursorAvailable);
        Assert.All(inventory.PlayerSlots, item => Assert.True(item.IsEmpty));
    }

    [Fact]
    public void UntrackedWindowLossDoesNotInvalidateTrackedFields()
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        inventory.MarkSlotUnavailable(2, 36);
        inventory.MarkContentsUnavailable(2);

        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.True(inventory.IsCursorAvailable);
        Assert.All(Enumerable.Range(0, 46), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
    }

    [Fact]
    public void LocalPredictionsAndCachedWritesCannotRestoreDecodeKnowledge()
    {
        var inventory = new InventoryState();
        inventory.SetAuthoritativeSlot(0, 36, TestItems.Stone());
        ContainerSnapshot before = inventory.CaptureActiveSnapshot();
        inventory.SetSlot(0, 36, TestItems.DiamondSword());
        inventory.RecordPrediction(0, 7, before);
        inventory.MarkSlotUnavailable(0, 36);

        Assert.False(inventory.HasPendingPrediction);
        Assert.Equal(TestItems.DiamondSword(), inventory.GetSlot(36));
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        inventory.SetSlot(0, 36, TestItems.Stone(2));
        inventory.ApplyPredictedSnapshot(new ContainerSnapshot(Empty(46), ItemStack.Empty, 7));
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsActiveSnapshotAvailable);

        inventory.SetAuthoritativeSlot(0, 36, ItemStack.Empty);
        Assert.True(inventory.IsActiveSnapshotAvailable);
    }

    [Fact]
    public void LostFirstContentInvalidatesPlayerTailUntilAuthoritativeShapeArrives()
    {
        var inventory = new InventoryState();
        inventory.SetAuthoritativeSlot(0, 36, TestItems.Stone());
        inventory.OpenContainer(3, 1);

        inventory.MarkContentsUnavailable(3, 63);

        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.All(Enumerable.Range(9, 36), slot => Assert.False(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsPlayerSlotAvailable(5));
        Assert.True(inventory.IsPlayerSlotAvailable(45));
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] playerContents = Empty(46);
        playerContents[36] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(0, playerContents, ItemStack.Empty, 7);
        inventory.ApplyAuthoritativeCursor(ItemStack.Empty);

        Assert.All(Enumerable.Range(9, 36), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] containerContents = Empty(63);
        containerContents[54] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(3, containerContents, ItemStack.Empty, 7);

        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
    }

    [Fact]
    public void LostSlotBeyondProvisionalExtentConservativelyInvalidatesPlayerTail()
    {
        var inventory = new InventoryState();
        inventory.SetAuthoritativeSlot(0, 36, TestItems.Stone());
        inventory.OpenContainer(3, 1);

        inventory.MarkSlotUnavailable(3, 54);

        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.All(Enumerable.Range(9, 36), slot => Assert.False(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] playerContents = Empty(46);
        playerContents[36] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(0, playerContents, ItemStack.Empty, 7);

        Assert.All(Enumerable.Range(9, 36), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] containerContents = Empty(63);
        containerContents[54] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(3, containerContents, ItemStack.Empty, 7);

        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AcceptedCrossWindowAliasSurvivesPendingContentRebase(int authoritativeRoute)
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        ItemStack[] contents = Empty(63);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 7);

        ContainerSnapshot before = inventory.CaptureActiveSnapshot();
        ClickResult prediction = ClickSimulator.Apply(before,
            new ClickAction.Pickup(54, MouseButton.Left),
            GenericLayouts.For(before.SlotCount, hasOpenContainer: true));
        SlotChange changed = Assert.Single(prediction.ChangedSlots);
        Assert.Equal(54, changed.Slot);
        Assert.True(changed.Item.IsEmpty);
        Assert.Equal(TestItems.Stone(), prediction.Cursor);
        inventory.ApplyPredictedSnapshot(prediction.State);
        inventory.RecordPrediction(3, 7, before);
        Assert.True(inventory.HasPendingPrediction);

        switch (authoritativeRoute)
        {
            case 0:
                inventory.ApplyAuthoritativeSlot(0, 36, TestItems.DiamondSword(), 7);
                break;
            case 1:
                inventory.SetAuthoritativeSlot(0, 36, TestItems.DiamondSword());
                break;
            case 2:
                ItemStack[] playerContents = Empty(46);
                playerContents[36] = TestItems.DiamondSword();
                inventory.ApplyAuthoritativeContent(0, playerContents, ItemStack.Empty, 7);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(authoritativeRoute));
        }

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
        Assert.Equal(TestItems.Stone(), inventory.Cursor);

        ItemStack[] accepted = Empty(63);
        accepted[54] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(3, accepted, TestItems.Stone(), 7);

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(3, 54));
        Assert.Equal(TestItems.Stone(), inventory.Cursor);
    }

    [Fact]
    public void CursorOnlyLossRequiresAcceptedCursorRecovery()
    {
        var inventory = new InventoryState();
        inventory.SetAuthoritativeSlot(0, 36, TestItems.Stone());
        inventory.ApplyAuthoritativeCursor(TestItems.DiamondSword());

        inventory.MarkCursorUnavailable();

        Assert.Equal(TestItems.DiamondSword(), inventory.Cursor);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        inventory.ApplyAuthoritativeSlot(0, 9, TestItems.Stone(2), 7);
        Assert.False(inventory.IsCursorAvailable);
        inventory.ApplyAuthoritativeCursor(ItemStack.Empty);

        Assert.True(inventory.IsCursorAvailable);
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.True(inventory.Cursor.IsEmpty);
    }

    [Theory]
    [InlineData(39)]
    [InlineData(63)]
    public void ReopenedContainerRemapsHeldLossUntilItsAliasRecovers(int slotCount)
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        ItemStack[] contents = Empty(63);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 7);
        inventory.MarkSlotUnavailable(0, 36);

        inventory.CloseContainer();
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsCursorAvailable);
        inventory.OpenContainer(4, slotCount);
        int heldAlias = slotCount - 9;

        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.Stone(), inventory.ContainerSlots![heldAlias]);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsSlotAvailable(4, heldAlias));
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        inventory.SetAuthoritativeSlot(4, heldAlias, TestItems.DiamondSword());

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![heldAlias]);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(4, heldAlias));
        Assert.True(inventory.IsActiveSnapshotAvailable);
    }

    [Fact]
    public void ProvisionalReopenRetainsInheritedHeldLossUntilFullShapeArrives()
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        ItemStack[] contents = Empty(63);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 7);
        inventory.MarkSlotUnavailable(0, 36);

        inventory.CloseContainer();
        inventory.OpenContainer(4, 1);

        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] playerContents = Empty(46);
        playerContents[36] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(0, playerContents, ItemStack.Empty, 7);
        inventory.ApplyAuthoritativeCursor(ItemStack.Empty);

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.All(Enumerable.Range(9, 36), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());

        ItemStack[] containerContents = Empty(63);
        containerContents[54] = TestItems.DiamondSword();
        inventory.ApplyAuthoritativeContent(4, containerContents, ItemStack.Empty, 7);

        Assert.Equal(63, inventory.ActiveSlotCount);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(4, 54));
        Assert.True(inventory.IsActiveSnapshotAvailable);
    }

    private static ItemStack[] Empty(int count)
    {
        var contents = new ItemStack[count];
        Array.Fill(contents, ItemStack.Empty);
        return contents;
    }
}
