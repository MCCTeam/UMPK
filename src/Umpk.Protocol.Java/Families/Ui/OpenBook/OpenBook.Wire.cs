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

public static partial class UiMiscCodecs
{
    /// <summary>Open book (770/776).</summary>
    public static readonly PacketCodec<ClientboundOpenBookPacket> OpenBookV1_14 =
        PacketCodec<ClientboundOpenBookPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenBookPacket p, PacketCodecContext _) => w.WriteVarInt(p.Hand),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundOpenBookPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareOpenBook(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.OpenBook)
            .From(JavaEras.Palettes, UiMiscCodecs.OpenBookV1_14);
    }
}
