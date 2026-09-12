using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Server links

    /// <summary>767-769 configuration server-links: a VarInt-prefixed list of untrusted entries whose custom labels are network-NBT components with legacy interactions. Server links arrived in 1.21, three releases before the component dialect moved, so this era exists; the play-phase copy (<c>UiMiscCodecs.ServerLinksV1_21</c>) already had it and the configuration copy did not.</summary>
    public static PacketCodec<ClientboundConfigServerLinksPacket> ServerLinksV1_21 { get; } =
        MakeServerLinks(ComponentWire.V1_20_3);

    /// <summary>770+ configuration server-links: the same body with modern label interactions.</summary>
    public static PacketCodec<ClientboundConfigServerLinksPacket> ServerLinksV1_21_5 { get; } =
        MakeServerLinks(ComponentWire.V1_21_5);

    private static PacketCodec<ClientboundConfigServerLinksPacket> MakeServerLinks(
        ComponentWire text) =>
        PacketCodec<ClientboundConfigServerLinksPacket>.Of(
            (ref PacketWriter w, ClientboundConfigServerLinksPacket p, PacketCodecContext _) =>
                w.WriteList(p.Links, (ref PacketWriter sw, ServerLinkEntry link) =>
                    CommonPayloads.WriteServerLink(ref sw, link, text)),
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigServerLinksPacket(
                    r.ReadList((ref PacketReader sr) => CommonPayloads.ReadServerLink(ref sr, text))),
            WireShape.Of("varint*server_link", text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareServerLinksConfiguration(PacketBindings bindings)
    {
        // Server links arrived in 1.21, three releases before the component dialect moved at 1.21.5.
        bindings.Packet(LoginFamilyPackets.Config.ServerLinks)
            .From(JavaProtocols.V1_21, ConfigurationCodecs.ServerLinksV1_21)
            .From(JavaProtocols.V1_21_5, ConfigurationCodecs.ServerLinksV1_21_5);
    }
}
