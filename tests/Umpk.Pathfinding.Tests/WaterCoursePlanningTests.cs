using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class WaterCoursePlanningTests
{
    private const int FloorY = 64;

    private static bool UsesSwim(PathResult result)
    {
        foreach (MoveType move in result.Moves)
            if (move == MoveType.Swim)
                return true;

        return false;
    }

    [Fact]
    public void ShallowCorridor_WadesThroughReachingTheGoal()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 4, FloorY);
        // One-block-deep channel (walkable floor under the water): waded, not swum.
        world.Fill(1, FloorY + 1, 0, 8, FloorY + 1, 0, FixtureWorld.Water);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(9, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode end = result.Path[^1];
        Assert.Equal(goal.X, end.X);
    }

    [Fact]
    public void DeepCorridor_SwimsThroughAFloodedChannel()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        // Deep water: 3-deep column x=1..8, so at the surface Y there is no walkable floor within reach.
        world.Fill(1, FloorY + 1, 0, 8, FloorY + 3, 0, FixtureWorld.Water);

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(8, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(UsesSwim(result), "a submerged corridor should require swim moves");
    }

    [Fact]
    public void DeepCorridor_DisabledWhenSwimNotAllowed()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, 0, 8, FloorY + 3, 0, FixtureWorld.Water);

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(8, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        var noSwim = PathfinderOptions.Default with { AllowSwim = false };
        PathResult result = PathPlanner.FindPath(view, noSwim, start, new GoalBlock(goal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    [Fact]
    public void DiveToDepth_ReachesASubmergedGoal()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 8, -4, 4, FloorY);
        // A 4-deep water column at x=1 and x=2.
        world.Fill(1, FloorY + 1, 0, 2, FloorY + 4, 0, FixtureWorld.Water);

        var start = new BlockPos(1, FloorY + 4, 0);
        var goal = new BlockPos(2, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(UsesSwim(result));
    }

    [Fact]
    public void SurfaceCrossing_SwimsAcrossADeepPool()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -6, 6, FloorY);
        // A wide, deep pool (2-deep) so crossing at the surface Y still swims.
        world.Fill(1, FloorY + 1, -1, 6, FloorY + 2, 1, FixtureWorld.Water);

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(6, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(UsesSwim(result));
    }

    [Fact]
    public void WaterExitOntoLand_SwimsThenClimbsOutToDryGoal()
    {
        var world = new FixtureWorld();
        // Dry land on both sides; a water-filled hole x=1..4 that is deep (bottom three below the rim).
        world.Floor(-4, 0, -4, 4, FloorY);
        world.Floor(5, 14, -4, 4, FloorY);
        world.Floor(1, 4, -4, 4, FloorY - 3);
        world.Fill(1, FloorY - 2, 0, 4, FloorY, 0, FixtureWorld.Water);

        var start = new BlockPos(2, FloorY - 1, 0); // submerged in the hole
        var goal = new BlockPos(9, FloorY + 1, 0);  // dry land past the pool
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(UsesSwim(result), "the route out of the pool should use swim moves");
        PathNode end = result.Path[^1];
        Assert.Equal(goal.X, end.X);
        Assert.Equal(goal.Z, end.Z);
    }

    [Fact]
    public void MixedLandWaterRoute_AlternatesWalkAndSwim()
    {
        var world = new FixtureWorld();
        // Dry land segments with a deep water-filled hole between x=2..4.
        world.Floor(-4, 1, -4, 4, FloorY);
        world.Floor(5, 14, -4, 4, FloorY);
        world.Floor(2, 4, -4, 4, FloorY - 3);
        world.Fill(2, FloorY - 2, 0, 4, FloorY, 0, FixtureWorld.Water);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(10, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        // Disable parkour so the water hole must be swum rather than jumped across.
        var options = PathfinderOptions.Default with { AllowParkour = false, AllowParkourAscend = false };
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(UsesSwim(result), "expected the water hole to be crossed with swim moves");

        bool sawWalk = false;
        foreach (MoveType move in result.Moves)
            if (move is MoveType.Traverse or MoveType.Diagonal)
            {
                sawWalk = true;
                break;
            }

        Assert.True(sawWalk, "expected the mixed route to include walk moves on the dry sections");
    }
}
