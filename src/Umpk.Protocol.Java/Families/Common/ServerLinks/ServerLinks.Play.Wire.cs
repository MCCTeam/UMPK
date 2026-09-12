using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    /// <summary>Server links, 767-769 (1.21-1.21.4): the packet arrives at 1.21 with a network-NBT custom label, so only the label's click/hover interaction era moves inside this band, at 1.21.5. A server-links label is exactly the kind of component that carries an open_url click, so the era is load-bearing here rather than theoretical.</summary>
    public static readonly PacketCodec<ClientboundServerLinksPacket> ServerLinksV1_21 =
        MakeServerLinks(ComponentWire.V1_20_3);

    /// <summary>Server links (770/776): the same body with modern label interactions.</summary>
    public static readonly PacketCodec<ClientboundServerLinksPacket> ServerLinksV1_21_5 =
        MakeServerLinks(ComponentWire.V1_21_5);

    private static PacketCodec<ClientboundServerLinksPacket> MakeServerLinks(
        ComponentWire text) =>
        PacketCodec<ClientboundServerLinksPacket>.Of(
            (ref PacketWriter w, ClientboundServerLinksPacket p, PacketCodecContext _) =>
                w.WriteList(p.Links, (ref PacketWriter sw, ServerLinkEntry link) =>
                    CommonPayloads.WriteServerLink(ref sw, link, text)),
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundServerLinksPacket(
                    r.ReadList((ref PacketReader sr) => CommonPayloads.ReadServerLink(ref sr, text))),
            WireShape.Of("varint*server_link", text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareServerLinksPlay(PacketBindings bindings)
    {
        // Custom server links carry a component label, so this chain follows the interaction boundary: 767-769 legacy, 770+ modern.
        bindings.Packet(UiPackets.Clientbound.ServerLinks)
            .From(JavaProtocols.V1_21, UiMiscCodecs.ServerLinksV1_21)
            .From(JavaProtocols.V1_21_5, UiMiscCodecs.ServerLinksV1_21_5);
    }
}
