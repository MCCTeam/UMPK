using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>One packet type bound to its wire id and its version-specific codec, plus the erased encode/decode entry points the dispatcher calls. Built by <see cref="ProtocolDescriptorBuilder"/> through generated registration code. A registered-but-not-yet-implemented packet carries a not-implemented marker; the dispatcher treats it as an unknown frame so it flows through the connection's <see cref="UnknownPacketPolicy"/>.</summary>
public sealed class BoundPacketCodec
{
    /// <summary>The <see cref="CodecIdentity"/> of a not-implemented marker.</summary>
    internal const string MarkerIdentity = "marker";

    // The version-independent identity of the bundle-delimiter packet (1.19.4+). Matched against the entry's own type, so the mechanism is recognized once a descriptor registers the packet and versions without it simply never match, leaving bundle accumulation inert.
    private static readonly Identifier BundleDelimiterId = Identifier.Minecraft("bundle_delimiter");

    private readonly PacketDecoderShim _decode;

    private readonly PacketEncoderShim _encode;

    private BoundPacketCodec(
        int wireId,
        PacketType type,
        bool isImplemented,
        string codecIdentity,
        WireShape shape,
        Identifier datasetIdentifier,
        PacketDecoderShim decode,
        PacketEncoderShim encode)
    {
        WireId = wireId;
        Type = type;
        IsImplemented = isImplemented;
        CodecIdentity = codecIdentity;
        Shape = shape;
        DatasetIdentifier = datasetIdentifier;
        _decode = decode;
        _encode = encode;

        // The delimiter bit comes from the packet's own identity, not from the curated gate table, which has terminal, compression and encryption rows and no bundle row. That keeps the mechanism inert on versions that never register the packet and keeps a delimiter in a new phase working without a table edit.
        if (type.Id == BundleDelimiterId)
            Role = FrameRole.BundleDelimiter;

    }

    // Ref-struct params cannot flow through Func/Action, so span entry points are concrete delegates.
    private delegate object PacketDecoderShim(ref PacketReader reader, PacketCodecContext context);

    private delegate void PacketEncoderShim(ref PacketWriter writer, object packet, PacketCodecContext context);

    /// <summary>The numeric wire id for this packet in the owning version's phase/flow.</summary>
    public int WireId { get; }

    /// <summary>The version-independent packet identity.</summary>
    public PacketType Type { get; }

    /// <summary>False when this entry is a not-implemented marker.</summary>
    public bool IsImplemented { get; }

    /// <summary>Why this marker is one, when a binding file said so. Null on every implemented entry, and on the markers nobody has written down yet.</summary>
    /// <remarks>Internal because it is a reviewing fact, not a wire fact: a consumer sees the same relayed frame either way, while the conformance suite reads this to check the binding surface and the intentional-marker allowlist against each other in both directions.</remarks>
    internal MarkerDeclaration? Declaration { get; private init; }

    /// <summary>The lifecycle roles of this frame, written once at descriptor build time and never recomputed per frame: the same predicates, from the same place, read from a field instead of from three side tables.</summary>
    public FrameRole Role { get; internal set; }

    /// <summary>The phase a terminal frame enters. Meaningful only when <see cref="Role"/> carries <see cref="FrameRole.Terminal"/>.</summary>
    public ProtocolPhase NextPhase { get; internal set; }

    /// <summary>A stable, human-readable name for the codec actually bound here, or <c>marker</c> for a not-implemented marker. It is the source expression the registration layer used to name the era codec (for example <c>LoginCodecs.ServerHelloV1_8</c>), captured at bind time.</summary>
    /// <remarks><see cref="IsImplemented"/> distinguishes markers from codecs but does not identify the selected era. This value makes that part of the registration contract observable to binding tests.</remarks>
    public string CodecIdentity { get; }

