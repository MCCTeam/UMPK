using System.Buffers;
using System.Collections.Immutable;
using Umpk.Transport;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Connection;

public sealed class BedrockConnectionTests
{
    [Fact]
    public async Task CapacityRejectionDoesNotAdvanceCipherState()
    {
        var transport = new FakeTransport { ReservationStatus = MessageAdmissionStatus.CapacityExceeded };
        await using var connection = new BedrockConnection(transport, CreateContext());
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        using var send = new BedrockEncryptionState(key, key);
        using var receive = new BedrockEncryptionState(key, key);
        await connection.EnableEncryptionAsync(send, receive);

        MessageAdmissionResult result = await connection.SendBatchAsync([new BedrockBatchMember(new BedrockPacketHeader(1, default, default), [1])]);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
        Assert.Equal(0UL, send.Counter);
        Assert.False(connection.IsTerminal);
    }

    [Fact]
    public async Task RejectionAfterEncryptionTerminatesWithoutRetry()
    {
        var transport = new FakeTransport { CommitStatus = MessageAdmissionStatus.Closed };
        await using var connection = new BedrockConnection(transport, CreateContext());
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        using var send = new BedrockEncryptionState(key, key);
        using var receive = new BedrockEncryptionState(key, key);
        await connection.EnableEncryptionAsync(send, receive);

        await Assert.ThrowsAsync<BedrockCodecException>(async () =>
            await connection.SendBatchAsync([new BedrockBatchMember(new BedrockPacketHeader(1, default, default), [1])]));

        Assert.Equal(1UL, send.Counter);
        Assert.True(connection.IsTerminal);
        Assert.Equal(1, transport.CommitCount);
        Assert.Equal(MessageConnectionTerminationReason.ProtocolFailure, transport.AbortReason);
    }

    [Fact]
    public async Task RegistryPublicationIsVisibleToNextMemberInSameBatch()
    {
        BedrockBatchMember[] members =
        [
            new(new BedrockPacketHeader(162, default, default), []),
            new(new BedrockPacketHeader(11, default, default), []),
        ];
        var transport = new FakeTransport();
        transport.QueueReceive(BedrockEnvelopeCodec.Wrap(BedrockBatchCodec.EncodeMembers(members, BedrockCodecLimits.Default)));
        await using var connection = new BedrockConnection(transport, CreateContext());
        var consumer = new RegistryBarrierConsumer();

        Assert.True(await connection.ReceiveAndDispatchAsync(consumer));

        Assert.True(consumer.DependentPacketObservedPublishedRegistry);
        Assert.Equal(1, connection.Context.Registries.Generation);
    }

    [Fact]
    public async Task NetworkSettingsBatchIsPlainAndFollowingBatchUsesCompressionPrefix()
    {
        var transport = new FakeTransport();
        await using var connection = new BedrockConnection(transport, CreateContext());
        var member = new BedrockBatchMember(
            new BedrockPacketHeader(193, default, default),
            BedrockBootstrapCodecs.Encode(new RequestNetworkSettingsPacket(2169)));

        Assert.Equal(MessageAdmissionStatus.Accepted, (await connection.SendBatchAsync([member])).Status);
        Assert.Equal("fe06c10100000879", Convert.ToHexString(transport.LastSent).ToLowerInvariant());

        await connection.EnableCompressionAsync(new BedrockCompressionSettings(ushort.MaxValue, BedrockCompressionAlgorithm.Deflate));
        Assert.Equal(MessageAdmissionStatus.Accepted, (await connection.SendBatchAsync([member])).Status);
        Assert.Equal("feff06c10100000879", Convert.ToHexString(transport.LastSent).ToLowerInvariant());
    }

