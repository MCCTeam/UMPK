using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 spawn player (<c>minecraft:add_player</c>).</summary>
        public static readonly PacketType<ClientboundAddPlayerPacket> AddPlayer =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_player"));
    }
}

/// <summary>1.8 spawn player: entity id, uuid, fixed-point position, byte rotations, held item, metadata.</summary>
public sealed record ClientboundAddPlayerPacket(
    int EntityId,
    Guid Uuid,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch,
    short CurrentItem,
    EntityMetadataList Metadata) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddPlayer;
}
