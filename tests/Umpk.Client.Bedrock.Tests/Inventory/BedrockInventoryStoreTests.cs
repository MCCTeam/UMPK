using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Inventory;

public sealed class BedrockInventoryStoreTests
{
    private static BedrockItemInstance AirItem() => new(0, 0, 0u, null, 0u, []);

    private static BedrockItemInstance StoneItem(ushort count) => new(1, count, 0u, null, 1u, []);

    [Fact]
    public void ContentReplacesAddressedContainerAtomically()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 12, null);
        var packet = new BedrockInventoryContentPacket(0, [StoneItem(5), AirItem()], address, AirItem());

        store.ApplyContent(packet, 1);

        Assert.Equal(2, store.GetSnapshot(address)!.Value.Items.Length);
        Assert.Equal(5, store.GetSnapshot(address)!.Value.Items[0].Count);
    }

    [Fact]
    public void SlotUpdateReplacesOneSlotOnly()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 12, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [AirItem(), AirItem()], address, AirItem()), 1);
        store.ApplySlot(new BedrockInventorySlotPacket(0, 1, null, null, StoneItem(3)), address, 1);

        Assert.Equal(0, store.GetSnapshot(address)!.Value.Items[0].Count);
        Assert.Equal(3, store.GetSnapshot(address)!.Value.Items[1].Count);
    }

    [Fact]
    public void StaleGenerationDoesNotMutateReplacementWorld()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 12, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(1)], address, AirItem()), 1);
        store.Invalidate();
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(2)], address, AirItem()), 2);

        store.ApplySlot(new BedrockInventorySlotPacket(0, 0, null, null, StoneItem(9)), address, 1);

        Assert.Equal(2, store.GetSnapshot(address)!.Value.Items[0].Count);
    }

    [Fact]
    public void SlotBeyondCurrentContainerIsRejected()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 12, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [AirItem()], address, AirItem()), 1);

        Assert.Throws<InvalidOperationException>(() => store.ApplySlot(new BedrockInventorySlotPacket(0, 4, null, null, AirItem()), address, 1));
    }

    [Fact]
    public void RemoveWindowDropsEveryAddressForThatWindowOnly()
    {
        var store = new BedrockInventoryStore();
        var first = new BedrockContainerAddress(1, 12, null);
        var second = new BedrockContainerAddress(1, 29, null);
        var other = new BedrockContainerAddress(2, 12, null);
        store.ApplyContent(new BedrockInventoryContentPacket(1, [StoneItem(1)], first, AirItem()), 1);
        store.ApplyContent(new BedrockInventoryContentPacket(1, [StoneItem(2)], second, AirItem()), 1);
        store.ApplyContent(new BedrockInventoryContentPacket(2, [StoneItem(3)], other, AirItem()), 1);

        Assert.Equal(2, store.RemoveWindow(1));

        Assert.Null(store.GetSnapshot(first));
        Assert.Null(store.GetSnapshot(second));
        Assert.NotNull(store.GetSnapshot(other));
    }

    [Fact]
    public void RemoveWindowForUnknownWindowRemovesNothing()
    {
        var store = new BedrockInventoryStore();

        Assert.Equal(0, store.RemoveWindow(9));
    }

    [Fact]
    public void DynamicAndFixedContainersDoNotAlias()
    {
        var store = new BedrockInventoryStore();
        var fixedAddress = new BedrockContainerAddress(0, 63, null);
        var dynamicAddress = new BedrockContainerAddress(0, 63, 7u);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(1)], fixedAddress, AirItem()), 1);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(2)], dynamicAddress, AirItem()), 1);

        Assert.Equal(1, store.GetSnapshot(fixedAddress)!.Value.Items[0].Count);
        Assert.Equal(2, store.GetSnapshot(dynamicAddress)!.Value.Items[0].Count);
    }
}
