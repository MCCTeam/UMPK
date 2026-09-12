using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class FallIntoWaterGateTests
{
    private const int StartY = 90;

    /// <summary>Builds a world with a single water cell at <paramref name="waterY"/> under the drop column.</summary>
    private static FixtureWorld ShaftInto(int waterY)
    {
        var world = new FixtureWorld();

        // A stone bed under the water so the column is a pool rather than a void, and walls so the scan cannot wander: only the (0,*,0) column is open.
        world.Fill(-1, waterY - 2, -1, 1, waterY - 2, 1, FixtureWorld.Stone);
        world.Set(0, waterY - 1, 0, FixtureWorld.Water);
        world.Set(0, waterY, 0, FixtureWorld.Water);

        return world;
    }

    private static MoveResult FallFrom(FixtureWorld world, int waterY, PathfinderOptions options)
    {
        CalculationContext ctx = FixtureContext.Build(
            world, new BlockPos(0, StartY, 0), new BlockPos(0, waterY - 4, 0), options, margin: 4);
        var move = new MoveFall();
        var result = default(MoveResult);
        move.Calculate(ctx, 0, StartY, 0, ref result);
        return result;
    }

    [Fact]
    public void Fall_RefusesAWaterLandingBeyondMaxFallHeightIntoWater()
    {
        int waterY = StartY - 25;
        MoveResult result = FallFrom(ShaftInto(waterY), waterY, PathfinderOptions.Default);

        Assert.True(
            result.IsImpossible,
            $"a 25-block drop into water is beyond the default MaxFallHeightIntoWater of 20, "
                + $"but the move landed at y={result.DestY} for {result.Cost} ticks");
    }

    /// <summary>The boundary: exactly the configured height still plans, and lands in the water cell.</summary>
    [Fact]
    public void Fall_AcceptsAWaterLandingExactlyAtMaxFallHeightIntoWater()
    {
        int waterY = StartY - PathfinderOptions.Default.MaxFallHeightIntoWater;
        MoveResult result = FallFrom(ShaftInto(waterY), waterY, PathfinderOptions.Default);

        Assert.False(result.IsImpossible);
        Assert.Equal(0, result.DestX);
        Assert.Equal(waterY, result.DestY);
        Assert.Equal(0, result.DestZ);
    }

    /// <summary>One block past the boundary is the first refusal, so the gate is on the stated number.</summary>
    [Fact]
    public void Fall_RefusesOneBlockPastMaxFallHeightIntoWater()
    {
        int waterY = StartY - PathfinderOptions.Default.MaxFallHeightIntoWater - 1;
        MoveResult result = FallFrom(ShaftInto(waterY), waterY, PathfinderOptions.Default);

        Assert.True(result.IsImpossible);
    }

    /// <summary><see cref="PathfinderOptions.UnsafeFalls"/> still means what it says: the same 25-block drop is planable when the caller lifts the guard.</summary>
    [Fact]
    public void Fall_AcceptsTheSameDropUnderUnsafeFalls()
    {
        int waterY = StartY - 25;
        MoveResult result = FallFrom(ShaftInto(waterY), waterY, PathfinderOptions.UnsafeFalls);

        Assert.False(result.IsImpossible);
        Assert.Equal(waterY, result.DestY);
    }

    /// <summary>A drop the body was never exposed to does not count against the gate: a ladder grab at 20 resets the unprotected height, so a 25-block shaft with a rung in it still plans on the defaults. This is the clause that keeps the gate a FALL-DAMAGE limit rather than a depth limit.</summary>
    [Fact]
    public void Fall_CountsOnlyTheUnprotectedHeight_SoALadderGrabResetsTheGate()
    {
        int waterY = StartY - 25;
        FixtureWorld world = ShaftInto(waterY);
        world.Set(0, StartY - 10, 0, FixtureWorld.Ladder);

        MoveResult result = FallFrom(world, waterY, PathfinderOptions.Default);

        Assert.False(result.IsImpossible);
        Assert.Equal(waterY, result.DestY);
    }

    /// <summary>The passive sink is not a fall and is not gated: a body already in water sinking one cell is charged <c>WaterSinkOneBlock</c>, whatever the fall limits say.</summary>
    [Fact]
    public void Fall_StillSinksOneCellWhenTheDropBeginsInWater()
    {
        var world = new FixtureWorld();
        world.Fill(-1, StartY - 40, -1, 1, StartY - 40, 1, FixtureWorld.Stone);
        world.Fill(0, StartY - 39, 0, 0, StartY, 0, FixtureWorld.Water);

        CalculationContext ctx = FixtureContext.Build(
            world, new BlockPos(0, StartY, 0), new BlockPos(0, StartY - 41, 0), PathfinderOptions.Default, margin: 4);
        var move = new MoveFall();
        var result = default(MoveResult);
        move.Calculate(ctx, 0, StartY, 0, ref result);

        Assert.False(result.IsImpossible);
        Assert.Equal(StartY - 1, result.DestY);
        Assert.Equal(ActionCosts.WaterSinkOneBlock, result.Cost);
    }
}

public sealed class LedgeIntoBasinPlanningTests
{
    private const int LedgeY = 100;

    /// <summary>An upper pad at x -4..0, a water basin at x 1..3 whose surface is flush with the lower platform, and the lower platform out to x 12, all <paramref name="depth"/> blocks down.</summary>
    private static FixtureWorld LedgeAndBasin(int depth)
    {
        int lowerY = LedgeY - depth;
        var world = new FixtureWorld();

        world.Floor(-4, 0, -2, 2, LedgeY);
        world.Floor(4, 12, -2, 2, lowerY);

        // The basin: a stone shell two cells deep, filled so the top water cell sits at the lower platform's own block layer.
        world.Fill(1, lowerY - 3, -2, 3, lowerY - 3, 2, FixtureWorld.Stone);
        world.Fill(1, lowerY - 2, -2, 3, lowerY, -2, FixtureWorld.Stone);
        world.Fill(1, lowerY - 2, 2, 3, lowerY, 2, FixtureWorld.Stone);
        world.Fill(1, lowerY - 2, -1, 3, lowerY, 1, FixtureWorld.Water);

        return world;
    }

    private static PathResult PlanDrop(int depth, PathfinderOptions options)
    {
        FixtureWorld world = LedgeAndBasin(depth);
        var start = new BlockPos(0, LedgeY + 1, 0);
        var goal = new BlockPos(2, LedgeY - depth, 0);
        return PathPlanner.FindPath(world.Capture(start, goal, margin: 6), options, start, new GoalBlock(goal));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(18)]
    public void C6_DropIntoWaterWithinTheLimit_PlansOnTheDefaults(int depth)
        => Assert.Equal(PathStatus.Success, PlanDrop(depth, PathfinderOptions.Default).Status);

    [Fact]
    public void C7_DropOf25IntoWater_IsRefusedOnTheDefaults()
        => Assert.NotEqual(PathStatus.Success, PlanDrop(25, PathfinderOptions.Default).Status);

    [Fact]
    public void C7_DropOf25IntoWater_PlansUnderUnsafeFalls()
        => Assert.Equal(PathStatus.Success, PlanDrop(25, PathfinderOptions.UnsafeFalls).Status);
}
