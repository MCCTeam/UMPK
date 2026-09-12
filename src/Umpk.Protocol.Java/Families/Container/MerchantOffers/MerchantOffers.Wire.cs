using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class MerchantCodecs
{
    /// <summary>Merchant offers for 1.21.5.</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffers770 { get; } = MakeMerchantOffers(Table770);

    /// <summary>Merchant offers for 26.2.</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffers776 { get; } = MakeMerchantOffers(Table776);

    /// <summary>477-485 (1.14-1.14.2) merchant offers: a BYTE offer count, each trade's second input cost behind a present bool as an OPTIONAL stack, an offer that ENDS at <c>writeFloat(priceMultiplier)</c> (no <c>writeInt(demand)</c> yet), and a packet frame that ends at the SINGLE <c>writeBoolean(showProgress)</c> - <c>canRestock</c> does not exist yet either. The stacks are present-id stacks with a NAMED NBT root.</summary>
    /// <remarks>
    /// The 1.14 offer loop writes a byte count, two required items, an optional third item, the out-of-stock flag, four integers, and the price multiplier. Version 1.14.4 adds the <c>demand</c> integer after the multiplier.
    /// <para>The surrounding packet changes one release earlier: protocols 477-485 end after <c>showProgress</c>, while protocol 490 adds <c>canRestock</c>. Reading that second Boolean on protocols 477-485 runs past the packet. A live 1.14.2 one-trade payload is 37 bytes; the two-Boolean reader asks for byte 38 and terminates the session.</para>
    /// <para>Protocols 477-763 have no structured-component item costs, so they must not use <see cref="MerchantOffers770"/>.</para>
    /// </remarks>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_14 { get; } =
        MakeMerchantOffersPreComponent(StackWire.PresentId, MerchantOffersWire.V1_14);

    /// <summary>490 (1.14.3) merchant offers: <see cref="MerchantOffersV1_14"/> plus the trailing <c>writeBoolean(canRestock)</c> the packet gained at 1.14.3, and still WITHOUT the per-offer demand int, which only arrives at 1.14.4.</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_14_3 { get; } =
        MakeMerchantOffersPreComponent(StackWire.PresentId, MerchantOffersWire.V1_14_3);

    /// <summary>498-758 (1.14.4-1.18.2) merchant offers: the 1.14.3 form plus the trailing <c>writeInt(demand)</c> that 1.14.4 added. Still a BYTE offer count, still an optional second cost behind a present bool, still present-id stacks with a NAMED NBT root. Protocol 758 is the last release on this form.</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_14_4 { get; } =
        MakeMerchantOffersPreComponent(StackWire.PresentId, MerchantOffersWire.V1_14_4);

    /// <summary>759-763 (1.19-1.20.1) merchant offers: 1.19 moved the offer list to <c>writeCollection</c> (a VarInt count) and made the second input cost an unconditional <c>writeItem</c>, dropping the separate present bool. The stacks are still present-id stacks with a NAMED NBT root; the unnamed network root only arrives at 1.20.2.</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_19 { get; } =
        MakeMerchantOffersPreComponent(StackWire.PresentId, MerchantOffersWire.V1_19);

    /// <summary>764/765 (1.20.2-1.20.3) merchant offers: identical framing to <see cref="MerchantOffersV1_19"/> with the unnamed-root varintId stacks 1.20.2 introduced (pre-ItemCost form).</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_20_2 { get; } =
        MakeMerchantOffersPreComponent(StackWire.VarIntId, MerchantOffersWire.V1_19);

    /// <summary>766 merchant offers (ItemCost form under the 1.20.5 component table).</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_20_5 { get; } = MakeMerchantOffersItemCost(Table766);

    /// <summary>767 merchant offers (ItemCost form under the 1.21 component table).</summary>
    public static PacketCodec<ClientboundMerchantOffersPacket> MerchantOffersV1_21 { get; } = MakeMerchantOffersItemCost(Table767);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMerchantOffers(PacketBindings bindings)
    {
        // The surrounding packet frame carries a VarInt container id, the offer list, VarInt level, VarInt experience, and the show-progress and can-restock flags. Five pre-component eras differ in list framing, item-stack framing, and the final fields:
        //   477-485  (1.14-1.14.2)   BYTE offer count, second cost optional behind a present bool, the
        //                            offer ENDS at priceMultiplier (no demand int yet), and the PACKET
        //                            ends at the single showProgress bool (no canRestock yet).
        //   490      (1.14.3)        as above plus the trailing canRestock bool the PACKET gained here,
        //                            still without the demand int. The 477-485 form has one trailing
        //                            Boolean; the 490 form has two. Reading the second Boolean on a
        //                            1.14.2 one-trade payload asks for byte 38 of a 37-byte packet and
        //                            terminates the session.
        //   498-758  (1.14.4-1.18.2) as above plus the trailing demand int 1.14.4 added.
        //   759-763  (1.19-1.20.1)   the list becomes writeCollection (VarInt count) and the second cost
        //                            an unconditional writeItem.
        //   764/765  (1.20.2-1.20.3) same fields, but the stacks' NBT root drops its name.
        // No corpus capture carries a merchant_offers frame, and an offer whose second cost is EMPTY is byte-identical under the byte-count and VarInt-count forms, so only a trade with a second input cost separates them.
        PacketTimelineBuilder<ClientboundMerchantOffersPacket> merchantOffers =
            bindings.Packet(ItemPackets.Clientbound.MerchantOffers)
                .From(JavaProtocols.V1_14, MerchantCodecs.MerchantOffersV1_14)
                .From(JavaProtocols.V1_14_3, MerchantCodecs.MerchantOffersV1_14_3)
                .From(JavaProtocols.V1_14_4, MerchantCodecs.MerchantOffersV1_14_4)
                .From(JavaProtocols.V1_19, MerchantCodecs.MerchantOffersV1_19)
                .From(JavaProtocols.V1_20_2, MerchantCodecs.MerchantOffersV1_20_2)
                .From(JavaProtocols.V1_20_5, MerchantCodecs.MerchantOffersV1_20_5)
                .From(JavaProtocols.V1_21, MerchantCodecs.MerchantOffersV1_21);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            merchantOffers.From(protocol, ItemPacketCodecShared.MakeMerchantOffers(table), $"MakeMerchantOffers({era})");

    }
}
