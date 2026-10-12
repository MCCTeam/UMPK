using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockOrdinaryReceiptReconciliationTests
{
    private static readonly BedrockContainerAddress s_address = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(short type = 306, ushort count = 5, int stackId = 10) => new(type, count, 7, stackId, 42, [1, 2, 3]);

    [Theory]
    [InlineData("content")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("source-aba")]
    [InlineData("destination-aba")]
    public void SameValueAndAbaAuthorityCannotAdmitTheOldPair(string replacement)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        Replace(store, before, replacement);

        Assert.False(store.MatchesSnapshot(before, [0, 1], 1));
        Assert.True(store.MatchesSnapshot(Read(store), [0, 1], 1));
    }

    [Theory]
    [InlineData("take", "content")]
    [InlineData("take", "source")]
    [InlineData("take", "destination")]
    [InlineData("take", "source-aba")]
    [InlineData("take", "destination-aba")]
    [InlineData("place", "content")]
    [InlineData("place", "source")]
    [InlineData("place", "destination")]
    [InlineData("place", "source-aba")]
    [InlineData("place", "destination-aba")]
    [InlineData("swap", "content")]
    [InlineData("swap", "source")]
    [InlineData("swap", "destination")]
    [InlineData("swap", "source-aba")]
    [InlineData("swap", "destination-aba")]
    public void SupersedingAuthoritySurvivesAnOlderPairedResponse(string action, string replacement)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore(action);
        BedrockItemStackReconciliation operation = Operation(before, action);
        Replace(store, before, replacement);
        BedrockInventorySnapshot newer = Read(store);

        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(Response(action)), operation.AffectedSlots, 1);

        BedrockInventorySnapshot after = Read(store);
        for (int slot = 0; slot < 2; slot++)
        {
            bool superseded = !ReferenceEquals(before.AuthorityReceipts[slot], newer.AuthorityReceipts[slot]);
            Assert.Equal(newer.Items[slot], after.Items[slot]);
            Assert.Equal(newer.ResponseCorrections[slot], after.ResponseCorrections[slot]);
            Assert.Equal(superseded, after.IsSlotKnown(slot));
            if (superseded)
                Assert.Same(newer.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
            else
                Assert.NotSame(before.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
        }
        Assert.Equal(newer.Items[2], after.Items[2]);
        Assert.Equal(newer.ResponseCorrections[2], after.ResponseCorrections[2]);
        Assert.Equal(newer.IsSlotKnown(2), after.IsSlotKnown(2));
        Assert.Same(newer.AuthorityReceipts[2], after.AuthorityReceipts[2]);
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public void MatchingReceiptsPublishTogetherAndOldResponseCannotMintAnotherPublication(string action)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore(action);
        BedrockItemStackReconciliation operation = Operation(before, action);
        Assert.Same(before.AuthorityReceipts[0], operation.Before.AuthorityReceipts[0]);
        Assert.Same(before.AuthorityReceipts[1], operation.Before.AuthorityReceipts[1]);
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response(action));

        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);
        BedrockInventorySnapshot published = Read(store);
        Assert.True(published.IsSlotKnown(0));
        Assert.True(published.IsSlotKnown(1));
        Assert.Equal(updates[0].Item, published.Items[0]);
        Assert.Equal(updates[1].Item, published.Items[1]);
        Assert.Equal("server-name", published.ResponseCorrections[0]!.CustomName);
        Assert.Same(published.AuthorityReceipts[0], published.AuthorityReceipts[1]);
        Assert.NotSame(before.AuthorityReceipts[0], published.AuthorityReceipts[0]);
        Assert.False(store.MatchesSnapshot(before, [0, 1], 1));

        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);

        AssertSnapshotPreserved(published, Read(store));
    }

    [Fact]
    public void UnrelatedSlotReplacementDoesNotSupersedeTheAffectedPair()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        ApplySlot(store, 2, before.Items[2]);
        BedrockInventorySnapshot unrelated = Read(store);
        Assert.True(store.MatchesSnapshot(before, [0, 1], 1));
        BedrockItemStackReconciliation operation = Operation(before, "place");

        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(Response("place")), operation.AffectedSlots, 1);

        BedrockInventorySnapshot after = Read(store);
        Assert.True(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.Equal(4, after.Items[0].Count);
        Assert.Equal(1, after.Items[1].Count);
        Assert.Same(unrelated.AuthorityReceipts[2], after.AuthorityReceipts[2]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("null")]
    public void UnqualifiedReceiptShapeCannotAdmitOrRetireCurrentAuthority(string malformed)
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        BedrockItemStackReconciliation operation = Operation(before, "place");
        BedrockInventorySnapshot invalid = before with
        {
            AuthorityReceipts = malformed switch
            {
                "missing" => default,
                "short" => [before.AuthorityReceipts[0]],
                _ => [null!, before.AuthorityReceipts[1], before.AuthorityReceipts[2]],
            },
        };

        Assert.False(store.MatchesSnapshot(invalid, [0, 1], 1));
        store.ApplyResponseUpdates(invalid, operation.Reconcile(Response("place")), operation.AffectedSlots, 1);

        AssertSnapshotPreserved(before, Read(store));
    }

    [Fact]
    public void ResponseShapedAuthorityStillSupersedesTheOldPair()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        BedrockItemStackReconciliation operation = Operation(before, "place");
        IReadOnlyList<BedrockInventoryReconciledSlot> updates = operation.Reconcile(Response("place"));
        ApplySlot(store, 0, updates[0].Item);
        BedrockInventorySnapshot newer = Read(store);

        store.ApplyResponseUpdates(operation.Before, updates, operation.AffectedSlots, 1);

        BedrockInventorySnapshot after = Read(store);
        Assert.True(after.IsSlotKnown(0));
        Assert.Same(newer.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Equal(newer.Items[0], after.Items[0]);
        Assert.Null(after.ResponseCorrections[0]);
        Assert.False(after.IsSlotKnown(1));
        Assert.Equal(before.Items[1], after.Items[1]);
    }

    [Fact]
    public void FreshSlotClearsOldCorrectionsAndStalePairCannotRestoreThem()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot original) = CreateStore("place");
        BedrockItemStackReconciliation first = Operation(original, "place");
        store.ApplyResponseUpdates(original, first.Reconcile(Response("place")), first.AffectedSlots, 1);
        BedrockInventorySnapshot before = Read(store);
        BedrockItemStackRequestPacket packet = BedrockItemStackEgress.BuildSwap(-5, 0, 1, before.Items[0].StackNetworkId!.Value, before.Items[1].StackNetworkId!.Value);
        BedrockItemStackReconciliation second = BedrockItemStackReconciliation.Create(before, packet, 1);
        ApplySlot(store, 0, before.Items[0]);
        BedrockInventorySnapshot newer = Read(store);
        var response = new BedrockItemStackResponse(0, -5, true, Response("swap").Containers);

        store.ApplyResponseUpdates(second.Before, second.Reconcile(response), second.AffectedSlots, 1);

        BedrockInventorySnapshot after = Read(store);
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(newer.Items[0], after.Items[0]);
        Assert.Same(newer.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Null(after.ResponseCorrections[0]);
        Assert.False(after.IsSlotKnown(1));
        Assert.Equal(before.Items[1], after.Items[1]);
        Assert.Null(after.ResponseCorrections[1]);
    }

    [Fact]
    public void MissingAndConflictingRowsRemainUnknownUntilFreshAuthorityRecoversThem()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        BedrockItemStackReconciliation operation = Operation(before, "place");
        var response = new BedrockItemStackResponse(0, -3, true,
        [
            new(new(28, null), [new(0, 0, 4, 11, "first", string.Empty, 0), new(0, 0, 4, 11, "conflict", string.Empty, 0)]),
        ]);

        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(response), operation.AffectedSlots, 1);

        BedrockInventorySnapshot unknown = Read(store);
        Assert.False(unknown.IsSlotKnown(0));
        Assert.False(unknown.IsSlotKnown(1));
        Assert.False(store.MatchesSnapshot(before, [0, 1], 1));
        ApplySlot(store, 0, before.Items[0]);
        ApplySlot(store, 1, before.Items[1]);
        BedrockInventorySnapshot recovered = Read(store);
        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(Response("place")), operation.AffectedSlots, 1);
        AssertSnapshotPreserved(recovered, Read(store));
        Assert.True(store.MatchesSnapshot(recovered, [0, 1], 1));
    }

    [Fact]
    public void StaleGenerationCannotAdmitOrPublishAgainstSurvivingPlayerAuthority()
    {
        (BedrockInventoryStore store, BedrockInventorySnapshot before) = CreateStore("place");
        BedrockItemStackReconciliation operation = Operation(before, "place");
        store.InvalidateTransient();
        BedrockInventorySnapshot newer = Read(store);

        Assert.False(store.MatchesSnapshot(before, [0, 1], 1));
        store.ApplyResponseUpdates(operation.Before, operation.Reconcile(Response("place")), operation.AffectedSlots, 1);

        AssertSnapshotPreserved(newer, Read(store));
        Assert.True(store.MatchesSnapshot(newer, [0, 1], 2));
    }

    private static (BedrockInventoryStore Store, BedrockInventorySnapshot Before) CreateStore(string action)
    {
        var store = new BedrockInventoryStore();
        BedrockItemInstance destination = action == "swap" ? Item(264, 2, 20) : Air();
        var content = new BedrockInventoryContentPacket(0, [Item(), destination, Item(1, 8, 30)], s_address, Air());
        store.ApplyContent(BedrockInventoryCodecs.DecodeContent(BedrockInventoryCodecs.Encode(content)), 1);
        return (store, Read(store));
    }

    private static BedrockInventorySnapshot Read(BedrockInventoryStore store) => store.GetSnapshot(s_address)!.Value;

    private static BedrockItemStackReconciliation Operation(BedrockInventorySnapshot before, string action)
    {
        BedrockItemStackRequestPacket packet = action switch
        {
            "take" => BedrockItemStackEgress.BuildTake(-3, 0, 1, 1, 10, 0),
            "swap" => BedrockItemStackEgress.BuildSwap(-3, 0, 1, 10, 20),
            _ => BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 10, 0),
        };
        return BedrockItemStackReconciliation.Create(before, packet, 1);
    }

    private static BedrockItemStackResponse Response(string action) => new(0, -3, true,
    [
        new(new(28, null),
        [
            new(0, 0, action == "swap" ? (byte)2 : (byte)4, 11, "server-name", "filtered", 17),
            new(1, 1, action == "swap" ? (byte)5 : (byte)1, 12, string.Empty, string.Empty, 0),
        ]),
    ]);

    private static void Replace(BedrockInventoryStore store, BedrockInventorySnapshot before, string replacement)
    {
        if (replacement == "content")
        {
            var content = new BedrockInventoryContentPacket(0, before.Items, before.Address, before.StorageItem);
            store.ApplyContent(BedrockInventoryCodecs.DecodeContent(BedrockInventoryCodecs.Encode(content)), 1);
            return;
        }
        int slot = replacement.StartsWith("source", StringComparison.Ordinal) ? 0 : 1;
        if (replacement.EndsWith("aba", StringComparison.Ordinal))
            ApplySlot(store, slot, Item(1, 9, 99));
        ApplySlot(store, slot, before.Items[slot]);
    }

    private static void ApplySlot(BedrockInventoryStore store, int slot, BedrockItemInstance item)
    {
        var packet = new BedrockInventorySlotPacket(0, slot, s_address, null, item);
        store.ApplySlot(BedrockInventoryCodecs.DecodeSlot(BedrockInventoryCodecs.Encode(packet)), s_address, 1);
    }

    private static void AssertSnapshotPreserved(BedrockInventorySnapshot expected, BedrockInventorySnapshot actual)
    {
        Assert.Equal(expected.Items.Length, actual.Items.Length);
        for (int slot = 0; slot < expected.Items.Length; slot++)
        {
            Assert.Equal(expected.Items[slot], actual.Items[slot]);
            Assert.Equal(expected.IsSlotKnown(slot), actual.IsSlotKnown(slot));
            Assert.Equal(expected.ResponseCorrections[slot], actual.ResponseCorrections[slot]);
            Assert.Same(expected.AuthorityReceipts[slot], actual.AuthorityReceipts[slot]);
        }
    }
}