    /// <summary>What the bound codec says it reads, taken off the codec object rather than off the bind site.</summary>
    /// <remarks><see cref="CodecIdentity"/> and this are complementary. The identity is the caller's source text, so a rename sweep moves it on every line and a mis-rebinding moves the same lines the same way; the two are indistinguishable in a pin diff. The shape comes from the codec, so no spelling at the bind site can reach it, and a rebinding across two codecs whose shapes differ moves it. Where the two codecs declare the same shape it moves nothing, which is a declared twin and belongs to the witness pin instead.</remarks>
    public WireShape Shape { get; }

    /// <summary>The identifier the VERSION DATASET spelled this packet with, which is what resolved the binding. Equal to <see cref="PacketType.Id"/> for the great majority of packets, and different exactly where a curated legacy alias fired (the 1.8 <c>minecraft:block_change</c> resolving the modern <c>minecraft:block_update</c> timeline, say).</summary>
    /// <remarks>A descriptor renders the canonical name for an aliased binding, so codec identity alone cannot prove that the dataset name matched the alias. Recording the resolving identifier lets the reachability gate check aliases as well as canonical keys.</remarks>
    public Identifier DatasetIdentifier { get; }

    /// <summary>Decodes a packet from a payload span, enforcing frame-exactness: any trailing byte raises a <see cref="ProtocolViolationException"/> carrying the packet identity.</summary>
    public object Decode(ReadOnlySpan<byte> payload, PacketCodecContext context)
    {
        var reader = new PacketReader(payload);
        object packet;
        try
        {
            packet = _decode(ref reader, context);
        }
        catch (UnmodeledItemComponentException ex) when (ex.WireId is null)
        {
            // Rethrown as the SAME type, not folded into ProtocolViolationException: the connection's packet-scoped recovery keys off this exact type, and collapsing it here would turn a recoverable modeling gap back into a session kill. Only the packet identity is added.
            throw new UnmodeledItemComponentException(
                ex.ComponentWireId,
                ex.ComponentId,
                $"Decoding {Type.Id} (wire 0x{WireId:X2}): {ex.Message}",
                ex)
            {
                WireId = WireId,
            };
        }
        catch (ProtocolViolationException ex) when (ex.WireId is null)
        {
            // A ran-past-the-end (or similar) fault from deep inside the codec carries no packet identity;
            // attach it so a decode mismatch names the offending packet instead of a bare offset.
            throw new ProtocolViolationException($"Decoding {Type.Id} (wire 0x{WireId:X2}): {ex.Message}", ex)
            {
                WireId = WireId,
                RemainingBytes = ex.RemainingBytes,
            };
        }

        if (reader.Remaining != 0)
            throw new ProtocolViolationException(
                $"Codec for {Type.Id} left {reader.Remaining} trailing byte(s) after decode.")
            {
                WireId = WireId,
                RemainingBytes = reader.Remaining,
            };

        return packet;
    }

    /// <summary>Encodes a packet body (no wire id) into the writer.</summary>
    public void Encode(ref PacketWriter writer, object packet, PacketCodecContext context) =>
        _encode(ref writer, packet, context);

