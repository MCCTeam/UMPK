using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Finish configuration (empty terminal packet), both directions.</summary>
    public static readonly PacketCodec<ClientboundFinishConfigurationPacket> FinishClient =
        PacketCodec<ClientboundFinishConfigurationPacket>.Of(
            static (ref PacketWriter _, ClientboundFinishConfigurationPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundFinishConfigurationPacket());

    /// <summary>Finish-configuration acknowledgment (empty terminal packet).</summary>
    public static readonly PacketCodec<ServerboundFinishConfigurationPacket> FinishServer =
        PacketCodec<ServerboundFinishConfigurationPacket>.Of(
            static (ref PacketWriter _, ServerboundFinishConfigurationPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundFinishConfigurationPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareFinishConfiguration(PacketBindings bindings)
    {
        bindings.Packet(ConfigurationPackets.Clientbound.FinishConfiguration)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.FinishClient);

        bindings.Packet(ConfigurationPackets.Serverbound.FinishConfiguration)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.FinishServer);
    }
}
