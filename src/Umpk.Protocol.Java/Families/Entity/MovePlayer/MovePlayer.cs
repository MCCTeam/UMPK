using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>1.8 status-only move (<c>minecraft:move_player</c>).</summary>
        public static readonly PacketType<ServerboundMovePlayerStatusPacket> MovePlayerStatus =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_player"));
    }
}

// Serverbound player movement

/// <summary>1.8 status-only move: just the on-ground flag.</summary>
public sealed record ServerboundMovePlayerStatusPacket(bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MovePlayerStatus;
}
