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
    [Fact]
    public void AcceptedResponseUpdatesKnownItemsAndPreservesCorrections()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 0, null);
        var original = new BedrockItemInstance(306, 5, 7, 2, 9, [1, 2]);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [original, AirItem(), StoneItem(8)], address, AirItem()), 1);
        BedrockInventorySnapshot before = store.GetSnapshot(address)!.Value;
        var sourceRow = new BedrockItemStackResponseSlot(0, 0, 4, 2, "name", "filtered", 17);
        var destinationRow = new BedrockItemStackResponseSlot(1, 1, 1, 3, string.Empty, string.Empty, 0);

        store.ApplyResponseUpdates(before,
            [new(0, new(306, 4, 7, 2, 9, [1, 2]), sourceRow), new(1, new(306, 1, 7, 3, 9, [1, 2]), destinationRow)], [0, 1], 1);

        BedrockInventorySnapshot after = store.GetSnapshot(address)!.Value;
        Assert.Equal(4, after.Items[0].Count);
        Assert.Equal(3, after.Items[1].StackNetworkId);
        Assert.Equal(7u, after.Items[1].MetadataValue);
        Assert.Equal(sourceRow, after.ResponseCorrections[0]);
        Assert.True(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.True(after.IsSlotKnown(2));
        Assert.Equal(8, after.Items[2].Count);
    }

    [Fact]
    public void MissingAffectedResponseBecomesUnavailableUntilAuthoritativeSlotArrives()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 0, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(5), AirItem(), StoneItem(8)], address, AirItem()), 1);
        BedrockInventorySnapshot before = store.GetSnapshot(address)!.Value;
        var row = new BedrockItemStackResponseSlot(1, 1, 1, 3, string.Empty, string.Empty, 0);

        store.ApplyResponseUpdates(before, [new(1, new(1, 1, 0, 3, 1, []), row)], [0, 1], 1);

        BedrockInventorySnapshot after = store.GetSnapshot(address)!.Value;
        Assert.False(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.True(after.IsSlotKnown(2));
        store.ApplySlot(new BedrockInventorySlotPacket(0, 0, null, null, StoneItem(4)), address, 1);
        Assert.True(store.GetSnapshot(address)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void ResponseDoesNotOverwriteAnInterveningAuthoritativeItem()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 0, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(5), AirItem()], address, AirItem()), 1);
        BedrockInventorySnapshot before = store.GetSnapshot(address)!.Value;
        store.ApplySlot(new BedrockInventorySlotPacket(0, 0, null, null, StoneItem(9)), address, 1);
        BedrockInventorySnapshot newer = store.GetSnapshot(address)!.Value;
        var row = new BedrockItemStackResponseSlot(0, 0, 4, 2, string.Empty, string.Empty, 0);

        store.ApplyResponseUpdates(before, [new(0, new(1, 4, 0, 2, 1, []), row)], [0, 1], 1);

        BedrockInventorySnapshot after = store.GetSnapshot(address)!.Value;
        Assert.Equal(9, after.Items[0].Count);
        Assert.True(after.IsSlotKnown(0));
        Assert.Same(newer.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Equal(newer.ResponseCorrections[0], after.ResponseCorrections[0]);
        Assert.False(after.IsSlotKnown(1));
        Assert.NotSame(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
    }

    [Fact]
    public void ResponseFromPreviousGenerationCannotChangeCurrentContent()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 0, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(5), AirItem()], address, AirItem()), 1);
        BedrockInventorySnapshot before = store.GetSnapshot(address)!.Value;
        store.Invalidate();
        store.ApplyContent(new BedrockInventoryContentPacket(0, [StoneItem(9), AirItem()], address, AirItem()), 2);

        store.ApplyResponseUpdates(before, [], [0, 1], 1);

        Assert.Equal(9, store.GetSnapshot(address)!.Value.Items[0].Count);
        Assert.True(store.GetSnapshot(address)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void AdmissionDetectsAnInterveningCorrectionReplacement()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CorrectedPair();
        store.ApplySlot(new BedrockInventorySlotPacket(0, 0, null, null, before.Items[0]), before.Address, 1);

        Assert.False(store.MatchesSnapshot(before, [0, 1], 1));
    }

    [Fact]
    public void ResponseDoesNotRestoreSupersededCorrectionState()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CorrectedPair();
        store.ApplySlot(new BedrockInventorySlotPacket(0, 0, null, null, before.Items[0]), before.Address, 1);
        BedrockInventorySnapshot newer = store.GetSnapshot(before.Address)!.Value;
        var first = new BedrockItemStackResponseSlot(0, 0, 3, 2, "named", "filtered", 17);
        var second = new BedrockItemStackResponseSlot(1, 1, 4, 3, "named", "filtered", 17);

        store.ApplyResponseUpdates(before, [new(0, new(306, 3, 0, 2, 0, []), first), new(1, new(306, 4, 0, 3, 0, []), second)], [0, 1], 1);

        BedrockInventorySnapshot after = store.GetSnapshot(before.Address)!.Value;
        Assert.Equal(4, after.Items[0].Count);
        Assert.Equal(3, after.Items[1].Count);
        Assert.True(after.IsSlotKnown(0));
        Assert.Same(newer.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Null(after.ResponseCorrections[0]);
        Assert.False(after.IsSlotKnown(1));
        Assert.NotSame(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
        Assert.Null(after.ResponseCorrections[1]);
    }

    private static (BedrockInventoryStore Store, BedrockInventorySnapshot Before) CorrectedPair()
    {
        var store = new BedrockInventoryStore();
        var address = new BedrockContainerAddress(0, 0, null);
        store.ApplyContent(new BedrockInventoryContentPacket(0, [new(306, 5, 0, 2, 0, []), new(306, 2, 0, 3, 0, [])], address, AirItem()), 1);
        BedrockInventorySnapshot before = store.GetSnapshot(address)!.Value;
        var first = new BedrockItemStackResponseSlot(0, 0, 4, 2, "named", "filtered", 17);
        var second = new BedrockItemStackResponseSlot(1, 1, 3, 3, "named", "filtered", 17);
        store.ApplyResponseUpdates(before, [new(0, new(306, 4, 0, 2, 0, []), first), new(1, new(306, 3, 0, 3, 0, []), second)], [0, 1], 1);
        return (store, store.GetSnapshot(address)!.Value);
    }

}
