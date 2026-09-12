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
    /// <summary>Modern player info remove (770/776).</summary>
    public static readonly PacketCodec<ClientboundPlayerInfoRemovePacket> PlayerInfoRemoveV1_19_3 =
        PacketCodec<ClientboundPlayerInfoRemovePacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerInfoRemovePacket p, PacketCodecContext _) =>
                w.WriteList(p.ProfileIds, static (ref PacketWriter sw, Guid g) => sw.WriteUuid(g)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundPlayerInfoRemovePacket(r.ReadList(static (ref PacketReader sr) => sr.ReadUuid())));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerInfoRemove(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.PlayerInfoRemove)
            .From(JavaProtocols.V1_19_3, PlayerListCodecs.PlayerInfoRemoveV1_19_3);
    }
}
