using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;
using Cases = Umpk.Client.Bedrock.State.Inventory.BedrockCursorPlaceReconciliationTests;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockCursorPlaceSessionTests
{
    private static async Task WaitAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate()) await Task.Delay(10, ct);
    }

    private static bool Released(Fixture fixture) => fixture.Session.PendingItemStackReconciliationCount == 0 && fixture.Session.ItemStackTracker.PendingCount == 0 && fixture.Session.ActionCoordinator.ClaimedCount == 0;
    private static async Task<Fixture> CreateAsync(CancellationToken ct)
    {
        Fixture fixture = await Fixture.CreateAsync(ct, [new BedrockItemRegistryPacket([new BedrockItemState("minecraft:oak_planks", 5, false, 2, new NbtCompound())])]);
        try
        {
            await ContentAsync(fixture, Cases.Player, Enumerable.Range(0, 36).Select(_ => Cases.Air()).ToArray(), ct);
            await ContentAsync(fixture, Cases.Pool, Enumerable.Range(0, 54).Select(_ => Cases.Air()).ToArray(), ct);
            var catalog = new BedrockCreativeContentPacket([], [new(17, new(5, 1, 0, 77, new byte[10]), 0)]);
            await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(catalog), ct);
            await WaitAsync(() => fixture.Session.LastCreativeContent is not null, ct);
            await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(2, -1, 0, 0, 0, -1)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is not null, ct);
            BedrockCreativeEntry entry = fixture.Session.LastCreativeContent!.Entries[0];
            var acquire = new BedrockItemStackRequestPacket([new(-3,
            [
                new(BedrockItemStackActionType.CraftCreative, 14, new BedrockCraftCreativePayload(entry.EntryId, 1)),
                new(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload([new(1, 1, "minecraft:oak_planks", 0, 1, 77, new byte[10])], 1)),
                new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(64, new(new(60, null), 50, -3), new(new(59, null), 0, 0))),
            ], [], -1)]);
            await fixture.Session.SubmitObservedCreativeCursorRequestAsync(entry, acquire, ct);
            Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new(0, -3, true, [new(new(59, null), [Cases.Row(64, 88)])])])), ct);
            await WaitAsync(() => Released(fixture) && fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.Items[0].Count == 64, ct);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static async Task ContentAsync(Fixture fixture, BedrockContainerAddress address, BedrockItemInstance[] items, CancellationToken ct)
    {
        int prior = fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 49);
        BedrockInventorySnapshot? previous = fixture.Session.InventoryStore.GetSnapshot(address);
        object? receipt = previous is { } old && !old.AuthorityReceipts.IsDefault && old.Items.Length > 0 ? old.AuthorityReceipts[0] : null;
        await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(address.WindowId, items, address, Cases.Air())), ct);
        await WaitAsync(() =>
        {
            _ = fixture.Session.PendingItemStackReconciliationCount;
            return fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 49) > prior
                && fixture.Session.InventoryStore.GetSnapshot(address) is { } snapshot && snapshot.Items.Length == items.Length && snapshot.Items[0].Equals(items[0])
                && (receipt is null || !ReferenceEquals(receipt, snapshot.AuthorityReceipts[0]));
        }, ct);
        _ = fixture.Session.PendingItemStackReconciliationCount;
    }

    private static Task<BedrockItemStackSubmitResult> Submit(Fixture fixture, CancellationToken ct, BedrockItemStackRequestPacket? packet = null) => fixture.Session.SubmitObservedCreativeCursorPlaceAsync(fixture.Session.LastCreativeContent!.Entries[0], packet ?? Cases.Packet(), ct).AsTask();
    private static async Task RespondAsync(Fixture fixture, CancellationToken ct, BedrockItemStackResponse? response = null)
    {
        long prior = fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount + fixture.Session.ItemStackTracker.UnknownCount;
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([response ?? Cases.Response()])), ct);
        await WaitAsync(() => fixture.Session.PendingItemStackReconciliationCount == 0 && fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount + fixture.Session.ItemStackTracker.UnknownCount > prior && Released(fixture), ct);
    }

    [Fact]
    public async Task AcceptedPlaceUsesPriorCursorIdAndPublishesBothFullSlotsWithout49()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        int contents = fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 49);
        var result = await Submit(fixture, timeout.Token);
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(-5, result.RequestId);
        Assert.Equal(Cases.Packet().Requests[0], await fixture.ReceiveRequestAsync(timeout.Token));
        await RespondAsync(fixture, timeout.Token);
        var cursor = fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value;
        var player = fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value;
        Assert.Equal(Cases.Air(), cursor.Items[0]);
        Assert.Equal(Cases.Oak(), player.Items[0]);
        Assert.True(cursor.IsSlotKnown(0));
        Assert.True(player.IsSlotKnown(0));
        Assert.Same(cursor.AuthorityReceipts[0], player.AuthorityReceipts[0]);
        Assert.Equal(Cases.Row(64, 88), player.ResponseCorrections[0]);
        Assert.Equal(contents, fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 49));
        Assert.Equal(2, fixture.Session.ItemStackTracker.AcceptedCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshTransferReplacementSurvivesOwnerRetirementButCannotRequalifyIt(bool catalog)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = fixture.Transport.HoldNextTransferResult();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            var source = fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value;
            await ContentAsync(fixture, Cases.Pool, source.Items.ToArray(), ct);
            await WaitAsync(() => Released(fixture), ct);
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
            if (catalog)
            {
                int prior = fixture.Session.CreativeContentObservationCount;
                await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(new BedrockCreativeContentPacket([], [new(17, new(5, 1, 0, 77, new byte[10]), 0)])), ct);
                await WaitAsync(() => fixture.Session.CreativeContentObservationCount > prior, ct);
            }
            else
            {
                object token = fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.AuthorityReceipts[0];
                await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(2, -1, 1, 0, 0, -1)), ct);
                await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is { X: 1 }, ct);
                Assert.Same(token, fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.AuthorityReceipts[0]);
            }
            _ = fixture.Session.PendingItemStackReconciliationCount;
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
            gate.Allow();
            Assert.Equal(-5, (await submit.WaitAsync(ct)).RequestId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct, Cases.Packet(id: -7)));
            await RespondAsync(fixture, ct);
            Assert.Equal(Cases.Oak(), fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.Items[0]);
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
            await ContentAsync(fixture, Cases.Pool, source.Items.ToArray(), ct);
            await ContentAsync(fixture, Cases.Player, Enumerable.Range(0, 36).Select(_ => Cases.Air()).ToArray(), ct);
            Assert.Equal(-7, (await Submit(fixture, ct, Cases.Packet(id: -7))).RequestId);
            Assert.Equal(-7, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task AffectedReplacementBeforeGrantPreservesNewAuthorityAndCancels(bool cursor, bool slot)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = fixture.Transport.HoldNextReservation();
        int commits = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            BedrockContainerAddress address = cursor ? Cases.Pool : Cases.Player;
            BedrockInventorySnapshot before = fixture.Session.InventoryStore.GetSnapshot(address)!.Value;
            if (slot)
                await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(address.WindowId, 0, address, null, before.Items[0])), ct);
            else
                await ContentAsync(fixture, address, before.Items.ToArray(), ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(ct));
            await WaitAsync(() => Released(fixture), ct);
            BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(address)!.Value;
            Assert.True(after.IsSlotKnown(0));
            Assert.Equal(before.Items[0], after.Items[0]);
            Assert.False(fixture.Session.InventoryStore.GetSnapshot(cursor ? Cases.Player : Cases.Pool)!.Value.IsSlotKnown(0));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            await RespondAsync(fixture, ct);
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(address)!.Value.IsSlotKnown(0));
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(ct); } catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GrantedReplacementDoesNotRecantSendOrOverwriteFreshData(bool cursor, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            BedrockContainerAddress address = cursor ? Cases.Pool : Cases.Player;
            BedrockInventorySnapshot before = fixture.Session.InventoryStore.GetSnapshot(address)!.Value;
            BedrockItemInstance replacement = cursor ? Cases.Oak(64, 99) : Cases.Oak(1, 99);
            await ContentAsync(fixture, address, before.Items.SetItem(0, replacement).ToArray(), ct);
            await WaitAsync(() => Released(fixture), ct);
            gate.Allow();
            Assert.Equal(-5, (await submit.WaitAsync(ct)).RequestId);
            Assert.Equal(Cases.Packet().Requests[0], await fixture.ReceiveRequestAsync(ct));
            await RespondAsync(fixture, ct);
            var after = fixture.Session.InventoryStore.GetSnapshot(address)!.Value;
            Assert.True(after.IsSlotKnown(0));
            Assert.Equal(replacement, after.Items[0]);
            Assert.True(Released(fixture));
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task UnrelatedSlotDuringCapacityWaitDoesNotCancelOrLoseItsPublication()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(124, 50, Cases.Pool, null, Cases.Oak(1, 55))), ct);
            await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.Items[50].StackNetworkId == 55, ct);
            Assert.False(submit.IsCompleted);
            Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
            gate.Allow();
            await submit.WaitAsync(ct);
            await fixture.ReceiveRequestAsync(ct);
            await RespondAsync(fixture, ct);
            Assert.Equal(Cases.Oak(1, 55), fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.Items[50]);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteAcceptedRowsBoundOnlyMissingAvailability(bool missingCursor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        await Submit(fixture, timeout.Token);
        await fixture.ReceiveRequestAsync(timeout.Token);
        var container = missingCursor ? new BedrockItemStackResponseContainer(new(28, null), [Cases.Row(64, 88)]) : new BedrockItemStackResponseContainer(new(59, null), [Cases.Row(0, null)]);
        await RespondAsync(fixture, timeout.Token, new(0, -5, true, [container]));
        var cursor = fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value;
        var player = fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value;
        Assert.Equal(!missingCursor, cursor.IsSlotKnown(0));
        Assert.Equal(missingCursor, player.IsSlotKnown(0));
        Assert.Same(cursor.AuthorityReceipts[0], player.AuthorityReceipts[0]);
        Assert.True(Released(fixture));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("cause")]
    [InlineData("identity")]
    [InlineData("destination")]
    public async Task UnqualifiedSuppliedShapeRejectsBeforeCapacityAndAllocation(string change)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        int commits = fixture.Transport.CommittedCount;
        var packet = change switch
        {
            "count" => Cases.Packet(count: 1),
            "cause" => Cases.Packet(cause: 0),
            "identity" => Cases.Packet(sourceId: 99),
            _ => Cases.Packet(destinationSlot: 1),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, timeout.Token, packet));
        Assert.True(Released(fixture));
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        Assert.Equal(-5, (await Submit(fixture, timeout.Token)).RequestId);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("life")]
    [InlineData("world")]
    public async Task OwnershipRetirementBeforeGrantCancelsAndReleases(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = fixture.Transport.HoldNextReservation();
        int commits = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (boundary == "close") await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(2, -1, true)), ct);
            else if (boundary == "life") fixture.Session.HandleRespawn();
            else fixture.Session.HandleWorldReplacement();
            await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(ct));
            Assert.True(Released(fixture));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            Assert.False(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(ct); } catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeWaitsForGrantedTransferResultButNotPending148(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            dispose = fixture.Session.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            gate.Allow();
            Assert.Equal(-5, (await submit.WaitAsync(ct)).RequestId);
            await dispose.WaitAsync(ct);
            Assert.True(fixture.Transport.DisposeEntered.Task.IsCompleted);
            Assert.True(Released(fixture));
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
            if (dispose is not null) await dispose.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task CallerCancellationBeforeGrantPreservesBothAuthoritativeBaselines()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();
        await using Fixture fixture = await CreateAsync(timeout.Token);
        var gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(timeout.Token);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit.WaitAsync(timeout.Token));
            Assert.True(Released(fixture));
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
            Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
            Assert.Equal(-7, (await Submit(fixture, timeout.Token, Cases.Packet(id: -7))).RequestId);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(timeout.Token); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task RejectionReleasesWithoutChangingBothBaselines()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        var before = fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value;
        await Submit(fixture, timeout.Token);
        await fixture.ReceiveRequestAsync(timeout.Token);
        await RespondAsync(fixture, timeout.Token, new(7, -5, false, []));
        Assert.True(Released(fixture));
        Assert.Equal(1, fixture.Session.ItemStackTracker.RejectedCount);
        Assert.Equal(before.Items[0], fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.Items[0]);
        Assert.Same(before.AuthorityReceipts[0], fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.AuthorityReceipts[0]);
        Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
        Assert.Equal(-7, (await Submit(fixture, timeout.Token, Cases.Packet(id: -7))).RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationAfterGrantPreservesActualResult(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();
        await using Fixture fixture = await CreateAsync(timeout.Token);
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(timeout.Token);
            caller.Cancel();
            gate.Allow();
            Assert.Equal(-5, (await submit.WaitAsync(timeout.Token)).RequestId);
            Assert.Equal(-5, (await fixture.ReceiveRequestAsync(timeout.Token)).RequestId);
            await RespondAsync(fixture, timeout.Token);
            Assert.True(Released(fixture));
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(timeout.Token);
        }
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("exception")]
    public async Task AdmissionFailureDrainsAndPreservesKnownBaselines(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        if (failure == "refused") fixture.Transport.RefuseNextReservation();
        else fixture.Transport.ThrowNextReservation();
        if (failure == "refused") await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, timeout.Token));
        else await Assert.ThrowsAsync<IOException>(() => Submit(fixture, timeout.Token));
        Assert.True(Released(fixture));
        Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
        Assert.True(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), timeout.Token);
        Assert.True(fixture.Transport.DisposeEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task PendingResponseIsNotPartOfSendDrainAndLateEntryIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        await Submit(fixture, timeout.Token);
        await fixture.ReceiveRequestAsync(timeout.Token);
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        BedrockCreativeEntry entry = fixture.Session.LastCreativeContent!.Entries[0];
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), timeout.Token);
        Assert.True(Released(fixture));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitObservedCreativeCursorPlaceAsync(entry, Cases.Packet(), timeout.Token).AsTask());
    }

    [Theory]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("life", false)]
    [InlineData("life", true)]
    [InlineData("world", false)]
    [InlineData("world", true)]
    public async Task GrantedOwnershipRetirementPreservesResultAndBoundsOldCounterpart(string boundary, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (boundary == "close")
            {
                await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(2, -1, true)), ct);
                await WaitAsync(() => Released(fixture), ct);
            }
            else if (boundary == "life") fixture.Session.HandleRespawn();
            else fixture.Session.HandleWorldReplacement();
            Assert.True(Released(fixture));
            Assert.False(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
            gate.Allow();
            Assert.Equal(-5, (await submit.WaitAsync(ct)).RequestId);
            Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            await RespondAsync(fixture, ct);
            Assert.False(fixture.Session.InventoryStore.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
            Assert.True(Released(fixture));
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(ct);
        }
    }

}
