using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayerListCodecs
{
    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerListHeaderFooter(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyTabList)
            .From(JavaProtocols.V1_8, PlayerListCodecs.TabListV1_8);
    }
}
