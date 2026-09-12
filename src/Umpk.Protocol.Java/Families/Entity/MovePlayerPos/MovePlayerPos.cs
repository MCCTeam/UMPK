using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Position (<c>minecraft:move_player_pos</c> / 1.8 <c>move_player_position</c>).</summary>
        public static readonly PacketType<ServerboundMovePlayerPosPacket> MovePlayerPos =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_player_pos"));
    }
}

/// <summary>Position move. 1.8: x/y/z double, on-ground byte. Modern: x/y/z double then a flags byte packing on-ground (0x01) and horizontal-collision (0x02).</summary>
public sealed record ServerboundMovePlayerPosPacket(double X, double Y, double Z, bool OnGround, bool HorizontalCollision) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MovePlayerPos;
}
