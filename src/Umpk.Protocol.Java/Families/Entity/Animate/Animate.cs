using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Animation (<c>minecraft:animate</c>).</summary>
        public static readonly PacketType<ClientboundAnimatePacket> Animate =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("animate"));
    }
}

// Events

/// <summary>Animation: entity id, unsigned action byte.</summary>
public sealed record ClientboundAnimatePacket(int EntityId, byte Action) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.Animate;
}
