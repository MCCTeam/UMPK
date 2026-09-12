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
        /// <summary>Set action bar text (<c>minecraft:set_action_bar_text</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetActionBarTextPacket> SetActionBarText =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_action_bar_text"));
    }
}

/// <summary>Set action bar text (770/776): one component.</summary>
public sealed record ClientboundSetActionBarTextPacket(Component Text) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetActionBarText;
}
