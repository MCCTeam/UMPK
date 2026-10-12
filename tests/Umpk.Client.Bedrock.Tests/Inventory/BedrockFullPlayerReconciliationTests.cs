using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockFullPlayerReconciliationTests
{
    private static readonly BedrockContainerAddress s_address = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(short type, ushort count, int id) => new(type, count, 7, id, 42, [1, 2, 3]);
    private static byte Namespace(byte slot) => slot <= 8 ? (byte)28 : (byte)29;

    [Theory]
    [InlineData("take", 8, 9)]
    [InlineData("place", 9, 8)]
    [InlineData("take", 9, 35)]
    [InlineData("place", 35, 9)]
    [InlineData("swap", 8, 35)]
    [InlineData("swap", 9, 35)]
    public void IdentityRowsUseQualifiedNamespacesAndRetainedMaterials(string action, byte source, byte destination)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(source, destination, action == "swap");
        BedrockItemStackReconciliation operation = Operation(before, action, source, destination);
        BedrockItemStackResponse response = Response(source, source, destination, destination);
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(response);
        Assert.Equal(2, updates.Count);
        Assert.Equal(action == "swap" ? 264 : 306, updates.Single(x => x.Slot == source).Item.NetworkId);
        Assert.Equal(306, updates.Single(x => x.Slot == destination).Item.NetworkId);
        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        Assert.True(Read(store).IsSlotKnown(source));
        Assert.True(Read(store).IsSlotKnown(destination));
    }

    [Theory]
    [InlineData(8, 9, false)]
    [InlineData(8, 9, true)]
    [InlineData(9, 35, false)]
    [InlineData(9, 35, true)]
    public void CompletePermutationPublishesPhysicalTargetsAndOriginalCorrections(byte source, byte destination, bool reverse)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(source, destination, true);
        BedrockItemStackReconciliation operation = Operation(before, "swap", source, destination);
        BedrockItemStackResponse response = Response(source, destination, destination, source, reverse);
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(response);
        Assert.Equal(2, updates.Count);
        BedrockInventoryReconciledSlot fromSource = updates.Single(x => x.Slot == destination);
        Assert.Equal(264, fromSource.Item.NetworkId);
        Assert.Equal(source, fromSource.Response.Slot);
        Assert.Equal(destination, fromSource.Response.HotbarSlot);
        Assert.Equal("source-correction", fromSource.Response.CustomName);
        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        BedrockInventorySnapshot after = Read(store);
        Assert.Equal(264, after.Items[destination].NetworkId);
        Assert.Equal(306, after.Items[source].NetworkId);
        Assert.Same(fromSource.Response, after.ResponseCorrections[destination]);
        Assert.Same(after.AuthorityReceipts[source], after.AuthorityReceipts[destination]);
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        Assert.Same(after.AuthorityReceipts[source], Read(store).AuthorityReceipts[source]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("collision")]
    [InlineData("third")]
    [InlineData("outside")]
    [InlineData("conflict")]
    [InlineData("no-id")]
    public void UnsupportedRemapRetiresOwnedPairWithoutThirdSlotWrites(string shape)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(8, 9, true);
        BedrockItemStackReconciliation operation = Operation(before, "swap", 8, 9);
        var first = new BedrockItemStackResponseSlot(8, shape == "third" ? (byte)10 : shape == "outside" ? (byte)36 : (byte)9, 2, shape == "no-id" ? null : 11, "source-correction", "filtered", 17);
        BedrockItemStackResponseSlot[] second = shape switch
        {
            "missing" => [],
            "collision" => [new(9, 9, 5, 12, string.Empty, string.Empty, 0)],
            "conflict" => [new(9, 8, 5, 12, string.Empty, string.Empty, 0), new(9, 8, 4, 12, string.Empty, string.Empty, 0)],
            _ => [new(9, 8, 5, 12, string.Empty, string.Empty, 0)],
        };
        var response = new BedrockItemStackResponse(0, -3, true, [new(new(28, null), [first]), new(new(29, null), second)]);
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(response);
        Assert.Empty(updates);
        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        BedrockInventorySnapshot after = Read(store);
        Assert.False(after.IsSlotKnown(8));
        Assert.False(after.IsSlotKnown(9));
        Assert.Equal(before.Items[10], after.Items[10]);
        Assert.True(after.IsSlotKnown(10));
        Assert.Same(before.AuthorityReceipts[10], after.AuthorityReceipts[10]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshSameValueAndAbaReceiptsWinOverPermutation(bool aba)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(8, 9, true);
        BedrockItemStackReconciliation operation = Operation(before, "swap", 8, 9);
        if (aba)
            store.ApplySlot(new(0, 9, s_address, null, Item(1, 4, 99)), s_address, 1);
        store.ApplySlot(new(0, 9, s_address, null, before.Items[9]), s_address, 1);
        BedrockInventorySnapshot newer = Read(store);
        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(Response(8, 9, 9, 8)), operation.AffectedSlots, 1);
        BedrockInventorySnapshot after = Read(store);
        Assert.True(after.IsSlotKnown(9));
        Assert.Equal(before.Items[9], after.Items[9]);
        Assert.Same(newer.AuthorityReceipts[9], after.AuthorityReceipts[9]);
        Assert.False(after.IsSlotKnown(8));
        Assert.Equal(before.Items[8], after.Items[8]);
        Assert.Null(after.ResponseCorrections[9]);
    }

    [Theory]
    [InlineData(28, null)]
    [InlineData(29, 7u)]
    public void MainRequestedEndpointCannotJoinWrongNamespace(byte container, uint? dynamicId)
    {
        (_, BedrockInventorySnapshot before) = Store(9, 35, false);
        BedrockItemStackReconciliation operation = Operation(before, "place", 9, 35);
        var response = new BedrockItemStackResponse(0, -3, true, [new(new(container, dynamicId), [new(9, 9, 4, 11, string.Empty, string.Empty, 0)])]);
        Assert.Empty(operation.Reconcile(response));
    }

    [Fact]
    public void FullTransferMakesSourceAirAndKeepsOriginalCorrection()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(9, 35, false);
        BedrockItemStackReconciliation operation = Operation(before, "place", 9, 35, 5);
        var row = new BedrockItemStackResponseSlot(9, 9, 0, 77, "empty-correction", "filtered", 17);
        var response = new BedrockItemStackResponse(0, -3, true, [new(new(29, null), [row, new(35, 35, 5, 12, string.Empty, string.Empty, 0)])]);
        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(response), operation.AffectedSlots, 1);
        Assert.Equal(Air(), Read(store).Items[9]);
        Assert.Same(row, Read(store).ResponseCorrections[9]);
        Assert.Equal(5, Read(store).Items[35].Count);
    }

    [Fact]
    public void MalformedKnownMaskIsRefusedBeforeSlotIndexing()
    {
        (_, BedrockInventorySnapshot before) = Store(9, 35, false);
        Assert.Throws<InvalidOperationException>(() => Operation(before with { KnownSlots = [true] }, "place", 9, 35));
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    public void MainMergeUsesOccupiedDestinationIdentityAndAuthoritativeCounts(string action)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot original) = Store(9, 35, false);
        store.ApplySlot(new(0, 35, s_address, null, Item(306, 4, 20)), s_address, 1);
        BedrockInventorySnapshot before = Read(store);
        BedrockItemStackReconciliation operation = Operation(before, action, 9, 35, 2);
        var response = new BedrockItemStackResponse(0, -3, true, [new(new(29, null), [new(9, 9, 3, 11, string.Empty, string.Empty, 0), new(35, 35, 6, 12, string.Empty, string.Empty, 0)])]);
        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(response), operation.AffectedSlots, 1);
        Assert.Equal(3, Read(store).Items[9].Count);
        Assert.Equal(6, Read(store).Items[35].Count);
        Assert.Equal(12, Read(store).Items[35].StackNetworkId);
        Assert.Equal(before.Items[35].ExtraData, Read(store).Items[35].ExtraData);
        Assert.Same(original.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Theory]
    [InlineData(255, 1, 1, true)]
    [InlineData(256, 1, 1, false)]
    [InlineData(5, 254, 1, true)]
    [InlineData(5, 255, 1, false)]
    public void ExplicitAmountsRespectResponseRepresentabilityWithoutAssumingMaximum64(ushort sourceCount, ushort destinationCount, byte count, bool supported)
    {
        (BedrockInventoryStore store, _) = Store(9, 35, false);
        store.ApplySlot(new(0, 9, s_address, null, Item(306, sourceCount, 10)), s_address, 1);
        store.ApplySlot(new(0, 35, s_address, null, Item(306, destinationCount, 20)), s_address, 1);
        BedrockInventorySnapshot before = Read(store);
        if (supported)
            Assert.NotNull(Operation(before, "place", 9, 35, count));
        else
            Assert.Throws<InvalidOperationException>(() => Operation(before, "place", 9, 35, count));
    }

    [Fact]
    public void IdenticalRepeatedMainRowsCoalesceAndStaleGenerationCannotPublishThem()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = Store(9, 35, true);
        BedrockItemStackReconciliation operation = Operation(before, "swap", 9, 35);
        BedrockItemStackResponse response = Response(9, 35, 35, 9);
        var duplicate = new BedrockItemStackResponse(0, -3, true, [.. response.Containers, response.Containers[0]]);
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(duplicate);
        Assert.Equal(2, updates.Count);
        store.InvalidateTransient();
        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        Assert.Equal(before.Items[9], Read(store).Items[9]);
        Assert.Same(before.AuthorityReceipts[35], Read(store).AuthorityReceipts[35]);
        Assert.True(Read(store).IsSlotKnown(9));
    }

    private static (BedrockInventoryStore, BedrockInventorySnapshot) Store(byte source, byte destination, bool swap)
    {
        var store = new BedrockInventoryStore();
        BedrockItemInstance[] items = Enumerable.Range(0, 36).Select(_ => Air()).ToArray();
        items[source] = Item(306, 5, 10);
        items[destination] = swap ? Item(264, 2, 20) : Air();
        store.ApplyContent(new(0, items, s_address, Air()), 1);
        return (store, Read(store));
    }

    private static BedrockInventorySnapshot Read(BedrockInventoryStore store) => store.GetSnapshot(s_address)!.Value;

    private static BedrockItemStackReconciliation Operation(BedrockInventorySnapshot before, string action, byte source, byte destination, byte count = 1)
    {
        var first = new BedrockStackRequestSlot(new(Namespace(source), null), source, 10);
        var second = new BedrockStackRequestSlot(new(Namespace(destination), null), destination, before.Items[destination].StackNetworkId ?? 0);
        BedrockItemStackAction entry = action switch
        {
            "take" => new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(count, first, second)),
            "swap" => new(BedrockItemStackActionType.Swap, 2, new BedrockSwapPayload(first, second)),
            _ => new(BedrockItemStackActionType.Place, 1, new BedrockPlacePayload(count, first, second)),
        };
        return BedrockItemStackReconciliation.Create(before, new([new(-3, [entry], [], 0)]), 1);
    }

    private static BedrockItemStackResponse Response(byte first, byte firstTarget, byte second, byte secondTarget, bool reverse = false)
    {
        BedrockItemStackResponseContainer[] containers = [new(new(Namespace(first), null), [new(first, firstTarget, 2, 11, "source-correction", "filtered", 17)]), new(new(Namespace(second), null), [new(second, secondTarget, 5, 12, string.Empty, string.Empty, 0)])];
        if (reverse)
            Array.Reverse(containers);
        return new(0, -3, true, containers);
    }
}
