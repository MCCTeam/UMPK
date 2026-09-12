using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginChannelCodecs
{
    // Login and configuration cookie packets share the same wire shape.

    /// <summary>Login-phase cookie request (clientbound).</summary>
    public static readonly PacketCodec<ClientboundLoginCookieRequestPacket> LoginCookieRequest =
        PacketCodec<ClientboundLoginCookieRequestPacket>.Of(
            static (ref PacketWriter w, ClientboundLoginCookieRequestPacket p, PacketCodecContext _) => WriteId(ref w, p.Key),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundLoginCookieRequestPacket(ReadId(ref r)),
            WireShape.Of("identifier"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieRequestLogin(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Login.CookieRequest)
            .From(JavaEras.ItemComponents, LoginChannelCodecs.LoginCookieRequest);
    }
}
