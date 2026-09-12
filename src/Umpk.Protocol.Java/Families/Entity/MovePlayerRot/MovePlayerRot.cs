using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Rotation (<c>minecraft:move_player_rot</c> / 1.8 <c>move_player_rotation</c>).</summary>
        public static readonly PacketType<ServerboundMovePlayerRotPacket> MovePlayerRot =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_player_rot"));
    }
}

/// <summary>Rotation move: yaw/pitch float; 1.8 on-ground byte, modern flags byte.</summary>
public sealed record ServerboundMovePlayerRotPacket(float Yaw, float Pitch, bool OnGround, bool HorizontalCollision) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MovePlayerRot;
}
