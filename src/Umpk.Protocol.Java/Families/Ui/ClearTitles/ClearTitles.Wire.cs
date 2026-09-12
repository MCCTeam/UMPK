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

public static partial class TitleCodecs
{
    /// <summary>Clear titles (770/776).</summary>
    public static readonly PacketCodec<ClientboundClearTitlesPacket> ClearTitlesV1_17 =
        PacketCodec<ClientboundClearTitlesPacket>.Of(
            static (ref PacketWriter w, ClientboundClearTitlesPacket p, PacketCodecContext _) => w.WriteBool(p.ResetTimes),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundClearTitlesPacket(r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClearTitles(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.ClearTitles)
            .From(JavaEras.Caves, TitleCodecs.ClearTitlesV1_17);
    }
}