    [Fact]
    public async Task ItemRegistryCodecPublishesBeforeNextDecodedPacket()
    {
        BedrockBatchMember[] members =
        [
            new(new BedrockPacketHeader(162, default, default), Convert.FromHexString("010f6d696e6563726166743a73746f6e65010000040a0000")),
            new(new BedrockPacketHeader(5, default, default), BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, true, null, null))),
        ];
        var transport = new FakeTransport();
        transport.QueueReceive(BedrockEnvelopeCodec.Wrap(BedrockBatchCodec.EncodeMembers(members, BedrockCodecLimits.Default)));
        await using var connection = new BedrockConnection(transport, CreateContext());
        bool observed = false;
        var dispatcher = new BedrockPacketDispatcher((packet, context) =>
        {
            if (packet is DisconnectPacket)
                observed = context.Registries.Items.TryGetValue(1, out string? name) && name == "minecraft:stone";
        });

        Assert.True(await connection.ReceiveAndDispatchAsync(dispatcher));

        Assert.True(observed);
    }

    private static BedrockSessionCodecContext CreateContext()
    {
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        var routing = new BedrockCodecContext(profile, BedrockPacketDirection.Clientbound, default, default);
        return new BedrockSessionCodecContext(routing, BedrockRegistrySnapshot.Empty, BedrockCodecLimits.Default);
    }

    private sealed class RegistryBarrierConsumer : IBedrockBatchConsumer
    {
        internal bool DependentPacketObservedPublishedRegistry { get; private set; }

        public BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context)
        {
            if (member.Header.PacketId == 162)
            {
                return new BedrockRegistrySnapshot(
                    1,
                    ImmutableDictionary<short, string>.Empty.Add(1, "minecraft:stone"),
                    ImmutableDictionary<string, int>.Empty);
            }

            if (member.Header.PacketId == 11)
                DependentPacketObservedPublishedRegistry = context.Registries.Generation == 1;

            return null;
        }
    }

    private sealed class FakeTransport : IMessageConnection, IMessageSendReservationProvider
    {
        private readonly Queue<OwnedMessage> _received = new();
        private readonly List<OwnedMessage> _sent = [];

        internal MessageAdmissionStatus ReservationStatus { get; init; } = MessageAdmissionStatus.Accepted;

        internal MessageAdmissionStatus CommitStatus { get; init; } = MessageAdmissionStatus.Accepted;

        internal int CommitCount { get; private set; }

        internal MessageConnectionTerminationReason? AbortReason { get; private set; }

        internal byte[] LastSent => _sent[^1].Memory.ToArray();

        public Task<MessageConnectionTermination> Completion { get; } = Task.FromResult(MessageConnectionTermination.PeerClosed);

        public ValueTask<MessageSendReservationResult> ReserveSendAsync(int maximumMessageLength, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MessageSendReservationResult result = ReservationStatus switch
            {
                MessageAdmissionStatus.Accepted => MessageSendReservationResult.Accepted(new Reservation(this, maximumMessageLength)),
                MessageAdmissionStatus.Closed => MessageSendReservationResult.Closed,
                MessageAdmissionStatus.CapacityExceeded => MessageSendReservationResult.CapacityExceeded,
                _ => throw new InvalidOperationException(),
            };
            return ValueTask.FromResult(result);
        }

        public ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_received.TryDequeue(out OwnedMessage? message) ? message : null);
        }

        public ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Abort(MessageConnectionTermination termination) => AbortReason = termination.Reason;

        public ValueTask DisposeAsync()
        {
            foreach (OwnedMessage message in _received)
                message.Dispose();
            foreach (OwnedMessage message in _sent)
                message.Dispose();
            return ValueTask.CompletedTask;
        }

        internal void QueueReceive(byte[] bytes)
        {
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(bytes.Length);
            bytes.CopyTo(owner.Memory.Span);
            _received.Enqueue(new OwnedMessage(owner, bytes.Length));
        }

        private sealed class Reservation(FakeTransport owner, int messageLength) : IMessageSendReservation
        {
            private readonly FakeTransport _owner = owner;
            private bool _used;

            public int MessageLength { get; } = messageLength;

            public ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
            {
                if (_used)
                    throw new InvalidOperationException();

                _used = true;
                _owner.CommitCount++;
                MessageAdmissionResult result = _owner.CommitStatus switch
                {
                    MessageAdmissionStatus.Accepted => MessageAdmissionResult.Accepted,
                    MessageAdmissionStatus.Closed => MessageAdmissionResult.Closed,
                    MessageAdmissionStatus.CapacityExceeded => MessageAdmissionResult.CapacityExceeded,
                    _ => throw new InvalidOperationException(),
                };
                if (result.OwnershipTransferred)
                    _owner._sent.Add(message);

                return ValueTask.FromResult(result);
            }

            public void Dispose()
            {
            }
        }
    }
}
