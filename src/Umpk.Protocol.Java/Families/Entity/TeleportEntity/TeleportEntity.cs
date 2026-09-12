using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Absolute teleport (<c>minecraft:teleport_entity</c> / 1.8 <c>entity_teleport</c>).</summary>
        public static readonly PacketType<ClientboundTeleportEntityPacket> TeleportEntity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("teleport_entity"));
    }
}

/// <summary>Absolute teleport. 1.8: entity id, fixed-point position, byte rotations, on-ground. Modern (1.21.2+): a <see cref="PositionMoveRotation"/>, a relative-flag bitset, and on-ground.</summary>
public sealed record ClientboundTeleportEntityPacket(
    int EntityId,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch,
    bool OnGround,
    PositionMoveRotation? ModernValues,
    int Relatives) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.TeleportEntity;
}
