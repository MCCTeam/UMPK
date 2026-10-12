using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockPlayerAuthInputAdmissionTests
{
    private static BedrockPlayerAuthInputPacket Idle(ulong tick) =>
        new(0f, 0f, new BedrockVector3(0.5f, 71.62001f, 0.5f), new BedrockVector2(0f, 0f), 0f, [], 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    [Fact]
    public async Task CallerCancellationDuringCapacityWaitPreservesTickForRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNext(GatePhase.BeforeCapacity);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), caller.Token).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            int committed = fixture.Transport.CommittedCount;
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
            Assert.Equal(committed, fixture.Transport.CommittedCount);
            AssertNoInput(fixture.Session);
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
        }
        BedrockPlayerAuthInputSubmitResult retry = await fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct);
        Assert.Equal(1UL, retry.Tick);
        Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
        Assert.Equal(1, fixture.Session.PlayerAuthInputSendCount);
    }

    [Theory]
    [InlineData("respawn")]
    [InlineData("world")]
    [InlineData("disconnect")]
    public async Task RetirementCancelsCapacityWaitBeforeGrant(string retirement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNext(GatePhase.BeforeCapacity);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            int committed = fixture.Transport.CommittedCount;
            Task retire = RetireAsync(fixture.Session, retirement, ct);
            await AssertRetiredAsync(submit, retirement, ct);
            await retire.WaitAsync(ct);
            Assert.Equal(committed, fixture.Transport.CommittedCount);
            AssertNoInput(fixture.Session);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask());
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
        }
    }

    [Fact]
    public async Task CallerCancellationWhileSerializedDoesNotCancelActiveInput()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNext(GatePhase.BeforeCapacity);
        Task<BedrockPlayerAuthInputSubmitResult> first = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        Task<BedrockPlayerAuthInputSubmitResult>? second = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            second = fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), caller.Token).AsTask();
            Assert.False(second.IsCompleted);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(1), ct));
            Assert.False(first.IsCompleted);
            AssertNoInput(fixture.Session);
            gate.Allow();
            Assert.Equal(1UL, (await first.WaitAsync(ct)).Tick);
            Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            Assert.Equal(2UL, (await fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), ct)).Tick);
            Assert.Equal(2UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            Assert.Equal(2, fixture.Session.PlayerAuthInputSendCount);
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
    [InlineData("respawn")]
    [InlineData("world")]
    [InlineData("disconnect")]
    public async Task RetirementCancelsSerializedWaitersWithoutCapacityRelease(string retirement)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNext(GatePhase.BeforeCapacity);
        Task<BedrockPlayerAuthInputSubmitResult> first = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        Task<BedrockPlayerAuthInputSubmitResult>? second = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            second = fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), ct).AsTask();
            Assert.False(second.IsCompleted);
            int committed = fixture.Transport.CommittedCount;
            Task retire = RetireAsync(fixture.Session, retirement, ct);
            await AssertRetiredAsync(second, retirement, ct);
            await AssertRetiredAsync(first, retirement, ct);
            await retire.WaitAsync(ct);
            Assert.Equal(committed, fixture.Transport.CommittedCount);
            AssertNoInput(fixture.Session);
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
    [InlineData(GatePhase.BeforeCommit)]
    [InlineData(GatePhase.AfterTransfer)]
    public async Task CallerCancellationAfterGrantKeepsOwnedCommitAndConsumesTick(GatePhase phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = fixture.Transport.HoldNext(phase);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), caller.Token).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (phase == GatePhase.AfterTransfer)
                Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            caller.Cancel();
            Assert.False(submit.IsCompleted);
            gate.Allow();
            BedrockPlayerAuthInputSubmitResult result = await submit.WaitAsync(ct);
            Assert.Equal(1UL, result.Tick);
            Assert.Equal(BedrockPlayerAuthInputCodecs.Encode(Idle(1)), result.OutboundPayload);
            if (phase == GatePhase.BeforeCommit)
                Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            AssertOneInput(fixture.Session);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask());
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
        }
    }

    [Theory]
    [InlineData("respawn", GatePhase.BeforeCommit)]
    [InlineData("world", GatePhase.BeforeCommit)]
    [InlineData("respawn", GatePhase.AfterTransfer)]
    [InlineData("world", GatePhase.AfterTransfer)]
    public async Task LifeOrWorldRetirementAfterGrantKeepsOwnedCommit(string retirement, GatePhase phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNext(phase);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (phase == GatePhase.AfterTransfer)
                Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            await RetireAsync(fixture.Session, retirement, ct);
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(1UL, (await submit.WaitAsync(ct)).Tick);
            if (phase == GatePhase.BeforeCommit)
                Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            AssertOneInput(fixture.Session);
            Assert.True(fixture.Session.Snapshot.IsReady);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), ct).AsTask());
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
        }
    }

    [Fact]
    public async Task DisconnectAfterTransferKeepsOwnedResultAndAccounting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNext(GatePhase.AfterTransfer);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            await fixture.Session.DisconnectAsync(ct);
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(1UL, (await submit.WaitAsync(ct)).Tick);
            AssertOneInput(fixture.Session);
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
        }
    }

    [Theory]
    [InlineData(GatePhase.BeforeCommit)]
    [InlineData(GatePhase.AfterTransfer)]
    public async Task DisposeWaitsForGrantedCommitBeforeDestroyingTransport(GatePhase phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNext(phase);
        Task<BedrockPlayerAuthInputSubmitResult> submit = fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask();
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (phase == GatePhase.AfterTransfer)
                Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
            dispose = fixture.Session.DisposeAsync().AsTask();
            await fixture.Session.Completion.WaitAsync(ct);
            Assert.False(fixture.Transport.AbortObserved.IsCompleted);
            Assert.False(dispose.IsCompleted);
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(1UL, (await submit.WaitAsync(ct)).Tick);
            await dispose.WaitAsync(ct);
            AssertOneInput(fixture.Session);
            Assert.True(fixture.Transport.AbortObserved.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitPlayerAuthInputAsync(Idle(2), ct).AsTask());
        }
        finally
        {
            gate.Allow();
            await ObserveForCleanupAsync(submit, ct);
            if (dispose is not null)
                await dispose.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task CapacityRefusalPreservesTickAndOnlyCountsRefusal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using MovementSession fixture = await MovementSession.CreateAsync(ct);
        fixture.Transport.RefuseNext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct).AsTask());
        Assert.Equal(0, fixture.Session.PlayerAuthInputSendCount);
        Assert.Equal(1, fixture.Session.RejectedSendAttempts);
        Assert.DoesNotContain(fixture.Session.WireTranscript, entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 144);
        Assert.Equal(1UL, (await fixture.Session.SubmitPlayerAuthInputAsync(Idle(1), ct)).Tick);
        Assert.Equal(1UL, (await fixture.ReceiveInputAsync(ct)).Tick);
        Assert.Equal(1, fixture.Session.PlayerAuthInputSendCount);
        Assert.Equal(1, fixture.Session.RejectedSendAttempts);
    }

    private static void AssertNoInput(BedrockClientSession session)
    {
        Assert.Equal(0, session.PlayerAuthInputSendCount);
        Assert.Equal(0, session.RejectedSendAttempts);
        Assert.DoesNotContain(session.WireTranscript, entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 144);
    }

    private static void AssertOneInput(BedrockClientSession session)
    {
        Assert.Equal(1, session.PlayerAuthInputSendCount);
        Assert.Equal(0, session.RejectedSendAttempts);
        Assert.Single(session.WireTranscript, entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 144);
    }

    private static async Task AssertRetiredAsync(Task<BedrockPlayerAuthInputSubmitResult> submit, string retirement, CancellationToken ct)
    {
        if (retirement == "disconnect")
            await Assert.ThrowsAsync<BedrockSessionException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
    }

    private static Task RetireAsync(BedrockClientSession session, string retirement, CancellationToken ct)
    {
        switch (retirement)
        {
            case "respawn":
                session.HandleRespawn();
                return Task.CompletedTask;
            case "world":
                session.HandleWorldReplacement();
                return Task.CompletedTask;
            case "disconnect":
                return session.DisconnectAsync(ct).AsTask();
            default:
                throw new InvalidOperationException("Unknown controlled retirement.");
        }
    }

    private static async Task ObserveForCleanupAsync(Task<BedrockPlayerAuthInputSubmitResult> submit, CancellationToken ct)
    {
        try
        {
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (BedrockSessionException)
        {
        }
        catch (BedrockCodecException)
        {
        }
    }

    public enum GatePhase { BeforeCapacity, BeforeCommit, AfterTransfer }

    private sealed class SendGate(GatePhase phase)
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GatePhase Phase { get; } = phase;
        internal Task Entered => _entered.Task;
        internal void Allow() => _allowed.TrySetResult();
        internal async Task WaitAsync(CancellationToken ct)
        {
            _entered.TrySetResult();
            await _allowed.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class GatedTransport : IMessageConnection, IMessageSendReservationProvider
    {
        private readonly IMessageConnection _inner;
        private readonly IMessageSendReservationProvider _reservations;
        private readonly Lock _gate = new();
        private readonly List<SendGate> _gates = [];
        private SendGate? _next;
        private int _refuse;
        private int _committed;
        private readonly TaskCompletionSource _abortObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task AbortObserved => _abortObserved.Task;
        internal GatedTransport(IMessageConnection inner)
        {
            _inner = inner;
            _reservations = (IMessageSendReservationProvider)inner;
        }
        internal int CommittedCount => Volatile.Read(ref _committed);
        public Task<MessageConnectionTermination> Completion => _inner.Completion;
        internal SendGate HoldNext(GatePhase phase)
        {
            lock (_gate)
            {
                var next = new SendGate(phase);
                _gates.Add(next);
                _next = next;
                return next;
            }
        }
        internal void RefuseNext() => Interlocked.Exchange(ref _refuse, 1);
        internal void ReleaseGates()
        {
            SendGate[] gates;
            lock (_gate)
                gates = _gates.ToArray();
            foreach (SendGate gate in gates)
                gate.Allow();
        }
        public async ValueTask<MessageSendReservationResult> ReserveSendAsync(int messageLength, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _refuse, 0) != 0)
                return MessageSendReservationResult.CapacityExceeded;
            SendGate? gate;
            lock (_gate)
            {
                gate = _next;
                _next = null;
            }
            if (gate is { Phase: GatePhase.BeforeCapacity })
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            MessageSendReservationResult result = await _reservations.ReserveSendAsync(messageLength, cancellationToken).ConfigureAwait(false);
            return result.IsReserved
                ? MessageSendReservationResult.Accepted(new Reservation(this, result.Reservation!, gate))
                : result;
        }
        public ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default) => _inner.SendAsync(message, cancellationToken);
        public ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default) => _inner.ReceiveAsync(cancellationToken);
        public ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default) => _inner.CloseAsync(drainTimeout, cancellationToken);
        public void Abort(MessageConnectionTermination termination)
        {
            _abortObserved.TrySetResult();
            _inner.Abort(termination);
        }
        public ValueTask DisposeAsync() => _inner.DisposeAsync();

        private sealed class Reservation(GatedTransport owner, IMessageSendReservation inner, SendGate? gate) : IMessageSendReservation
        {
            public int MessageLength => inner.MessageLength;
            public async ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
            {
                if (gate is { Phase: GatePhase.BeforeCommit })
                    await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                MessageAdmissionResult result = await inner.CommitAsync(message).ConfigureAwait(false);
                if (result.OwnershipTransferred)
                {
                    Interlocked.Increment(ref owner._committed);
                    if (gate is { Phase: GatePhase.AfterTransfer })
                        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                return result;
            }
            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class MovementSession : IAsyncDisposable
    {
        private readonly TestMessageConnectionPair _pair;
        private readonly BedrockFakePeer _peer;
        private readonly BedrockClient _client;
        internal GatedTransport Transport { get; }
        internal BedrockClientSession Session { get; }
        private MovementSession(TestMessageConnectionPair pair, BedrockFakePeer peer, GatedTransport transport, BedrockClient client, BedrockClientSession session)
        {
            _pair = pair;
            _peer = peer;
            _client = client;
            Transport = transport;
            Session = session;
        }
        internal static async Task<MovementSession> CreateAsync(CancellationToken ct)
        {
            TestMessageConnectionPair pair = BedrockFakePeer.CreatePair(capacity: 64);
            var transport = new GatedTransport(pair.First);
            BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            var client = new BedrockClient(new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("MovementAdmission", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            }, (_, _) => ValueTask.FromResult<IMessageConnection>(transport));
            BedrockClientSession session = await client.ConnectAsync(ct);
            var fixture = new MovementSession(pair, peer, transport, client, session);
            try
            {
                await CompleteJoinAsync(peer, session, ct);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }
        internal async Task<BedrockPlayerAuthInputPacket> ReceiveInputAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                (int packetId, byte[] payload) = await _peer.ReceiveAsync(ct);
                if (packetId == 144)
                {
                    BedrockPlayerAuthInputPacket input = BedrockPlayerAuthInputCodecs.Decode(payload);
                    Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(input));
                    return input;
                }
            }
            throw new InvalidOperationException("Controlled peer did not receive auth input.");
        }
        public async ValueTask DisposeAsync()
        {
            Transport.ReleaseGates();
            await Session.DisposeAsync();
            await _client.DisposeAsync();
            await _peer.DisposeAsync();
            await _pair.DisposeAsync();
        }
    }

    private static async Task CompleteJoinAsync(BedrockFakePeer peer, BedrockClientSession session, CancellationToken ct)
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
            (162, BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket([new BedrockItemState("minecraft:stone", 1, false, 2, new NbtCompound())]))),
            (119, BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(identifiers))),
            (122, BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3]))),
            (11, BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(JoinTrailer())))),
        ], ct);
        await peer.SendAsync(2, BedrockBootstrapCodecs.Encode(new PlayStatusPacket(PlayStatusCode.PlayerSpawn)), ct);
        Assert.Equal(113, (await peer.ReceiveAsync(ct)).PacketId);
        await peer.SendAsync(63, [1], ct);
        await session.WaitUntilReadyAsync(ct);
        Assert.Equal(312, (await peer.ReceiveIncludingLoadingAsync(ct)).PacketId);
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
