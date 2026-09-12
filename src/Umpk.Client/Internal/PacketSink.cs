using Umpk.Protocol.Java;

namespace Umpk.Client.Internal;

/// <summary>The concrete outbound path over a live <see cref="JavaConnection"/>.</summary>
internal sealed class PacketSink(JavaConnection connection) : IPacketSink
{
    public ValueTask SendAsync(object packet, CancellationToken ct) => connection.SendAsync(packet, ct);

    public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => connection.SendFrameAsync(wireId, payload, ct);
}

/// <summary>A sink that resolves the live sink lazily, so actions/appliers can be constructed before the connection exists. Throws a descriptive error if used before connect.</summary>
internal sealed class DeferredSink(Func<IPacketSink> resolve) : IPacketSink
{
    public ValueTask SendAsync(object packet, CancellationToken ct) => resolve().SendAsync(packet, ct);

    public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => resolve().SendFrameAsync(wireId, payload, ct);
}
