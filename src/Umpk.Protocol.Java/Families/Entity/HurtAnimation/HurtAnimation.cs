using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Hurt animation (<c>minecraft:hurt_animation</c>, 1.19.4+).</summary>
        public static readonly PacketType<ClientboundHurtAnimationPacket> HurtAnimation =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("hurt_animation"));
    }
}

/// <summary>Hurt animation (1.19.4+): entity id, hurt yaw as a float.</summary>
public sealed record ClientboundHurtAnimationPacket(int EntityId, float Yaw) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.HurtAnimation;
}
