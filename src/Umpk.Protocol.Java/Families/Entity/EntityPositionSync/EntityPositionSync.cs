using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Absolute position sync (<c>minecraft:entity_position_sync</c>, 1.21.2+).</summary>
        public static readonly PacketType<ClientboundEntityPositionSyncPacket> EntityPositionSync =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("entity_position_sync"));
    }
}

/// <summary>Entity position sync (1.21.2+): entity id, position/move/rotation, on-ground. From 26.3 the path itself rides the packet (see <see cref="Path"/>); <see cref="Values"/> mirrors the destination so era-blind consumers keep working.</summary>
public sealed record ClientboundEntityPositionSyncPacket(int EntityId, PositionMoveRotation Values, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.EntityPositionSync;

    /// <summary>The 26.3+ position path. Null on older eras, which carry a single position/move/rotation instead.</summary>
    public EntityPositionPath? Path { get; init; }
}
