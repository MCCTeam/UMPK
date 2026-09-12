using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class SoulSpeedPricingTests
{
    private const int FloorY = 64;
    private const int FeetY = FloorY + 1;

    private static readonly Identifier SoulSpeed = Identifier.Minecraft("soul_speed");

    // The multiplier itself

    /// <summary>The one-argument signature remains a context-free calculation used by <c>FloorSpeedPricingTests</c> and <c>SegmentBudgetPolicyTests</c>. Capability-aware pricing is a separate overload, so existing callers retain the measured result.</summary>
    [Fact]
    public void TheOneArgumentSignature_StillAnswersTheMeasuredNumber()
    {
        Assert.Equal(ActionCosts.MeasuredSlowFloorCostMultiplier, ActionCosts.SpeedFactorCostMultiplier(0.4f), 6);
        Assert.Equal(1.0, ActionCosts.SpeedFactorCostMultiplier(1.0f), 6);
        Assert.Equal(2.0, ActionCosts.SpeedFactorCostMultiplier(0.5f), 6);
    }

    /// <summary>The overload charges nothing extra when the floor's slowdown is bypassed, and is identical to the one-argument form when it is not.</summary>
    [Theory]
    [InlineData(0.4f, false)]
    [InlineData(1.0f, false)]
    [InlineData(0.5f, false)]
    public void TheOverload_MatchesTheOneArgumentForm_WhenNothingIsBypassed(float factor, bool bypassed)
        => Assert.Equal(
            ActionCosts.SpeedFactorCostMultiplier(factor),
            ActionCosts.SpeedFactorCostMultiplier(factor, bypassed),
            10);

    [Theory]
    [InlineData(0.4f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void TheOverload_ChargesNothing_WhenTheSlowdownIsBypassed(float factor)
        => Assert.Equal(1.0, ActionCosts.SpeedFactorCostMultiplier(factor, bypassed: true), 10);

    // The capability read, and its polarity

    /// <summary>A capture with no inventory cannot see boots, so it reports level 0 and the lane keeps today's conservative charge. Empty must not read as "definitely bare".</summary>
    [Fact]
    public void SoulSpeedLevel_IsZero_WhenTheInventoryIsUnknown()
    {
        Assert.False(PathfinderCapabilities.None.InventoryKnown);
        Assert.Equal(0, PathfinderCapabilities.None.SoulSpeedLevel);
    }

    /// <summary>A capture that CAN see the inventory and finds no boots also reports 0, and that 0 means something different. The two are told apart by <see cref="PathfinderCapabilities.InventoryKnown"/>, exactly as <c>CountOf</c>'s zero is.</summary>
    [Fact]
    public void SoulSpeedLevel_IsZero_WithNoBoots()
    {
        PathfinderCapabilities caps = Geared(level: 0);
        Assert.True(caps.InventoryKnown);
        Assert.Equal(0, caps.SoulSpeedLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SoulSpeedLevel_ReadsTheBootsEnchantment(int level)
        => Assert.Equal(level, Geared(level).SoulSpeedLevel);

    // What a plan is charged

    /// <summary>A soul-sand lane costs 1.691 times a stone lane for a bare bot and the same as stone for a bot wearing Soul Speed boots.</summary>
    [Fact]
    public void SoulSandCost_DropsToOne_WithSoulSpeedBoots()
    {
        Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            FloorCost(FixtureWorld.SoulSand, PathfinderCapabilities.None),
            6);
        Assert.Equal(1.0, FloorCost(FixtureWorld.SoulSand, Geared(3)), 6);
    }

    /// <summary>The polarity test, and the one that must stay conservative: a capture that could not see the inventory keeps the full charge.</summary>
    [Fact]
    public void SoulSandCost_StaysConservative_WhenTheInventoryIsUnknown()
        => Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            FloorCost(FixtureWorld.SoulSand, PathfinderCapabilities.None),
            6);

    /// <summary>Boots the bot is not wearing change nothing.</summary>
    [Fact]
    public void SoulSandCost_StaysConservative_WithNoBoots()
        => Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            FloorCost(FixtureWorld.SoulSand, Geared(0)),
            6);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void HoneyCost_StaysConservative_EvenWithBoots(int level)
        => Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            FloorCost(FixtureWorld.Honey, Geared(level)),
            6);

    /// <summary>Soul soil is in the tag at speed factor 1.0, so it was never charged anything and still is not. The bypass must not become a DISCOUNT on a floor that was already free.</summary>
    [Fact]
    public void SoulSoilCost_IsOne_BothWays()
    {
        Assert.Equal(1.0, FloorCost(FixtureBlockData.SoulSoilState, PathfinderCapabilities.None), 6);
        Assert.Equal(1.0, FloorCost(FixtureBlockData.SoulSoilState, Geared(3)), 6);
    }

    /// <summary>The two-cell read still applies: a carpet laid over soul sand is priced by the soul sand underneath, and the bypass has to reach the same cell the charge came from.</summary>
    [Fact]
    public void CarpetOverSoulSand_IsBypassedToo()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.SoulSand);
        world.Set(0, FeetY, 0, FixtureWorld.Carpet);

        Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            MoveHelperFloorCost(world, PathfinderCapabilities.None),
            6);
        Assert.Equal(1.0, MoveHelperFloorCost(world, Geared(3)), 6);
    }

    // The deliberate asymmetry

    [Fact]
    public void LandSlack_StaysBoundToTheMeasuredConstant_AndIsNotCapabilityAware()
        => Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier,
            Umpk.Pathfinding.Execution.SegmentBudgetPolicy.LandSlack,
            10);

    private static PathfinderCapabilities Geared(int level)
    {
        var items = new List<CapabilityItem>();
        if (level > 0)
            items.Add(new CapabilityItem(
                Identifier.Minecraft("netherite_boots"),
                Count: 1,
                MenuSlot: 8,
                Enchantments: new EnchantmentReadout(
                    BootStack(level), "V1_8", NoLegacyIds.Instance, sessionCanNameEnchantments: true)));

        return new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items = items,
        };
    }

    /// <summary>A real component-era boots stack carrying a real soul-speed instance, built the way a 766+ wire decode leaves one. Nothing here is a stand-in for the readout itself: the test drives the same <see cref="EnchantmentReadout"/> the live capture builds.</summary>
    private static ItemStack BootStack(int level)
    {
        Registry<ItemDefinition> items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
            .Add(1, Identifier.Minecraft("netherite_boots"), new ItemDefinition(1))
            .Build();
        Registry<EnchantmentDefinition> enchants = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(1, SoulSpeed, new EnchantmentDefinition(MaxLevel: 3))
            .Build();
        Assert.True(items.TryGet(1, out RegistryEntry<ItemDefinition> boots));
        Assert.True(enchants.TryGet(SoulSpeed, out RegistryEntry<EnchantmentDefinition> soulSpeed));
        return new ItemStack(boots, 1).With(
            DataComponents.Enchantments,
            new EnchantmentsComponent([new EnchantmentInstance(soulSpeed, level)]));
    }

    /// <summary>A bridge source with no legacy table at all: this suite only builds component-era stacks, and a source that answered anything would be pretending the legacy path was under test.</summary>
    private sealed class NoLegacyIds : ILegacyItemBridgeSource
    {
        public static readonly NoLegacyIds Instance = new();

        public bool TryMapNbtKey(string era, string nbtKey, out Identifier componentId)
        {
            componentId = default;
            return false;
        }

        public bool TryMapEnchantmentId(string era, int numericId, out Identifier enchantmentId)
        {
            enchantmentId = default;
            return false;
        }
    }

    private static double FloorCost(int floorState, PathfinderCapabilities capabilities)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, floorState);
        return MoveHelperFloorCost(world, capabilities);
    }

    private static double MoveHelperFloorCost(FixtureWorld world, PathfinderCapabilities capabilities)
    {
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, capabilities: capabilities);
        return Umpk.Pathfinding.Moves.MoveHelper.FloorSpeedPenalty(ctx, 0, FeetY, 0);
    }
}
