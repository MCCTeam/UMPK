using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Configuration ping and pong

    /// <summary>Configuration ping (clientbound int id).</summary>
    public static readonly PacketCodec<ClientboundConfigPingPacket> Ping =
        PacketCodec<ClientboundConfigPingPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigPingPacket p, PacketCodecContext _) => CommonPayloads.WritePingId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundConfigPingPacket(CommonPayloads.ReadPingId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePingConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.Ping)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.Ping);
    }
}
