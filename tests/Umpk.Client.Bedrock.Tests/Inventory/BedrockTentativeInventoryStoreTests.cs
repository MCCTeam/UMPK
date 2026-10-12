using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Inventory;

public sealed class BedrockTentativeInventoryStoreTests
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);

    [Fact]
    public void MissingTentativeReadsReturnNothingAndDoNotAllocateContainers()
    {
        var store = Seed();
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.Null(store.GetTentativeSnapshot(new(9, 0, null), 0));
        Assert.Equal(1, store.ContainerCount);
        Assert.True(Read(store).IsSlotKnown(0));
    }

    [Fact]
    public void TentativePublicationKeepsServerDataAndReceiptWhileMakingOnlySourceUnavailable()
    {
        var store = Seed();
        var before = Read(store);

        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));

        var after = Read(store);
        var tentative = store.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.Equal(before.Items.ToArray(), after.Items.ToArray());
        Assert.Equal(before.StorageItem, after.StorageItem);
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Same(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
        Assert.False(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.Equal(64, after.Items[0].Count);
        Assert.Equal(1, after.Items[0].StackNetworkId);
        Assert.Equal(s_player, tentative.Address);
        Assert.Equal(0, tentative.Slot);
        Assert.Equal(store.Generation, tentative.Generation);
        Assert.Equal(-4, tentative.LegacyRequestId);
        Assert.Equal(before.Items[0], tentative.AuthorityItem);
        Assert.Equal(63, tentative.TentativeItem.Count);
        Assert.Null(tentative.TentativeItem.StackNetworkId);
        Assert.Same(before.AuthorityReceipts[0], tentative.AuthorityReceipt);
        Assert.Null(tentative.AuthorityCorrection);
        Assert.False(store.MatchesSnapshot(before, [0], store.Generation));
        Assert.True(store.MatchesSnapshot(before, [1], store.Generation));
    }

    [Fact]
    public void NeutralResponseCorrectionIsRetainedSeparatelyFromTentativeData()
    {
        var store = Seed();
        var original = Read(store);
        var neutral = new BedrockItemStackResponseSlot(0, 0, 64, 1, string.Empty, string.Empty, 0);
        store.ApplyResponseUpdates(original, [new(0, Item(64, 1), neutral)], [0], store.Generation);
        var before = Read(store);

        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));

        Assert.Equal(neutral, store.GetTentativeSnapshot(s_player, 0)!.Value.AuthorityCorrection);
        Assert.Equal(neutral, Read(store).ResponseCorrections[0]);
        Assert.Same(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Fact]
    public void TentativeRecordsAndTheirReturnedItemsOwnOpaqueDataCopies()
    {
        var store = Seed();
        var before = Read(store);
        var tentativeInput = Remainder();
        Assert.True(store.TryPublishTentativeSlot(before, 0, tentativeInput, -4, store.Generation));
        before.Items[0].ExtraData[0] = 91;
        tentativeInput.ExtraData[1] = 92;
        var first = store.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.Equal(new byte[] { 1, 2 }, first.AuthorityItem.ExtraData);
        Assert.Equal(new byte[] { 1, 2 }, first.TentativeItem.ExtraData);
        first.AuthorityItem.ExtraData[0] = 93;
        first.TentativeItem.ExtraData[1] = 94;

        var second = store.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.Equal(new byte[] { 1, 2 }, second.AuthorityItem.ExtraData);
        Assert.Equal(new byte[] { 1, 2 }, second.TentativeItem.ExtraData);
        Assert.Equal(new byte[] { 1, 2 }, Read(store).Items[0].ExtraData);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OrdinarySnapshotsOwnOpaqueDataThroughTentativeLifecycle(int stage)
    {
        var store = new BedrockInventoryStore();
        store.ApplyContent(new(0, [Item(64, 1), Item(8, 2)], s_player, Item(8, 3)), store.Generation);
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var update = new BedrockInventoryReconciledSlot(0, Item(63, 7), new(0, 0, 63, 7, string.Empty, string.Empty, 0));
        if (stage == 1)
            Assert.True(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
        else if (stage == 2)
            Assert.True(store.TryApplyTentativeAuthorityUpdates(before, [update], [0], store.Generation));

        var snapshot = Read(store);
        object receipt = snapshot.AuthorityReceipts[0];
        snapshot.Items[0].ExtraData[0] = 99;
        snapshot.StorageItem.ExtraData[1] = 98;

        var current = Read(store);
        Assert.Equal(new byte[] { 1, 2 }, current.Items[0].ExtraData);
        Assert.Equal(new byte[] { 1, 2 }, current.StorageItem.ExtraData);
        Assert.Same(receipt, current.AuthorityReceipts[0]);
        Assert.Equal(stage == 2, current.IsSlotKnown(0));
        Assert.Equal(stage == 2 ? 63 : 64, current.Items[0].Count);
        if (stage == 0)
        {
            Assert.Equal(new byte[] { 1, 2 }, store.GetTentativeSnapshot(s_player, 0)!.Value.AuthorityItem.ExtraData);
            Assert.True(store.TryApplyTentativeAuthorityUpdates(before, [update], [0], store.Generation));
            Assert.True(Read(store).IsSlotKnown(0));
            Assert.NotSame(receipt, Read(store).AuthorityReceipts[0]);
        }
        else
            Assert.Null(store.GetTentativeSnapshot(s_player, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(256)]
    public void OutOfRangeSlotsRefuseWithoutMutation(int slot)
    {
        var store = Seed();
        var before = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, slot, Remainder(), -4, store.Generation));
        AssertUnchanged(store, before);
        Assert.Null(store.GetTentativeSnapshot(s_player, slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public void UnsupportedLegacyIdentitiesRefuseWithoutInventingAuthority(int legacy)
    {
        var store = Seed();
        var before = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), legacy, store.Generation));
        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void NonpositiveServerIdentityCannotSeedTentativeSource(int identity)
    {
        var store = Seed(Item(64, identity));
        var before = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MalformedRetainedShapesRefuseBeforeIndexingOrPublishing(int shape)
    {
        var store = Seed();
        var before = Read(store);
        var malformed = shape switch
        {
            0 => default,
            1 => before with { Items = default },
            2 => before with { KnownSlots = [true] },
            3 => before with { AuthorityReceipts = default },
            4 => before with { AuthorityReceipts = [new object()] },
            5 => before with { ResponseCorrections = [null] },
            6 => before with { Items = [null!, before.Items[1]] },
            _ => before with { Items = [.. Enumerable.Repeat(Item(64, 1), BedrockInventoryStore.MaxSlotsPerContainer + 1)] }
        };
        Assert.False(store.TryPublishTentativeSlot(malformed, 0, Remainder(), -4, store.Generation));
        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(65)]
    public void NonRemainderCountsRefuseWithoutPartialMutation(int count)
    {
        var store = Seed();
        var before = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Item((ushort)count, null), -4, store.Generation));
        AssertUnchanged(store, before);
    }

    [Fact]
    public void TentativeValueCannotCarryServerIdentityOrReplaceItemMaterial()
    {
        var store = Seed();
        var before = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Item(63, 1), -4, store.Generation));
        Assert.False(store.TryPublishTentativeSlot(before, 0, new(6, 63, 0, null, 42, [1, 2]), -4, store.Generation));
        Assert.False(store.TryPublishTentativeSlot(before, 0, new(5, 63, 0, null, 42, [9]), -4, store.Generation));
        AssertUnchanged(store, before);
    }

    [Fact]
    public void TentativeCanonicalAirIsNotAuthoritativeEmpty()
    {
        var store = Seed(Item(1, 1));
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Air(), -4, store.Generation));
        Assert.Equal(1, Read(store).Items[0].Count);
        Assert.False(Read(store).IsSlotKnown(0));
        Assert.Equal(Air(), store.GetTentativeSnapshot(s_player, 0)!.Value.TentativeItem);
        Assert.Same(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Fact]
    public void UnknownSourceAndDuplicateTentativePublicationRefuse()
    {
        var store = Seed();
        var before = Read(store);
        store.ApplyResponseUpdates(before, [], [0], store.Generation);
        var unknown = Read(store);
        Assert.False(store.TryPublishTentativeSlot(unknown, 0, Remainder(), -4, store.Generation));
        AssertUnchanged(store, unknown);
        store.ApplyContent(Content(), store.Generation);
        before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var first = store.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.False(store.TryPublishTentativeSlot(before, 0, Item(62, null), -6, store.Generation));
        Assert.Equal(first, store.GetTentativeSnapshot(s_player, 0)!.Value);
    }

    [Fact]
    public void SameValueSlotReplacementAndABARefuseTheOldReceipt()
    {
        var store = Seed();
        var before = Read(store);
        store.ApplySlot(Slot(0, Item(64, 1)), s_player, store.Generation);
        var replacement = Read(store);
        Assert.NotSame(before.AuthorityReceipts[0], replacement.AuthorityReceipts[0]);
        Assert.True(store.MatchesSnapshot(before, [0], store.Generation));
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        AssertUnchanged(store, replacement);
        store.ApplySlot(Slot(0, Item(9, 1)), s_player, store.Generation);
        store.ApplySlot(Slot(0, Item(64, 1)), s_player, store.Generation);
        var restored = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        AssertUnchanged(store, restored);
    }

    [Fact]
    public void ChangedCorrectionAndStaleGenerationRefuseWithoutMutation()
    {
        var store = Seed();
        var before = Read(store);
        store.ApplyResponseUpdates(before, [new(0, Item(64, 1), new(0, 0, 64, 1, "actual", string.Empty, 3))], [0], store.Generation);
        var corrected = Read(store);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.False(store.TryPublishTentativeSlot(corrected, 0, Remainder(), -4, store.Generation - 1));
        AssertUnchanged(store, corrected);
    }

    [Fact]
    public void UnrelatedSlotCorrectionPreservesTentativeRecordAndItsReceipt()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        store.ApplySlot(Slot(1, Item(8, 2)), s_player, store.Generation);
        var after = Read(store);
        Assert.False(after.IsSlotKnown(0));
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
        Assert.Equal(8, after.Items[1].Count);
        Assert.True(after.IsSlotKnown(1));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    public void ActualSlotCorrectionClearsOnlyItsRecordIncludingByteIdenticalAuthority(int count)
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.True(store.TryPublishTentativeSlot(before, 1, Item(7, null), -6, store.Generation));
        int identity = count == 64 ? 1 : 7;
        store.ApplySlot(Slot(0, Item((ushort)count, identity)), s_player, store.Generation);
        var after = Read(store);
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(count, after.Items[0].Count);
        Assert.Equal(identity, after.Items[0].StackNetworkId);
        Assert.NotSame(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 1));
        Assert.False(after.IsSlotKnown(1));
    }

    [Fact]
    public void ContentReplacementClearsOnlyItsAddressAndSameValueCreatesNewAuthority()
    {
        var store = Seed();
        var other = new BedrockContainerAddress(7, 0, 9);
        store.ApplyContent(Content(other), store.Generation);
        var before = Read(store);
        var otherBefore = store.GetSnapshot(other)!.Value;
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.True(store.TryPublishTentativeSlot(otherBefore, 0, Remainder(), -6, store.Generation));
        store.ApplyContent(Content(), store.Generation);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.NotNull(store.GetTentativeSnapshot(other, 0));
        Assert.True(Read(store).IsSlotKnown(0));
        Assert.NotSame(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Fact]
    public void ExplicitAuthorityUpdatesApplyOnlySuppliedRowsAndKeepMissingRowsUnavailable()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.True(store.TryPublishTentativeSlot(before, 1, Item(7, null), -6, store.Generation));
        var actual = new BedrockInventoryReconciledSlot(0, Item(63, 7), new(0, 0, 63, 7, "actual", "filtered", 4));

        Assert.True(store.TryApplyTentativeAuthorityUpdates(before, [actual], [0, 1], store.Generation));

        var after = Read(store);
        Assert.Equal(actual.Item, after.Items[0]);
        Assert.Equal(actual.Response, after.ResponseCorrections[0]);
        Assert.True(after.IsSlotKnown(0));
        Assert.NotSame(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.False(after.IsSlotKnown(1));
        Assert.Equal(before.Items[1], after.Items[1]);
        Assert.Same(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 1));
        actual.Item.ExtraData[0] = 99;
        Assert.Equal(new byte[] { 1, 2 }, Read(store).Items[0].ExtraData);
    }

    [Fact]
    public void EmptyExplicitAuthorityRowsDoNotMintReceiptsOrCompleteTentativeState()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var pending = Read(store);
        Assert.False(store.TryApplyTentativeAuthorityUpdates(before, [], [0], store.Generation));
        AssertUnchanged(store, pending);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void ActualCanonicalAirAuthorityClearsTheOverlayAndRestoresKnownSlot()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.True(store.TryApplyTentativeAuthorityUpdates(before, [new(0, Air(), new(0, 0, 0, 0, string.Empty, string.Empty, 0))], [0], store.Generation));
        Assert.True(Read(store).IsSlotKnown(0));
        Assert.Equal(Air(), Read(store).Items[0]);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.NotSame(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Fact]
    public void NullArgumentsThrowWithoutChangingCurrentAuthority()
    {
        var store = Seed();
        var before = Read(store);
        Assert.Throws<ArgumentNullException>(() => store.TryPublishTentativeSlot(before, 0, null!, -4, store.Generation));
        Assert.Throws<ArgumentNullException>(() => store.TryRetireTentativeSlot(s_player, 0, null!, store.Generation));
        Assert.Throws<ArgumentNullException>(() => store.TryApplyTentativeAuthorityUpdates(before, null!, [0], store.Generation));
        Assert.Throws<ArgumentNullException>(() => store.TryApplyTentativeAuthorityUpdates(before, [], null!, store.Generation));
        AssertUnchanged(store, before);
    }

    [Fact]
    public void GenericResponseApplyRefusesTheEntireAffectedSetWhenTentativeDataExists()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var pending = Read(store);
        store.ApplyResponseUpdates(before, [new(0, Item(63, 7), new(0, 0, 63, 7, string.Empty, string.Empty, 0)), new(1, Item(7, 2), new(1, 1, 7, 2, string.Empty, string.Empty, 0))], [0, 1], store.Generation);
        AssertUnchanged(store, pending);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void GenericResponseOnUnrelatedSlotsKeepsExistingBehavior()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        store.ApplyResponseUpdates(before, [new(1, Item(7, 2), new(1, 1, 7, 2, string.Empty, string.Empty, 0))], [1], store.Generation);
        Assert.Equal(7, Read(store).Items[1].Count);
        Assert.True(Read(store).IsSlotKnown(1));
        Assert.False(Read(store).IsSlotKnown(0));
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void StaleExplicitAuthorityAndRetirementCannotOverwriteFreshSameValueReplacement()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        store.ApplySlot(Slot(0, Item(64, 1)), s_player, store.Generation);
        var fresh = Read(store);
        Assert.False(store.TryApplyTentativeAuthorityUpdates(before, [new(0, Item(63, 7), new(0, 0, 63, 7, string.Empty, string.Empty, 0))], [0], store.Generation));
        Assert.False(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
        AssertUnchanged(store, fresh);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidExplicitAuthorityShapeRefusesAllMutation(int shape)
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var pending = Read(store);
        var row = new BedrockInventoryReconciledSlot(0, Item(63, 7), new(0, 0, 63, 7, string.Empty, string.Empty, 0));
        var rows = shape switch
        {
            0 => new[] { row, row },
            1 => new[] { row with { Slot = 2 } },
            2 => new[] { row with { Response = new(0, 0, 62, 7, string.Empty, string.Empty, 0) } },
            _ => new[] { row with { Item = Item(63, -4) } }
        };
        Assert.False(store.TryApplyTentativeAuthorityUpdates(before, rows, [0], store.Generation));
        AssertUnchanged(store, pending);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidExplicitAffectedOrBaselineShapeRefusesBeforeMutation(int shape)
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        var pending = Read(store);
        int[] affected = shape switch { 0 => [0, 0], 1 => [0, 2], _ => [0] };
        var baseline = shape == 2 ? before with { AuthorityReceipts = default } : before;
        Assert.False(store.TryApplyTentativeAuthorityUpdates(baseline, [new(0, Item(63, 7), new(0, 0, 63, 7, string.Empty, string.Empty, 0))], affected, store.Generation));
        AssertUnchanged(store, pending);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void ReceiptSpecificRetirementRemovesOnlyItsRecordAndKeepsOldAuthorityUnavailable()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.False(store.TryRetireTentativeSlot(s_player, 0, new object(), store.Generation));
        Assert.True(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.False(Read(store).IsSlotKnown(0));
        Assert.Equal(before.Items[0], Read(store).Items[0]);
        Assert.Same(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
        Assert.False(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
    }

    [Fact]
    public void TransientRetirementRemovesEligibilityAndRecordsWithoutMakingOldSourceKnown()
    {
        var store = Seed();
        var before = Read(store);
        var transient = new BedrockContainerAddress(7, 0, null);
        store.ApplyContent(Content(transient), store.Generation);
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, store.Generation));
        Assert.True(store.TryPublishTentativeSlot(store.GetSnapshot(transient)!.Value, 0, Remainder(), -6, store.Generation));
        int oldGeneration = store.Generation;
        store.InvalidateTransient();
        Assert.Equal(oldGeneration + 1, store.Generation);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.Null(store.GetSnapshot(transient));
        Assert.False(Read(store).IsSlotKnown(0));
        Assert.Equal(before.Items[0], Read(store).Items[0]);
        Assert.Same(before.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
        Assert.False(store.TryPublishTentativeSlot(before, 0, Remainder(), -8, oldGeneration));
    }

    [Fact]
    public void TransientRetirementPreservesNewerRealAuthorityAndRejectsOldOverlayReceipt()
    {
        var store = Seed();
        var before = Read(store);
        int originalGeneration = store.Generation;
        Assert.True(store.TryPublishTentativeSlot(before, 0, Remainder(), -4, originalGeneration));
        store.ApplySlot(Slot(0, Item(63, 7)), s_player, originalGeneration);
        var replacement = Read(store);
        store.InvalidateTransient();
        Assert.True(Read(store).IsSlotKnown(0));
        Assert.Equal(replacement.Items.ToArray(), Read(store).Items.ToArray());
        Assert.Same(replacement.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
        Assert.False(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], originalGeneration));
        Assert.False(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void AtMostOneRecordExistsPerExistingBoundedSlot()
    {
        var store = new BedrockInventoryStore();
        store.ApplyContent(new(0, Enumerable.Range(0, BedrockInventoryStore.MaxSlotsPerContainer).Select(index => Item(64, index + 1)).ToArray(), s_player, Air()), store.Generation);
        var before = Read(store);
        for (int slot = 0; slot < BedrockInventoryStore.MaxSlotsPerContainer; slot++)
        {
            Assert.True(store.TryPublishTentativeSlot(before, slot, Remainder(), -4 - 2 * slot, store.Generation));
            Assert.NotNull(store.GetTentativeSnapshot(s_player, slot));
        }
        Assert.False(store.TryPublishTentativeSlot(before, BedrockInventoryStore.MaxSlotsPerContainer, Remainder(), -518, store.Generation));
        Assert.Equal(1, store.ContainerCount);
        Assert.Equal(BedrockInventoryStore.MaxSlotsPerContainer, Read(store).Items.Length);
    }

    [Fact]
    public void WindowRemovalFullInvalidationAndEmptyContentDoNotLeaveOrReanimateRecords()
    {
        var store = Seed();
        Assert.True(store.TryPublishTentativeSlot(Read(store), 0, Remainder(), -4, store.Generation));
        store.ApplyContent(new(0, [], s_player, Air()), store.Generation);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.Empty(Read(store).Items);
        store.ApplyContent(Content(), store.Generation);
        Assert.True(store.TryPublishTentativeSlot(Read(store), 0, Remainder(), -6, store.Generation));
        Assert.Equal(1, store.RemoveWindow(0));
        Assert.Null(store.GetSnapshot(s_player));
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        store.ApplyContent(Content(), store.Generation);
        Assert.True(store.TryPublishTentativeSlot(Read(store), 0, Remainder(), -8, store.Generation));
        store.Invalidate();
        Assert.Equal(0, store.ContainerCount);
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void OverLimitContentFailureKeepsAuthorityReceiptAndTentativeRecord()
    {
        var store = Seed();
        Assert.True(store.TryPublishTentativeSlot(Read(store), 0, Remainder(), -4, store.Generation));
        var pending = Read(store);
        Assert.Throws<InvalidOperationException>(() => store.ApplyContent(new(0, Enumerable.Repeat(Air(), BedrockInventoryStore.MaxSlotsPerContainer + 1).ToArray(), s_player, Air()), store.Generation));
        AssertUnchanged(store, pending);
        Assert.NotNull(store.GetTentativeSnapshot(s_player, 0));
    }

    private static BedrockInventoryStore Seed(BedrockItemInstance? source = null)
    {
        var store = new BedrockInventoryStore();
        store.ApplyContent(new(0, [source ?? Item(64, 1), Item(8, 2)], s_player, Air()), store.Generation);
        return store;
    }

    private static BedrockInventorySnapshot Read(BedrockInventoryStore store) => store.GetSnapshot(s_player)!.Value;

    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);

    private static BedrockItemInstance Item(ushort count, int? identity) => new(5, count, 0, identity, 42, [1, 2]);

    private static BedrockItemInstance Remainder() => Item(63, null);

    private static BedrockInventoryContentPacket Content(BedrockContainerAddress? address = null)
    {
        var actual = address ?? s_player;
        return new(actual.WindowId, [Item(64, 1), Item(8, 2)], actual, Air());
    }

    private static BedrockInventorySlotPacket Slot(int slot, BedrockItemInstance item) => new(0, slot, null, null, item);

    private static void AssertUnchanged(BedrockInventoryStore store, BedrockInventorySnapshot before)
    {
        var after = store.GetSnapshot(before.Address)!.Value;
        Assert.Equal(before.Items.ToArray(), after.Items.ToArray());
        Assert.Equal(before.StorageItem, after.StorageItem);
        Assert.Equal(before.KnownSlots.ToArray(), after.KnownSlots.ToArray());
        Assert.Equal(before.ResponseCorrections.ToArray(), after.ResponseCorrections.ToArray());
        Assert.Equal(before.AuthorityReceipts.Length, after.AuthorityReceipts.Length);
        for (int slot = 0; slot < before.AuthorityReceipts.Length; slot++)
            Assert.Same(before.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
    }
}
