using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set title animation times (<c>minecraft:set_titles_animation</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetTitlesAnimationPacket> SetTitlesAnimation =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_titles_animation"));
    }
}

/// <summary>Set title animation times (770/776): fade-in, stay, fade-out (big-endian ints, in ticks).</summary>
public sealed record ClientboundSetTitlesAnimationPacket(int FadeIn, int Stay, int FadeOut) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetTitlesAnimation;
}
