using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Entity event / status (<c>minecraft:entity_event</c> / 1.8 <c>entity_status</c>).</summary>
        public static readonly PacketType<ClientboundEntityEventPacket> EntityEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("entity_event"));
    }
}

/// <summary>Entity event / status: 4-byte entity id and a byte event/status code.</summary>
public sealed record ClientboundEntityEventPacket(int EntityId, sbyte EventId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.EntityEvent;
}
