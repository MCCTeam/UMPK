namespace Umpk.Protocol.Java;

/// <summary>A packet's version-independent identity: <c>(phase, flow, Identifier)</c>, not a numeric id and not a CLR type lookup. Numeric wire ids are per-version table data. This non-generic base carries the identity; <see cref="PacketType{TPacket}"/> binds it to a record type.</summary>
public abstract class PacketType
{
    private protected PacketType(ProtocolPhase phase, PacketFlow flow, Identifier id, Type payloadType)
    {
        Phase = phase;
        Flow = flow;
        Id = id;
        PayloadType = payloadType;
    }

    /// <summary>The phase this packet belongs to.</summary>
    public ProtocolPhase Phase { get; }

    /// <summary>The flow direction this packet travels.</summary>
    public PacketFlow Flow { get; }

    /// <summary>The stable namespaced identity (for example <c>minecraft:login</c>).</summary>
    public Identifier Id { get; }

    /// <summary>The CLR record type carrying this packet's payload.</summary>
    public Type PayloadType { get; }

    /// <summary>The packet record type carrying this packet's payload.</summary>
    public override string ToString() => $"{Phase}/{Flow} {Id} ({PayloadType.Name})";
}

/// <summary>A <see cref="PacketType"/> bound to its packet record type.</summary>
/// <typeparam name="TPacket">The packet record type.</typeparam>
public sealed class PacketType<TPacket> : PacketType
    where TPacket : class, IPacket
{
    /// <summary>Declares a packet type. Declared once per semantic packet as a static readonly field.</summary>
    public PacketType(ProtocolPhase phase, PacketFlow flow, Identifier id)
        : base(phase, flow, id, typeof(TPacket))
    {
    }
}

/// <summary>A packet identity for a registered-but-unimplemented packet (a marker). It has no bound record type; the payload type is <see cref="UnknownPacket"/> so a proxy can still relay it.</summary>
public sealed class MarkerPacketType : PacketType
{
    /// <summary>Declares a marker identity for an unimplemented packet id.</summary>
    public MarkerPacketType(ProtocolPhase phase, PacketFlow flow, Identifier id)
        : base(phase, flow, id, typeof(UnknownPacket))
    {
    }
}

/// <summary>The contract every packet record implements: it can report its own <see cref="PacketType"/>.</summary>
public interface IPacket
{
    /// <summary>This packet's version-independent identity.</summary>
    PacketType Type { get; }
}
