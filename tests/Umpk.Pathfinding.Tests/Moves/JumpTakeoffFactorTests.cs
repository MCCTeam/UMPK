using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class JumpTakeoffFactorTests
{
    /// <summary>The course's own plane: support blocks at 99, a body at 100.</summary>
    private const int FloorY = 99;

    private const int FeetY = FloorY + 1;

    /// <summary>The fixture mirrors the dataset: honey 0.5, and nothing else moves.</summary>
    [Theory]
    [InlineData(FixtureWorld.Honey, 0.5f)]
    [InlineData(FixtureWorld.SoulSand, 1.0f)]
    [InlineData(FixtureWorld.Stone, 1.0f)]
    [InlineData(FixtureWorld.Carpet, 1.0f)]
    public void TheFixtureCarriesTheDatasetsJumpFactors(int stateId, float expected)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(expected, ctx.GetBlock(0, FloorY, 0).JumpFactor, 3);
    }

    /// <summary>The gate itself, over both cells. The carpet rows are the point: a carpet laid over honey is the real vanilla trick for keeping a body's feet off the honey's own box, and it does NOT give the jump back.</summary>
    [Theory]
    [InlineData(FixtureWorld.Stone, FixtureWorld.Stone, true, "open ground")]
    [InlineData(FixtureWorld.SoulSand, FixtureWorld.Stone, true, "soul sand slows a walk and does nothing to a jump")]
    [InlineData(FixtureWorld.Honey, FixtureWorld.Stone, false, "feet inside the honey at 0.9375")]
    [InlineData(FixtureWorld.Carpet, FixtureWorld.Honey, false, "carpet reads 1.0, the fall-through finds the honey")]
    [InlineData(FixtureWorld.Carpet, FixtureWorld.Stone, true, "carpet reads 1.0, and so does the stone under it")]
    [InlineData(FixtureWorld.BottomSlab, FixtureWorld.Honey, false, "a slab is half a block, so the feet are still in its cell")]
    [InlineData(FixtureWorld.Air, FixtureWorld.Air, true, "nothing underfoot at all")]
    public void CanTakeOffForAJump_ReadsTheFeetCellThenTheOneBelow(int floor, int under, bool expected, string why)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY - 1, 0, under);
        world.Set(0, FloorY, 0, floor);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(expected, MoveHelper.CanTakeOffForAJump(ctx, 0, FeetY, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>Course row <b>J8 honeystep</b>: six cells of honey abutting a +1 step. The rise a honey cell really presents is 101 - 99.9375 = 1.0625, and the apex out of one is 0.383852, so the step is unreachable and the plan must say so up front rather than walk the bot into it.</summary>
    [Fact]
    public void J8_HoneyToStep_Refuses_AndSaysWhy()
    {
        PathResult result = StepBeyondALane(FixtureWorld.Honey, carpeted: false);

        Assert.NotEqual(PathStatus.Success, result.Status);
        Assert.True(
            result.Diagnostics.SlowJumpFloorTakeoffsRefused > 0,
            "the refusal has to name the takeoff gate, or it is indistinguishable from an unreachable goal.");
    }

    /// <summary>The control that makes J8 a statement about honey: the identical shape over stone is planned, through an <see cref="MoveType.Ascend"/>, with the gate never firing.</summary>
    [Fact]
    public void TheSameStepOverStone_IsPlanned()
    {
        PathResult result = StepBeyondALane(FixtureWorld.Stone, carpeted: false);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(MoveType.Ascend, result.Moves);
        Assert.Equal(0, result.Diagnostics.SlowJumpFloorTakeoffsRefused);
    }

    /// <summary>Course row <b>J10 soulsandstep</b>: the SAME shape in soul sand, which must keep planning.</summary>
    /// <remarks>Soul sand carries no jump factor at all, so the apex is the ordinary 1.2522 and the rise it has to clear - 101 - 99.875 = 1.125 - fits under it. M11b drove exactly this shape and clocked the arrival at tick 45, reached after three run-ups rather than one clean jump; slower is not refused. J8 and J10 differ ONLY in the jump factor - both floors are speed factor 0.4 - so a gate keyed on the speed factor, or on "is this a partial support", would take J10 out with J8.</remarks>
    [Fact]
    public void J10_SoulSandToStep_StillPlans()
    {
        PathResult result = StepBeyondALane(FixtureWorld.SoulSand, carpeted: false);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(MoveType.Ascend, result.Moves);
        Assert.Equal(0, result.Diagnostics.SlowJumpFloorTakeoffsRefused);
    }

    /// <summary>The <b>J9</b> decision - carpet over honey - at the planner level, over a GAP, which is where a carpeted honey lane still meets the jump family.</summary>
    /// <remarks>A parkour takeoff rather than a step, for a geometric reason: a carpet lane rests at <c>y + 0.0625</c>, so the next node up from it is a 1.9375 rise that nothing in the game clears, and no carpeted lane has a plannable ascend to gate. A gap does not have that problem - takeoff and landing are the same carpeted plane - so it isolates the two-cell read cleanly, and the control differs in exactly one cell per column, the one the feet are NOT in.</remarks>
    [Fact]
    public void J9_ParkourFromCarpetOverHoney_Refuses_AndSaysWhy()
    {
        PathResult overHoney = JumpAGapFromACarpetedLane(FixtureWorld.Honey);
        PathResult overStone = JumpAGapFromACarpetedLane(FixtureWorld.Stone);

        Assert.Equal(PathStatus.Success, overStone.Status);
        Assert.Contains(MoveType.Parkour, overStone.Moves);
        Assert.Equal(0, overStone.Diagnostics.SlowJumpFloorTakeoffsRefused);

        Assert.NotEqual(PathStatus.Success, overHoney.Status);
        Assert.True(overHoney.Diagnostics.SlowJumpFloorTakeoffsRefused > 0);
    }

    /// <summary>A jump is refused; a STEP is not. The 0.6 auto-step needs no jump power at all, so a body on honey still walks up a slab kerb - which is what keeps the gate a takeoff rule rather than a blanket "honey is impassable".</summary>
    [Fact]
    public void AWalkedStepOutOfHoney_IsStillPlanned()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, -1, 4, FloorY, 1, FixtureWorld.Stone);
        world.Fill(2, FloorY, -1, 4, FloorY, 1, FixtureWorld.Honey);

        // A bottom slab on the far bank: its top is 0.5, and the honey rests the body at 0.9375, so the body steps DOWN 0.4375 - inside the auto-step, and no jump anywhere in it.
        world.Fill(5, FloorY, -1, 7, FloorY, 1, FixtureWorld.Stone);
        world.Fill(5, FloorY + 1, -1, 7, FloorY + 1, 1, FixtureWorld.BottomSlab);

        var start = new BlockPos(0, FeetY, 0);
        var goal = new BlockPos(6, FeetY + 1, 0);
        PathResult result = PathPlanner.FindPath(
            world.Capture(start, goal, margin: 8), PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
    }

    [Fact]
    public void J9sLiteralGeometry_IsRefused_BecauseALevelWalkComparesElevations()
    {
        PathResult overHoney = StepBeyondALane(FixtureWorld.Honey, carpeted: true);
        PathResult overStone = StepBeyondALane(FixtureWorld.Stone, carpeted: true);

        Assert.Equal(PathStatus.Failed, overHoney.Status);
        Assert.Equal(PathStatus.Success, overStone.Status);

        // takeoff rules were therefore never consulted on it.
        Assert.Contains(MoveType.Ascend, overStone.Moves);

        // The gate decided the honey run. Over stone nothing slow-floored exists to refuse.
        Assert.True(overHoney.Diagnostics.SlowJumpFloorTakeoffsRefused > 0);
        Assert.Equal(0, overStone.Diagnostics.SlowJumpFloorTakeoffsRefused);
    }

    /// <summary>Course rows J8 / J9 / J10: a six-cell lane of one floor family abutting a one-block step, with the goal on top of the step.</summary>
    private static PathResult StepBeyondALane(int laneState, bool carpeted)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, -1, 6, FloorY, 1, FixtureWorld.Stone);
        world.Fill(3, FloorY, -1, 6, FloorY, 1, laneState);
        if (carpeted)
            world.Fill(3, FloorY + 1, -1, 6, FloorY + 1, 1, FixtureWorld.Carpet);

        world.Fill(7, FloorY + 1, -1, 9, FloorY + 1, 1, FixtureWorld.Stone);

        var start = new BlockPos(0, FeetY, 0);
        var goal = new BlockPos(8, FeetY + 1, 0);
        return PathPlanner.FindPath(
            world.Capture(start, goal, margin: 8), PathfinderOptions.Default, start, new GoalBlock(goal));
    }

    /// <summary>A carpeted lane, a two-cell gap, and a carpeted landing on the same plane: the shape a takeoff gate and nothing else decides.</summary>
    private static PathResult JumpAGapFromACarpetedLane(int underState)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, -1, 4, FloorY, 1, underState);
        world.Fill(0, FloorY + 1, -1, 4, FloorY + 1, 1, FixtureWorld.Carpet);
        world.Fill(7, FloorY, -1, 10, FloorY, 1, FixtureWorld.Stone);
        world.Fill(7, FloorY + 1, -1, 10, FloorY + 1, 1, FixtureWorld.Carpet);

        var start = new BlockPos(0, FeetY + 1, 0);
        var goal = new BlockPos(9, FeetY + 1, 0);
        return PathPlanner.FindPath(
            world.Capture(start, goal, margin: 8), PathfinderOptions.Default, start, new GoalBlock(goal));
    }
}
