using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Swing animation (<c>minecraft:swing_animation</c>, 26.3+).</summary>
        public static readonly PacketType<ClientboundSwingAnimationPacket> SwingAnimation =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("swing_animation"));
    }
}

/// <summary>Swing animation (26.3+): entity id, hand (0 main, 1 off), animation type (0 none, 1 whack, 2 stab), duration ticks.</summary>
public sealed record ClientboundSwingAnimationPacket(int EntityId, int Hand, int AnimationType, int Duration) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SwingAnimation;
}
