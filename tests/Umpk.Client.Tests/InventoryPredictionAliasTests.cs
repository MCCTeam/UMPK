using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class InventoryPredictionAliasTests
{
    [Fact]
    public void UnrelatedPredictedClickRetainsNewerDirectPlayerValueAndItsContainerAlias()
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        var contents = new ItemStack[63];
        Array.Fill(contents, ItemStack.Empty);
        contents[0] = TestItems.Stone(3);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 3);
        inventory.ApplyAuthoritativeSlot(0, 36, TestItems.DiamondSword(), 4);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);

        ContainerSnapshot before = inventory.CaptureActiveSnapshot();
        ClickResult prediction = ClickSimulator.Apply(before,
            new ClickAction.Pickup(0, MouseButton.Left),
            GenericLayouts.For(before.SlotCount, hasOpenContainer: true));
        SlotChange changed = Assert.Single(prediction.ChangedSlots);
        Assert.Equal(0, changed.Slot);
        Assert.True(changed.Item.IsEmpty);
        Assert.Equal(TestItems.Stone(3), prediction.Cursor);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);

        inventory.ApplyPredictedSnapshot(prediction.State);

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
        Assert.True(inventory.GetSlot(0).IsEmpty);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(3, inventory.StateId);
    }
}
