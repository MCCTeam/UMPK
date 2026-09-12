using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration-phase cookie response (serverbound).</summary>
    public static readonly PacketCodec<ServerboundConfigCookieResponsePacket> ConfigCookieResponse =
        PacketCodec<ServerboundConfigCookieResponsePacket>.Of(
            static (ref PacketWriter w, ServerboundConfigCookieResponsePacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Key);
                WriteNullableByteArray(ref w, p.Payload);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundConfigCookieResponsePacket(ReadId(ref r), ReadNullableByteArray(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCookieResponseConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.CookieResponse)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.ConfigCookieResponse);
    }
}
