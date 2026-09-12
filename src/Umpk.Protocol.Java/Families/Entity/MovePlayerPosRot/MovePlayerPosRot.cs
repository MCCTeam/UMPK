using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Position + rotation (<c>minecraft:move_player_pos_rot</c> / 1.8 <c>move_player_position_rotation</c>).</summary>
        public static readonly PacketType<ServerboundMovePlayerPosRotPacket> MovePlayerPosRot =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_player_pos_rot"));
    }
}

/// <summary>Position + rotation move.</summary>
public sealed record ServerboundMovePlayerPosRotPacket(
    double X, double Y, double Z, float Yaw, float Pitch, bool OnGround, bool HorizontalCollision) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MovePlayerPosRot;
}
