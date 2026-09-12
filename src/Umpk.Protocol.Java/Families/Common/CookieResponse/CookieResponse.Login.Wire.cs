using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginChannelCodecs
{
    /// <summary>Login-phase cookie response (serverbound).</summary>
    public static readonly PacketCodec<ServerboundLoginCookieResponsePacket> LoginCookieResponse =
        PacketCodec<ServerboundLoginCookieResponsePacket>.Of(
            static (ref PacketWriter w, ServerboundLoginCookieResponsePacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Key);
                WriteNullableByteArray(ref w, p.Payload);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundLoginCookieResponsePacket(ReadId(ref r), ReadNullableByteArray(ref r)),
            WireShape.Of("identifier,opt_bytes"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieResponseLogin(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Login.CookieResponse)
            .From(JavaEras.ItemComponents, LoginChannelCodecs.LoginCookieResponse);
    }
}
