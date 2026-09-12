using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Spectator teleport to an entity (<c>minecraft:teleport_to_entity</c>).</summary>
        public static readonly PacketType<ServerboundTeleportToEntityPacket> TeleportToEntity =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("teleport_to_entity"));
    }
}

/// <summary>Spectator teleport to an entity (1.9+): the target entity's UUID. Sent by a spectator to teleport to the entity it is spectating.</summary>
/// <param name="Target">The target entity's UUID.</param>
public sealed record ServerboundTeleportToEntityPacket(Guid Target) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.TeleportToEntity;
}
