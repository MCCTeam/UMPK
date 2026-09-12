using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>A frame whose wire id has no implemented codec in the current phase/flow (unmapped, or a registered-but-unimplemented marker under <see cref="UnknownPacketPolicy.Preserve"/>, or a decode failure surfaced under <see cref="DecodeFailureMode.ForwardVerbatim"/>). It carries the known wire id and the raw payload so it can be re-emitted verbatim by a proxy.</summary>
public sealed record UnknownPacket(int WireId, ReadOnlyMemory<byte> Payload) : IPacket
{
    /// <summary>Unknown packets have no version-independent identity.</summary>
    public PacketType Type => throw new InvalidOperationException("UnknownPacket has no PacketType.");
}

/// <summary>A bundle of packets delivered atomically (1.19.4+ bundle delimiter semantics). The dispatcher accumulates packets between two <c>bundle_delimiter</c> frames and delivers them as one item.</summary>
/// <remarks><see cref="Frames"/> is index-aligned with <see cref="Packets"/> and always the same length. The bundle crosses the channel as one <see cref="InboundItem"/> whose own <see cref="InboundItem.Frame"/> is <c>default</c> (a bundle is not a frame), so without this the wire id and byte count of every bundled packet would be lost and a consumer publishing frame identity would have to invent one. Entries are <c>default</c> only when the bundle was assembled through a <see cref="BundleAccumulator"/> overload that was never given a frame, which is the session-layer path over already-decoded packets; <see cref="JavaConnection"/> always supplies the real frame.</remarks>
public sealed record PacketBundle(IReadOnlyList<object> Packets, IReadOnlyList<InboundFrame> Frames)
{
    /// <summary>Creates a bundle whose packets carry no captured frame identity, for the session-layer accumulator path that is handed already-decoded packets and never sees a frame.</summary>
    public PacketBundle(IReadOnlyList<object> packets)
        : this(packets, NoFrames(packets))
    {
    }

    private static InboundFrame[] NoFrames(IReadOnlyList<object> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        return packets.Count == 0 ? [] : new InboundFrame[packets.Count];
    }
}

/// <summary>Behavior when a mapped codec fails to decode a frame.</summary>
public enum DecodeFailureMode
{
    /// <summary>Fail the connection with the decode exception (default for client/server roles).</summary>
    FailConnection,

    /// <summary>Skip the frame, count it, and continue.</summary>
    SkipFrameAndReport,

    /// <summary>Surface the frame as an <see cref="UnknownPacket"/> carrying its wire id and raw payload.</summary>
    ForwardVerbatim,
}
