using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration resource-pack pop (optional uuid), 1.20.3+ only.</summary>
    public static readonly PacketCodec<ClientboundConfigResourcePackPopPacket> ResourcePackPop =
        PacketCodec<ClientboundConfigResourcePackPopPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigResourcePackPopPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigResourcePackPopPacket(CommonPayloads.ReadResourcePackId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackPopConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.ResourcePackPop)
            .From(JavaEras.ComponentNbtTransport, ConfigurationCodecs.ResourcePackPop);
    }
}
