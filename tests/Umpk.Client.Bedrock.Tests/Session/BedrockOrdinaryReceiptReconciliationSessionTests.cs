using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using OwnershipSession = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockOrdinaryReceiptReconciliationSessionTests
{
    private static readonly BedrockContainerAddress s_address = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);

    [Theory]
    [InlineData("take", "content")]
    [InlineData("take", "source")]
    [InlineData("take", "destination-aba")]
    [InlineData("place", "content")]
    [InlineData("place", "source")]
    [InlineData("place", "destination-aba")]
    [InlineData("swap", "content")]
    [InlineData("swap", "source")]
    [InlineData("swap", "destination-aba")]
    public async Task ActualAuthorityPacketsWinWhileTransferredResultIsStillOwned(string action, string replacement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await CreateAsync(action, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockItemStackOwnershipTests.SendGate gate = fixture.Transport.HoldNextTransferResult();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, action, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            BedrockItemStackRequest actual = await fixture.ReceiveRequestAsync(ct);
            Assert.Equal(-3, actual.RequestId);
            Assert.False(submit.IsCompleted);
            await ReplaceAsync(fixture, before, replacement, ct);
            BedrockInventorySnapshot newer = Read(fixture);

            await RespondAsync(fixture, actual.RequestId, action, ct);

            AssertResolved(fixture, 1);
            BedrockInventorySnapshot after = Read(fixture);
            for (int slot = 0; slot < 2; slot++)
            {
                bool superseded = !ReferenceEquals(before.AuthorityReceipts[slot], newer.AuthorityReceipts[slot]);
                Assert.Equal(newer.Items[slot], after.Items[slot]);
                Assert.Equal(newer.ResponseCorrections[slot], after.ResponseCorrections[slot]);
                Assert.Equal(superseded, after.IsSlotKnown(slot));
                if (superseded)
                    Assert.Same(newer.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
            }
            Assert.Same(newer.AuthorityReceipts[2], after.AuthorityReceipts[2]);
            Assert.True(after.IsSlotKnown(2));
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(actual.RequestId, (await submit.WaitAsync(ct)).RequestId);
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public async Task UnchangedReceiptsPublishActualResponseCountsAndCorrections(string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await CreateAsync(action, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockItemStackSubmitResult submit = await Submit(fixture, action, ct);
        Assert.Equal(submit.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);

        await RespondAsync(fixture, submit.RequestId, action, ct);

        BedrockInventorySnapshot after = Read(fixture);
        Assert.True(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(1));
        Assert.Equal(action == "swap" ? 2 : 4, after.Items[0].Count);
        Assert.Equal(action == "swap" ? 5 : 1, after.Items[1].Count);
        Assert.Equal(action == "swap" ? 264 : 306, after.Items[0].NetworkId);
        Assert.Equal(306, after.Items[1].NetworkId);
        Assert.Equal("accepted-name", after.ResponseCorrections[0]!.CustomName);
        Assert.Equal("filtered", after.ResponseCorrections[0]!.FilteredCustomName);
        Assert.Equal(17, after.ResponseCorrections[0]!.DurabilityCorrection);
        Assert.Same(after.AuthorityReceipts[0], after.AuthorityReceipts[1]);
        Assert.NotSame(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        AssertResolved(fixture, 1);
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public async Task RecoveryNeedsRealAuthorityForTheStillOwnedSibling(string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await CreateAsync(action, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockItemStackSubmitResult submit = await Submit(fixture, action, ct);
        Assert.Equal(submit.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        await ReplaceAsync(fixture, before, "source", ct);
        await RespondAsync(fixture, submit.RequestId, action, ct);
        AssertResolved(fixture, 1);
        Assert.True(Read(fixture).IsSlotKnown(0));
        Assert.False(Read(fixture).IsSlotKnown(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, action, ct));
        await ApplySlotAsync(fixture, 1, before.Items[1], ct);
        BedrockItemStackSubmitResult retry = await Submit(fixture, action, ct);

        Assert.Equal(-5, retry.RequestId);
        Assert.Equal(retry.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        await RespondAsync(fixture, retry.RequestId, action, ct);
        AssertResolved(fixture, 2);
        Assert.True(Read(fixture).IsSlotKnown(0));
        Assert.True(Read(fixture).IsSlotKnown(1));
        Assert.False(fixture.Session.Completion.IsCompleted);
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public async Task AuthorityReplacementDuringCapacityWaitCannotBeOverwrittenAfterCommit(string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await CreateAsync(action, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockItemStackOwnershipTests.SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, action, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await ReplaceAsync(fixture, before, "content", ct);
            BedrockInventorySnapshot newer = Read(fixture);
            gate.Allow();
            BedrockItemStackSubmitResult result = await submit.WaitAsync(ct);
            Assert.Equal(result.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);

            await RespondAsync(fixture, result.RequestId, action, ct);

            BedrockInventorySnapshot after = Read(fixture);
            for (int slot = 0; slot < after.Items.Length; slot++)
            {
                Assert.Equal(newer.Items[slot], after.Items[slot]);
                Assert.Equal(newer.ResponseCorrections[slot], after.ResponseCorrections[slot]);
                Assert.Equal(newer.IsSlotKnown(slot), after.IsSlotKnown(slot));
                Assert.Same(newer.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
            }
            AssertResolved(fixture, 1);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

    private static async Task<OwnershipSession> CreateAsync(string action, CancellationToken ct)
    {
        OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        if (action == "swap")
            await ApplySlotAsync(fixture, 1, new(264, 2, 0, 9, 0, []), ct);
        return fixture;
    }

    private static Task<BedrockItemStackSubmitResult> Submit(OwnershipSession fixture, string action, CancellationToken ct) => action switch
    {
        "take" => fixture.Session.SubmitInventoryTakeAsync(s_address, 0, 1, 1, ct).AsTask(),
        "swap" => fixture.Session.SubmitInventorySwapAsync(s_address, 0, 1, ct).AsTask(),
        _ => fixture.Session.SubmitInventoryPlaceAsync(s_address, 0, 1, 1, ct).AsTask(),
    };

    private static BedrockInventorySnapshot Read(OwnershipSession fixture) => fixture.Session.InventoryStore.GetSnapshot(s_address)!.Value;

    private static async Task ReplaceAsync(OwnershipSession fixture, BedrockInventorySnapshot before, string replacement, CancellationToken ct)
    {
        if (replacement == "content")
        {
            var content = new BedrockInventoryContentPacket(0, before.Items, before.Address, before.StorageItem);
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(content), ct);
            await WaitUntilAsync(() => !ReferenceEquals(before.AuthorityReceipts[0], Read(fixture).AuthorityReceipts[0]), ct);
            return;
        }
        int slot = replacement == "source" ? 0 : 1;
        if (replacement.EndsWith("aba", StringComparison.Ordinal))
            await ApplySlotAsync(fixture, slot, new(1, 9, 0, 99, 0, []), ct);
        await ApplySlotAsync(fixture, slot, before.Items[slot], ct);
    }

    private static async Task ApplySlotAsync(OwnershipSession fixture, int slot, BedrockItemInstance item, CancellationToken ct)
    {
        object previousReceipt = Read(fixture).AuthorityReceipts[slot];
        var packet = new BedrockInventorySlotPacket(0, slot, s_address, null, item);
        await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(packet), ct);
        await WaitUntilAsync(() => !ReferenceEquals(previousReceipt, Read(fixture).AuthorityReceipts[slot]), ct);
        Assert.Equal(item, Read(fixture).Items[slot]);
    }

    private static async Task RespondAsync(OwnershipSession fixture, int requestId, string action, CancellationToken ct)
    {
        var response = new BedrockItemStackResponse(0, requestId, true,
        [
            new(new(28, null),
            [
                new(0, 0, action == "swap" ? (byte)2 : (byte)4, 11, "accepted-name", "filtered", 17),
                new(1, 1, action == "swap" ? (byte)5 : (byte)1, 12, string.Empty, string.Empty, 0),
            ]),
        ]);
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([response])), ct);
        // This property takes the Session gate, so observing zero joins the Store publication inside that same gate.
        await WaitUntilAsync(() => fixture.Session.PendingItemStackReconciliationCount == 0, ct);
    }

    private static void AssertResolved(OwnershipSession fixture, int accepted)
    {
        Assert.Equal(accepted, fixture.Session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, fixture.Session.ItemStackTracker.RejectedCount);
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
            await Task.Delay(10, ct);
    }
}
