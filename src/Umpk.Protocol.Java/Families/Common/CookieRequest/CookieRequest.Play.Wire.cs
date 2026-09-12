using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Play-phase cookie request: a single identifier key.</summary>
    public static readonly PacketCodec<ClientboundCookieRequestPacket> CookieRequest =
        PacketCodec<ClientboundCookieRequestPacket>.Of(
            static (ref PacketWriter w, ClientboundCookieRequestPacket p, PacketCodecContext _) => WriteId(ref w, p.Key),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundCookieRequestPacket(ReadId(ref r)),
            WireShape.Of("identifier"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieRequestPlay(PacketBindings bindings)
    {
        // Play-phase cookie exchange and transfer use the same common wire forms as configuration.
        bindings.Packet(PlayPackets.Clientbound.CookieRequest)
            .From(JavaEras.ItemComponents, PlayCommonCodecs.CookieRequest);
    }
}
