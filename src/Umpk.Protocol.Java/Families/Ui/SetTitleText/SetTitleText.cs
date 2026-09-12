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
        // titles

        /// <summary>Set title text (<c>minecraft:set_title_text</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetTitleTextPacket> SetTitleText =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_title_text"));
    }
}

/// <summary>Set title text (770/776): one component.</summary>
public sealed record ClientboundSetTitleTextPacket(Component Text) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetTitleText;
}
