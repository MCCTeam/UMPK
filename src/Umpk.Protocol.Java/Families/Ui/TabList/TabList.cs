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
        /// <summary>Player list header/footer (<c>minecraft:tab_list</c> 770/776, <c>minecraft:player_list_header_footer</c> 47).</summary>
        public static readonly PacketType<ClientboundTabListPacket> TabList =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("tab_list"));
    }
}

/// <summary>Player list header/footer (770/776 tab_list, 47 player_list_header_footer): two components.</summary>
public sealed record ClientboundTabListPacket(Component Header, Component Footer) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.TabList;
}
