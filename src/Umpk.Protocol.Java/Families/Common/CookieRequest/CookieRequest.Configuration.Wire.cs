using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration-phase cookie request (clientbound).</summary>
    public static readonly PacketCodec<ClientboundConfigCookieRequestPacket> ConfigCookieRequest =
        PacketCodec<ClientboundConfigCookieRequestPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigCookieRequestPacket p, PacketCodecContext _) => WriteId(ref w, p.Key),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundConfigCookieRequestPacket(ReadId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieRequestConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.CookieRequest)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.ConfigCookieRequest);
    }
}
