namespace Umpk.Client.Internal;

/// <summary>The outbound packet path used by actions and appliers. Sends are ordered and safe from any thread (the underlying connection serializes writes); callers on the session loop should still await.</summary>
internal interface IPacketSink
{
    /// <summary>Sends a decoded packet object, resolving its wire id through the bound codec.</summary>
    ValueTask SendAsync(object packet, CancellationToken ct);

    /// <summary>Sends a raw frame (wire id + payload) without codec encoding.</summary>
    ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
