using Umpk.Game.Items;

namespace Umpk.Game.Inventory;

/// <summary>One villager/wandering-trader offer. The <see cref="AdjustedFirstCost"/> is the price the client shows after the special-price and demand adjustments; <see cref="BaseFirstCost"/> is the unadjusted base.</summary>
/// <param name="BaseFirstCost">The base first input cost, before special-price/demand adjustment.</param>
/// <param name="AdjustedFirstCost">The first input cost the client shows after adjustment.</param>
/// <param name="SecondCost">The optional second input cost.</param>
/// <param name="Result">The output stack.</param>
/// <param name="Uses">How many times the trade has been used.</param>
/// <param name="MaxUses">The maximum uses before the trade locks.</param>
/// <param name="Xp">Villager experience granted per trade.</param>
/// <param name="PriceMultiplier">The demand price multiplier.</param>
/// <param name="SpecialPrice">The flat special-price delta (can be negative).</param>
/// <param name="Demand">The current demand counter feeding the price adjustment.</param>
public sealed record MerchantOffer(
    ItemStack BaseFirstCost,
    ItemStack AdjustedFirstCost,
    ItemStack? SecondCost,
    ItemStack Result,
    int Uses,
    int MaxUses,
    int Xp,
    float PriceMultiplier,
    int SpecialPrice,
    int Demand)
{
    /// <summary>True when the trade is out of uses and currently disabled. This is derived from <see cref="Uses"/>/<see cref="MaxUses"/> and matches the wire bool exactly. A recorded out-of-stock flag forces <c>uses = maxUses</c>, so a vanilla server never sends an out-of-stock bool that diverges from this derivation. The encoder therefore re-emits <see cref="IsSoldOut"/> byte-identically without retaining the wire bool.</summary>
    public bool IsSoldOut => Uses >= MaxUses;
}

/// <summary>The set of offers attached to an open merchant window, plus the merchant-level fields.</summary>
/// <param name="Offers">The offers, in list order (the trade index a client selects).</param>
/// <param name="VillagerLevel">The merchant level (1..5); 0 for a wandering trader.</param>
/// <param name="Experience">The merchant's total experience.</param>
/// <param name="IsRegularVillager">Whether the merchant is a regular (leveling) villager.</param>
/// <param name="CanRestock">Whether the merchant can restock.</param>
public sealed record MerchantOffers(
    IReadOnlyList<MerchantOffer> Offers,
    int VillagerLevel = 0,
    int Experience = 0,
    bool IsRegularVillager = false,
    bool CanRestock = false);
