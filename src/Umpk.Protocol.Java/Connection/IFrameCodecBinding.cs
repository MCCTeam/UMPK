namespace Umpk.Protocol.Java;

/// <summary>The decode/encode seam the packet-codec layer implements and binds to a <see cref="JavaConnection"/>. The transport core owns framing, compression, encryption, phase tracking, ordering, and backpressure; it delegates the meaning of a frame's bytes to this binding. Kept deliberately minimal so the codec layer slots in without the transport referencing packet records or codec tables.</summary>
/// <remarks>Implementations are supplied a <see cref="FrameDecodeContext"/> describing the frame's phase, flow, wire id, and payload; they return a decoded object (the codec layer's <c>InboundItem</c>/<c>IPacket</c>) or signal that the frame is unknown. The transport applies its <see cref="UnknownPacketPolicy"/> to unknown frames.</remarks>
public interface IFrameCodecBinding
{
    /// <summary>Attempts to decode a frame into a packet object. Returns false when the wire id is not mapped in the current phase/flow (the transport then applies its unknown-packet policy). The implementation must consume the whole payload or throw <see cref="ProtocolViolationException"/>.</summary>
    bool TryDecode(in FrameDecodeContext context, out object packet);

    /// <summary>Encodes a packet object into <paramref name="output"/> as the uncompressed frame content (wire id VarInt + fields). Returns false if the packet type is not valid to send in the current phase/flow.</summary>
    bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, System.Buffers.IBufferWriter<byte> output);

    /// <summary>Reports whether a just-decoded frame is a terminal packet that ends the current phase, and if so, the phase to transition into. The transport pauses inbound reading at the transition until the consumer acknowledges via <see cref="JavaConnection.SetPhase"/>.</summary>
    bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase);

    /// <summary>Reports whether a just-observed frame is the set-compression frame, after which every subsequent frame uses the compressed wire format. The transport pauses inbound reading at this frame boundary until the consumer enables compression via <see cref="JavaConnection.EnableCompression"/>, so the next frame is read with the compressed reader (the transform-enable point).</summary>
    bool IsCompressionEnablePoint(in FrameDecodeContext context);

    /// <summary>Reports whether a just-observed frame is the encryption-request frame, after which the peer's subsequent bytes are AES-CFB8 encrypted (client role: the clientbound encryption request; server role: the serverbound key response). The transport pauses inbound reading at this frame boundary until the consumer enables encryption via <see cref="JavaConnection.EnableEncryption"/>, so the read loop cannot append post-handshake bytes to its buffer undecrypted (mirror of the set-compression gate). Defaults to <see langword="false"/> for bindings with no encryption step.</summary>
    bool IsEncryptionEnablePoint(in FrameDecodeContext context) => false;

    /// <summary>Reports whether a just-decoded frame is the <c>minecraft:bundle_delimiter</c> (1.19.4+). The transport toggles bundle accumulation on each delimiter, buffering the packets between a pair and delivering them as one atomic <see cref="PacketBundle"/>. Defaults to <see langword="false"/> for bindings/versions without bundles.</summary>
    bool IsBundleDelimiter(in FrameDecodeContext context) => false;

    /// <summary>Resolves one frame in a single lookup. Returns false when this binding has no resolved form, in which case the caller falls back to <see cref="TryDecode(in FrameDecodeContext, out object)"/> plus the four predicate calls.</summary>
    /// <remarks>The default returns false rather than an empty result on purpose. A binding that holds no <see cref="BoundPacketCodec"/> would inherit an empty resolution that arms no gate and recognises no delimiter, and it would still compile: "keeps compiling" and "keeps working" are not the same answer, and only one of them is checked by a compiler.</remarks>
    /// <param name="context">The frame being resolved.</param>
    /// <param name="frame">The resolved entry, when this binding has one.</param>
    /// <returns>True when <paramref name="frame"/> is authoritative for this frame.</returns>
    bool TryResolve(in FrameDecodeContext context, out ResolvedFrame frame)
    {
        frame = default;
        return false;
    }

    /// <summary>Decodes a frame the caller already resolved. The default forwards to the wire-id path so an existing implementer keeps working unchanged.</summary>
    /// <param name="frame">The resolved entry.</param>
    /// <param name="context">The same frame the resolve was performed on.</param>
    /// <param name="packet">The decoded packet.</param>
    /// <returns>True when the frame decoded.</returns>
    bool TryDecode(in ResolvedFrame frame, in FrameDecodeContext context, out object packet) =>
        TryDecode(in context, out packet);
}

/// <summary>Context passed to <see cref="IFrameCodecBinding.TryDecode(in FrameDecodeContext, out object)"/>.</summary>
public readonly ref struct FrameDecodeContext
{
    internal FrameDecodeContext(ProtocolPhase phase, PacketFlow flow, int wireId, ReadOnlySpan<byte> payload)
    {
        Phase = phase;
        Flow = flow;
        WireId = wireId;
        Payload = payload;
    }

    /// <summary>The connection's current phase.</summary>
    public ProtocolPhase Phase { get; }

    /// <summary>The inbound flow being decoded.</summary>
    public PacketFlow Flow { get; }

    /// <summary>The frame's wire id.</summary>
    public int WireId { get; }

    /// <summary>The raw decrypted, decompressed payload following the wire id.</summary>
    public ReadOnlySpan<byte> Payload { get; }
}
