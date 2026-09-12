using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Start configuration (clientbound play): empty payload.</summary>
    public static PacketCodec<ClientboundStartConfigurationPacket> StartConfiguration { get; } =
        PacketCodec<ClientboundStartConfigurationPacket>.Of(
            static (ref PacketWriter _, ClientboundStartConfigurationPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundStartConfigurationPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStartConfiguration(PacketBindings bindings)
    {
        // play <-> configuration re-entry (1.20.2+, empty payloads; phase gates in ProtocolGates)
        bindings.Packet(PlayPackets.Clientbound.StartConfiguration)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.StartConfiguration);
    }
}
