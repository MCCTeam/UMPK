using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class HangingTakeoffTests
{
    /// <summary>The rung's feet cell.</summary>
    private const int RungY = 79;

    /// <summary>Builds the rung. The climbable at <c>(0, RungY, 0)</c> hangs on a wall at <c>(1, RungY, 0)</c>; <paramref name="floorUnderTheRung"/> is the whole variable - with it the body is STANDING in a cell that happens to hold a climbable, without it the body is HANGING.</summary>
    private static CalculationContext Rung(bool floorUnderTheRung)
    {
        var world = new FixtureWorld();

        // A floor far below, so an unsupported rung is genuinely unsupported and the fixture still has somewhere for a fall to end.
        world.Floor(-8, 8, -8, 8, RungY - 4);

        world.Set(0, RungY, 0, FixtureWorld.Ladder);
        world.Set(1, RungY, 0, FixtureWorld.Stone);

        // The cardinal ascend's support, at -X, and the diagonal ascend's, at (-X, +Z).
        world.Set(-1, RungY, 0, FixtureWorld.Stone);
        world.Set(-1, RungY, 1, FixtureWorld.Stone);

        // The parkour landing, two cells out along +Z with an open gap between - a reach of 2 is under ParkourFeasibility's 3.5 run-up threshold, so the arm turns on the takeoff and nothing else.
        world.Set(0, RungY - 1, 2, FixtureWorld.Stone);

        if (floorUnderTheRung)
            world.Set(0, RungY - 1, 0, FixtureWorld.Stone);

        return FixtureContext.Around(world, 0, RungY, 0, margin: 12);
    }

    private static bool Possible(CalculationContext ctx, IMove move)
    {
        var result = default(MoveResult);
        move.Calculate(ctx, 0, RungY, 0, ref result);
        return !result.IsImpossible;
    }

    /// <summary>The complete standing and hanging truth table for each jump family.</summary>
    [Theory]
    [InlineData("cardinal-ascend", true, true, "a grounded step up is an ordinary jump")]
    [InlineData("cardinal-ascend", false, true, "the climb lift reaches 1.2520, measured, so the step-off is real")]
    [InlineData("diagonal-ascend", true, true, "a grounded diagonal is a ballistic arc over the corner")]
    [InlineData("diagonal-ascend", false, false, "hanging, there is no arc and the corner cell holds no lift")]
    [InlineData("parkour", true, true, "a grounded sprint jump has its run-up")]
    [InlineData("parkour", false, false, "hanging, handleOnClimbable clamps the run-up to 0.15")]
    public void TheJumpFamilyOffARung(string arm, bool floorUnderTheRung, bool expected, string why)
    {
        CalculationContext ctx = Rung(floorUnderTheRung);
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
    [InlineData(true, true, "standing on the patched floor, the arm is an ordinary sidewall")]
    [InlineData(false, false, "hanging over the hole, there is neither the jump nor the run-up")]
    public void TheSidewallArmOffARung(bool floorUnderTheRung, bool expected, string why)
    {
        var world = new FixtureWorld();
        world.Floor(-6, 8, -6, 8, RungY - 1);
        world.Set(0, RungY, 0, FixtureWorld.Ladder);
        world.Set(1, RungY, 0, FixtureWorld.Stone);
        world.Set(0, RungY - 1, 0, floorUnderTheRung ? FixtureWorld.Stone : FixtureWorld.Air);

        CalculationContext ctx = FixtureContext.Around(world, 0, RungY, 0, margin: 16);

        Assert.Equal(expected, Possible(ctx, MoveJump.Sidewall(1, 2)));
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>The classifier itself, so a later reader can see which half of it did the work. A cell that is climbable is not enough and a cell with no floor is not enough; it is the pair.</summary>
    [Theory]
    [InlineData(FixtureWorld.Ladder, FixtureWorld.Air, true, "the rung the owner's bot hung on")]
    [InlineData(FixtureWorld.Ladder, FixtureWorld.Stone, false, "standing on stone with a ladder at the feet")]
    [InlineData(FixtureWorld.Air, FixtureWorld.Air, false, "unsupported, but nothing to hang FROM")]
    [InlineData(FixtureWorld.Scaffolding, FixtureWorld.Scaffolding, false, "a scaffold's own top plate is a floor")]
    [InlineData(FixtureWorld.Ladder, FixtureWorld.Ladder, true, "a rung above a rung is still a rung")]
    [InlineData(FixtureWorld.WaterloggedLadder, FixtureWorld.Air, false, "in water the body swims, it does not hang")]
    [InlineData(FixtureWorld.WaterloggedLadder, FixtureWorld.Water, false, "the same, with the column flooded under it")]
    public void IsHangingOnAClimbable_IsThePairOfTests(int feet, int below, bool expected, string why)
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, RungY - 4);
        world.Set(0, RungY, 0, feet);
        world.Set(0, RungY - 1, 0, below);

        CalculationContext ctx = FixtureContext.Around(world, 0, RungY, 0, margin: 12);

        Assert.Equal(expected, MoveHelper.IsHangingOnAClimbable(ctx, 0, RungY, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }
}
