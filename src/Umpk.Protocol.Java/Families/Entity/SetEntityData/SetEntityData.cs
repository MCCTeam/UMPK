using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Entity metadata (<c>minecraft:set_entity_data</c>).</summary>
        public static readonly PacketType<ClientboundSetEntityDataPacket> SetEntityData =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_entity_data"));
    }
}

// Metadata / relationships

/// <summary>Entity metadata: entity id then the metadata list (see <see cref="EntityMetadataList"/>).</summary>
public sealed record ClientboundSetEntityDataPacket(int EntityId, EntityMetadataList Metadata) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetEntityData;
}
