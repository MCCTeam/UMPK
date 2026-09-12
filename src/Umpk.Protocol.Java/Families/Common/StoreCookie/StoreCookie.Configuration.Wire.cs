using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Store cookie (clientbound; payload capped at 5120 bytes by vanilla, VarInt-prefixed here).</summary>
    public static readonly PacketCodec<ClientboundConfigStoreCookiePacket> StoreCookie =
        PacketCodec<ClientboundConfigStoreCookiePacket>.Of(
            static (ref PacketWriter w, ClientboundConfigStoreCookiePacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Key);
                WriteCookieByteArray(ref w, p.Payload);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigStoreCookiePacket(ReadId(ref r), ReadCookieByteArray(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStoreCookieConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.StoreCookie)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.StoreCookie);
    }
}
