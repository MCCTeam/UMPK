using System.Buffers;
using System.Threading;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>Implements <see cref="IFrameCodecBinding"/> over a bound <see cref="ProtocolDescriptor"/> and a swappable codec context, so a <see cref="JavaConnection"/> carries typed packets. The connection owns framing/phase tracking; this binding owns the meaning of a frame's bytes: it looks the wire id up in the current phase registry, decodes (frame-exact), and reports terminal packets for the phase-transition pause gate.</summary>
/// <remarks>The codec context is swapped only through <see cref="SetCodecState"/>, which the connection calls while inbound reading is paused at a phase transition (the memory-model rule). The swap is a single volatile write; decode always reads one coherent context.</remarks>
public sealed class DescriptorFrameCodecBinding : IFrameCodecBinding
{
    private readonly ProtocolDescriptor _descriptor;

    private PacketCodecContext _context;

    private PhaseRoute? _route;

    /// <summary>Creates a binding over a descriptor, starting with an empty-registry context.</summary>
    public DescriptorFrameCodecBinding(ProtocolDescriptor descriptor)
        : this(descriptor, new PacketCodecContext(EmptyRegistries.Access, IConnectionCodecState.Empty))
    {
    }

    /// <summary>Creates a binding over a descriptor with an initial codec context.</summary>
    public DescriptorFrameCodecBinding(ProtocolDescriptor descriptor, PacketCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);
        _descriptor = descriptor;
        _context = context;
    }

    /// <summary>The descriptor this binding decodes against.</summary>
    public ProtocolDescriptor Descriptor => _descriptor;

    /// <summary>The current codec context (registry view + dynamic state).</summary>
    public PacketCodecContext Context => Volatile.Read(ref _context);

    /// <summary>Swaps the codec context (registries + dynamic state). Called by the connection at a phase pause per the memory model; not to be called from the read loop mid-phase.</summary>
    public void SetCodecState(RegistryAccess registries, IConnectionCodecState state)
    {
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(state);
        Volatile.Write(ref _context, new PacketCodecContext(registries, state));
    }

    /// <inheritdoc />
    public bool TryDecode(in FrameDecodeContext context, out object packet)
    {
        if (!_descriptor.TryGetRegistry(context.Phase, context.Flow, out PhaseRegistry registry)
            || !registry.TryGetInbound(context.WireId, out BoundPacketCodec entry)
            || !entry.IsImplemented)
        {
            // Unmapped or a not-yet-implemented marker: the transport applies its unknown policy.
            packet = null!;
            return false;
        }

        packet = entry.Decode(context.Payload, Volatile.Read(ref _context));
        return true;
    }

    /// <inheritdoc />
    public bool TryResolve(in FrameDecodeContext context, out ResolvedFrame frame)
    {
        PhaseRoute? route = Volatile.Read(ref _route);
        if (route is null || route.Phase != context.Phase || route.Flow != context.Flow)
        {
            if (!_descriptor.TryGetRegistry(context.Phase, context.Flow, out PhaseRegistry found))
            {
                frame = new ResolvedFrame(Codec: null, context.WireId, FrameRole.None, ProtocolPhase.Handshake);
                return true;
            }

            route = new PhaseRoute(context.Phase, context.Flow, found);
            Volatile.Write(ref _route, route);
        }

        BoundPacketCodec? entry = route.Registry.TryGetInbound(context.WireId, out BoundPacketCodec e) ? e : null;
        frame = new ResolvedFrame(
            entry,
            context.WireId,
            entry?.Role ?? FrameRole.None,
            entry?.NextPhase ?? ProtocolPhase.Handshake);
        return true;
    }

    /// <inheritdoc />
    public bool TryDecode(in ResolvedFrame frame, in FrameDecodeContext context, out object packet)
    {
        if (frame.Codec is not { IsImplemented: true } entry)
        {
            packet = null!;
            return false;
        }

        packet = entry.Decode(context.Payload, Volatile.Read(ref _context));
        return true;
    }

    /// <inheritdoc />
    public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);

        if (packet is UnknownPacket unknown)
        {
            var uw = new PacketWriter(output);
            uw.WriteVarInt(unknown.WireId);
            uw.WriteBytes(unknown.Payload.Span);
            return true;
        }

        if (packet is not IPacket typed
            || !_descriptor.TryGetRegistry(phase, outboundFlow, out PhaseRegistry registry)
            || !registry.TryGetOutbound(typed.Type, out int wireId, out BoundPacketCodec entry)
            || !entry.IsImplemented)
            return false;

        var writer = new PacketWriter(output);
        writer.WriteVarInt(wireId);
        entry.Encode(ref writer, packet, Volatile.Read(ref _context));
        return true;
    }

    /// <inheritdoc />
    public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase) =>
        _descriptor.TryGetTerminalTransition(context.Phase, context.Flow, context.WireId, out nextPhase);

    /// <inheritdoc />
    public bool IsCompressionEnablePoint(in FrameDecodeContext context) =>
        _descriptor.IsCompressionEnablePoint(context.Phase, context.Flow, context.WireId);

    /// <inheritdoc />
    public bool IsEncryptionEnablePoint(in FrameDecodeContext context) =>
        _descriptor.IsEncryptionEnablePoint(context.Phase, context.Flow, context.WireId);

    /// <inheritdoc />
    public bool IsBundleDelimiter(in FrameDecodeContext context) =>
        _descriptor.TryGetRegistry(context.Phase, context.Flow, out PhaseRegistry registry)
        && registry.TryGetInbound(context.WireId, out BoundPacketCodec entry)
        && (entry.Role & FrameRole.BundleDelimiter) != 0;

    // One immutable triple, published with a single write, so a reader can never see a phase from one transition with the registry of another. It is validated against the context the read loop passes rather than against connection state, which is why there is no invalidation contract to get wrong: SetPhase and BindCodec both reach this cache only through the next frame's context.
    private sealed record PhaseRoute(ProtocolPhase Phase, PacketFlow Flow, PhaseRegistry Registry);
}
