using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Inventory;

public sealed class BedrockTransferredRetiredSourceStoreTests
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(ushort count = 64, int? identity = 1) => new(5, count, 0, identity, 42, [1, 2]);
    private static BedrockInventorySnapshot Read(BedrockInventoryStore store) => store.GetSnapshot(s_player)!.Value;
    private static BedrockInventoryStore Seed(BedrockContainerAddress? address = null, BedrockItemInstance? source = null)
    {
        var store = new BedrockInventoryStore();
        BedrockContainerAddress actual = address ?? s_player;
        store.ApplyContent(new(actual.WindowId, [source ?? Item(), Item(8, 2)], actual, Air()), store.Generation);
        return store;
    }

    [Fact]
    public void MarksOnlyUnchangedSourceUnavailableWithoutReceiptOrOverlayPublication()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryPublishTentativeSlot(before, 1, Item(7, null), -6, store.Generation));
        var unrelated = store.GetTentativeSnapshot(s_player, 1)!.Value;

        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));

        var after = Read(store);
        Assert.Equal(before.Items.ToArray(), after.Items.ToArray());
        Assert.Equal(before.StorageItem, after.StorageItem);
        Assert.Equal(before.ResponseCorrections.ToArray(), after.ResponseCorrections.ToArray());
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Same(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
        Assert.False(after.IsSlotKnown(0));
        Assert.False(after.IsSlotKnown(1));
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
        Assert.Equal(unrelated, store.GetTentativeSnapshot(s_player, 1)!.Value);
        Assert.False(store.CanAdmitRawItemStackRequest(store.Generation));
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        AssertUnchanged(store, after);
    }

    [Fact]
    public void PreservesExistingCorrectionFieldsAndAllPublicationReceipts()
    {
        var store = Seed();
        var original = Read(store);
        var correction = new BedrockItemStackResponseSlot(0, 0, 64, 1, "actual", "filtered", 3);
        store.ApplyResponseUpdates(original, [new(0, Item(), correction)], [0], store.Generation);
        var before = Read(store);

        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));

        var after = Read(store);
        Assert.Equal(correction, after.ResponseCorrections[0]);
        Assert.Equal(before.Items[0], after.Items[0]);
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Same(before.AuthorityReceipts[1], after.AuthorityReceipts[1]);
        Assert.False(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.Null(store.GetTentativeSnapshot(s_player, 0));
    }

    [Fact]
    public void OriginalReceiptCanBeCarriedAcrossTransientGenerationsOnlyWithCurrentArgument()
    {
        var store = Seed();
        var before = Read(store);
        int originalGeneration = store.Generation;
        store.InvalidateTransient();
        store.InvalidateTransient();
        var carried = Read(store);
        Assert.Equal(originalGeneration + 2, store.Generation);
        Assert.Same(before.AuthorityReceipts[0], carried.AuthorityReceipts[0]);
        Assert.True(carried.IsSlotKnown(0));
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, originalGeneration));
        AssertUnchanged(store, carried);

        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));

        var after = Read(store);
        Assert.Equal(before.Items[0], after.Items[0]);
        Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.False(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.Equal(originalGeneration + 2, store.Generation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void StaleOrFutureGenerationCannotInvalidateCurrentSource(int generation)
    {
        var store = Seed();
        var before = Read(store);
        store.InvalidateTransient();
        var current = Read(store);
        Assert.Equal(2, store.Generation);
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, generation));
        AssertUnchanged(store, current);
        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("content")]
    [InlineData("aba")]
    public void FreshSameValueAuthorityReceiptWinsEvenAfterGenerationCarry(string replacement)
    {
        var store = Seed();
        var before = Read(store);
        store.InvalidateTransient();
        if (replacement == "content")
            store.ApplyContent(new(0, [Item(), Item(8, 2)], s_player, Air()), store.Generation);
        else
        {
            if (replacement == "aba")
                store.ApplySlot(new(0, 0, s_player, null, Item(9)), s_player, store.Generation);
            store.ApplySlot(new(0, 0, s_player, null, Item()), s_player, store.Generation);
        }
        var fresh = Read(store);
        Assert.Equal(before.Items[0], fresh.Items[0]);
        Assert.NotSame(before.AuthorityReceipts[0], fresh.AuthorityReceipts[0]);

        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));

        AssertUnchanged(store, fresh);
        Assert.True(fresh.IsSlotKnown(0));
        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(fresh, 0, store.Generation));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("removed")]
    [InlineData("empty")]
    [InlineData("window")]
    [InlineData("dynamic")]
    [InlineData("other_container")]
    public void MissingOrNonPlayerAddressCannotBeInvalidated(string state)
    {
        var store = Seed();
        var before = Read(store);
        if (state == "missing")
            store = new BedrockInventoryStore();
        else if (state == "removed")
            store.RemoveWindow(0);
        else if (state == "empty")
            store.ApplyContent(new(0, [], s_player, Air()), store.Generation);
        else
        {
            BedrockContainerAddress address = state switch
            {
                "window" => new(7, 0, null),
                "dynamic" => new(0, 0, 9),
                _ => new(0, 1, null),
            };
            store = Seed(address);
            before = store.GetSnapshot(address)!.Value;
        }
        BedrockInventorySnapshot? current = store.GetSnapshot(before.Address);
        int containers = store.ContainerCount;
        int generation = store.Generation;

        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, generation));

        Assert.Equal(containers, store.ContainerCount);
        Assert.Equal(generation, store.Generation);
        if (current is { } snapshot)
            AssertUnchanged(store, snapshot);
        else
            Assert.Null(store.GetSnapshot(before.Address));
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
    public void InvalidRetainedSnapshotShapeOrCorrectionRefusesBeforeMutation(int shape)
    {
        var store = Seed();
        var before = Read(store);
        var malformed = shape switch
        {
            0 => default,
            1 => before with { Items = [null!, before.Items[1]] },
            2 => before with { AuthorityReceipts = default },
            3 => before with { AuthorityReceipts = [null!, before.AuthorityReceipts[1]] },
            4 => before with { KnownSlots = [true] },
            5 => before with { ResponseCorrections = [null] },
            6 => before with { ResponseCorrections = [new(0, 0, 64, 1, "different", string.Empty, 0), null] },
            _ => before with { KnownSlots = [false, true] },
        };

        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(malformed, 0, store.Generation));

        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public void InvalidSlotIndexRefusesBeforeIndexingOrMutation(int slot)
    {
        var store = Seed();
        var before = Read(store);
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, slot, store.Generation));
        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData("no_identity")]
    [InlineData("zero_identity")]
    [InlineData("negative_identity")]
    [InlineData("empty")]
    [InlineData("zero_material")]
    [InlineData("zero_count")]
    public void OriginalSourceMustBeKnownNonemptyWithPositiveAuthorityIdentity(string source)
    {
        BedrockItemInstance item = source switch
        {
            "no_identity" => Item(64, null),
            "zero_identity" => Item(64, 0),
            "negative_identity" => Item(64, -4),
            "empty" => Air(),
            "zero_count" => Item(0, 1),
            _ => new(0, 64, 0, 1, 42, [1, 2]),
        };
        var store = Seed(source: item);
        var before = Read(store);
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        AssertUnchanged(store, before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableTargetAndExistingOverlayMustUseTheirOwnRetirementPath(bool overlay)
    {
        var store = Seed();
        var before = Read(store);
        if (overlay)
            Assert.True(store.TryPublishTentativeSlot(before, 0, Item(63, null), -4, store.Generation));
        else
            store.ApplyResponseUpdates(before, [], [0], store.Generation);
        var unavailable = Read(store);
        BedrockTentativeInventorySnapshot? record = store.GetTentativeSnapshot(s_player, 0);

        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(unavailable, 0, store.Generation));

        AssertUnchanged(store, unavailable);
        Assert.Equal(record, store.GetTentativeSnapshot(s_player, 0));
        if (overlay)
        {
            Assert.True(store.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], store.Generation));
            Assert.Null(store.GetTentativeSnapshot(s_player, 0));
            Assert.False(Read(store).IsSlotKnown(0));
        }
    }

    [Fact]
    public void ChangedRetainedOpaqueBytesCannotPassExactValueGuard()
    {
        var store = Seed();
        var before = Read(store);
        var untouched = Read(store);
        before.Items[0].ExtraData[0] = 99;
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        AssertUnchanged(store, untouched);
    }

    [Fact]
    public void MarkingUnavailableDoesNotInstallMutableRetainedOrReturnedItemObjects()
    {
        var store = Seed();
        var before = Read(store);
        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        var expected = Read(store);
        var returned = Read(store);
        before.Items[0].ExtraData[0] = 98;
        returned.Items[0].ExtraData[1] = 99;
        AssertUnchanged(store, expected);
        Assert.Equal(new byte[] { 1, 2 }, Read(store).Items[0].ExtraData);
        Assert.Same(expected.AuthorityReceipts[0], Read(store).AuthorityReceipts[0]);
    }

    [Fact]
    public void ChangedCurrentOpaqueDataWithSameReceiptStillRefusesOldValue()
    {
        BedrockItemInstance currentItem = Item();
        var store = Seed(source: currentItem);
        var before = Read(store);
        currentItem.ExtraData[0] = 97;
        var current = Read(store);
        Assert.Same(before.AuthorityReceipts[0], current.AuthorityReceipts[0]);
        Assert.NotEqual(before.Items[0], current.Items[0]);
        Assert.False(store.TryMarkTransferredRetiredPlayerSlotUnavailable(before, 0, store.Generation));
        AssertUnchanged(store, current);
        Assert.True(store.TryMarkTransferredRetiredPlayerSlotUnavailable(current, 0, store.Generation));
    }

    private static void AssertUnchanged(BedrockInventoryStore store, BedrockInventorySnapshot before)
    {
        BedrockInventorySnapshot after = store.GetSnapshot(before.Address)!.Value;
        Assert.Equal(before.Items.ToArray(), after.Items.ToArray());
        Assert.Equal(before.StorageItem, after.StorageItem);
        Assert.Equal(before.KnownSlots.ToArray(), after.KnownSlots.ToArray());
        Assert.Equal(before.ResponseCorrections.ToArray(), after.ResponseCorrections.ToArray());
        Assert.Equal(before.AuthorityReceipts.Length, after.AuthorityReceipts.Length);
        for (int slot = 0; slot < before.AuthorityReceipts.Length; slot++)
            Assert.Same(before.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
    }
}
