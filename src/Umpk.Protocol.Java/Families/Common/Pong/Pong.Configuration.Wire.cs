using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration pong (serverbound int id).</summary>
    public static readonly PacketCodec<ServerboundConfigPongPacket> Pong =
        PacketCodec<ServerboundConfigPongPacket>.Of(
            static (ref PacketWriter w, ServerboundConfigPongPacket p, PacketCodecContext _) => CommonPayloads.WritePingId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundConfigPongPacket(CommonPayloads.ReadPingId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePongConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.Pong)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.Pong);
    }
}
