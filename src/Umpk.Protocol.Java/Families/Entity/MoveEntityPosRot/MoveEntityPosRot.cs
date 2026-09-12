using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Relative move + rotation (<c>minecraft:move_entity_pos_rot</c> / 1.8 <c>move_entity_position_rotation</c>).</summary>
        public static readonly PacketType<ClientboundMoveEntityPosRotPacket> MoveEntityPosRot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("move_entity_pos_rot"));
    }
}

/// <summary>Relative move + rotation.</summary>
public sealed record ClientboundMoveEntityPosRotPacket(
    int EntityId, short DeltaX, short DeltaY, short DeltaZ, float Yaw, float Pitch, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.MoveEntityPosRot;
}
