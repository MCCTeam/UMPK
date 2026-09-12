using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Spawn a generic/object entity (<c>minecraft:add_entity</c>).</summary>
        public static readonly PacketType<ClientboundAddEntityPacket> AddEntity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_entity"));
    }
}

// Spawn / lifecycle

/// <summary>Spawn a generic entity. Field superset across eras: 1.8 uses int fixed-point (x32) positions, byte object-data-gated velocity, and no UUID/head-yaw; the modern shape has a UUID, double positions, byte rotations, VarInt data, and velocity (1.21.5 as three shorts; 26.1 the low precision velocity block held raw, see <see cref="ModernVelocityRaw"/>).</summary>
public sealed record ClientboundAddEntityPacket(
    int EntityId,
    Guid Uuid,
    int TypeId,
    double X,
    double Y,
    double Z,
    float XRot,
    float YRot,
    float YHeadRot,
    int Data,
    short VelocityX,
    short VelocityY,
    short VelocityZ,
    byte[]? ModernVelocityRaw) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddEntity;
}
