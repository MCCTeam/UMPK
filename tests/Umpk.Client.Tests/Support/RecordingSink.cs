using Umpk.Client.Internal;

namespace Umpk.Client.Tests.Support;

/// <summary>Records outbound packets/frames sent by appliers or actions for assertion.</summary>
internal sealed class RecordingSink : IPacketSink
{
    public List<object> Packets { get; } = [];

    public List<(int WireId, byte[] Payload)> Frames { get; } = [];

    public ValueTask SendAsync(object packet, CancellationToken ct)
    {
        Packets.Add(packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        Frames.Add((wireId, payload.ToArray()));
        return ValueTask.CompletedTask;
    }
}
