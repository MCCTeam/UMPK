using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Time;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockInitialLoadingTests
{
    [Fact]
    public async Task InitialStartTransfersAfterStartGameAndBeforeInitialization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(pair.First);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await StreamBootstrapAsync(peer, ct);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
        await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);

        (int packetId, byte[] payload) = await peer.ReceiveIncludingLoadingAsync(ct);
        Assert.Equal(312, packetId);
        Assert.Equal(new byte[] { 2, 0 }, payload);
        Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
        Assert.False(session.Snapshot.IsReady);
    }

    [Fact]
    public async Task ReadyWaitsForEndOwnershipRatherThanBootstrapPredicatesAlone()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        LoadingGate gate = transport.HoldNext();
        try
        {
            Task ready = session.WaitUntilReadyAsync(ct).AsTask();
            await TriggerAcceptanceAsync(peer, ct);
            await gate.Entered.WaitAsync(ct);
            Assert.True(session.Snapshot.LocalPlayerInitializationAccepted);
            Assert.False(session.Snapshot.IsReady);
            Assert.False(ready.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(), ct).AsTask());
            Assert.Equal(0, session.PlayerAuthInputSendCount);
            gate.Allow();
            (int packetId, byte[] payload) = await peer.ReceiveIncludingLoadingAsync(ct);
            Assert.Equal(312, packetId);
            Assert.Equal(new byte[] { 4, 0 }, payload);
            await ready.WaitAsync(ct);
            Assert.True(session.Snapshot.IsReady);
            // End ownership is local completion. This test observes no remote protection acknowledgement or input acceptance.
            Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Theory]
    [InlineData("respawn")]
    [InlineData("world")]
    [InlineData("disconnect")]
    public async Task RetirementBeforeEndCapacityPreventsCommitAndReady(string retirement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        LoadingGate gate = transport.HoldNext();
        try
        {
            await TriggerAcceptanceAsync(peer, ct);
            await gate.Entered.WaitAsync(ct);
            int committed = transport.CommittedCount;
            Task retirementTask = RetireAsync(session, retirement, ct);
            await session.Completion.WaitAsync(ct);
            await retirementTask.WaitAsync(ct);
            Assert.Equal(committed, transport.CommittedCount);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(1, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Theory]
    [InlineData("respawn")]
    [InlineData("world")]
    [InlineData("disconnect")]
    public async Task RetirementAfterEndTransferDoesNotResendOrPublishStaleReady(string retirement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        LoadingGate gate = transport.HoldNext(afterTransfer: true);
        try
        {
            await TriggerAcceptanceAsync(peer, ct);
            await gate.Entered.WaitAsync(ct);
            (int packetId, byte[] payload) = await peer.ReceiveIncludingLoadingAsync(ct);
            Assert.Equal(312, packetId);
            Assert.Equal(new byte[] { 4, 0 }, payload);
            int committed = transport.CommittedCount;
            Task retirementTask = RetireAsync(session, retirement, ct);
            Assert.False(session.Snapshot.IsReady);
            gate.Allow();
            await session.Completion.WaitAsync(ct);
            await retirementTask.WaitAsync(ct);
            Assert.Equal(committed, transport.CommittedCount);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Fact]
    public async Task EndCapacityRefusalDoesNotPublishReady()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        transport.RefuseNext();
        await TriggerAcceptanceAsync(peer, ct);
        BedrockTermination terminal = await session.Completion.WaitAsync(ct);
        Assert.Equal(BedrockSessionEndReason.TransportFailure, terminal.Reason);
        Assert.False(session.Snapshot.IsReady);
        Assert.Equal(1, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
    }

    [Theory]
    [InlineData("respawn")]
    [InlineData("world")]
    public async Task LaterRetirementKeepsPublishedReadyObservation(string retirement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(pair.First);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        await TriggerAcceptanceAsync(peer, ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        await session.WaitUntilReadyAsync(ct);
        await RetireAsync(session, retirement, ct);
        Assert.True(session.Snapshot.IsReady);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(), ct).AsTask());
        Assert.Contains("recovery", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, session.PlayerAuthInputSendCount);
        Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
    }

    [Theory]
    [InlineData("respawn", false)]
    [InlineData("world", false)]
    [InlineData("disconnect", false)]
    [InlineData("respawn", true)]
    [InlineData("world", true)]
    [InlineData("disconnect", true)]
    public async Task RetirementBeforeBootstrapCapacityPreventsFurtherCommit(string retirement, bool initialization)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await StreamBootstrapAsync(peer, ct);
        if (initialization)
        {
            await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
            Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        }
        LoadingGate gate = transport.HoldNext();
        try
        {
            if (initialization)
                await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
            else
                await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
            await gate.Entered.WaitAsync(ct);
            int committed = transport.CommittedCount;
            Task retirementTask = RetireAsync(session, retirement, ct);
            await session.Completion.WaitAsync(ct);
            await retirementTask.WaitAsync(ct);
            Assert.Equal(committed, transport.CommittedCount);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(initialization ? 1 : 0, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
            Assert.DoesNotContain(session.WireTranscript, entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 113);
        }
        finally
        {
            gate.Allow();
        }
    }

    [Fact]
    public async Task LastRegistryAfterInitializationAcceptanceTriggersSingleEnd()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(pair.First);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await StreamBootstrapAsync(peer, ct, includeBiomes: false);
        await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
        Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
        await TriggerAcceptanceAsync(peer, ct);
        while (!session.Snapshot.LocalPlayerInitializationAccepted)
            await Task.Delay(10, ct);
        Assert.False(session.Snapshot.IsReady);
        await peer.SendAsync(122, BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3])), ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        await session.WaitUntilReadyAsync(ct);
        Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
    }

    [Fact]
    public async Task CancelledReadyWaitAfterEndTransferDoesNotCancelOwnedCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        using var caller = new CancellationTokenSource();
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        LoadingGate gate = transport.HoldNext(afterTransfer: true);
        try
        {
            Task wait = session.WaitUntilReadyAsync(caller.Token).AsTask();
            await TriggerAcceptanceAsync(peer, ct);
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
            Assert.False(session.Completion.IsCompleted);
            gate.Allow();
            await session.WaitUntilReadyAsync(ct);
            Assert.True(session.Snapshot.IsReady);
            Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReadyDeadlineTerminatesBlockedLoadingWithoutAbandoningTransferredCommit(bool end, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        var time = new VirtualTimeProvider(DateTimeOffset.UtcNow);
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport, time);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        if (end)
            await AwaitInitializationAsync(peer, ct);
        else
            await StreamBootstrapAsync(peer, ct);
        LoadingGate gate = transport.HoldNext(afterTransfer);
        try
        {
            if (end)
                await TriggerAcceptanceAsync(peer, ct);
            else
                await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer)
                Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
            int committed = transport.CommittedCount;

            time.Advance(TimeSpan.FromMilliseconds(100));
            BedrockTermination terminal = await session.Completion.WaitAsync(TimeSpan.FromMilliseconds(500), ct);
            Assert.Equal(BedrockSessionEndReason.Timeout, terminal.Reason);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(committed, transport.CommittedCount);
            // A token-free commit result remains owned until it returns, even after prompt timeout publication.
            gate.Allow();
            await session.DisposeAsync();
            Assert.Equal(committed, transport.CommittedCount);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(end ? (afterTransfer ? 2 : 1) : 0, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
            Assert.Equal(BedrockSessionEndReason.Timeout, (await session.Completion).Reason);
        }
        finally
        {
            gate.Allow();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadyDeadlineAlsoTerminatesInitializationAdmission(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        var time = new VirtualTimeProvider(DateTimeOffset.UtcNow);
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport, time);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await StreamBootstrapAsync(peer, ct);
        await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        LoadingGate gate = transport.HoldNext(afterTransfer);
        try
        {
            await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer)
                Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
            int committed = transport.CommittedCount;
            time.Advance(TimeSpan.FromMilliseconds(100));
            BedrockTermination terminal = await session.Completion.WaitAsync(TimeSpan.FromMilliseconds(500), ct);
            Assert.Equal(BedrockSessionEndReason.Timeout, terminal.Reason);
            Assert.False(session.Snapshot.IsReady);
            Assert.Equal(committed, transport.CommittedCount);
            Assert.DoesNotContain(session.WireTranscript, entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 113);
            gate.Allow();
            await session.DisposeAsync();
            Assert.Equal(committed, transport.CommittedCount);
            Assert.Equal(afterTransfer ? 1 : 0, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 113));
            Assert.Equal(1, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
            Assert.Equal(BedrockSessionEndReason.Timeout, (await session.Completion).Reason);
        }
        finally
        {
            gate.Allow();
        }
    }

    [Fact]
    public async Task DeadlineRecheckRejectsReadyWhenTimerCallbackIsDelayed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        var time = new DeferredTimeProvider();
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using var transport = new LoadingTransport(pair.First);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using var client = CreateClient(transport, time);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await AwaitInitializationAsync(peer, ct);
        LoadingGate gate = transport.HoldNext(afterTransfer: true);
        try
        {
            await TriggerAcceptanceAsync(peer, ct);
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
            time.AdvanceWithoutFiring(TimeSpan.FromMilliseconds(100));
            Assert.False(session.Completion.IsCompleted);
            gate.Allow();
            BedrockTermination terminal = await session.Completion.WaitAsync(TimeSpan.FromMilliseconds(500), ct);
            Assert.Equal(BedrockSessionEndReason.Timeout, terminal.Reason);
            Assert.False(session.Snapshot.IsReady);
            await session.DisposeAsync();
            Assert.Equal(2, session.WireTranscript.Count(entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 312));
            Assert.Equal(0, time.ActiveTimers);
        }
        finally
        {
            gate.Allow();
        }
    }

    // This clock deliberately delays all timer callbacks; the deadline check must use the current clock even then.
    private sealed class DeferredTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<DeferredTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        internal int ActiveTimers
        {
            get
            {
                lock (_gate)
                    return _timers.Count(timer => !timer.IsDisposed);
            }
        }
        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
                return _now;
        }
        internal void AdvanceWithoutFiring(TimeSpan duration)
        {
            lock (_gate)
                _now += duration;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DeferredTimer();
            lock (_gate)
                _timers.Add(timer);
            return timer;
        }
        private sealed class DeferredTimer : ITimer
        {
            private int _disposed;
            internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private static BedrockPlayerAuthInputPacket Idle() =>
        new(0f, 0f, new BedrockVector3(0.5f, 70f, 0.5f), new BedrockVector2(0f, 0f), 0f, null, 1, 0, 2, new BedrockVector2(0f, 0f), 1UL, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    private static Task RetireAsync(BedrockClientSession session, string retirement, CancellationToken ct)
    {
        switch (retirement)
        {
            case "respawn": session.HandleRespawn(); break;
            case "world": session.HandleWorldReplacement(); break;
            case "disconnect": return session.DisconnectAsync(ct).AsTask();
        }
        return Task.CompletedTask;
    }

    private static async Task AwaitInitializationAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        await StreamBootstrapAsync(peer, ct);
        await peer.SendAsync(11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer()))), ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
        Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
    }

    private static ValueTask TriggerAcceptanceAsync(BedrockFakePeer peer, CancellationToken ct) =>
        peer.SendAsync(63, [1], ct);

    private sealed class LoadingGate(bool afterTransfer)
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool AfterTransfer { get; } = afterTransfer;
        internal Task Entered => _entered.Task;
        internal void Allow() => _allowed.TrySetResult();
        internal async ValueTask WaitAsync(CancellationToken ct)
        {
            _entered.TrySetResult();
            await _allowed.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class LoadingTransport(IMessageConnection inner) : IMessageConnection, IMessageSendReservationProvider
    {
        private readonly IMessageSendReservationProvider _reservations = (IMessageSendReservationProvider)inner;
        private LoadingGate? _gate;
        private LoadingGate? _active;
        private int _refuse;
        private int _committed;
        internal int CommittedCount => Volatile.Read(ref _committed);
        public Task<MessageConnectionTermination> Completion => inner.Completion;
        internal LoadingGate HoldNext(bool afterTransfer = false) => _gate = new LoadingGate(afterTransfer);
        internal void RefuseNext() => Interlocked.Exchange(ref _refuse, 1);
        public async ValueTask<MessageSendReservationResult> ReserveSendAsync(int messageLength, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _refuse, 0) != 0)
                return MessageSendReservationResult.CapacityExceeded;
            LoadingGate? gate = Interlocked.Exchange(ref _gate, null);
            _active = gate;
            if (gate is { AfterTransfer: false })
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            MessageSendReservationResult result = await _reservations.ReserveSendAsync(messageLength, cancellationToken).ConfigureAwait(false);
            return result.IsReserved ? MessageSendReservationResult.Accepted(new LoadingReservation(this, result.Reservation!, gate)) : result;
        }
        public ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default) => inner.SendAsync(message, cancellationToken);
        public ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default) => inner.CloseAsync(drainTimeout, cancellationToken);
        public void Abort(MessageConnectionTermination termination) => inner.Abort(termination);
        public ValueTask DisposeAsync()
        {
            _gate?.Allow();
            _active?.Allow();
            return inner.DisposeAsync();
        }
        private sealed class LoadingReservation(LoadingTransport owner, IMessageSendReservation reservation, LoadingGate? gate) : IMessageSendReservation
        {
            public int MessageLength => reservation.MessageLength;
            public async ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
            {
                MessageAdmissionResult admission = await reservation.CommitAsync(message).ConfigureAwait(false);
                if (admission.OwnershipTransferred)
                {
                    Interlocked.Increment(ref owner._committed);
                    if (gate is { AfterTransfer: true })
                        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                return admission;
            }
            public void Dispose() => reservation.Dispose();
        }
    }

    private static BedrockClient CreateClient(IMessageConnection transport, TimeProvider? timeProvider = null) => new(new BedrockClientSettings
    {
        Host = "127.0.0.1",
        Port = 19132,
        TimeProvider = timeProvider ?? TimeProvider.System,
        Timeouts = timeProvider is null ? BedrockClientTimeouts.Default : BedrockClientTimeouts.Default with { Ready = TimeSpan.FromMilliseconds(100) },
        CredentialProvider = new OfflineBedrockCredentialProvider("LoadingTest", Guid.NewGuid()),
        ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
    }, (_, _) => ValueTask.FromResult(transport));

    private static async Task StreamBootstrapAsync(BedrockFakePeer peer, CancellationToken ct, bool includeBiomes = true)
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
        await peer.SendBatchAsync([
            (162, BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket([new BedrockItemState("minecraft:diamond", 306, false, 2, new NbtCompound())]))),
            (119, BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(identifiers))),
        ], ct);
        if (includeBiomes)
            await peer.SendAsync(122, BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3])), ct);
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
