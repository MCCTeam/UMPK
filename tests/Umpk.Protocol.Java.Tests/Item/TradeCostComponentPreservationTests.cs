using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

// Every ItemCost predicate component must survive a MerchantOffers decode. MaxStackSize represents a component outside the common damage, name, and custom-data set. The item-cost wire form carries the complete component predicate.
public sealed class TradeCostComponentPreservationTests
{
    [Fact]
    public void TradeCost_WithMaxStackSizeComponent_SurvivesRoundTrip()
    {
        var costComponents = DataComponentMap.Empty.With(DataComponents.MaxStackSize, new MaxStackSizeComponent(16));
        var firstCost = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 2, costComponents);

        var offers = new MerchantOffers(
        [
            new MerchantOffer(firstCost, firstCost, SecondCost: null,
                Result: new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 1),
                Uses: 0, MaxUses: 12, Xp: 2, PriceMultiplier: 0.05f, SpecialPrice: 0, Demand: 0),
        ], VillagerLevel: 1, Experience: 0, IsRegularVillager: true, CanRestock: true);

        var p = new ClientboundMerchantOffersPacket(3, offers);
        ClientboundMerchantOffersPacket decoded = ItemCodecRoundTrip.Cycle(MerchantCodecs.MerchantOffers770, p);

        ItemStack decodedCost = decoded.Offers.Offers[0].BaseFirstCost;
        Assert.True(
            decodedCost.Components.TryGet(DataComponents.MaxStackSize, out MaxStackSizeComponent? mss),
            "trade-cost MaxStackSize component was dropped on decode (ApplyComponent whitelist).");
        Assert.Equal(16, mss!.Value);
    }
}
