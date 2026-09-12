using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>764 configuration resource-pack response: the action ordinal alone. 1.20.2 carries a single action enum, and 1.20.3 carries a UUID before it. Sending the 1.20.3 form here puts 16 bytes of uuid in front of the ordinal, so a real 1.20.2 server reads the first bytes of the uuid as the action and the exchange is lost.</summary>
    public static PacketCodec<ServerboundConfigResourcePackPacket> ResourcePackResponseV1_20_2 { get; } =
        PacketCodec<ServerboundConfigResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundConfigResourcePackPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackResponse(ref w, p.Id, p.Action, hasId: false),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Guid id, int action) = CommonPayloads.ReadResourcePackResponse(ref r, hasId: false);
                return new ServerboundConfigResourcePackPacket(id, action);
            });

    /// <summary>765+ configuration resource-pack response (uuid + VarInt action ordinal).</summary>
    public static PacketCodec<ServerboundConfigResourcePackPacket> ResourcePackResponseV1_20_3 { get; } =
        PacketCodec<ServerboundConfigResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundConfigResourcePackPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackResponse(ref w, p.Id, p.Action, hasId: true),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Guid id, int action) = CommonPayloads.ReadResourcePackResponse(ref r, hasId: true);
                return new ServerboundConfigResourcePackPacket(id, action);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackConfiguration(PacketBindings bindings)
    {
        // The client's answer gained the pack uuid at the same 1.20.3 split. Sending the uuid form to a 1.20.2 server puts 16 bytes in front of the action ordinal, so the server reads uuid bytes as the action; the reverse runs off the end of a one-byte payload.
        bindings.Packet(LoginFamilyPackets.Config.ResourcePack)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ResourcePackResponseV1_20_2)
            .From(JavaEras.ComponentNbtTransport, ConfigurationCodecs.ResourcePackResponseV1_20_3);
    }
}
