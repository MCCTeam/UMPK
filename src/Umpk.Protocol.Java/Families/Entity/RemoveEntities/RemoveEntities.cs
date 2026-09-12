using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Remove entities (<c>minecraft:remove_entities</c> / 1.8 <c>minecraft:entity_destroy</c>).</summary>
        public static readonly PacketType<ClientboundRemoveEntitiesPacket> RemoveEntities =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("remove_entities"));
    }
}

/// <summary>Remove entities: a VarInt list of entity ids.</summary>
public sealed record ClientboundRemoveEntitiesPacket(IReadOnlyList<int> EntityIds) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.RemoveEntities;
}
