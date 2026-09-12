using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 base no-op entity movement (<c>minecraft:entity</c>).</summary>
        public static readonly PacketType<ClientboundEntityPacket> Entity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("entity"));
    }
}

// Movement

/// <summary>1.8 base no-op entity movement: only the entity id.</summary>
public sealed record ClientboundEntityPacket(int EntityId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.Entity;
}
