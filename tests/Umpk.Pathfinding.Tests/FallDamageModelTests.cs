using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class FallDamageModelTests
{
    private const int FloorY = 64;

    private static BlockState StateOf(int fixtureState)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(0, FloorY, 0, fixtureState);
        return FixtureContext.Around(world, 0, FloorY, 0).GetBlock(0, FloorY, 0);
    }

    [Theory]
    [InlineData(FixtureWorld.DripstoneTipUp, 2.5, 2.0)]
    [InlineData(FixtureWorld.DripstoneTipDown, 0.0, 1.0)]
    [InlineData(FixtureWorld.DripstoneBaseUp, 0.0, 1.0)]
    [InlineData(FixtureWorld.Stone, 0.0, 1.0)]
    [InlineData(FixtureWorld.SlimeBlock, 0.0, 0.0)]
    [InlineData(FixtureWorld.HayBlock, 0.0, 0.2)]
    public void ImpactFor_CarriesTheDistanceBonusOnlyForAStalagmiteTip(int state, double bonus, double multiplier)
    {
        FallDamageModel.LandingImpact impact = FallDamageModel.ImpactFor(StateOf(state));

        Assert.Equal(bonus, impact.DistanceBonus);
        Assert.Equal(multiplier, impact.Multiplier);
    }

    /// <summary><see cref="FallDamageModel.MultiplierFor"/> stays a thin wrapper, so every existing caller keeps its shape and the two views of the same block can never drift apart.</summary>
    [Theory]
    [InlineData(FixtureWorld.DripstoneTipUp)]
    [InlineData(FixtureWorld.DripstoneTipDown)]
    [InlineData(FixtureWorld.Stone)]
    [InlineData(FixtureWorld.SlimeBlock)]
    [InlineData(FixtureWorld.HayBlock)]
    public void MultiplierFor_AgreesWithImpactFor(int state)
    {
        BlockState landing = StateOf(state);

        Assert.Equal(FallDamageModel.ImpactFor(landing).Multiplier, FallDamageModel.MultiplierFor(landing));
    }

    [Fact]
    public void Damage_MatchesTheLiveStalagmiteMeasurementToTheDocumentedUpperBound()
    {
        Assert.Equal(6, FallDamageModel.Damage(3.3125, 2.5, 2.0));

        // Without the bonus and the doubling, the same fall was priced at ceil(0.3125) = 1, which is the under-charge the live run caught: it planned the drop and paid five.
        Assert.Equal(1, FallDamageModel.Damage(3.3125, 0.0, 1.0));
    }

    /// <summary>The bonus is inside the safe-distance subtraction, not outside it, so it can turn a free fall into a charged one. A one-block drop onto a stalagmite really does hurt in vanilla: <c>floor((1 + 2.5 - 3) * 2) = 1</c>.</summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 1)]
    [InlineData(2.0, 3)]
    [InlineData(3.0, 5)]
    public void Damage_ChargesAStalagmiteFromTheFirstBlock(double blocks, int expected)
    {
        Assert.Equal(expected, FallDamageModel.Damage(blocks, 2.5, 2.0));
    }

    [Fact]
    public void Damage_IsZeroWhenTheMultiplierIsZeroWhateverTheBonus()
    {
        Assert.Equal(0, FallDamageModel.Damage(40.0, 2.5, 0.0));
    }

    /// <summary>The two-argument overload is unchanged, because there are pins on it and because most of the game's blocks have no bonus to carry.</summary>
    [Theory]
    [InlineData(3.0, 1.0, 0)]
    [InlineData(6.0, 1.0, 3)]
    [InlineData(10.0, 0.2, 2)]
    public void Damage_TwoArgumentOverloadIsTheZeroBonusCase(double blocks, double multiplier, int expected)
    {
        Assert.Equal(expected, FallDamageModel.Damage(blocks, multiplier));
        Assert.Equal(expected, FallDamageModel.Damage(blocks, 0.0, multiplier));
    }

    [Theory]
    [InlineData(FixtureWorld.Stone, 10f, true)]
    [InlineData(FixtureWorld.DripstoneTipUp, 10f, false)]
    [InlineData(FixtureWorld.DripstoneTipDown, 10f, true)]
    [InlineData(FixtureWorld.DripstoneTipUp, 20f, true)]     // 5 against 14 spendable: a healthy body affords it
    [InlineData(FixtureWorld.DripstoneTipUp, 8f, false)]
    public void LandingIsAffordable_PricesTheStalagmiteWithItsBonus(int state, float health, bool affordable)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(0, FloorY, 0, state);

        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = true,
            InventoryKnown = true,
            VitalsKnown = true,
            Health = health,
        };

        CalculationContext ctx = FixtureContext.Around(
            world, 0, FloorY, 0, PathfinderOptions.UnsafeFalls, capabilities: capabilities);

        Assert.Equal(affordable, ctx.LandingIsAffordable(ctx.GetBlock(0, FloorY, 0), 3));
    }

    [Theory]
    [InlineData(FixtureWorld.DripstoneTipUp)]
    [InlineData(FixtureWorld.DripstoneTipDown)]
    [InlineData(FixtureWorld.DripstoneBaseUp)]
    public void PointedDripstoneIsNotAHazardAndIsWalkedPastFreely(int state)
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 2, 0, state);   // at HEAD height beside the lane, like a cave ceiling

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.False(MoveHelper.IsHazard(ctx, ctx.GetBlock(0, FloorY + 2, 0)));
    }
}
