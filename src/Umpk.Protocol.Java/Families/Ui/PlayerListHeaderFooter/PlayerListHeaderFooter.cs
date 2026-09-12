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
        /// <summary>Legacy 1.8 player list header/footer (<c>minecraft:player_list_header_footer</c>, 47).</summary>
        public static readonly PacketType<ClientboundTabListPacket> LegacyTabList =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_list_header_footer"));
    }
}
