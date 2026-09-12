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

public static partial class PlayerListCodecs
{
    /// <summary>Player list header/footer (770/776).</summary>
    public static readonly PacketCodec<ClientboundTabListPacket> TabListV1_21_5 =
        PacketCodec<ClientboundTabListPacket>.Of(
            static (ref PacketWriter w, ClientboundTabListPacket p, PacketCodecContext _) =>
            {
                WriteModernComponent(ref w, p.Header);
                WriteModernComponent(ref w, p.Footer);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundTabListPacket(ReadModernComponent(ref r), ReadModernComponent(ref r)));

    /// <summary>Legacy 1.8 player list header/footer (47).</summary>
    public static readonly PacketCodec<ClientboundTabListPacket> TabListV1_8 =
        PacketCodec<ClientboundTabListPacket>.Of(
            static (ref PacketWriter w, ClientboundTabListPacket p, PacketCodecContext _) =>
            {
                WriteLegacyComponent(ref w, p.Header);
                WriteLegacyComponent(ref w, p.Footer);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundTabListPacket(ReadLegacyComponent(ref r), ReadLegacyComponent(ref r)));

    /// <summary>
    /// 107-764 tab list header/footer: two JSON-string chat components, legacy interactions.
    /// <para>The validation cap changes within this band, but a cap never rides the wire, so one codec covers protocols 107 through 764.</para>
    /// <para>Byte-identical to <see cref="TabListV1_8"/>, which serves 1.8's separate <c>minecraft:player_list_header_footer</c> identity; the two stay distinct members because they are bound to distinct packet identities.</para>
    /// </summary>
    public static PacketCodec<ClientboundTabListPacket> TabListV1_9 { get; } =
        MakeTabList(ComponentWire.V1_8);

    /// <summary>765-769 tab list header/footer: two network-NBT components with legacy <c>clickEvent</c>/<c>hoverEvent</c> interaction names. The interaction era moves at 1.21.5.</summary>
    public static PacketCodec<ClientboundTabListPacket> TabListV1_20_3 { get; } =
        MakeTabList(ComponentWire.V1_20_3);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTabList(PacketBindings bindings)
    {
        // minecraft:tab_list exists from 1.9 on (1.8 carries the same two components under the minecraft:player_list_header_footer identity, bound above as LegacyTabList). The header and footer are JSON-string components from 107 through 764 and network NBT from 765 on; see PlayerListCodecs.TabListV1_9 for the era boundary. The NBT band runs to 769, not 767: the interaction era is a separate boundary and it moves at 1.21.5.
        bindings.Packet(UiPackets.Clientbound.TabList)
            .From(JavaEras.Combat, PlayerListCodecs.TabListV1_9)
            .From(JavaEras.ComponentNbtTransport, PlayerListCodecs.TabListV1_20_3)
            .From(JavaEras.ModernComponents, PlayerListCodecs.TabListV1_21_5);
    }
}
