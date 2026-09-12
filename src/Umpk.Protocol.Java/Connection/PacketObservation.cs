namespace Umpk.Protocol.Java;

/// <summary>A raw-frame observation surfaced by <see cref="JavaConnection.PacketObserved"/> (inbound) and <see cref="JavaConnection.OutboundPacketObserved"/> (outbound). Carries the direction, phase, wire id, and the raw decrypted+decompressed frame bytes before decoding. The packet-level object, when a codec binding decoded or encoded the frame, is exposed through <see cref="DecodedPacket"/>.</summary>
public readonly struct PacketObservation
{
    private readonly byte[] _payload;

    private readonly int _offset;

    private readonly int _length;

    internal PacketObservation(
        PacketFlow flow, ProtocolPhase phase, int wireId, byte[] payload, int offset, int length, object? decodedPacket)
    {
        Flow = flow;
        Phase = phase;
        WireId = wireId;
        _payload = payload;
        _offset = offset;
        _length = length;
        DecodedPacket = decodedPacket;
    }

    /// <summary>Direction the frame travelled.</summary>
    public PacketFlow Flow { get; }

    /// <summary>Phase the connection was in when the frame was observed.</summary>
    public ProtocolPhase Phase { get; }

    /// <summary>The frame's wire id.</summary>
    public int WireId { get; }

    /// <summary>The raw decrypted, decompressed frame payload (after the wire id). Valid only for the duration of the observer callback; the backing buffer is pooled (inbound) or is the connection's shared encode scratch (outbound), and both are reused by the next frame.</summary>
    public ReadOnlySpan<byte> RawPayload => _payload is null ? default : _payload.AsSpan(_offset, _length);

    /// <summary>The payload length in bytes, readable without touching <see cref="RawPayload"/>. This is the frame body after the wire id, before compression and encryption are applied on the wire.</summary>
    public int PayloadLength => _payload is null ? 0 : _length;

    /// <summary>Copies the payload into a freshly allocated array so it can outlive the callback.</summary>
    public byte[] CopyPayload() => RawPayload.ToArray();

    /// <summary>The decoded packet object when a codec binding decoded this frame, otherwise null. Typed as <see cref="object"/> so the transport layer stays independent of the packet model.</summary>
    public object? DecodedPacket { get; }

    /// <summary>A <see cref="PacketFrame"/> ref-struct view of this observation. Prefer this over holding the <see cref="PacketObservation"/> itself past the observer callback: unlike this ordinary struct, <see cref="PacketFrame"/> cannot be boxed, captured by a lambda, stored in a field, or put in a collection, so the pooled-buffer hazard <see cref="RawPayload"/> already documents becomes a compile error for a consumer of this method instead of a bug that only shows up as garbled bytes.</summary>
    public PacketFrame AsFrame() => new(Flow, Phase, WireId, RawPayload, DecodedPacket);
}
