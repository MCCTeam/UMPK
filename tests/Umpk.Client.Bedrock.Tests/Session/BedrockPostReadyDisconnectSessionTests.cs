using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using SendGate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

[Collection("Bedrock pre-ready inventory capture")]
public sealed class BedrockPostReadyDisconnectSessionTests
{
    private static readonly BedrockContainerAddress s_inventory = new(0, 0, null);

    [Fact]
    public async Task ValidPostReadyDisconnectPreservesRemoteReasonAndCountsOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        Assert.True(fixture.Session.Snapshot.IsReady);
        await fixture.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, false, "remote end", "remote end")), ct);
        await WaitUntilAsync(() => fixture.Session.WireTranscript.Any(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5), ct);
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5);
        BedrockTermination termination = await fixture.Session.Completion.WaitAsync(TimeSpan.FromSeconds(1), ct);
        Assert.Equal(BedrockSessionEndReason.RemoteDisconnect, termination.Reason);
        Assert.Equal(BedrockSessionScope.Session, termination.Scope);
        Assert.Equal("remote end", termination.Detail);
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5);
        Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task HiddenPostReadyDisconnectUsesExistingReasonDetail()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await fixture.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, true, null, null)), ct);
        Assert.Equal("reason 0", (await RemoteCompletionAsync(fixture, ct)).Detail);
        Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task PostReadyDisconnectUsesExistingControlRemovalAndLengthBound()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        string message = "\0\n" + new string('x', 300);
        await fixture.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, false, message, "filtered")), ct);
        Assert.Equal(new string('x', 256), (await RemoteCompletionAsync(fixture, ct)).Detail);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("boolean")]
    [InlineData("trailing")]
    public async Task MalformedPostReadyDisconnectRemainsProtocolFailure(string malformed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        byte[] payload = malformed switch
        {
            "truncated" => [0],
            "boolean" => [0, 2],
            _ => [0, 1, 0],
        };
        await fixture.SendAsync(5, payload, ct);
        BedrockTermination termination = await fixture.Session.Completion.WaitAsync(TimeSpan.FromSeconds(2), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.True(fixture.Transport.AbortEntered.Task.IsCompleted);
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5);
    }

    [Fact]
    public Task ValidDisconnectPreservesPrecedingInventoryAndIgnoresFollowingBody() => WithInventoryCaptureAsync(async ct =>
    {
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        int priorRaw = fixture.Session.CapturedInventoryContentPayloads.Count;
        int priorTyped = fixture.Session.CapturedInventoryContents.Count;
        byte[] preceding = BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [Air()], s_inventory, Air()));
        byte[] following = BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [Air(), Air()], s_inventory, Air()));
        await fixture.SendBatchAsync([(49, preceding), (5, Disconnect()), (49, following)], ct);
        await RemoteCompletionAsync(fixture, ct);
        Assert.Equal(priorRaw + 1, fixture.Session.CapturedInventoryContentPayloads.Count);
        Assert.Equal(preceding, fixture.Session.CapturedInventoryContentPayloads[^1]);
        Assert.Equal(priorTyped + 1, fixture.Session.CapturedInventoryContents.Count);
        Assert.Single(fixture.Session.CapturedInventoryContents[^1].Items);
        Assert.Null(fixture.Session.InventoryStore.GetSnapshot(s_inventory));
        Assert.Equal(new[] { 49, 5, 49 }, fixture.Session.WireTranscript.Where(row => row.Direction == BedrockWireDirection.Inbound).TakeLast(3).Select(row => row.PacketId));
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5);
        Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisconnectCutoffDistinguishesMalformedPredecessorFromIgnoredSuccessor(bool disconnectFirst)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        byte[] malformedContent = [0, 2, 1];
        await fixture.SendBatchAsync(disconnectFirst ? [(5, Disconnect()), (49, malformedContent)] : [(49, malformedContent), (5, Disconnect())], ct);
        BedrockTermination termination = await fixture.Session.Completion.WaitAsync(TimeSpan.FromSeconds(2), ct);
        Assert.Equal(disconnectFirst ? BedrockSessionEndReason.RemoteDisconnect : BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Equal(!disconnectFirst, fixture.Transport.AbortEntered.Task.IsCompleted);
        // The successor's framing was decoded, but its malformed payload receives no semantic qualification after valid5.
        Assert.Equal(disconnectFirst ? 1 : 0, fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5));
    }

    [Fact]
    public async Task FirstDisconnectWinsWhileEachActualHeaderIsObservedOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await fixture.SendBatchAsync([(5, Disconnect("first")), (5, [0, 2])], ct);
        Assert.Equal("first", (await RemoteCompletionAsync(fixture, ct)).Detail);
        Assert.Equal(2, fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 5));
        Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
    }

    [Fact]
    public Task RemoteDisconnectRetiresPendingResponseWithoutWaitingOrApplyingLater148() => WithInventoryCaptureAsync(async ct =>
    {
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        BedrockItemStackSubmitResult submitted = await fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct);
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        int generation = fixture.Session.InventoryStore.Generation;
        byte[] response = BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submitted.RequestId, true, [])]));
        await fixture.SendBatchAsync([(5, Disconnect()), (148, response)], ct);
        await RemoteCompletionAsync(fixture, ct);
        AssertReleased(fixture.Session);
        Assert.Empty(fixture.Session.CapturedItemStackResponsePayloads);
        Assert.Equal(0, fixture.Session.ItemStackTracker.AcceptedCount);
        Assert.Equal(generation + 1, fixture.Session.InventoryStore.Generation);
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct);
        Assert.Equal(generation + 1, fixture.Session.InventoryStore.Generation);
    });

    [Theory]
    [InlineData("open")]
    [InlineData("raw")]
    [InlineData("retained")]
    [InlineData("movement")]
    public async Task ActualRemoteDisconnectCancelsCapacityOwnersWithoutCommitting(string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        int committed = fixture.Transport.CommittedCount;
        Task operation = StartOperation(fixture, kind, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.SendAsync(5, Disconnect(), ct);
            await RemoteCompletionAsync(fixture, ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => operation.WaitAsync(TimeSpan.FromSeconds(2), ct));
            Assert.Equal(committed, fixture.Transport.CommittedCount);
            Assert.Equal(0, Sends(fixture.Session, PacketId(kind)));
            Assert.Equal(0, fixture.Session.PlayerAuthInputSendCount);
            AssertReleased(fixture.Session);
            await Assert.ThrowsAsync<InvalidOperationException>(() => StartOperation(fixture, kind, ct));
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(operation, ct);
        }
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("retained")]
    [InlineData("movement")]
    public async Task ActualRemoteDisconnectCancelsConnectionQueuedWorkWhileGrantedOpenStaysOwned(string queuedKind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextGrantedCommit();
        Task opening = StartOperation(fixture, "open", ct);
        Task? queued = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            queued = StartOperation(fixture, queuedKind, ct);
            Assert.False(queued.IsCompleted);
            await fixture.SendAsync(5, Disconnect(), ct);
            await RemoteCompletionAsync(fixture, ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2), ct));
            Assert.False(opening.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.Equal(0, Sends(fixture.Session, PacketId(queuedKind)));
            gate.Allow();
            await AssertTransferredResultAsync(opening, "open", ct);
            Assert.Equal(1, Sends(fixture.Session, 33));
            AssertReleased(fixture.Session);
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(opening, ct);
            if (queued is not null)
                await ObserveForCleanupAsync(queued, ct);
        }
    }

    [Fact]
    public async Task ActualRemoteDisconnectCancelsMovementSemaphoreWaiterWithoutDiscardingGrantedInput()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockPlayerAuthInputSubmitResult> first = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        Task<BedrockPlayerAuthInputSubmitResult>? second = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            second = fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), ct).AsTask();
            Assert.False(second.IsCompleted);
            await fixture.SendAsync(5, Disconnect(), ct);
            await RemoteCompletionAsync(fixture, ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => second.WaitAsync(TimeSpan.FromSeconds(2), ct));
            Assert.False(first.IsCompleted);
            gate.Allow();
            Assert.Equal(1UL, (await first.WaitAsync(TimeSpan.FromSeconds(2), ct)).Tick);
            Assert.Equal(1, fixture.Session.PlayerAuthInputSendCount);
            Assert.Equal(1, Sends(fixture.Session, 144));
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(first, ct);
            if (second is not null)
                await ObserveForCleanupAsync(second, ct);
        }
    }

    [Theory]
    [InlineData("open", false)]
    [InlineData("open", true)]
    [InlineData("raw", false)]
    [InlineData("raw", true)]
    [InlineData("retained", false)]
    [InlineData("retained", true)]
    [InlineData("movement", false)]
    [InlineData("movement", true)]
    public async Task ValidRemoteDisconnectPreservesGrantedResultAndDisposeDrain(string kind, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        int committed = fixture.Transport.CommittedCount;
        Task operation = StartOperation(fixture, kind, ct);
        Task? dispose = null;
        byte[]? actualTransferredPayload = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer)
                actualTransferredPayload = await ReceivePayloadAsync(fixture, PacketId(kind), ct);
            await fixture.SendAsync(5, Disconnect(), ct);
            await RemoteCompletionAsync(fixture, ct);
            Assert.False(operation.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            AssertReleased(fixture.Session);
            dispose = fixture.Session.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            gate.Allow();
            byte[] resultPayload = await AssertTransferredResultAsync(operation, kind, ct);
            if (actualTransferredPayload is not null)
                Assert.Equal(actualTransferredPayload, resultPayload);
            await dispose.WaitAsync(TimeSpan.FromSeconds(3), ct);
            Assert.Equal(committed + 1, fixture.Transport.CommittedCount);
            Assert.Equal(1, Sends(fixture.Session, PacketId(kind)));
            Assert.Equal(kind == "movement" ? 1 : 0, fixture.Session.PlayerAuthInputSendCount);
            Assert.True(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.True(fixture.Transport.DisposeEntered.Task.IsCompleted);
            InvalidOperationException eof = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReceiveAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct));
            Assert.Equal("The fake peer transport closed.", eof.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => StartOperation(fixture, kind, ct));
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(operation, ct);
            if (dispose is not null)
                await dispose.WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
    }

    [Theory]
    [InlineData("open")]
    [InlineData("retained")]
    [InlineData("movement")]
    public async Task ActualRemoteDisconnectRejectsCrossOperationReentryBeforeTerminalPublication(string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextInlineCancellation();
        int committed = fixture.Transport.CommittedCount;
        Task raw = StartRaw(fixture, ct);
        Task? attempted = null;
        bool? terminalPublishedAtEntry = null;
        Task continuation = raw.ContinueWith(_ =>
        {
            terminalPublishedAtEntry = fixture.Session.Completion.IsCompleted;
            attempted = StartOperation(fixture, kind, ct);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.SendAsync(5, Disconnect(), ct);
            await RemoteCompletionAsync(fixture, ct);
            await continuation.WaitAsync(TimeSpan.FromSeconds(2), ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => raw);
            Assert.False(terminalPublishedAtEntry);
            Assert.NotNull(attempted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempted);
            Assert.Equal(committed, fixture.Transport.CommittedCount);
            Assert.Equal(0, Sends(fixture.Session, PacketId(kind)));
            AssertReleased(fixture.Session);
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(raw, ct);
            if (attempted is not null)
                await ObserveForCleanupAsync(attempted, ct);
        }
    }

    private static Task StartOperation(Fixture fixture, string kind, CancellationToken ct) => kind switch
    {
        "open" => fixture.Session.SubmitNormalInventoryOpenAsync(ct).AsTask(),
        "movement" => fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask(),
        "retained" => fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct).AsTask(),
        _ => StartRaw(fixture, ct),
    };

    private static Task<BedrockItemStackSubmitResult> StartRaw(Fixture fixture, CancellationToken ct)
    {
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(-3, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(-3, generation));
        return fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 7, 0), -3, generation, ct).AsTask();
    }

    private static async Task<byte[]> AssertTransferredResultAsync(Task operation, string kind, CancellationToken ct)
    {
        if (kind == "open")
        {
            var result = await ((Task<(ulong TargetRuntimeId, byte[] OutboundPayload)>)operation).WaitAsync(TimeSpan.FromSeconds(3), ct);
            Assert.Equal(789UL, result.TargetRuntimeId);
            Assert.Equal(new byte[] { 6, 0x95, 6, 0 }, result.OutboundPayload);
            return result.OutboundPayload;
        }
        if (kind == "movement")
        {
            BedrockPlayerAuthInputSubmitResult result = await ((Task<BedrockPlayerAuthInputSubmitResult>)operation).WaitAsync(TimeSpan.FromSeconds(3), ct);
            Assert.Equal(1UL, result.Tick);
            Assert.Equal(1UL, BedrockPlayerAuthInputCodecs.Decode(result.OutboundPayload).Tick);
            return result.OutboundPayload;
        }
        BedrockItemStackSubmitResult inventoryResult = await ((Task<BedrockItemStackSubmitResult>)operation).WaitAsync(TimeSpan.FromSeconds(3), ct);
        Assert.Equal(-3, inventoryResult.RequestId);
        Assert.Equal(-3, Assert.Single(BedrockItemStackCodecs.DecodeRequest(inventoryResult.OutboundPayload).Requests).RequestId);
        return inventoryResult.OutboundPayload;
    }

    private static int PacketId(string kind) => kind == "open" ? 33 : kind == "movement" ? 144 : 147;
    private static int Sends(BedrockClientSession session, int id) => session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == id);
    private static byte[] Disconnect(string detail = "remote end") => BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, false, detail, detail));
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockPlayerAuthInputPacket Idle(ulong tick) =>
        new(0f, 0f, new BedrockVector3(0.5f, 71.62001f, 0.5f), new BedrockVector2(0f, 0f), 0f, [], 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    private static async Task<BedrockTermination> RemoteCompletionAsync(Fixture fixture, CancellationToken ct)
    {
        BedrockTermination termination = await fixture.Session.Completion.WaitAsync(TimeSpan.FromSeconds(2), ct);
        Assert.Equal(BedrockSessionEndReason.RemoteDisconnect, termination.Reason);
        Assert.Equal(BedrockSessionScope.Session, termination.Scope);
        return termination;
    }

    private static void AssertReleased(BedrockClientSession session)
    {
        Assert.Equal(0, session.PendingItemStackReconciliationCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    private static async Task<byte[]> ReceivePayloadAsync(Fixture fixture, int id, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            (int packetId, byte[] payload) = await fixture.ReceiveAsync(ct);
            if (packetId == id)
                return payload;
        }
        throw new InvalidOperationException("The controlled peer did not receive the expected granted packet.");
    }

    private static async Task WithInventoryCaptureAsync(Func<CancellationToken, Task> proof)
    {
        const string variable = "UMPK_BEDROCK_CAPTURE_INVENTORY";
        string? previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "1");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await proof(timeout.Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private static async Task ObserveForCleanupAsync(Task operation, CancellationToken ct)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (BedrockSessionException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(10, ct);
    }
}
