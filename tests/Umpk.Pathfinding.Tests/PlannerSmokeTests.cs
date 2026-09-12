using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>End-to-end smoke and determinism coverage for the session-free planner over fixture worlds.</summary>
public sealed class PlannerSmokeTests
{
    private const int FloorY = 64;

    private static PlanningWorldView FlatView(FixtureWorld world, BlockPos a, BlockPos b)
    {
        world.Floor(-16, 16, -16, 16, FloorY);
        return world.Capture(a, b, margin: 8);
    }

    [Fact]
    public void FlatWalk_FindsStraightLinePath()
    {
        var world = new FixtureWorld();
        var start = new BlockPos(0, FloorY + 1, 0);
        var goalPos = new BlockPos(6, FloorY + 1, 0);
        PlanningWorldView view = FlatView(world, start, goalPos);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goalPos));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(result.Path.Count >= 2);
        PathNode end = result.Path[^1];
        Assert.Equal(goalPos.X, end.X);
        Assert.Equal(goalPos.Z, end.Z);
    }

    [Fact]
    public void ReplanFromMidAir_PlansFromWhereTheBodyWillLand()
    {
        var world = new FixtureWorld();
        var goalPos = new BlockPos(6, FloorY + 1, 0);
        PlanningWorldView view = FlatView(world, new BlockPos(0, FloorY + 9, 0), goalPos);

        // Eight blocks up, nothing under it: a body in free fall over the floor it is going to land on.
        var falling = new BlockPos(0, FloorY + 9, 0);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, falling, new GoalBlock(goalPos));

        Assert.True(
            result.Status == PathStatus.Success,
            $"a replan from a body eight blocks into a fall returned {result.Status} after "
            + $"{result.NodesExplored} nodes");
        PathNode first = result.Path[0];
        Assert.Equal(FloorY + 1, first.Y);
        PathNode end = result.Path[^1];
        Assert.Equal(goalPos.X, end.X);
        Assert.Equal(goalPos.Z, end.Z);
    }

    /// <summary>GUARD: a start IN water is a swimmer, not a faller, and must not be dragged to the bed. The water move family plans from exactly such cells.</summary>
    [Fact]
    public void AStartInWater_IsNotResolvedDownwards()
    {
        var world = new FixtureWorld();
        world.Floor(-16, 16, -16, 16, FloorY);
        world.Fill(-4, FloorY + 1, -4, 12, FloorY + 6, 4, FixtureWorld.Water);
        var start = new BlockPos(0, FloorY + 5, 0);
        var goalPos = new BlockPos(8, FloorY + 5, 0);
        PlanningWorldView view = world.Capture(start, goalPos, margin: 8);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goalPos));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(FloorY + 5, result.Path[0].Y);
    }

    [Fact]
    public void AlreadyAtGoal_ReturnsSingleNode()
    {
        var world = new FixtureWorld();
        var start = new BlockPos(0, FloorY + 1, 0);
        PlanningWorldView view = FlatView(world, start, start);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(start));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Single(result.Path);
    }

    [Fact]
    public void SameInputs_ProduceIdenticalPaths()
    {
        var world = new FixtureWorld();
        var start = new BlockPos(0, FloorY + 1, 0);
        var goalPos = new BlockPos(8, FloorY + 1, 3);
        PlanningWorldView view = FlatView(world, start, goalPos);

        PathResult first = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goalPos));
        PathResult second = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goalPos));

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Path.Count, second.Path.Count);
        for (int i = 0; i < first.Path.Count; i++)
        {
            Assert.Equal(first.Path[i].X, second.Path[i].X);
            Assert.Equal(first.Path[i].Y, second.Path[i].Y);
            Assert.Equal(first.Path[i].Z, second.Path[i].Z);
            Assert.Equal(first.Path[i].MoveUsed, second.Path[i].MoveUsed);
        }
    }

    [Fact]
    public void NodeBudget_ExhaustsAndReportsPartialOrFail()
    {
        var world = new FixtureWorld();
        var start = new BlockPos(0, FloorY + 1, 0);
        var goalPos = new BlockPos(15, FloorY + 1, 15);
        PlanningWorldView view = FlatView(world, start, goalPos);

        var tinyBudget = PathfinderOptions.Default with { MaxNodes = 3 };
        PathResult result = PathPlanner.FindPath(view, tinyBudget, start, new GoalBlock(goalPos));

        Assert.NotEqual(PathStatus.Success, result.Status);
        Assert.True(result.Diagnostics.NodeBudgetExhausted);
    }
}
