using System.Net;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockItemStackOwnershipTests
{
    private static readonly BedrockContainerAddress s_inventoryAddress = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);

    [Fact]
    public async Task CallerCancellationBeforeReservationGrantReleasesRetainedStateAndDoesNotReuseId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, caller.Token).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            AssertPending(fixture.Session, -3);
            await caller.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));

            AssertReleased(fixture.Session);
            Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            try
            {
                await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
            }
        }

        BedrockItemStackSubmitResult retry = await fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct);
        Assert.Equal(-5, retry.RequestId);
        Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        AssertPending(fixture.Session, retry.RequestId);
        await fixture.RejectAsync(retry.RequestId, ct);
        AssertReleased(fixture.Session);
    }

    [Fact]
    public async Task RespawnBeforeReservationGrantRetiresRequestWithoutCommitting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            AssertPending(fixture.Session, -3);
            fixture.Session.HandleRespawn();

            await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));

            AssertReleased(fixture.Session);
            Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct).AsTask());
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            try
            {
                await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    [Fact]
    public async Task ReservationRefusalReleasesRetainedStateAndDoesNotReuseId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        int commitsBefore = fixture.Transport.CommittedCount;
        fixture.Transport.RefuseNextReservation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct).AsTask());

        AssertReleased(fixture.Session);
        Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
        Assert.False(fixture.Session.Completion.IsCompleted);
        BedrockItemStackSubmitResult retry = await fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct);
        Assert.Equal(-5, retry.RequestId);
        Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        await fixture.RejectAsync(retry.RequestId, ct);
        AssertReleased(fixture.Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationAfterTransferRetainsRequestUntilTerminalResponse(bool accepted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNextTransferResult();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, caller.Token).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            BedrockItemStackRequest request = await fixture.ReceiveRequestAsync(ct);
            Assert.Equal(-3, request.RequestId);
            Assert.Equal(commitsBefore + 1, fixture.Transport.CommittedCount);
            await caller.CancelAsync();

            Assert.False(submit.IsCompleted);
            AssertPending(fixture.Session, request.RequestId);
            gate.Allow();
            BedrockItemStackSubmitResult result = await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(request.RequestId, result.RequestId);
            AssertPending(fixture.Session, request.RequestId);

            if (accepted)
                await fixture.AcceptAsync(request.RequestId, ct);
            else
                await fixture.RejectAsync(request.RequestId, ct);

            AssertReleased(fixture.Session);
            Assert.Equal(accepted ? 1 : 0, fixture.Session.ItemStackTracker.AcceptedCount);
            Assert.Equal(accepted ? 0 : 1, fixture.Session.ItemStackTracker.RejectedCount);
            BedrockInventorySnapshot snapshot = fixture.Session.InventoryStore.GetSnapshot(s_inventoryAddress)!.Value;
            Assert.True(snapshot.IsSlotKnown(0));
            Assert.True(snapshot.IsSlotKnown(1));
            Assert.Equal(accepted ? 4 : 5, snapshot.Items[0].Count);
            Assert.Equal(accepted ? 306 : 0, snapshot.Items[1].NetworkId);
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Fact]
    public async Task DisconnectBeforeReservationGrantCancelsLifetimeAndClearsRetainedState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            AssertPending(fixture.Session, -3);

            await fixture.Session.DisconnectAsync(ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => submit.WaitAsync(TimeSpan.FromSeconds(5), ct));

            AssertReleased(fixture.Session);
            Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
            Assert.True(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            try
            {
                await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (BedrockSessionException)
            {
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TeardownWithTransferredPendingRequestClearsRetainedState(bool transportFault)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemStackSubmitResult submit = await fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct);
        Assert.Equal(-3, submit.RequestId);
        Assert.Equal(submit.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        AssertPending(fixture.Session, submit.RequestId);

        if (transportFault)
        {
            fixture.Transport.Abort(MessageConnectionTermination.ProtocolFailure(new IOException("Controlled inventory transport fault.")));
            await fixture.Session.Completion.WaitAsync(ct);
        }
        else
        {
            await fixture.Session.DisconnectAsync(ct);
        }

        AssertReleased(fixture.Session);
        Assert.True(fixture.Session.Completion.IsCompleted);
        Assert.Equal(0, fixture.Session.InventoryStore.ContainerCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisposeJoinsGrantedInventoryCommitAndAccounting(bool raw, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = StartSubmit(fixture, raw, ct);
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(commitsBefore + (afterTransfer ? 1 : 0), fixture.Transport.CommittedCount);
            if (afterTransfer)
                Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            dispose = fixture.Session.DisposeAsync().AsTask();
            await Task.WhenAny(fixture.Transport.DisposeEntered.Task, Task.Delay(250, ct));
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted, "Session aborted owned transport while a granted inventory commit/result remained held.");
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            Assert.False(dispose.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await submit.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(commitsBefore + 1, fixture.Transport.CommittedCount);
            Assert.Single(fixture.Session.WireTranscript, row => row.PacketId == 147 && row.Direction == BedrockWireDirection.Outbound);
            AssertReleased(fixture.Session);
        }
        finally
        {
            gate.Allow();
            try
            {
                await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (ObjectDisposedException)
            {
                // The deliberate red witness releases its held grant before cleaning up the old implementation.
            }
            if (dispose is not null)
                await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("close")]
    [InlineData("life")]
    [InlineData("world")]
    [InlineData("terminal")]
    public async Task RawCapacityWaitHonorsCallerAndActualLifecycleRetirement(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = StartSubmit(fixture, true, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(ct);
            switch (boundary)
            {
                case "caller": await caller.CancelAsync(); break;
                case "close":
                    await fixture.SendCloseAsync(ct);
                    break;
                case "life": fixture.Session.HandleRespawn(); break;
                case "world": fixture.Session.HandleWorldReplacement(); break;
                case "terminal": await fixture.Session.DisconnectAsync(ct); break;
            }
            if (boundary == "caller")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
            else if (boundary == "terminal")
                await Assert.ThrowsAsync<BedrockSessionException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
            AssertReleased(fixture.Session);
            Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or BedrockSessionException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDoesNotWaitForTransferredPendingResponseAndRejectsLateRawEntry(bool raw)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemStackSubmitResult result = await StartSubmit(fixture, raw, ct);
        Assert.Equal(result.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
        Assert.Equal(raw ? 0 : 1, fixture.Session.PendingItemStackReconciliationCount);
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), ct);
        int commitsBefore = fixture.Transport.CommittedCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildPlace(-5, 0, 1, 1, 7, 0), -5, fixture.Session.ItemStackTracker.Generation, ct).AsTask());
        Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
        AssertReleased(fixture.Session);
    }

    [Theory]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("life", false)]
    [InlineData("life", true)]
    [InlineData("world", false)]
    [InlineData("world", true)]
    public async Task RawGrantedCommitKeepsTransferredResultAcrossCancellation(string boundary, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = StartSubmit(fixture, true, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer)
                Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            switch (boundary)
            {
                case "caller": await caller.CancelAsync(); break;
                case "close": await fixture.SendCloseAsync(ct); break;
                case "life": fixture.Session.HandleRespawn(); break;
                case "world": fixture.Session.HandleWorldReplacement(); break;
            }
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await submit.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            if (!afterTransfer)
                Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            Assert.Single(fixture.Session.WireTranscript, row => row.PacketId == 147 && row.Direction == BedrockWireDirection.Outbound);
            Assert.Equal(boundary == "caller" ? 1 : 0, fixture.Session.ItemStackTracker.PendingCount);
            Assert.Equal(boundary == "caller" ? 1 : 0, fixture.Session.ActionCoordinator.ClaimedCount);
            Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Theory]
    [InlineData("encode")]
    [InlineData("refused")]
    [InlineData("exception")]
    public async Task FailedRawSendReleasesDrainBeforeDispose(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        int commitsBefore = fixture.Transport.CommittedCount;
        if (failure == "encode")
        {
            int generation = fixture.Session.ItemStackTracker.Generation;
            Assert.True(fixture.Session.ActionCoordinator.TryClaim(-3, BedrockActionEmission.Standalone));
            Assert.True(fixture.Session.ItemStackTracker.TryRegister(-3, generation));
            var descriptor = new BedrockItemStackResultDescriptor(1, 1, null, 0, 1, 0, []);
            var action = new BedrockItemStackAction(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload([descriptor], 1));
            var packet = new BedrockItemStackRequestPacket([new BedrockItemStackRequest(-3, [action], [], 0)]);
            await Assert.ThrowsAsync<BedrockCodecException>(() => fixture.Session.SendItemStackRequestAsync(packet, -3, generation, ct).AsTask());
        }
        else if (failure == "refused")
        {
            fixture.Transport.RefuseNextReservation();
            await Assert.ThrowsAsync<InvalidOperationException>(() => StartSubmit(fixture, true, ct));
        }
        else
        {
            fixture.Transport.ThrowNextReservation();
            await Assert.ThrowsAsync<IOException>(() => StartSubmit(fixture, true, ct));
        }
        AssertReleased(fixture.Session);
        Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), ct);
    }

    [Fact]
    public async Task InlineReservationCancellationCanFinishRawSendInsideLifecycleRetirement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockItemStackSubmitResult> submit = StartSubmit(fixture, true, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            fixture.Session.HandleRespawn();
            Assert.True(submit.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => submit);
            AssertReleased(fixture.Session);
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), ct);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeCancelsUngrantableInventoryWaitBeforeOwnedTeardown(bool raw)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commitsBefore = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> submit = StartSubmit(fixture, raw, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => submit);
            Assert.Equal(commitsBefore, fixture.Transport.CommittedCount);
            AssertReleased(fixture.Session);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (BedrockSessionException) { }
        }
    }

    private static Task<BedrockItemStackSubmitResult> StartSubmit(OwnershipSession fixture, bool raw, CancellationToken ct)
    {
        if (!raw)
            return fixture.Session.SubmitInventoryPlaceAsync(s_inventoryAddress, 0, 1, 1, ct).AsTask();
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(-3, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(-3, generation));
        return fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 7, 0), -3, generation, ct).AsTask();
    }

    private static void AssertPending(BedrockClientSession session, int requestId)
    {
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
        Assert.Equal(1, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.ActionCoordinator.IsClaimed(requestId));
        Assert.Equal(1, session.PendingItemStackReconciliationCount);
    }

    private static void AssertReleased(BedrockClientSession session)
    {
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Equal(0, session.PendingItemStackReconciliationCount);
    }

    internal sealed class SendGate(bool afterTransfer = false, bool afterGrant = false, bool inlineCancellation = false)
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowed = new(inlineCancellation ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool AfterTransfer { get; } = afterTransfer;
        internal bool AfterGrant { get; } = afterGrant;
        internal Task Entered => _entered.Task;
        internal void Allow() => _allowed.TrySetResult();
        internal async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            if (inlineCancellation)
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(() => _allowed.TrySetCanceled(cancellationToken));
                await _allowed.Task.ConfigureAwait(false);
            }
            else
                await _allowed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed class GatedTransport : IMessageConnection, IMessageSendReservationProvider
    {
        private readonly IMessageConnection _inner;
        private readonly IMessageSendReservationProvider _reservations;
        private SendGate? _nextGate;
        private SendGate? _activeGate;
        private int _refuseNextReservation;
        private int _throwNextReservation;
        private int _committedCount;
        internal TaskCompletionSource AbortEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CommittedCount => Volatile.Read(ref _committedCount);
        public Task<MessageConnectionTermination> Completion => _inner.Completion;
        internal GatedTransport(IMessageConnection inner)
        {
            _inner = inner;
            _reservations = Assert.IsAssignableFrom<IMessageSendReservationProvider>(inner);
        }
        internal SendGate HoldNextReservation() => HoldNext(afterTransfer: false);
        internal SendGate HoldNextTransferResult() => HoldNext(afterTransfer: true);
        internal SendGate HoldNextGrantedCommit() => HoldNext(afterTransfer: false, afterGrant: true);
        internal SendGate HoldNextInlineCancellation() => HoldNext(afterTransfer: false, inlineCancellation: true);
        private SendGate HoldNext(bool afterTransfer, bool afterGrant = false, bool inlineCancellation = false)
        {
            var gate = new SendGate(afterTransfer, afterGrant, inlineCancellation);
            if (Interlocked.CompareExchange(ref _nextGate, gate, null) is not null)
                throw new InvalidOperationException("A controlled reservation is already armed.");
            return gate;
        }
        internal void ThrowNextReservation() => Interlocked.Exchange(ref _throwNextReservation, 1);
        internal void RefuseNextReservation()
        {
            if (Interlocked.Exchange(ref _refuseNextReservation, 1) != 0)
                throw new InvalidOperationException("A controlled refusal is already armed.");
        }
        internal void ReleaseGates()
        {
            _nextGate?.Allow();
            _activeGate?.Allow();
        }
        public async ValueTask<MessageSendReservationResult> ReserveSendAsync(int messageLength, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _throwNextReservation, 0) != 0)
                throw new IOException("Controlled reservation exception.");
            if (Interlocked.Exchange(ref _refuseNextReservation, 0) != 0)
                return MessageSendReservationResult.CapacityExceeded;
            SendGate? gate = Interlocked.Exchange(ref _nextGate, null);
            _activeGate = gate;
            try
            {
                if (gate is { AfterTransfer: false, AfterGrant: false })
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                MessageSendReservationResult result = await _reservations.ReserveSendAsync(messageLength, cancellationToken).ConfigureAwait(false);
                if (!result.IsReserved)
                {
                    _activeGate = null;
                    return result;
                }
                if (gate is not { AfterTransfer: true } and not { AfterGrant: true })
                    _activeGate = null;
                return MessageSendReservationResult.Accepted(new CountingReservation(this, result.Reservation!, gate is { AfterTransfer: true } or { AfterGrant: true } ? gate : null));
            }
            catch
            {
                _activeGate = null;
                throw;
            }
        }
        public ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default) => _inner.ReceiveAsync(cancellationToken);
        public ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default) => _inner.SendAsync(message, cancellationToken);
        public ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default) => _inner.CloseAsync(drainTimeout, cancellationToken);
        public void Abort(MessageConnectionTermination termination)
        {
            AbortEntered.TrySetResult();
            _inner.Abort(termination);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        private sealed class CountingReservation(GatedTransport owner, IMessageSendReservation innerReservation, SendGate? transferGate) : IMessageSendReservation
        {
            public int MessageLength => innerReservation.MessageLength;
            public async ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
            {
                try
                {
                    if (transferGate is { AfterGrant: true })
                        await transferGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    MessageAdmissionResult admission = await innerReservation.CommitAsync(message).ConfigureAwait(false);
                    if (admission.OwnershipTransferred)
                    {
                        Interlocked.Increment(ref owner._committedCount);
                        if (transferGate is { AfterTransfer: true })
                            await transferGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    return admission;
                }
                finally
                {
                    owner._activeGate = null;
                }
            }
            public void Dispose() => innerReservation.Dispose();
        }
    }

    internal sealed class OwnershipSession(TestMessageConnectionPair pair, BedrockFakePeer peer, GatedTransport transport, BedrockClient client, BedrockClientSession session) : IAsyncDisposable
    {
        internal BedrockClientSession Session { get; } = session;
        internal GatedTransport Transport { get; } = transport;
        internal static async Task<OwnershipSession> CreateAsync(CancellationToken ct, IReadOnlyList<BedrockItemRegistryPacket>? registries = null, Func<BedrockClientSession, CancellationToken, Task>? beforeJoin = null)
        {
            TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
            var transport = new GatedTransport(pair.First);
            BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            var client = new BedrockClient(new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("OwnershipTest", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            }, (endpoint, _) =>
            {
                Assert.Equal(IPAddress.Loopback, endpoint.Address);
                return ValueTask.FromResult<IMessageConnection>(transport);
            });
            BedrockClientSession session = await client.ConnectAsync(ct);
            var fixture = new OwnershipSession(pair, peer, transport, client, session);
            try
            {
                if (beforeJoin is not null)
                    await beforeJoin(session, ct);
                await CompleteJoinAsync(peer, session, ct, registries);
                BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray();
                items[0] = new BedrockItemInstance(306, 5, 0, 7, 0, []);
                await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, items, s_inventoryAddress, Air())), ct);
                await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(s_inventoryAddress) is not null, ct);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }
        internal async Task<BedrockItemStackRequest> ReceiveRequestAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
                if (packetId == 147)
                    return Assert.Single(BedrockItemStackCodecs.DecodeRequest(payload).Requests);
            }
            throw new InvalidOperationException("The controlled transport did not deliver the inventory request.");
        }
        internal ValueTask SendAsync(int packetId, byte[] payload, CancellationToken ct) => peer.SendAsync(packetId, payload, ct);
        internal ValueTask<(int PacketId, byte[] Payload)> ReceiveAsync(CancellationToken ct) => peer.ReceiveAsync(ct);
        internal async Task SendCloseAsync(CancellationToken ct)
        {
            await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(1, 0, true)), ct);
            await WaitUntilAsync(() => Session.ItemStackTracker.PendingCount == 0, ct);
        }
        internal async Task RejectAsync(int requestId, CancellationToken ct)
        {
            await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(59, requestId, false, [])])), ct);
            await WaitUntilAsync(() => Session.PendingItemStackReconciliationCount == 0, ct);
        }
        internal async Task AcceptAsync(int requestId, CancellationToken ct)
        {
            var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null),
            [
                new BedrockItemStackResponseSlot(0, 0, 4, 7, string.Empty, string.Empty, 0),
                new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0),
            ]);
            await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, requestId, true, [container])])), ct);
            await WaitUntilAsync(() => Session.PendingItemStackReconciliationCount == 0, ct);
        }
        public async ValueTask DisposeAsync()
        {
            Transport.ReleaseGates();
            Transport.Abort(MessageConnectionTermination.Aborted());
            await Session.DisposeAsync();
            await client.DisposeAsync();
            await peer.DisposeAsync();
            await pair.DisposeAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(10, ct);
    }

    private static async Task CompleteJoinAsync(BedrockFakePeer peer, BedrockClientSession session, CancellationToken ct, IReadOnlyList<BedrockItemRegistryPacket>? registries)
    {
        Assert.Equal(193, (await peer.ReceiveAsync(ct)).PacketId);
        await peer.SendAsync(143, BedrockBootstrapCodecs.Encode(new NetworkSettingsPacket(1, BedrockCompressionAlgorithm.Deflate, false, 0, 0f)), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        Assert.Equal(1, (await peer.ReceiveAsync(ct)).PacketId);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.LoginSuccess)), ct);
        await peer.SendAsync(6, BedrockJoinCodecs.Encode(new ResourcePacksInfoPacket(false, false, false, false, Guid.Empty, string.Empty, [])), ct);
        Assert.Equal(8, (await peer.ReceiveAsync(ct)).PacketId);
        Assert.Equal(129, (await peer.ReceiveAsync(ct)).PacketId);
        Assert.Equal(69, (await peer.ReceiveAsync(ct)).PacketId);
        await peer.SendAsync(7, BedrockJoinCodecs.Encode(new ResourcePackStackPacket(false, [], string.Empty, [], false, false)), ct);
        Assert.Equal(8, (await peer.ReceiveAsync(ct)).PacketId);
        var pig = new NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var identifiers = new NbtCompound();
        identifiers.Put("idlist", new NbtList(NbtTagType.Compound) { pig });
        registries ??= [new BedrockItemRegistryPacket([new BedrockItemState("minecraft:diamond", 306, false, 2, new NbtCompound())])];
        var bootstrap = new List<(int PacketId, byte[] Payload)>();
        foreach (BedrockItemRegistryPacket registry in registries)
            bootstrap.Add((162, BedrockItemRegistryCodec.Encode(registry)));
        bootstrap.Add((119, BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(identifiers))));
        bootstrap.Add((122, BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3]))));
        bootstrap.Add((11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer())))));
        await peer.SendBatchAsync(bootstrap, ct);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
        Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
        byte[] terrain = new byte[26];
        terrain[0] = 1;
        Array.Fill(terrain, (byte)0xff, 2, 23);
        await peer.SendAsync(58, BedrockWorldCodecs.Encode(new LevelChunkPacket(0, 0, 0, 0, null, false, [], terrain)), ct);
        await session.WaitUntilReadyAsync(ct);
    }

    private static BedrockStartGameTrailer JoinTrailer() =>
        new(
            1,
            new BedrockVector3(0.5f, 70f, 0.5f),
            0f,
            0f,
            99UL,
            0,
            "minecraft:plains",
            0,
            1,
            0,
            false,
            1,
            new BedrockStartGamePosition(0, 70, 0),
            false,
            0,
            false,
            false,
            0,
            0U,
            false,
            string.Empty,
            0f,
            0f,
            false,
            true,
            false,
            0U,
            0U,
            false,
            false,
            [],
            [],
            false,
            false,
            false,
            1,
            4,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "*",
            16,
            16,
            false,
            string.Empty,
            string.Empty,
            false,
            0,
            false,
            0,
            false,
            "test-level",
            "test-world",
            "00000000-0000-0000-0000-000000000000",
            false,
            0,
            false,
            0L,
            0,
            [],
            "correlation",
            false,
            "engine",
            new NbtCompound(),
            0UL,
            Guid.Empty,
            false,
            false,
            false,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}
