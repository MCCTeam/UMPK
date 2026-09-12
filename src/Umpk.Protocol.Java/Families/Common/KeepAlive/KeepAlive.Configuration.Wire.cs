using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration keep-alive request.</summary>
    public static readonly PacketCodec<ClientboundConfigKeepAlivePacket> KeepAliveClient =
        PacketCodec<ClientboundConfigKeepAlivePacket>.Of(
            static (ref PacketWriter w, ClientboundConfigKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundConfigKeepAlivePacket(CommonPayloads.ReadKeepAliveId(ref r)));

    /// <summary>Configuration keep-alive response.</summary>
    public static readonly PacketCodec<ServerboundConfigKeepAlivePacket> KeepAliveServer =
        PacketCodec<ServerboundConfigKeepAlivePacket>.Of(
            static (ref PacketWriter w, ServerboundConfigKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundConfigKeepAlivePacket(CommonPayloads.ReadKeepAliveId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareKeepAliveConfiguration(PacketBindings bindings)
    {
        bindings.Packet(ConfigurationPackets.Clientbound.KeepAlive)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.KeepAliveClient);

        bindings.Packet(ConfigurationPackets.Serverbound.KeepAlive)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.KeepAliveServer);
    }
}
