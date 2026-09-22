using Umpk.Protocol.Java;

namespace Umpk.Client.Internal;

/// <summary>Resolves play-phase wire ids by identifier for a bound version. Used where the client needs raw-frame send/receive for packets the typed packet surface does not model as decoded records (e.g. play custom_payload). All lookups go through the descriptor's phase registry, so wrong-phase or unmapped ids are impossible by construction.</summary>
internal sealed class WireIndex
{
    private readonly ProtocolDescriptor _descriptor;

    public WireIndex(JavaVersion version)
    {
        _descriptor = version.Protocol;
    }

    /// <summary>Resolves the clientbound play wire id for an identifier, or -1 when absent.</summary>
    public int ClientboundPlay(Identifier id) => Lookup(ProtocolPhase.Play, PacketFlow.Clientbound, id);

    /// <summary>Resolves the serverbound play wire id for an identifier, or -1 when absent.</summary>
    public int ServerboundPlay(Identifier id) => Lookup(ProtocolPhase.Play, PacketFlow.Serverbound, id);

    /// <summary>Resolves the serverbound configuration wire id for an identifier, or -1 when absent.</summary>
    public int ServerboundConfiguration(Identifier id) => Lookup(ProtocolPhase.Configuration, PacketFlow.Serverbound, id);

    /// <summary>Whether a serverbound play packet TYPE can actually be encoded on this version. Unlike the identifier lookups above this consults the outbound table, which is keyed by packet identity: a marker occupies a wire id under its own <see cref="MarkerPacketType"/> instance, so <c>ServerboundPlay(id) &gt;= 0</c> is true for a packet with no codec and is not a substitute for this. Neither is the identifier enough where two records share one identifier across an era boundary (<c>edit_book</c> binds the legacy item-stack record on 393-753, which the client never constructs). Version-optional sends must gate here.</summary>
    public bool CanSendPlay(PacketType type) => CanSend(ProtocolPhase.Play, type);

    /// <summary>The serverbound-configuration mirror of <see cref="CanSendPlay"/>.</summary>
    public bool CanSendConfiguration(PacketType type) => CanSend(ProtocolPhase.Configuration, type);

    private bool CanSend(ProtocolPhase phase, PacketType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Exactly the predicate DescriptorFrameCodecBinding.TryEncode applies, IsImplemented included, so a true answer here means the frame encodes rather than throwing inside the connection's write lock. The marker path cannot reach a real PacketType today, but mirroring the encoder's whole condition is what keeps that true if it ever can.
        return OutboundCodec(phase, type) is not null;
    }

    /// <summary>The bound outbound entry for a packet TYPE on this version, or null when it has no implemented codec. Lets callers branch on what the bound codec reads (its wire shape) rather than on a protocol number.</summary>
    internal BoundPacketCodec? OutboundCodec(ProtocolPhase phase, PacketType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_descriptor.TryGetRegistry(phase, PacketFlow.Serverbound, out PhaseRegistry registry)
            && registry.TryGetOutbound(type, out _, out BoundPacketCodec entry)
            && entry.IsImplemented)
            return entry;

        return null;
    }

    private int Lookup(ProtocolPhase phase, PacketFlow flow, Identifier id)
    {
        if (!_descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
            return -1;

        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        return -1;
    }
}
