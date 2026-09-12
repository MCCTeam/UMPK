using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>How one pre-component era frames the merchant-offers packet. The three facts move at three different releases and in an order nothing would guess: the trailing <c>canRestock</c> bool arrives at 1.14.3, the per-offer <c>demand</c> int one release LATER at 1.14.4, and the offer list stops being a byte count with an optional second cost at 1.19.</summary>
/// <param name="LegacyList">Pre-1.19: a byte offer count and a present bool before the second input cost. From 1.19 the list is a <c>writeCollection</c> VarInt count and the second cost is written unconditionally. The two agree byte for byte until a trade actually carries a second cost.</param>
/// <param name="WithDemand">1.14.4+: the per-offer trailing <c>writeInt(demand)</c>.</param>
/// <param name="WithRestock">1.14.3+: the packet's second trailing bool, <c>canRestock</c>.</param>
internal readonly record struct MerchantOffersWire(bool LegacyList, bool WithDemand, bool WithRestock)
{
    /// <summary>477-485 (1.14-1.14.2): one trailing bool, no demand.</summary>
    internal static MerchantOffersWire V1_14 { get; } = new(LegacyList: true, WithDemand: false, WithRestock: false);

    /// <summary>490 (1.14.3): the canRestock bool arrives.</summary>
    internal static MerchantOffersWire V1_14_3 { get; } = V1_14 with { WithRestock = true };

    /// <summary>498-758 (1.14.4-1.18.2): the per-offer demand int arrives.</summary>
    internal static MerchantOffersWire V1_14_4 { get; } = V1_14_3 with { WithDemand = true };

    /// <summary>759-765 (1.19-1.20.3): the offer list becomes a VarInt-counted collection.</summary>
    internal static MerchantOffersWire V1_19 { get; } = V1_14_4 with { LegacyList = false };

    /// <inheritdoc />
    public override string ToString() =>
        $"legacylist={(LegacyList ? 1 : 0)},demand={(WithDemand ? 1 : 0)},restock={(WithRestock ? 1 : 0)}";
}
