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
    /// <summary>Play-phase cookie response: the key plus an optional payload.</summary>
    public static readonly PacketCodec<ServerboundCookieResponsePacket> CookieResponse =
        PacketCodec<ServerboundCookieResponsePacket>.Of(
            static (ref PacketWriter w, ServerboundCookieResponsePacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Key);
                WriteNullableByteArray(ref w, p.Payload);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundCookieResponsePacket(ReadId(ref r), ReadNullableByteArray(ref r)),
            WireShape.Of("identifier,opt_bytes"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieResponsePlay(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Serverbound.CookieResponse)
            .From(JavaEras.ItemComponents, PlayCommonCodecs.CookieResponse);
    }
}
