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
    /// <summary>Play-phase store cookie: the key plus a VarInt-prefixed payload.</summary>
    public static readonly PacketCodec<ClientboundStoreCookiePacket> StoreCookie =
        PacketCodec<ClientboundStoreCookiePacket>.Of(
            static (ref PacketWriter w, ClientboundStoreCookiePacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Key);
                WriteCookieByteArray(ref w, p.Payload);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundStoreCookiePacket(ReadId(ref r), ReadCookieByteArray(ref r)),
            WireShape.Of("identifier,bytes"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStoreCookiePlay(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Clientbound.StoreCookie)
            .From(JavaEras.ItemComponents, PlayCommonCodecs.StoreCookie);
    }
}
