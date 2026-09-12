using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class FloatingTakeoffTests
{
    /// <summary>The takeoff node's feet cell.</summary>
    private const int FeetY = 70;

    private static CalculationContext Pool(int fluid, bool wetLanding)
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 4);

        // The takeoff: the body stands on stone with the fluid at its feet.
        world.Set(0, FeetY - 1, 0, FixtureWorld.Stone);
        world.Set(0, FeetY, 0, fluid);

        // The cardinal ascend's support, at -X, and the diagonal ascend's, at (-X, +Z).
        world.Set(-1, FeetY, 0, FixtureWorld.Stone);
        world.Set(-1, FeetY, 1, FixtureWorld.Stone);

        // The parkour landing, two cells out along +Z with an open gap between. A reach of 2 is under ParkourFeasibility's 3.5 run-up threshold, so the arm turns on the takeoff and nothing else.
        world.Set(0, FeetY - 1, 2, FixtureWorld.Stone);

        if (wetLanding)
        {
            world.Set(-1, FeetY + 1, 0, FixtureWorld.Water);
            world.Set(-1, FeetY + 1, 1, FixtureWorld.Water);
            world.Set(0, FeetY, 2, FixtureWorld.Water);
        }

        return FixtureContext.Around(world, 0, FeetY, 0, margin: 14);
    }

    private static bool Possible(CalculationContext ctx, IMove move)
    {
        var result = default(MoveResult);
        move.Calculate(ctx, 0, FeetY, 0, ref result);
        return !result.IsImpossible;
    }

    [Theory]
    // Shallow takeoff: the 0.42 ground jump exists, and every arm is untouched whatever the landing is.
    [InlineData("cardinal-ascend", 7, true, true, "amount 1, height 1/9: a puddle")]
    [InlineData("cardinal-ascend", 7, false, true, "amount 1, height 1/9: a puddle")]
    [InlineData("diagonal-ascend", 5, true, true, "amount 3, height 1/3: the last amount under the line")]
    [InlineData("diagonal-ascend", 5, false, true, "amount 3, height 1/3: the last amount under the line")]
    // Deep takeoff, DRY landing: a climb-out, and it is deliberately left alone.
    [InlineData("cardinal-ascend", 4, false, true, "amount 4, over the line, but the bank is dry")]
    [InlineData("cardinal-ascend", 0, false, true, "a source, and the bank is dry: this is C6b's own move")]
    [InlineData("cardinal-ascend", 8, false, true, "the FALLING state, and the bank is dry")]
    [InlineData("diagonal-ascend", 0, false, true, "the diagonal climb-out C6b actually plans")]
    // Deep takeoff, WET landing: afloat at both ends, so the whole move is a swim.
    [InlineData("cardinal-ascend", 4, true, false, "amount 4, height 4/9: the first amount over the line")]
    [InlineData("cardinal-ascend", 0, true, false, "a full source block: the ordinary wade cell")]
    [InlineData("cardinal-ascend", 8, true, false, "level 8, the FALLING state a waterfall is made of")]
    [InlineData("diagonal-ascend", 4, true, false, "amount 4, height 4/9: the first amount over the line")]
    [InlineData("diagonal-ascend", 0, true, false, "a full source block: the ordinary wade cell")]
    [InlineData("diagonal-ascend", 8, true, false, "level 8, the FALLING state a waterfall is made of")]
    // And the dry control, which is what proves the gate is the water and not the fixture.
    [InlineData("cardinal-ascend", -1, false, true, "dry")]
    [InlineData("diagonal-ascend", -1, false, true, "dry")]
    [InlineData("parkour", -1, false, true, "dry")]
    // The parkour column is its own story: see the remarks.
    [InlineData("parkour", 7, true, false, "a puddle - but EvaluateSprintJump refuses ANY IsFluid takeoff, and did before this fix")]
    [InlineData("parkour", 0, true, false, "the same, cruder, pre-existing rule")]
    [InlineData("parkour", 0, false, false, "the same again: that rule does not look at the landing at all")]
    public void TheJumpFamilyOutOfAPool(string arm, int level, bool wetLanding, bool expected, string why)
    {
        int fluid = level < 0 ? FixtureWorld.Air : FixtureWorld.WaterAtLevel(level);
        CalculationContext ctx = Pool(fluid, wetLanding);
        IMove move = arm switch
        {
            "cardinal-ascend" => MoveJump.Ascend(-1, 0),
            "diagonal-ascend" => MoveJump.DiagonalAscend(-1, 1),
            "parkour" => MoveJump.Parkour(0, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };

        Assert.Equal(expected, Possible(ctx, move));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Theory]
    [InlineData(false, "clear air over the pool: course rows C6b, C6c and C7 climb out on exactly this")]
    [InlineData(true, "a FALLING column over it: vanilla refuses, this planner does not - see #113")]
    public void AClimbOutOntoDryLandIsAlwaysOffered(bool fallingOverhead, string why)
    {
        Assert.False(string.IsNullOrEmpty(why));
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 4);
        world.Set(0, FeetY - 1, 0, FixtureWorld.Stone);
        world.Set(0, FeetY, 0, FixtureWorld.WaterAtLevel(8));
        world.Set(-1, FeetY, 0, FixtureWorld.Stone);

        if (fallingOverhead)
        {
            world.Set(0, FeetY + 1, 0, FixtureWorld.WaterAtLevel(8));
            world.Set(0, FeetY + 2, 0, FixtureWorld.WaterAtLevel(1));
        }

        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 14);

        // Afloat at the takeoff and DRY at the landing in both rows: the overhead column is the only variable, and it deliberately changes nothing.
        Assert.True(MoveHelper.IsAfloatInWater(ctx, 0, FeetY, 0));
        Assert.False(MoveHelper.IsAfloatInWater(ctx, -1, FeetY + 1, 0));
        Assert.True(Possible(ctx, MoveJump.Ascend(-1, 0)));
    }

    [Theory]
    [InlineData(FixtureWorld.Ladder, true, "a dry rung on a floor: an ordinary takeoff")]
    [InlineData(FixtureWorld.WaterloggedLadder, false, "the same rung, flooded, landing under water too")]
    public void TheParkourArmsOldRuleCannotSeeAFloodedRung(int feet, bool expected, string why)
    {
        Assert.False(string.IsNullOrEmpty(why));
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 4);
        world.Set(0, FeetY - 1, 0, FixtureWorld.Stone);
        world.Set(0, FeetY, 0, feet);
        world.Set(0, FeetY - 1, 2, FixtureWorld.Stone);
        if (feet == FixtureWorld.WaterloggedLadder)
            world.Set(0, FeetY, 2, FixtureWorld.Water);

        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 14);

        Assert.False(ctx.GetBlock(0, FeetY, 0).IsFluid, "the old rule cannot see this cell either way");
        Assert.Equal(expected, Possible(ctx, MoveJump.Parkour(0, 2)));
    }

    [Theory]
    [InlineData(false, true, "a lily pad on a dry bed: an ordinary kerb, and a jump the body has")]
    [InlineData(true, false, "the pad on real water, and the kerb under it too: a swim at both ends")]
    public void TheLevelJumpArmOffALilyPad(bool flooded, bool expected, string why)
    {
        Assert.False(string.IsNullOrEmpty(why));
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 3);
        world.Set(0, FeetY - 1, 0, FixtureWorld.LilyPad);
        world.Set(1, FeetY - 1, 0, FixtureWorld.Stone);
        if (flooded)
        {
            world.Set(0, FeetY, 0, FixtureWorld.Water);
            world.Set(1, FeetY, 0, FixtureWorld.Water);
        }

        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 12);

        // The arm has to be REACHED, or the row proves nothing: a walked crossing never gets here.
        Assert.False(MoveHelper.IsWalkedLevelCrossing(ctx, 0, FeetY, 0, 1, 0));
        Assert.Equal(flooded, MoveHelper.IsAfloatInWater(ctx, 0, FeetY, 0));
        Assert.Equal(expected, Possible(ctx, MoveJump.Traverse(1, 0)));
    }

    [Theory]
    [InlineData(7, 1, true, "one thin film and nothing else: 1/9 over the floor, a real puddle")]
    [InlineData(7, 2, false, "the same film with a full cell UNDER it: the body is in a pool")]
    public void TheGateReadsTheStanceAndNotOneBlock(int level, int layers, bool expected, string why)
    {
        // Layers are stacked feet-cell-upwards, so layers == 2 puts the film at the head and a full cell at the feet. Build that explicitly rather than through Pool's uniform fill.
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 4);
        world.Set(0, FeetY - 1, 0, FixtureWorld.Stone);
        world.Set(0, FeetY, 0, layers == 2 ? FixtureWorld.Water : FixtureWorld.WaterAtLevel(level));
        if (layers == 2)
            world.Set(0, FeetY + 1, 0, FixtureWorld.WaterAtLevel(level));

        world.Set(-1, FeetY, 0, FixtureWorld.Stone);
        world.Set(-1, FeetY + 1, 0, FixtureWorld.Water);
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 14);

        Assert.Equal(expected, Possible(ctx, MoveJump.Ascend(-1, 0)));
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>The classifier itself, so a later reader can see which half of it did the work: the depth, and the stance the depth is measured from.</summary>
    [Theory]
    [InlineData(FixtureWorld.Air, false, "dry")]
    [InlineData(FixtureWorld.Stone, false, "not even passable, let alone wet")]
    public void IsAfloatInWater_NeedsWater(int feet, bool expected, string why)
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 1);
        world.Set(0, FeetY, 0, feet);
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 12);

        Assert.Equal(expected, MoveHelper.IsAfloatInWater(ctx, 0, FeetY, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>Every water amount against the 0.4 threshold, directly from the classifier. Together with the table above, this pins both the selected arm and the threshold that selects it.</summary>
    [Theory]
    [InlineData(7, 1.0 / 9.0, false)]
    [InlineData(6, 2.0 / 9.0, false)]
    [InlineData(5, 3.0 / 9.0, false)]
    [InlineData(4, 4.0 / 9.0, true)]
    [InlineData(3, 5.0 / 9.0, true)]
    [InlineData(1, 7.0 / 9.0, true)]
    [InlineData(0, 8.0 / 9.0, true)]
    [InlineData(8, 8.0 / 9.0, true)]
    public void IsAfloatInWater_IsTheThreshold(int level, double ownHeight, bool expected)
    {
        Assert.Equal(expected, ownHeight > 0.4);

        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FeetY - 1);
        world.Set(0, FeetY, 0, FixtureWorld.WaterAtLevel(level));
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, margin: 12);

        Assert.Equal(expected, MoveHelper.IsAfloatInWater(ctx, 0, FeetY, 0));
    }
}
