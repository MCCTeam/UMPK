using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Modern status flags only (<c>minecraft:move_player_status_only</c>).</summary>
        public static readonly PacketType<ServerboundMovePlayerStatusOnlyPacket> MovePlayerStatusOnly =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_player_status_only"));
    }
}

/// <summary>Modern status-only move: a flags byte (on-ground, horizontal-collision).</summary>
public sealed record ServerboundMovePlayerStatusOnlyPacket(bool OnGround, bool HorizontalCollision) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MovePlayerStatusOnly;
}
