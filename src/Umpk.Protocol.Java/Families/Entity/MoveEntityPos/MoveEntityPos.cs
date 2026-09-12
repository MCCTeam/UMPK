using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Relative move (<c>minecraft:move_entity_pos</c> / 1.8 <c>move_entity_position</c>).</summary>
        public static readonly PacketType<ClientboundMoveEntityPosPacket> MoveEntityPos =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("move_entity_pos"));
    }
}

/// <summary>Relative move. 1.8 encodes deltas as bytes (block/128 fixed point); modern encodes them as shorts (block*4096). The record holds the raw wire deltas so the era codec applies the correct scale.</summary>
public sealed record ClientboundMoveEntityPosPacket(int EntityId, short DeltaX, short DeltaY, short DeltaZ, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.MoveEntityPos;
}
