using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase post effects (<c>minecraft:post_effects</c>, 26.3+).</summary>
        public static readonly PacketType<ClientboundPostEffectsPacket> PostEffects =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("post_effects"));
    }
}

/// <summary>Play-phase post effects (26.3+): the shader post-processing passes the client applies.</summary>
public sealed record ClientboundPostEffectsPacket(IReadOnlyList<Identifier> Effects) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.PostEffects;
}
