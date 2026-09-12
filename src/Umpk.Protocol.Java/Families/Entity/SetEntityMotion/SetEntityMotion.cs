using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Velocity (<c>minecraft:set_entity_motion</c>).</summary>
        public static readonly PacketType<ClientboundSetEntityMotionPacket> SetEntityMotion =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_entity_motion"));
    }
}

/// <summary>Velocity. Pre-26 (1.8 / 1.21.5): entity id plus three short components (block/8000 per tick). 26.1+ replaces the three shorts with the low-precision quantized velocity block (<c>Vec3.LP_STREAM_CODEC</c> / <c>LpVec3</c>), which has no lossless short model and is carried raw in <see cref="ModernVelocityRaw"/>. When that field is set the short components are unused.</summary>
public sealed record ClientboundSetEntityMotionPacket(
    int EntityId,
    short VelocityX,
    short VelocityY,
    short VelocityZ,
    byte[]? ModernVelocityRaw = null) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetEntityMotion;
}
