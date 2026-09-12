using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Rotation only (<c>minecraft:move_entity_rot</c> / 1.8 <c>move_entity_rotation</c>).</summary>
        public static readonly PacketType<ClientboundMoveEntityRotPacket> MoveEntityRot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("move_entity_rot"));
    }
}

/// <summary>Rotation only: entity id, packed yaw/pitch angle bytes, on-ground flag.</summary>
public sealed record ClientboundMoveEntityRotPacket(int EntityId, float Yaw, float Pitch, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.MoveEntityRot;
}
