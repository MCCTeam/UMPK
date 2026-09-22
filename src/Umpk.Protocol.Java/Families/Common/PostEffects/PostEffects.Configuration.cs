using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Post effects (<c>minecraft:post_effects</c>), clientbound, 26.3+.</summary>
        public static readonly PacketType<ClientboundConfigPostEffectsPacket> PostEffects =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("post_effects"));
    }
}

/// <summary>Configuration post-effects: server tells the client which shader passes to apply.</summary>
public sealed record ClientboundConfigPostEffectsPacket(IReadOnlyList<Identifier> Effects) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.PostEffects;
}
