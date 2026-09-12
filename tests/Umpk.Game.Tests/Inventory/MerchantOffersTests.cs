using Umpk.Game.Inventory;
using Umpk.Game.Tests.Items;
using Xunit;

namespace Umpk.Game.Tests.Inventory;

public class MerchantOffersTests
{
    private static MerchantOffer SampleOffer(int uses, int maxUses) => new(
        BaseFirstCost: ItemTestData.Stack("apple", 4),
        AdjustedFirstCost: ItemTestData.Stack("apple", 5),
        SecondCost: ItemTestData.Stack("bread", 1),
        Result: ItemTestData.Stack("diamond_sword", 1),
        Uses: uses,
        MaxUses: maxUses,
        Xp: 2,
        PriceMultiplier: 0.05f,
        SpecialPrice: 1,
        Demand: 3);

    [Fact]
    public void Offer_CarriesAllFields()
    {
        var offer = SampleOffer(1, 12);
        Assert.Equal(4, offer.BaseFirstCost.Count);
        Assert.Equal(5, offer.AdjustedFirstCost.Count);
        Assert.NotNull(offer.SecondCost);
        Assert.Equal(2, offer.Xp);
        Assert.Equal(0.05f, offer.PriceMultiplier);
        Assert.Equal(1, offer.SpecialPrice);
        Assert.Equal(3, offer.Demand);
    }

    [Fact]
    public void IsSoldOut_WhenUsesReachMax()
    {
        Assert.False(SampleOffer(1, 12).IsSoldOut);
        Assert.True(SampleOffer(12, 12).IsSoldOut);
    }

    [Fact]
    public void Offers_CarryMerchantLevelAndOffers()
    {
        var offers = new MerchantOffers([SampleOffer(0, 16), SampleOffer(0, 16)], VillagerLevel: 3, Experience: 250, IsRegularVillager: true, CanRestock: true);
        Assert.Equal(2, offers.Offers.Count);
        Assert.Equal(3, offers.VillagerLevel);
        Assert.Equal(250, offers.Experience);
        Assert.True(offers.IsRegularVillager);
        Assert.True(offers.CanRestock);
    }

    [Fact]
    public void Offer_ValueEquality()
    {
        Assert.Equal(SampleOffer(1, 12), SampleOffer(1, 12));
        Assert.NotEqual(SampleOffer(1, 12), SampleOffer(2, 12));
    }
}
