using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;

namespace Umpk.Client.Bedrock.Session;

internal sealed class BedrockFakePeer : IAsyncDisposable
{
    private readonly IMessageConnection _transport;
    private readonly BedrockConnection _connection;
    private readonly Queue<BedrockBatchMember> _inbound = new();
    private readonly List<BedrockLoadingScreenPacket> _initialLoadingNotifications = [];

    private BedrockFakePeer(IMessageConnection transport, BedrockSessionCodecContext context)
    {
        _transport = transport;
        _connection = new BedrockConnection(transport, context);
    }

    internal static BedrockFakePeer Create(IMessageConnection transport)
    {
        var routing = new BedrockCodecContext(
            BedrockProtocols.V1_26_45,
            BedrockPacketDirection.Serverbound,
            default,
            default);
        return new BedrockFakePeer(
            transport,
            new BedrockSessionCodecContext(routing, BedrockRegistrySnapshot.Empty, BedrockCodecLimits.Default));
    }

    internal static TestMessageConnectionPair CreatePair(int capacity = 64) => TestMessageConnectionPair.Create(capacity);

    internal async ValueTask SendAsync(int packetId, byte[] payload, CancellationToken cancellationToken = default)
    {
        var member = new BedrockBatchMember(
            new BedrockPacketHeader(packetId, new BedrockSubclientId(0), new BedrockSubclientId(0)),
            payload);
        MessageAdmissionResult admission = await _connection.SendBatchAsync([member], cancellationToken).ConfigureAwait(false);
        if (!admission.OwnershipTransferred)
            throw new InvalidOperationException($"The fake peer send was rejected with {admission.Status}.");
    }

    internal async ValueTask SendBatchAsync(IReadOnlyList<(int PacketId, byte[] Payload)> members, CancellationToken cancellationToken = default)
    {
        var batch = new List<BedrockBatchMember>(members.Count);
        foreach ((int packetId, byte[] payload) in members)
        {
            batch.Add(new BedrockBatchMember(
                new BedrockPacketHeader(packetId, new BedrockSubclientId(0), new BedrockSubclientId(0)),
                payload));
        }

        MessageAdmissionResult admission = await _connection.SendBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        if (!admission.OwnershipTransferred)
            throw new InvalidOperationException($"The fake peer send was rejected with {admission.Status}.");
    }

    internal async ValueTask<(int PacketId, byte[] Payload)> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            (int packetId, byte[] payload) = await ReceiveIncludingLoadingAsync(cancellationToken).ConfigureAwait(false);
            if (packetId != 312)
                return (packetId, payload);
        }
    }

    internal async ValueTask<(int PacketId, byte[] Payload)> ReceiveIncludingLoadingAsync(CancellationToken cancellationToken = default)
    {
        if (_inbound.Count == 0)
        {
            var collector = new CollectingConsumer(_inbound);
            bool received = await _connection.ReceiveAndDispatchAsync(collector, cancellationToken).ConfigureAwait(false);
            if (!received)
                throw new InvalidOperationException("The fake peer transport closed.");
        }

        BedrockBatchMember member = _inbound.Dequeue();
        if (member.Header.PacketId == 312)
        {
            BedrockLoadingScreenPacket notification = BedrockLoadingScreenCodecs.Decode(member.Payload);
            BedrockLoadingScreenType expected = _initialLoadingNotifications.Count switch
            {
                0 => BedrockLoadingScreenType.StartLoadingScreen,
                1 => BedrockLoadingScreenType.EndLoadingScreen,
                _ => throw new InvalidOperationException("The fake peer received a duplicate initial loading notification."),
            };
            if (notification.Type != expected || notification.LoadingScreenId.HasValue)
                throw new InvalidOperationException("The fake peer received an invalid initial loading notification.");
            _initialLoadingNotifications.Add(notification);
        }
        return (member.Header.PacketId, member.Payload);
    }

    internal ValueTask EnableCompressionAsync(BedrockCompressionSettings settings, CancellationToken cancellationToken = default) =>
        _connection.EnableCompressionAsync(settings, cancellationToken);

    internal ValueTask EnableEncryptionAsync(BedrockEncryptionState send, BedrockEncryptionState receive, CancellationToken cancellationToken = default) =>
        _connection.EnableEncryptionAsync(send, receive, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class CollectingConsumer(Queue<BedrockBatchMember> inbound) : IBedrockBatchConsumer
    {
        public BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context)
        {
            inbound.Enqueue(new BedrockBatchMember(member.Header, (byte[])member.Payload.Clone()));
            return null;
        }
    }
}
