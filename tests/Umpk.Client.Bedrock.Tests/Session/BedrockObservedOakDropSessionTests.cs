using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using Gate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockObservedOakDropSessionTests
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private static readonly BedrockContainerAddress s_cursor = new(124, 0, null);
    private const string BusyMessage = "Inventory submissions are unavailable while the fixed oak Drop send is active.";
    private const string ConsumedMessage = "The fixed oak Drop opportunity is consumed until reconnect.";
    private const string ModeledDropPayloadHex = "0701011d010001000102000101000100000500400000004d0a0000000000000000000005003f0000004d0a000000000000000000000201000101000000000000000000000500010000004d0a00000000000000000000";

    [Fact]
    public async Task FixedTransferSeparatesAuthorityAndAllowsAnUnrelatedKnownPlaceAfterFinally()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        BedrockObservedOakDropSubmitResult result = await Drop(fixture, ct);
        Assert.Equal(BedrockObservedOakDropDisposition.TransferredWithTentativeRemainder, result.Disposition);
        Assert.Equal(-4, result.LegacyRequestId);
        Assert.Equal(before.Items[0], Snapshot(fixture).Items[0]);
        Assert.Same(before.AuthorityReceipts[0], result.AuthorityReceipt);
        Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        BedrockTentativeInventorySnapshot overlay = fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.Equal(Oak(63, null), overlay.TentativeItem);
        Assert.Equal(-4, overlay.LegacyRequestId);
        Assert.Equal(result.OutboundPayload, await ReceiveDropAsync(fixture, ct));
        AssertFinished(fixture, receiptRetained: true);
        byte[] firstRead = result.OutboundPayload;
        firstRead[0] = 0;
        Assert.Equal(7, result.OutboundPayload[0]);

        BedrockItemStackSubmitResult place = await fixture.Session.SubmitInventoryPlaceAsync(s_player, 2, 3, 1, ct);
        Assert.Equal(-3, place.RequestId);
        Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        await ReplyAsync(fixture, new(0, place.RequestId, true, [new(new(28, null), [new(2, 2, 4, 7, "", "", 0), new(3, 3, 1, 9, "", "", 0)])]), ct);
        Assert.Equal(Diamond(4, 7), Snapshot(fixture).Items[2]);
        Assert.Equal(Diamond(1, 9), Snapshot(fixture).Items[3]);
        Assert.True(Snapshot(fixture).IsSlotKnown(2));
        Assert.True(Snapshot(fixture).IsSlotKnown(3));
        Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitInventoryPlaceAsync(s_player, 0, 1, 1, ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartRaw(fixture, -101, ct));
        AssertFinished(fixture, receiptRetained: true);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("grant")]
    [InlineData("result")]
    public async Task NoPredictionBeforeTheActualOwnershipTransferResult(string phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        Gate gate = Hold(fixture, phase);
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.False(drop.IsCompleted);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            if (phase == "result") _ = await ReceiveDropAsync(fixture, ct);
            gate.Allow();
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredWithTentativeRemainder, (await drop.WaitAsync(ct)).Disposition);
            if (phase != "result") _ = await ReceiveDropAsync(fixture, ct);
            Assert.False(Snapshot(fixture).IsSlotKnown(0));
            AssertFinished(fixture, receiptRetained: true);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Fact]
    public async Task UnrelatedSlotAndCursorContentDuringCapacityWaitPreserveTheEligibleSource()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        object sourceReceipt = Snapshot(fixture).AuthorityReceipts[0];
        object cursorReceipt = fixture.Session.InventoryStore.GetSnapshot(s_cursor)!.Value.AuthorityReceipts[0];
        Gate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 2, s_player, null, Diamond(5, 99))), ct);
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(124, Enumerable.Repeat(Air(), 54).ToArray(), s_cursor, Air())), ct);
            await WaitAsync(() => Snapshot(fixture).Items[2].StackNetworkId == 99 && !ReferenceEquals(cursorReceipt, fixture.Session.InventoryStore.GetSnapshot(s_cursor)!.Value.AuthorityReceipts[0]), ct);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            Assert.Same(sourceReceipt, Snapshot(fixture).AuthorityReceipts[0]);
            Assert.False(drop.IsCompleted);
            gate.Allow();
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredWithTentativeRemainder, (await drop.WaitAsync(ct)).Disposition);
            _ = await ReceiveDropAsync(fixture, ct);
            Assert.Equal(Diamond(5, 99), Snapshot(fixture).Items[2]);
            Assert.True(Snapshot(fixture).IsSlotKnown(2));
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData(false, "same")]
    [InlineData(true, "same")]
    [InlineData(false, "changed")]
    [InlineData(true, "changed")]
    [InlineData(false, "aba")]
    [InlineData(true, "aba")]
    public async Task ActualAffectedAuthorityBeforeCapacityGrantRetiresWithoutOverwritingIt(bool content, string replacement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        int commits = fixture.Transport.CommittedCount;
        Gate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            if (replacement == "aba") await SourceAsync(fixture, Oak(64, 99), content, ct);
            await SourceAsync(fixture, Oak(64, replacement == "changed" ? 99 : 88), content, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => drop.WaitAsync(ct));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 30);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            Assert.NotSame(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
            Assert.Equal(Oak(64, replacement == "changed" ? 99 : 88), Snapshot(fixture).Items[0]);
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            AssertFinished(fixture, receiptRetained: false);
            Assert.Equal(ConsumedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct))).Message);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NewerSameValueReceiptWinsEvenWhenAnOwnerBoundaryAlsoOccurs(bool afterTransfer, bool ownerRetires)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = Hold(fixture, afterTransfer ? "result" : "grant");
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            await SourceAsync(fixture, Oak(64, 99), false, ct);
            await SourceAsync(fixture, Oak(64, 88), false, ct);
            object newReceipt = Snapshot(fixture).AuthorityReceipts[0];
            if (ownerRetires) await BoundaryAsync(fixture, "catalog", ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.False(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            gate.Allow();
            BedrockObservedOakDropSubmitResult result = await drop.WaitAsync(ct);
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredButSuperseded, result.Disposition);
            Assert.Equal(result.OutboundPayload, await ReceiveDropAsync(fixture, ct));
            Assert.Same(newReceipt, Snapshot(fixture).AuthorityReceipts[0]);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            AssertFinished(fixture, receiptRetained: false);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("reopen", false)]
    [InlineData("reopen", true)]
    [InlineData("catalog", false)]
    [InlineData("catalog", true)]
    [InlineData("registry", false)]
    [InlineData("registry", true)]
    [InlineData("life", false)]
    [InlineData("life", true)]
    [InlineData("world", false)]
    [InlineData("world", true)]
    [InlineData("terminal", false)]
    [InlineData("terminal", true)]
    public async Task GrantedRetiredOwnerReturnsRealTransferAndOnlyInvalidatesUnchangedCarriedAuthority(string boundary, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        BedrockCreativeEntry originalEntry = Entry(fixture);
        int generation = fixture.Session.InventoryStore.Generation;
        Gate gate = Hold(fixture, afterTransfer ? "result" : "grant");
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            await BoundaryAsync(fixture, boundary, ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.False(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.False(drop.IsCompleted);
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            if (boundary != "terminal")
            {
                Assert.True(Snapshot(fixture).IsSlotKnown(0));
                Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
                if (boundary is "life" or "world") Assert.True(fixture.Session.InventoryStore.Generation > generation);
            }
            gate.Allow();
            BedrockObservedOakDropSubmitResult result = await drop.WaitAsync(ct);
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredButOwnerRetired, result.Disposition);
            Assert.Equal(result.OutboundPayload, await ReceiveDropAsync(fixture, ct));
            if (boundary == "terminal") Assert.Null(fixture.Session.InventoryStore.GetSnapshot(s_player));
            else
            {
                Assert.False(Snapshot(fixture).IsSlotKnown(0));
                Assert.Equal(before.Items[0], Snapshot(fixture).Items[0]);
                Assert.Equal(before.ResponseCorrections[0], Snapshot(fixture).ResponseCorrections[0]);
                Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
                Assert.True(Snapshot(fixture).IsSlotKnown(2));
            }
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            AssertFinished(fixture, receiptRetained: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitObservedOakDropOneAsync(originalEntry, ct).AsTask());
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData("close")]
    [InlineData("catalog")]
    [InlineData("life")]
    [InlineData("world")]
    public async Task FreshAuthorityAfterRetirementWinsOverTheCarriedReceiptFallback(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextTransferResult();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            await BoundaryAsync(fixture, boundary, ct);
            await SourceAsync(fixture, Oak(64, 88), false, ct);
            object freshReceipt = Snapshot(fixture).AuthorityReceipts[0];
            gate.Allow();
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredButSuperseded, (await drop.WaitAsync(ct)).Disposition);
            _ = await ReceiveDropAsync(fixture, ct);
            Assert.Same(freshReceipt, Snapshot(fixture).AuthorityReceipts[0]);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("close")]
    [InlineData("catalog")]
    [InlineData("registry")]
    [InlineData("life")]
    [InlineData("world")]
    [InlineData("terminal")]
    public async Task CapacityCancellationOrRetirementNeverPredictsOrTransfers(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        int commits = fixture.Transport.CommittedCount;
        Gate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            if (boundary == "caller") caller.Cancel(); else await BoundaryAsync(fixture, boundary, ct);
            if (boundary == "caller")
            {
                OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drop.WaitAsync(ct));
                Assert.Equal(caller.Token, error.CancellationToken);
            }
            else if (boundary == "terminal") await Assert.ThrowsAsync<BedrockSessionException>(() => drop.WaitAsync(ct));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => drop.WaitAsync(ct));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
            if (boundary != "terminal")
            {
                Assert.True(Snapshot(fixture).IsSlotKnown(0));
                Assert.Equal(before.Items[0], Snapshot(fixture).Items[0]);
                Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
            }
            AssertFinished(fixture, receiptRetained: false);
            if (boundary == "caller") Assert.Equal(ConsumedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct))).Message);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Fact]
    public async Task InlineCancellationRetiresEligibilityBeforeResumingAndDrainsWithoutDeadlock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            fixture.Session.HandleRespawn();
            Assert.True(drop.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => drop);
            Assert.False(fixture.Session.ObservedOakDropPredictionEligible);
            AssertFinished(fixture, receiptRetained: false);
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(ct);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Fact]
    public async Task InlineTerminalRetirementRecordsItsCauseBeforeCancellationResumesTheSend()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            await BoundaryAsync(fixture, "terminal", ct);
            Assert.True(drop.IsCompleted);
            BedrockSessionException failure = await Assert.ThrowsAsync<BedrockSessionException>(() => drop);
            Assert.Equal(BedrockSessionEndReason.RemoteDisconnect, failure.Termination.Reason);
            Assert.Null(fixture.Session.InventoryStore.GetSnapshot(s_player));
            AssertFinished(fixture, receiptRetained: false);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Fact]
    public async Task ConcurrentTerminalCallersKeepTheDropFailureBoundToTheActualWinningObject()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? disconnect = null;
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            disconnect = Task.Run(async () => { await start.Task.WaitAsync(ct); await fixture.Session.DisconnectAsync(ct); }, ct);
            dispose = Task.Run(async () => { await start.Task.WaitAsync(ct); await fixture.Session.DisposeAsync(); }, ct);
            start.TrySetResult();
            BedrockTermination winner = await fixture.Session.Completion.WaitAsync(ct);
            BedrockSessionException failure = await Assert.ThrowsAsync<BedrockSessionException>(() => drop.WaitAsync(ct));
            Assert.Same(winner, failure.Termination);
            Assert.Equal(BedrockSessionEndReason.LocalDisconnect, winner.Reason);
            Assert.True(winner.Detail is null or "The session was disposed.");
            await Task.WhenAll(disconnect, dispose).WaitAsync(ct);
            Assert.Same(winner, await fixture.Session.Completion.WaitAsync(ct));
            AssertFinished(fixture, receiptRetained: false);
        }
        finally
        {
            start.TrySetResult(); gate.Allow();
            await ObserveForCleanupAsync(drop, ct);
            if (disconnect is not null) await ObserveForCleanupAsync(disconnect, ct);
            if (dispose is not null) await ObserveForCleanupAsync(dispose, ct);
        }
    }

    [Fact]
    public async Task InlineFailureContinuationCannotLetAReentrantDisposeReplaceTheRemoteTerminalWinner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        Task? dispose = null;
        bool reenteredBeforeCompletion = false;
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            // This owned continuation models a caller immediately disposing after its inline send failure; it installs no production publishing seam.
            dispose = drop.ContinueWith(_ =>
            {
                reenteredBeforeCompletion = !fixture.Session.Completion.IsCompleted;
                return fixture.Session.DisposeAsync().AsTask();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            await fixture.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(4, false, "winning remote reason", "winning remote reason")), ct);
            BedrockTermination winner = await fixture.Session.Completion.WaitAsync(ct);
            BedrockSessionException failure = await Assert.ThrowsAsync<BedrockSessionException>(() => drop.WaitAsync(ct));
            Assert.True(reenteredBeforeCompletion, "The continuation did not exercise reentry before the winning lifetime completion.");
            Assert.Equal(BedrockSessionEndReason.RemoteDisconnect, winner.Reason);
            Assert.Equal("winning remote reason", winner.Detail);
            Assert.Same(winner, failure.Termination);
            await dispose.WaitAsync(ct);
            Assert.Same(winner, await fixture.Session.Completion.WaitAsync(ct));
            AssertFinished(fixture, receiptRetained: false);
        }
        finally
        {
            gate.Allow(); await ObserveForCleanupAsync(drop, ct);
            if (dispose is not null) await ObserveForCleanupAsync(dispose, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationAfterGrantPreservesTransferredResultAndPrediction(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = Hold(fixture, afterTransfer ? "result" : "grant");
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            caller.Cancel();
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            gate.Allow();
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredWithTentativeRemainder, (await drop.WaitAsync(ct)).Disposition);
            _ = await ReceiveDropAsync(fixture, ct);
            Assert.False(Snapshot(fixture).IsSlotKnown(0));
            AssertFinished(fixture, receiptRetained: true);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("exception")]
    public async Task FailedAdmittedSendReleasesEverythingButNeverReplenishesMinusFour(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        if (failure == "refused") fixture.Transport.RefuseNextReservation(); else fixture.Transport.ThrowNextReservation();
        if (failure == "refused") await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct));
        else await Assert.ThrowsAsync<IOException>(() => Drop(fixture, ct));
        AssertFinished(fixture, receiptRetained: false);
        Assert.True(Snapshot(fixture).IsSlotKnown(0));
        Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
        Assert.Equal(before.Items[0], Snapshot(fixture).Items[0]);
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        Assert.Equal(ConsumedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct))).Message);
    }

    [Theory]
    [InlineData("place", false)]
    [InlineData("take", false)]
    [InlineData("swap", false)]
    [InlineData("raw", false)]
    [InlineData("creative", false)]
    [InlineData("cursor", false)]
    [InlineData("open", false)]
    [InlineData("place", true)]
    [InlineData("take", true)]
    [InlineData("swap", true)]
    [InlineData("raw", true)]
    [InlineData("creative", true)]
    [InlineData("cursor", true)]
    [InlineData("open", true)]
    public async Task EveryInventoryEntryRefusesTheActiveHolderEvenAfterEligibilityRetirement(string entry, bool retired)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = Hold(fixture, retired ? "grant" : "capacity");
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        Task? conflict = null;
        Gate? unexpected = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            if (retired) await BoundaryAsync(fixture, "catalog", ct);
            unexpected = fixture.Transport.HoldNextReservation();
            conflict = ConflictingEntry(fixture, entry, ct);
            Assert.True(conflict.IsCompleted, "A conflicting entry queued behind the active fixed Drop rather than refusing before output wait.");
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => conflict);
            Assert.Equal(BusyMessage, error.Message);
            Assert.False(unexpected.Entered.IsCompleted);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
            Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-4));
            Assert.False(fixture.Session.ActionCoordinator.IsClaimed(-101));
            gate.Allow();
            _ = await drop.WaitAsync(ct);
            _ = await ReceiveDropAsync(fixture, ct);
        }
        finally
        {
            gate.Allow(); unexpected?.Allow(); fixture.Transport.ReleaseGates();
            await ObserveForCleanupAsync(drop, ct);
            if (conflict is not null) await ObserveForCleanupAsync(conflict, ct);
        }
    }

    [Fact]
    public async Task RawNumericCollisionCannotReleaseTheDropClaimOrRegisterAnEvenResponseOwner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate gate = fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            Assert.False(fixture.Session.ActionCoordinator.TryClaim(-4, BedrockActionEmission.Standalone));
            Assert.True(fixture.Session.ItemStackTracker.TryRegister(-4, fixture.Session.ItemStackTracker.Generation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(Raw(-4), -4, fixture.Session.ItemStackTracker.Generation, ct).AsTask());
            Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-4));
            Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            gate.Allow();
            _ = await drop.WaitAsync(ct);
            _ = await ReceiveDropAsync(fixture, ct);
            AssertFinished(fixture, receiptRetained: true);
        }
        finally { gate.Allow(); await ObserveForCleanupAsync(drop, ct); }
    }

    [Theory]
    [InlineData("active")]
    [InlineData("pending")]
    [InlineData("open")]
    public async Task OlderOperationRefusesDropBeforeBurnAndDuplicateRawKeepsItsIncumbent(string older)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        Gate? gate = older == "pending" ? null : fixture.Transport.HoldNextGrantedCommit();
        Task? incumbent = null;
        Task<BedrockObservedOakDropSubmitResult>? refusedDrop = null;
        try
        {
            if (older == "open") incumbent = fixture.Session.SubmitNormalInventoryOpenAsync(ct).AsTask();
            else incumbent = StartRaw(fixture, -101, ct);
            if (gate is not null) await gate.Entered.WaitAsync(ct); else await incumbent.WaitAsync(ct);
            refusedDrop = Drop(fixture, ct);
            Assert.True(refusedDrop.IsCompleted, "Drop queued behind an existing owner instead of refusing before its first opportunity was burned.");
            await Assert.ThrowsAsync<InvalidOperationException>(() => refusedDrop);
            Assert.False(fixture.Session.ObservedOakDropSendActive);
            if (older == "active")
            {
                Assert.Equal("The inventory request already has an active send.", (await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(Raw(-101), -101, fixture.Session.ItemStackTracker.Generation, ct).AsTask())).Message);
                Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-101));
                Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
            }
            gate?.Allow();
            await incumbent.WaitAsync(ct);
            if (older != "open")
            {
                Assert.Equal(-101, (await fixture.ReceiveRequestAsync(ct)).RequestId);
                await ReplyAsync(fixture, new(1, -101, false, []), ct);
            }
            _ = await Drop(fixture, ct);
            _ = await ReceiveDropAsync(fixture, ct);
        }
        finally
        {
            gate?.Allow(); fixture.Transport.ReleaseGates();
            if (incumbent is not null) await ObserveForCleanupAsync(incumbent, ct);
            if (refusedDrop is not null) await ObserveForCleanupAsync(refusedDrop, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAffectedCorrectionClearsOverlayAndOwnerWhileUnrelatedDataDoesNot(bool content)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        _ = await Drop(fixture, ct);
        _ = await ReceiveDropAsync(fixture, ct);
        object oldReceipt = Snapshot(fixture).AuthorityReceipts[0];
        await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 2, s_player, null, Diamond(5, 99))), ct);
        await WaitAsync(() => Snapshot(fixture).Items[2].StackNetworkId == 99, ct);
        Assert.True(fixture.Session.ObservedOakDropReceiptRetained);
        Assert.NotNull(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        await SourceAsync(fixture, Oak(64, 88), content, ct);
        Assert.True(Snapshot(fixture).IsSlotKnown(0));
        Assert.NotSame(oldReceipt, Snapshot(fixture).AuthorityReceipts[0]);
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        AssertFinished(fixture, receiptRetained: false);
        Assert.Equal(ConsumedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct))).Message);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reopen")]
    [InlineData("catalog")]
    [InlineData("registry")]
    [InlineData("life")]
    [InlineData("world")]
    public async Task PostTransferOwnerRetirementRemovesOnlyOverlayAndDoesNotMintAuthority(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        _ = await Drop(fixture, ct);
        _ = await ReceiveDropAsync(fixture, ct);
        BedrockInventorySnapshot before = Snapshot(fixture);
        await BoundaryAsync(fixture, boundary, ct);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        Assert.Equal(before.Items[0], Snapshot(fixture).Items[0]);
        Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        AssertFinished(fixture, receiptRetained: false);
    }

    [Fact]
    public async Task UnrelatedEventsAndEvenOrOddResponseAndNewItemNeverAcknowledgeDrop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        _ = await Drop(fixture, ct);
        _ = await ReceiveDropAsync(fixture, ct);
        object receipt = Snapshot(fixture).AuthorityReceipts[0];
        int unknown = fixture.Session.ItemStackTracker.UnknownCount;
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new(0, -4, true, [new(new(28, null), [new(0, 0, 63, 99, "", "", 0)])]), new(0, -99, true, []), new(0, 4, true, [])])), ct);
        await WaitAsync(() => fixture.Session.ItemStackTracker.UnknownCount >= unknown + 3, ct);
        await fixture.SendAsync(15, BedrockActorCodecs.Encode(new BedrockAddItemEntityPacket(1000, 1000, Oak(1, null), new(1, 2, 3), new(0, 0, 0), [], false)), ct);
        await WaitAsync(() => fixture.Session.ActorSnapshots.Any(actor => actor.Handle.RuntimeEntityId == 1000), ct);
        await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(8, 0, true)), ct);
        await WaitAsync(() => fixture.Session.WireTranscript.Any(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 47), ct);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        Assert.Same(receipt, Snapshot(fixture).AuthorityReceipts[0]);
        Assert.Equal(Oak(64, 88), Snapshot(fixture).Items[0]);
        Assert.NotNull(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        AssertFinished(fixture, receiptRetained: true);
    }

    [Fact]
    public async Task ValidationDoesNotBurnButTransferAndCorrectionsNeverReuseTheOpportunityAndReconnectIsFresh()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        BedrockCreativeEntry actual = Entry(fixture);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitObservedOakDropOneAsync(new(actual.EntryId, actual.Item, actual.GroupIndex), ct).AsTask());
        _ = await Drop(fixture, ct);
        _ = await ReceiveDropAsync(fixture, ct);
        await SourceAsync(fixture, Oak(64, 88), false, ct);
        Assert.Equal(ConsumedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => Drop(fixture, ct))).Message);
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 30);
        await using Fixture reconnect = await PrepareAsync(ct);
        Assert.Equal(-4, (await Drop(reconnect, ct)).LegacyRequestId);
        _ = await ReceiveDropAsync(reconnect, ct);
        Assert.Single(reconnect.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 30);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeJoinsGrantedAccountingBeforeTransportAbortAndPeerClosure(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await PrepareAsync(ct);
        byte[] expectedPayload = Convert.FromHexString(ModeledDropPayloadHex);
        int commits = fixture.Transport.CommittedCount;
        Gate gate = Hold(fixture, afterTransfer ? "result" : "grant");
        Task<BedrockObservedOakDropSubmitResult> drop = Drop(fixture, ct);
        Task? dispose = null;
        Task<byte[]>? peerDrop = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.True(fixture.Session.ObservedOakDropPredictionEligible);
            if (afterTransfer)
            {
                // The actual message exists while the returned-result gate still owns the send; join this reader before disposal can abort queued data.
                peerDrop = ReceiveDropAsync(fixture, ct);
                Assert.Equal(expectedPayload, await peerDrop.WaitAsync(ct));
            }
            dispose = fixture.Session.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.True(fixture.Session.ObservedOakDropSendActive);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            gate.Allow();
            BedrockObservedOakDropSubmitResult result = await drop.WaitAsync(ct);
            Assert.Equal(BedrockObservedOakDropDisposition.TransferredButOwnerRetired, result.Disposition);
            Assert.Equal(expectedPayload, result.OutboundPayload);
            if (afterTransfer) Assert.Equal(await peerDrop!.WaitAsync(ct), result.OutboundPayload);
            await dispose.WaitAsync(ct);
            Assert.Equal(commits + 1, fixture.Transport.CommittedCount);
            Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 30);
            Assert.True(fixture.Transport.DisposeEntered.Task.IsCompleted);
            AssertFinished(fixture, receiptRetained: false);
            // Ownership transfer precedes Abort; Abort may discard unread queued messages. EOF is required independently of delivery.
            await AssertPeerClosedAsync(fixture, expectedPayload, afterTransfer, ct);
        }
        finally
        {
            gate.Allow(); await ObserveForCleanupAsync(drop, ct);
            if (peerDrop is not null) await ObserveForCleanupAsync(peerDrop, ct);
            if (dispose is not null) await dispose.WaitAsync(ct);
        }
    }

    private static async Task AssertPeerClosedAsync(Fixture fixture, byte[] expectedPayload, bool dropAlreadyRead, CancellationToken ct)
    {
        for (int index = 0; index < 16; index++)
        {
            try
            {
                (int packetId, byte[] payload) = await fixture.ReceiveAsync(ct);
                if (packetId == 30)
                {
                    Assert.False(dropAlreadyRead, "The peer received a duplicate fixed Drop after its earlier controlled receive.");
                    Assert.Equal(expectedPayload, payload);
                    dropAlreadyRead = true;
                }
            }
            catch (InvalidOperationException exception) when (exception.Message == "The fake peer transport closed.")
            {
                return;
            }
        }
        Assert.Fail("The peer did not observe EOF after the joined owned transport disposal.");
    }

    private static async Task<Fixture> PrepareAsync(CancellationToken ct)
    {
        Fixture fixture = await Fixture.CreateAsync(ct, [new BedrockItemRegistryPacket([new("minecraft:oak_planks", 5, false, 2, new NbtCompound()), new("minecraft:diamond", 306, false, 2, new NbtCompound())])]);
        try
        {
            BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray();
            items[0] = Oak(64, 88); items[2] = Diamond(5, 7); items[4] = Diamond(1, 10); items[5] = Diamond(2, 11);
            object receipt = Snapshot(fixture).AuthorityReceipts[0];
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, items, s_player, Air())), ct);
            await WaitAsync(() => !ReferenceEquals(receipt, Snapshot(fixture).AuthorityReceipts[0]), ct);
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(124, Enumerable.Repeat(Air(), 54).ToArray(), s_cursor, Air())), ct);
            await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(new BedrockCreativeContentPacket([], [new(17, new(5, 1, 0, 77, new byte[10]), 0)])), ct);
            await WaitAsync(() => fixture.Session.LastCreativeContent is not null && fixture.Session.InventoryStore.GetSnapshot(s_cursor) is not null, ct);
            await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(2, -1, 0, 0, 0, -1)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is not null, ct);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private static async Task BoundaryAsync(Fixture fixture, string boundary, CancellationToken ct)
    {
        if (boundary == "life") fixture.Session.HandleRespawn();
        else if (boundary == "world") fixture.Session.HandleWorldReplacement();
        else if (boundary == "terminal")
        {
            await fixture.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(4, false, "controlled drop termination", "controlled drop termination")), ct);
            await fixture.Session.Completion.WaitAsync(ct);
        }
        else if (boundary == "registry")
        {
            await fixture.SendAsync(162, BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket([new("minecraft:oak_planks", 5, false, 2, new NbtCompound())])), ct);
        }
        else if (boundary == "catalog")
        {
            int prior = fixture.Session.CreativeContentObservationCount;
            await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(new BedrockCreativeContentPacket([], [new(17, new(5, 1, 0, 77, new byte[10]), 0)])), ct);
            await WaitAsync(() => fixture.Session.CreativeContentObservationCount > prior, ct);
        }
        else if (boundary == "reopen")
        {
            await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(2, -1, 1, 0, 0, -1)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is { X: 1 }, ct);
        }
        else
        {
            await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(2, -1, true)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is null, ct);
        }
        await WaitAsync(() => !fixture.Session.ObservedOakDropPredictionEligible && !fixture.Session.ObservedOakDropReceiptRetained, ct);
    }

    private static async Task SourceAsync(Fixture fixture, BedrockItemInstance item, bool content, CancellationToken ct)
    {
        object receipt = Snapshot(fixture).AuthorityReceipts[0];
        if (content)
        {
            BedrockInventorySnapshot before = Snapshot(fixture);
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, before.Items.SetItem(0, item), s_player, before.StorageItem)), ct);
        }
        else await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 0, s_player, null, item)), ct);
        await WaitAsync(() => !ReferenceEquals(receipt, Snapshot(fixture).AuthorityReceipts[0]) && Snapshot(fixture).Items[0].Equals(item), ct);
    }

    private static async Task ReplyAsync(Fixture fixture, BedrockItemStackResponse response, CancellationToken ct)
    {
        int prior = fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount;
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([response])), ct);
        await WaitAsync(() => fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount > prior
            && fixture.Session.PendingItemStackReconciliationCount == 0 && fixture.Session.ItemStackTracker.PendingCount == 0
            && fixture.Session.ActionCoordinator.ClaimedCount == 0, ct);
    }

    private static async Task<byte[]> ReceiveDropAsync(Fixture fixture, CancellationToken ct)
    {
        for (int i = 0; i < 16; i++)
        {
            (int packetId, byte[] payload) = await fixture.ReceiveAsync(ct);
            if (packetId != 30) continue;
            BedrockInventoryTransactionPacket packet = BedrockInventoryTransactionCodecs.Decode(payload);
            Assert.Equal(-4, packet.Legacy.RequestId);
            Assert.Equal(2, packet.Actions.Length);
            Assert.Equal(Oak(64, null), packet.Actions[0].OldItem);
            Assert.Equal(Oak(63, null), packet.Actions[0].NewItem);
            Assert.Equal(Air(), packet.Actions[1].OldItem);
            Assert.Equal(Oak(1, null), packet.Actions[1].NewItem);
            Assert.Equal((sbyte)0, packet.Actions[0].WindowId);
            Assert.Null(packet.Actions[0].Flags);
            Assert.Null(packet.Actions[1].WindowId);
            Assert.Equal((uint)0, packet.Actions[1].Flags);
            Assert.True(packet.Actions.All(action => action.ContainerPresence && action.FlagPresence && action.Slot == 0));
            return payload;
        }
        throw new InvalidOperationException("The actual controlled peer did not receive fixed Drop30.");
    }

    private static Task ConflictingEntry(Fixture fixture, string entry, CancellationToken ct) => entry switch
    {
        "place" => fixture.Session.SubmitInventoryPlaceAsync(s_player, 2, 3, 1, ct).AsTask(),
        "take" => fixture.Session.SubmitInventoryTakeAsync(s_player, 2, 3, 1, ct).AsTask(),
        "swap" => fixture.Session.SubmitInventorySwapAsync(s_player, 4, 5, ct).AsTask(),
        "raw" => StartRaw(fixture, -101, ct),
        "creative" => fixture.Session.SubmitObservedCreativeCursorRequestAsync(Entry(fixture), Creative(), ct).AsTask(),
        "cursor" => fixture.Session.SubmitObservedCreativeCursorPlaceAsync(Entry(fixture), new([new(-3, [new(BedrockItemStackActionType.Place, 1, new BedrockPlacePayload(64, new(new(59, null), 0, 88), new(new(28, null), 0, 0)))], [], -1)]), ct).AsTask(),
        _ => fixture.Session.SubmitNormalInventoryOpenAsync(ct).AsTask(),
    };

    private static BedrockItemStackRequestPacket Creative() => new([new(-3,
    [
        new(BedrockItemStackActionType.CraftCreative, 14, new BedrockCraftCreativePayload(17, 1)),
        new(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload([new(1, 1, "minecraft:oak_planks", 0, 1, 77, new byte[10])], 1)),
        new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(64, new(new(60, null), 50, -3), new(new(59, null), 0, 0))),
    ], [], -1)]);
    private static BedrockItemStackRequestPacket Raw(int id) => new([new(id, [new(BedrockItemStackActionType.Place, 1, new BedrockPlacePayload(1, new(new(28, null), 2, 7), new(new(28, null), 3, 0)))], [], 0)]);
    private static Task<BedrockItemStackSubmitResult> StartRaw(Fixture fixture, int id, CancellationToken ct)
    {
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(id, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(id, generation));
        return fixture.Session.SendItemStackRequestAsync(Raw(id), id, generation, ct).AsTask();
    }
    private static Task<BedrockObservedOakDropSubmitResult> Drop(Fixture fixture, CancellationToken ct) => fixture.Session.SubmitObservedOakDropOneAsync(Entry(fixture), ct).AsTask();
    private static BedrockCreativeEntry Entry(Fixture fixture) => fixture.Session.LastCreativeContent!.Entries[0];
    private static BedrockInventorySnapshot Snapshot(Fixture fixture) => fixture.Session.InventoryStore.GetSnapshot(s_player)!.Value;
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Oak(ushort count, int? stackId) => new(5, count, 0, stackId, 77, new byte[10]);
    private static BedrockItemInstance Diamond(ushort count, int? stackId) => new(306, count, 0, stackId, 0, []);
    private static Gate Hold(Fixture fixture, string phase) => phase switch { "capacity" => fixture.Transport.HoldNextReservation(), "grant" => fixture.Transport.HoldNextGrantedCommit(), _ => fixture.Transport.HoldNextTransferResult() };
    private static void AssertFinished(Fixture fixture, bool receiptRetained)
    {
        Assert.False(fixture.Session.ObservedOakDropSendActive);
        Assert.False(fixture.Session.ObservedOakDropPredictionEligible);
        Assert.Equal(receiptRetained, fixture.Session.ObservedOakDropReceiptRetained);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
    }
    private static async Task WaitAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate()) await Task.Delay(10, ct);
    }
    private static async Task ObserveForCleanupAsync(Task task, CancellationToken ct)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        catch (BedrockSessionException) { }
        catch (IOException) { }
    }
}
