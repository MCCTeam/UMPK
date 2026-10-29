using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class InventoryMirrorCoverageTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(28, false)]
    [InlineData(54, true)]
    public void AuthoritativeContainerDeltaOnlyMirrorsItsAddressedPlayerSlot(int containerSlot, bool addressesHeldSlot)
    {
        var inventory = new InventoryState();
        inventory.OpenContainer(3, 63);
        var contents = new ItemStack[63];
        Array.Fill(contents, ItemStack.Empty);
        contents[54] = TestItems.Stone();
        inventory.ApplyAuthoritativeContent(3, contents, ItemStack.Empty, 3);
        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);

        inventory.ApplyAuthoritativeSlot(0, 36, TestItems.DiamondSword(), 4);
        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);

        inventory.ApplyAuthoritativeSlot(3, containerSlot, TestItems.Stone(3), 5);

        Assert.Equal(TestItems.Stone(3), inventory.GetSlot(containerSlot));
        Assert.Equal(addressesHeldSlot ? TestItems.Stone(3) : TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(5, inventory.StateId);
    }
}
