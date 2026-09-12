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
        /// <summary>Set subtitle text (<c>minecraft:set_subtitle_text</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetSubtitleTextPacket> SetSubtitleText =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_subtitle_text"));
    }
}

/// <summary>Set subtitle text (770/776): one component.</summary>
public sealed record ClientboundSetSubtitleTextPacket(Component Text) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetSubtitleText;
}
