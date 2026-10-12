using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockItemStackReconciliationTests
{
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);

    private static BedrockItemInstance Item(short type, ushort count, int stackId) => new(type, count, 7, stackId, 42, [1, 2, 3]);

    private static BedrockInventorySnapshot Snapshot(BedrockItemInstance source, BedrockItemInstance destination) =>
        new(new BedrockContainerAddress(0, 0, null), [source, destination, .. Enumerable.Range(0, 7).Select(_ => Air())], Air());

    private static BedrockItemStackResponseSlot Row(byte slot, byte count, int? stackId) => new(slot, slot, count, stackId, string.Empty, string.Empty, 0);

    private static BedrockItemStackResponse Response(params BedrockItemStackResponseSlot[] rows) =>
        new(0, -3, true, [new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), rows)]);

    private static BedrockItemStackReconciliation Place(BedrockInventorySnapshot before) =>
        BedrockItemStackReconciliation.Create(before, BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 10, before.Items[1].StackNetworkId ?? 0), 7);

    [Fact]
    public void PlaceUsesRetainedSourceForEmptyDestinationWithoutContentRefresh()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Air());
        BedrockItemStackReconciliation operation = Place(before);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response(Row(0, 3, 11), Row(1, 2, 12)));

        Assert.Equal(-3, operation.RequestId);
        Assert.Equal(7, operation.Generation);
        Assert.Equal(new[] { 0, 1 }, operation.AffectedSlots.ToArray());
        Assert.Equal(2, updates.Count);
        Assert.Equal(new BedrockItemInstance(306, 3, 7, 11, 42, [1, 2, 3]), updates.Single(value => value.Slot == 0).Item);
        Assert.Equal(new BedrockItemInstance(306, 2, 7, 12, 42, [1, 2, 3]), updates.Single(value => value.Slot == 1).Item);
        Assert.Equal(5, before.Items[0].Count);
        Assert.Equal(0, before.Items[1].NetworkId);
    }

    [Fact]
    public void MergePreservesKnownDestinationItemData()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Item(306, 2, 20));

        BedrockInventoryReconciledSlot update = Place(before).Reconcile(Response(Row(1, 4, 21))).Single();

        Assert.Equal(1, update.Slot);
        Assert.Equal(new BedrockItemInstance(306, 4, 7, 21, 42, [1, 2, 3]), update.Item);
        Assert.NotSame(before.Items[1].ExtraData, update.Item.ExtraData);
    }

    [Fact]
    public void TakeRetainsSourceTypeAndAppliesAuthoritativeCounts()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Air());
        var packet = BedrockItemStackEgress.BuildTake(-3, 0, 1, 2, 10, 0);
        BedrockItemStackReconciliation operation = BedrockItemStackReconciliation.Create(before, packet, 7);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response(Row(0, 3, 11), Row(1, 2, 12)));

        Assert.Equal(306, updates.Single(value => value.Slot == 0).Item.NetworkId);
        Assert.Equal(306, updates.Single(value => value.Slot == 1).Item.NetworkId);
    }

    [Fact]
    public void SwapReversesRetainedItemTypes()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Item(264, 2, 20));
        var packet = BedrockItemStackEgress.BuildSwap(-3, 0, 1, 10, 20);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = BedrockItemStackReconciliation.Create(before, packet, 7).Reconcile(Response(Row(0, 2, 21), Row(1, 5, 11)));

        Assert.Equal(264, updates.Single(value => value.Slot == 0).Item.NetworkId);
        Assert.Equal(306, updates.Single(value => value.Slot == 1).Item.NetworkId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void ZeroCountProducesCanonicalAirAndPreservesReturnedIdentity(int? returnedStackId)
    {
        BedrockItemStackResponseSlot row = Row(0, 0, returnedStackId);

        BedrockInventoryReconciledSlot update = Place(Snapshot(Item(306, 5, 10), Air())).Reconcile(Response(row)).Single();

        Assert.Equal(Air(), update.Item);
        Assert.Same(row, update.Response);
        Assert.Equal(returnedStackId, update.Response.StackId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void PositiveCountWithoutPositiveServerIdentityRemainsUnavailable(int? stackId)
    {
        BedrockItemStackReconciliation operation = Place(Snapshot(Item(306, 5, 10), Air()));

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response(Row(0, 4, stackId), Row(1, 1, 12)));

        Assert.Equal(1, Assert.Single(updates).Slot);
    }

    [Fact]
    public void PositiveCountCannotInventTypeFromRetainedAir()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Air());
        var packet = BedrockItemStackEgress.BuildSwap(-3, 0, 1, 10, 0);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = BedrockItemStackReconciliation.Create(before, packet, 7).Reconcile(Response(Row(0, 1, 21), Row(1, 5, 11)));

        Assert.Equal(1, Assert.Single(updates).Slot);
    }

    [Theory]
    [InlineData(-5, 0, true)]
    [InlineData(-3, 1, true)]
    [InlineData(-3, 7, true)]
    [InlineData(-3, 0, false)]
    public void UncorrelatedOrUnacceptedResponseProducesNoUpdates(int requestId, byte status, bool containersPresent)
    {
        BedrockItemStackResponse ok = Response(Row(0, 4, 11), Row(1, 1, 12));
        var response = new BedrockItemStackResponse(status, requestId, containersPresent, ok.Containers);

        Assert.Empty(Place(Snapshot(Item(306, 5, 10), Air())).Reconcile(response));
    }

    [Theory]
    [InlineData(29, null)]
    [InlineData(28, 8u)]
    public void ForeignContainerRowsDoNotUpdateHotbar(byte containerId, uint? dynamicId)
    {
        var response = new BedrockItemStackResponse(0, -3, true, [new BedrockItemStackResponseContainer(new BedrockFullContainerName(containerId, dynamicId), [Row(0, 4, 11), Row(1, 1, 12)])]);

        Assert.Empty(Place(Snapshot(Item(306, 5, 10), Air())).Reconcile(response));
    }

    [Fact]
    public void MissingOrUnknownRowsDoNotInventAffectedUpdates()
    {
        BedrockItemStackReconciliation operation = Place(Snapshot(Item(306, 5, 10), Air()));

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response(Row(0, 4, 11), Row(8, 1, 99)));

        Assert.Equal(0, Assert.Single(updates).Slot);
    }

    [Fact]
    public void ChangedRequestedToHotbarMappingRemainsUnavailable()
    {
        var malformed = new BedrockItemStackResponseSlot(0, 1, 4, 11, string.Empty, string.Empty, 0);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = Place(Snapshot(Item(306, 5, 10), Air())).Reconcile(Response(malformed, Row(1, 1, 12)));

        Assert.Empty(updates);
    }

    [Fact]
    public void ConflictingDuplicatesAcrossContainersSuppressOnlyTheirAffectedSlot()
    {
        var response = new BedrockItemStackResponse(0, -3, true,
        [
            new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [Row(0, 4, 11), Row(1, 1, 12)]),
            new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [Row(0, 3, 11)]),
        ]);

        IReadOnlyList<BedrockInventoryReconciledSlot> updates = Place(Snapshot(Item(306, 5, 10), Air())).Reconcile(response);

        Assert.Equal(1, Assert.Single(updates).Slot);
    }

    [Fact]
    public void DuplicateIdenticalRowsCollapseAndCorrectionConflictsRemainUnavailable()
    {
        BedrockItemStackReconciliation operation = Place(Snapshot(Item(306, 5, 10), Air()));
        BedrockItemStackResponseSlot row = Row(0, 4, 11);
        var changedCorrection = new BedrockItemStackResponseSlot(0, 0, 4, 11, "renamed", string.Empty, 0);

        Assert.Single(operation.Reconcile(Response(row, Row(0, 4, 11))));
        Assert.Empty(operation.Reconcile(Response(row, changedCorrection)));
    }

    [Fact]
    public void CorrectionsRemainSeparateFromClonedOpaqueItemData()
    {
        BedrockInventorySnapshot before = Snapshot(Item(306, 5, 10), Air());
        BedrockItemStackReconciliation operation = Place(before);
        before.Items[0].ExtraData[0] = 99;
        var row = new BedrockItemStackResponseSlot(1, 1, 1, 12, "name", "filtered", 17);

        BedrockInventoryReconciledSlot update = operation.Reconcile(Response(row)).Single();

        Assert.Equal(new byte[] { 1, 2, 3 }, operation.Before.Items[0].ExtraData);
        Assert.Equal(new byte[] { 1, 2, 3 }, update.Item.ExtraData);
        Assert.Equal(7u, update.Item.MetadataValue);
        Assert.Equal(42u, update.Item.BlockRuntimeId);
        Assert.Same(row, update.Response);
        Assert.Equal("name", update.Response.CustomName);
        Assert.Equal("filtered", update.Response.FilteredCustomName);
        Assert.Equal(17, update.Response.DurabilityCorrection);
        update.Item.ExtraData[0] = 77;
        Assert.Equal(1, operation.Before.Items[0].ExtraData[0]);
    }

    [Fact]
    public void UnsupportedActionGraphsCannotBeRetained()
    {
        var before = Snapshot(Item(306, 5, 10), Air());
        var request = BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 10, 0).Requests[0];
        var packet = new BedrockItemStackRequestPacket([new BedrockItemStackRequest(-3, [request.Actions[0], request.Actions[0]], [], 0)]);

        Assert.Throws<InvalidOperationException>(() => BedrockItemStackReconciliation.Create(before, packet, 7));
    }

    [Fact]
    public void MismatchedRetainedIdentityCannotBeReconciled()
    {
        var before = Snapshot(Item(306, 5, 10), Air());
        var packet = BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 99, 0);

        Assert.Throws<InvalidOperationException>(() => BedrockItemStackReconciliation.Create(before, packet, 7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnavailableSourceOrDestinationCannotBeRetained(int unavailableSlot)
    {
        BedrockInventorySnapshot initial = Snapshot(Item(306, 5, 10), Air());
        var store = new BedrockInventoryStore();
        store.ApplyContent(new BedrockInventoryContentPacket(0, initial.Items, initial.Address, initial.StorageItem), 1);
        store.ApplyResponseUpdates(store.GetSnapshot(initial.Address)!.Value, [], [unavailableSlot], 1);
        BedrockInventorySnapshot unavailable = store.GetSnapshot(initial.Address)!.Value;
        var packet = BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 10, 0);

        Assert.False(unavailable.IsSlotKnown(unavailableSlot));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackReconciliation.Create(unavailable, packet, 1));
    }

    [Theory]
    [InlineData("other", "filtered", 17)]
    [InlineData("name", "other", 17)]
    [InlineData("name", "filtered", 18)]
    public void MergeRejectsConflictingCorrectionFields(string name, string filteredName, int durability)
    {
        BedrockInventorySnapshot before = WithCorrections(
            Snapshot(Item(306, 5, 10), Item(306, 2, 20)),
            new(0, 0, 5, 10, "name", "filtered", 17),
            new(1, 1, 2, 20, name, filteredName, durability));

        Assert.Throws<InvalidOperationException>(() => Place(before));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergeRejectsOneSidedCorrectionState(bool sourceCorrected)
    {
        var correction = new BedrockItemStackResponseSlot(0, 0, 5, 10, "name", "filtered", 17);
        BedrockInventorySnapshot before = WithCorrections(
            Snapshot(Item(306, 5, 10), Item(306, 2, 20)),
            sourceCorrected ? correction : null,
            sourceCorrected ? null : correction);

        Assert.Throws<InvalidOperationException>(() => Place(before));
    }

    [Fact]
    public void MergeAcceptsMatchingCorrectionsWithDifferentSlotCountAndStackIdentity()
    {
        BedrockInventorySnapshot before = WithCorrections(
            Snapshot(Item(306, 5, 10), Item(306, 2, 20)),
            new(0, 0, 5, 10, "name", "filtered", 17),
            new(1, 1, 2, 20, "name", "filtered", 17));

        Assert.Equal(-3, Place(before).RequestId);
    }

    [Fact]
    public void MergeAcceptsOriginalItemDataWhenBothCorrectionStatesAreAbsent()
    {
        BedrockInventorySnapshot before = WithCorrections(Snapshot(Item(306, 5, 10), Item(306, 2, 20)), null, null);

        Assert.Equal(-3, Place(before).RequestId);
    }

    [Fact]
    public void EmptyDestinationDoesNotRequireMergeCorrectionCompatibility()
    {
        BedrockInventorySnapshot before = WithCorrections(
            Snapshot(Item(306, 5, 10), Air()),
            new(0, 0, 5, 10, "name", "filtered", 17),
            null);

        Assert.Equal(-3, Place(before).RequestId);
    }

    [Fact]
    public void MatchingCorrectionsDoNotPermitDifferentOriginalItemData()
    {
        BedrockInventorySnapshot before = WithCorrections(
            Snapshot(Item(306, 5, 10), new(306, 2, 8, 20, 42, [1, 2, 3])),
            new(0, 0, 5, 10, "name", "filtered", 17),
            new(1, 1, 2, 20, "name", "filtered", 17));

        Assert.Throws<InvalidOperationException>(() => Place(before));
    }

    private static BedrockInventorySnapshot WithCorrections(BedrockInventorySnapshot snapshot, BedrockItemStackResponseSlot? source, BedrockItemStackResponseSlot? destination)
    {
        var corrections = new BedrockItemStackResponseSlot?[snapshot.Items.Length];
        corrections[0] = source;
        corrections[1] = destination;
        return snapshot with { ResponseCorrections = [.. corrections] };
    }
}