    /// <summary>Binds a real, implemented codec for a packet record type.</summary>
    /// <param name="wireId">The numeric wire id in the owning phase/flow.</param>
    /// <param name="type">The version-independent packet identity.</param>
    /// <param name="codec">The era codec to bind.</param>
    /// <param name="codecIdentity">A stable name for <paramref name="codec"/>, surfaced as <see cref="CodecIdentity"/>. Null falls back to <c>unnamed</c>, which is deliberately useless in a pin diff so an unnamed bind is visible.</param>
    /// <param name="datasetIdentifier">The identifier the version dataset spelled this packet with, surfaced as <see cref="DatasetIdentifier"/>. Null means the dataset used the packet's own canonical identity.</param>
    public static BoundPacketCodec Create<TPacket>(
        int wireId,
        PacketType<TPacket> type,
        PacketCodec<TPacket> codec,
        string? codecIdentity = null,
        Identifier? datasetIdentifier = null)
        where TPacket : class, IPacket
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(codec);
        return new BoundPacketCodec(
            wireId, type, isImplemented: true, NormalizeIdentity(codecIdentity), codec.Shape, datasetIdentifier ?? type.Id,
            decode: (ref PacketReader r, PacketCodecContext c) => codec.Decode(ref r, c),
            encode: (ref PacketWriter w, object p, PacketCodecContext c) => codec.Encode(ref w, (TPacket)p, c));
    }

    /// <summary>Binds a not-yet-implemented marker for a packet id. Decode and encode throw <see cref="NotImplementedCodecException"/>; the dispatcher catches the decode case and applies the unknown policy.</summary>
    public static BoundPacketCodec Marker(int wireId, PacketType type) => Marker(wireId, type, declaration: null);

    /// <summary>Binds a not-yet-implemented marker carrying the declaration that made it one, when a binding file declared the absence rather than simply never mentioning the packet.</summary>
    internal static BoundPacketCodec Marker(int wireId, PacketType type, MarkerDeclaration? declaration)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new BoundPacketCodec(
            // A marker's PacketType is built from the dataset row itself, so the rendered identity already IS the dataset identifier; there is no alias to hide behind.
            wireId, type, isImplemented: false, MarkerIdentity, WireShape.Opaque, type.Id,
            decode: (ref PacketReader _, PacketCodecContext _) => throw new NotImplementedCodecException(type),
            encode: (ref PacketWriter _, object _, PacketCodecContext _) => throw new NotImplementedCodecException(type))
        {
            Declaration = declaration,
        };
    }

    /// <summary>Runs the bound decoder over a caller-supplied canonical payload and reports how many bytes it consumed, or the type name of the fault it raised. This is the behavioural half of the codec pin: <see cref="CodecIdentity"/> is a NAME, so an in-place edit to a codec body changes nothing about it, while the consumed-byte count moves whenever the wire shape moves.</summary>
    /// <remarks>Deliberately not routed through <see cref="Decode"/>: that method enforces frame-exactness, which would collapse "consumed 3 of 256" into an exception and throw the measurement away. The canonical payload is the caller's; an all-zero buffer keeps every length prefix at zero, so no codec allocates from it and the probe cannot depend on chosen field values.</remarks>
    /// <param name="payload">The canonical probe payload.</param>
    /// <param name="context">The codec context to decode under.</param>
    /// <param name="consumed">Bytes of <paramref name="payload"/> the decoder read, when it completed.</param>
    /// <param name="faultType">The runtime type name of the fault raised, when it did not complete.</param>
    /// <returns>True when the decoder ran to completion.</returns>
    internal bool TryProbeShape(
        ReadOnlySpan<byte> payload,
        PacketCodecContext context,
        out int consumed,
        out string faultType)
    {
        var reader = new PacketReader(payload);
        try
        {
            _decode(ref reader, context);
        }
        catch (Exception ex)
        {
            // Catching broadly is the measurement, not a swallow: the probe's whole job is to classify whatever the codec does with the canonical payload, and a fault type is a perfectly good fingerprint. Nothing here runs on a connection.
            consumed = 0;
            faultType = ex.GetType().Name;
            return false;
        }

        consumed = payload.Length - reader.Remaining;
        faultType = string.Empty;
        return true;
    }

    private static string NormalizeIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return "unnamed";

        // CallerArgumentExpression preserves the caller's source text verbatim, including the line breaks and indentation of a wrapped argument. Collapse it so the pin records the expression and not the formatting, and so a reflow of the binding file is not a pin diff.
        return string.Join(' ', identity.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Thrown by a <see cref="BoundPacketCodec"/> marker for a packet that is registered but whose codec is not implemented yet. The dispatcher catches it and treats the frame as unknown.</summary>
public sealed class NotImplementedCodecException : Exception
{
    /// <summary>Creates the marker exception for a packet type.</summary>
    public NotImplementedCodecException(PacketType type)
        : base($"No codec is implemented yet for {type?.Id} ({type?.Phase}/{type?.Flow}).")
        => Type = type ?? throw new ArgumentNullException(nameof(type));

    /// <summary>The packet identity whose codec is unimplemented.</summary>
    public PacketType Type { get; }
}
