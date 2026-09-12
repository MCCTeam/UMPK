using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Update enabled features

    /// <summary>Configuration update-enabled-features (VarInt-prefixed set of feature ids).</summary>
    public static readonly PacketCodec<ClientboundConfigUpdateEnabledFeaturesPacket> UpdateEnabledFeatures =
        PacketCodec<ClientboundConfigUpdateEnabledFeaturesPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigUpdateEnabledFeaturesPacket p, PacketCodecContext _) =>
                w.WriteList(p.Features, WriteId),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigUpdateEnabledFeaturesPacket(r.ReadList(ReadId)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateEnabledFeatures(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.UpdateEnabledFeatures)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.UpdateEnabledFeatures);
    }
}
