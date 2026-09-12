using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration acknowledged (serverbound play): empty payload.</summary>
    public static PacketCodec<ServerboundConfigurationAcknowledgedPacket> ConfigurationAcknowledged { get; } =
        PacketCodec<ServerboundConfigurationAcknowledgedPacket>.Of(
            static (ref PacketWriter _, ServerboundConfigurationAcknowledgedPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundConfigurationAcknowledgedPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareConfigurationAcknowledged(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Serverbound.ConfigurationAcknowledged)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ConfigurationAcknowledged);
    }
}
