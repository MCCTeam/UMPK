namespace Umpk.Protocol.Java;

/// <summary>
/// A ref-struct view of one raw protocol frame, obtained from <see cref="PacketObservation.AsFrame"/>: the same pre-decode bytes <see cref="PacketObservation"/> already carries, without letting the payload escape the callback that received it.
/// <para>This is a <c>ref struct</c> on purpose, and it is why this type exists alongside <see cref="PacketObservation"/> rather than replacing it. <see cref="Payload"/> points at a POOLED inbound buffer or at the connection's shared outbound encode scratch (see <c>JavaConnection.PacketObserved</c> and <c>JavaConnection.OutboundPacketObserved</c>), and the next frame reuses both. <see cref="PacketObservation"/> is an ordinary struct, so a consumer that stashes one in a field, a list, or a captured lambda compiles cleanly and then reads a recycled buffer with no warning. A ref struct cannot be boxed, captured by a lambda, stored in a field, or put in a collection, so that exact mistake is a compile error here instead. Call <see cref="CopyPayload"/> to keep the bytes past the call that handed them to you.</para>
/// <para>The guard lives beside the hazard, in <c>Umpk.Protocol.Java</c> itself, so every consumer of the raw frame feed gets it for free: <c>Umpk.PacketRecorder</c>, any future proxy, and a downstream SDK that wraps this type for its own plugin surface.</para>
/// </summary>
public readonly ref struct PacketFrame
{
    private readonly ReadOnlySpan<byte> _payload;

    internal PacketFrame(
        PacketFlow flow, ProtocolPhase phase, int wireId, ReadOnlySpan<byte> payload, object? decodedPacket)
    {
        Flow = flow;
        Phase = phase;
        WireId = wireId;
        _payload = payload;
        DecodedPacket = decodedPacket;
    }

    /// <summary>The direction the frame travelled.</summary>
    public PacketFlow Flow { get; }

    /// <summary>The protocol phase the connection was in when the frame was observed.</summary>
    public ProtocolPhase Phase { get; }

    /// <summary>The frame's real wire id for this protocol version.</summary>
    public int WireId { get; }

    /// <summary>The decrypted, decompressed frame body after the wire id. Valid ONLY for the duration of the handler call; see the type remarks.</summary>
    public ReadOnlySpan<byte> Payload => _payload;

    /// <summary>The body length in bytes, readable without touching <see cref="Payload"/>.</summary>
    public int PayloadLength => _payload.Length;

    /// <summary>The decoded packet object when a codec binding decoded or encoded this frame, otherwise null.</summary>
    public object? DecodedPacket { get; }

    /// <summary>True when the frame came FROM the server.</summary>
    public bool IsClientbound => Flow == PacketFlow.Clientbound;

    /// <summary>Copies the payload into a freshly allocated array so it can outlive this call.</summary>
    public byte[] CopyPayload() => _payload.ToArray();
}

/// <summary>Handles one observed <see cref="PacketFrame"/>. A plain <c>Action&lt;PacketFrame&gt;</c> cannot be used because <see cref="PacketFrame"/> is a ref struct, which is exactly the constraint that keeps the payload from escaping the callback.</summary>
/// <param name="frame">The frame. Do not let it, or its payload span, outlive this call.</param>
public delegate void PacketFrameHandler(in PacketFrame frame);
