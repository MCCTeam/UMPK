using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class DemandFaultedRegionPlanningTests
{
    private const int FloorY = 64;
    private const int Margin = 24;

    private static void AssertIdenticalPlans(FixtureWorld world, BlockPos start, BlockPos goal, PathfinderOptions options)
    {
        PlanningWorldView eagerView = world.Capture(start, goal, Margin);
        PlanningWorldView lazyView = world.CaptureOnDemand(start, goal, Margin);

        Assert.False(eagerView.Region.IsDemandFaulted);
        Assert.True(lazyView.Region.IsDemandFaulted);
        Assert.Equal(0, lazyView.Region.SectionsCaptured);
        Assert.Equal(eagerView.Region.Min, lazyView.Region.Min);
        Assert.Equal(eagerView.Region.CellCount, lazyView.Region.CellCount);

        PathResult eager = PathPlanner.FindPath(eagerView, options, start, new GoalBlock(goal));
        PathResult lazy = PathPlanner.FindPath(lazyView, options, start, new GoalBlock(goal));

        Assert.Equal(eager.Status, lazy.Status);
        Assert.Equal(eager.Cost, lazy.Cost);
        Assert.Equal(eager.Diagnostics.NodesExplored, lazy.Diagnostics.NodesExplored);
        Assert.Equal(eager.Diagnostics.UnloadedChunkHits, lazy.Diagnostics.UnloadedChunkHits);
        Assert.Equal(eager.Diagnostics.TimedOut, lazy.Diagnostics.TimedOut);
        Assert.Equal(eager.Diagnostics.NodeBudgetExhausted, lazy.Diagnostics.NodeBudgetExhausted);
        Assert.Equal(eager.Path.Count, lazy.Path.Count);

        for (int i = 0; i < eager.Path.Count; i++)
        {
            PathNode a = eager.Path[i];
            PathNode b = lazy.Path[i];
            if (a.X != b.X || a.Y != b.Y || a.Z != b.Z || a.MoveUsed != b.MoveUsed || a.GCost != b.GCost)
                Assert.Fail(
                    $"node {i} of {eager.Path.Count} differs: "
                    + $"captured ({a.X},{a.Y},{a.Z}) {a.MoveUsed} g={a.GCost} vs "
                    + $"demand-faulted ({b.X},{b.Y},{b.Z}) {b.MoveUsed} g={b.GCost}");

        }

        // The whole point: the demand-faulted view materialised strictly less of the box.
        Assert.True(
            lazyView.Region.SectionsCaptured <= eagerView.Region.SectionsCaptured,
            $"demand faulting copied {lazyView.Region.SectionsCaptured} sections against the capture's "
                + $"{eagerView.Region.SectionsCaptured}");
    }

    private static FixtureWorld Runway(int spanX, int spanZ)
    {
        var world = new FixtureWorld();
        world.Floor(-4, spanX + 4, -4, spanZ + 4, FloorY);
        return world;
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(100, 0)]
    [InlineData(500, 0)]
    [InlineData(60, 60)]
    public void StraightAndDiagonalRoutes_PlanIdenticallyThroughBothViews(int spanX, int spanZ)
    {
        FixtureWorld world = Runway(spanX, spanZ);
        AssertIdenticalPlans(
            world,
            new BlockPos(0, FloorY + 1, 0),
            new BlockPos(spanX, FloorY + 1, spanZ),
            PathfinderOptions.Default);
    }

    /// <summary>A route over broken ground: step-ups, a drop, and a wall to go round.</summary>
    [Fact]
    public void BrokenTerrain_PlansIdenticallyThroughBothViews()
    {
        FixtureWorld world = Runway(80, 8);
        world.Fill(20, FloorY + 1, -4, 24, FloorY + 1, 12, FixtureWorld.Stone);
        world.Fill(40, FloorY, -4, 44, FloorY, 12, FixtureWorld.Air);
        world.Fill(40, FloorY - 2, -4, 44, FloorY - 2, 12, FixtureWorld.Stone);
        world.Fill(60, FloorY + 1, -4, 60, FloorY + 4, 6, FixtureWorld.Stone);

        AssertIdenticalPlans(
            world,
            new BlockPos(0, FloorY + 1, 0),
            new BlockPos(80, FloorY + 1, 8),
            PathfinderOptions.Default);
    }

    /// <summary>Water puts both the swim family and breath dimension in the comparison. Breath checks cells one block above the movement cells, exercising reads across the section boundary.</summary>
    [Fact]
    public void FloodedChannel_PlansIdenticallyThroughBothViews()
    {
        FixtureWorld world = Runway(40, 4);
        world.Fill(8, FloorY, -2, 30, FloorY, 4, FixtureWorld.Air);
        world.Fill(8, FloorY - 4, -2, 30, FloorY - 4, 4, FixtureWorld.Stone);
        world.Fill(8, FloorY - 3, -2, 30, FloorY + 1, 4, FixtureWorld.Water);

        AssertIdenticalPlans(
            world,
            new BlockPos(0, FloorY + 1, 0),
            new BlockPos(40, FloorY + 1, 4),
            PathfinderOptions.Default);
    }

    [Fact]
    public void SealedGoal_ExploresTheSameNodesThroughBothViews()
    {
        FixtureWorld world = Runway(70, 28);
        world.Fill(66, FloorY + 1, 10, 66, FloorY + 4, 18, FixtureWorld.Stone);
        world.Fill(74, FloorY + 1, 10, 74, FloorY + 4, 18, FixtureWorld.Stone);
        world.Fill(66, FloorY + 1, 10, 74, FloorY + 4, 10, FixtureWorld.Stone);
        world.Fill(66, FloorY + 1, 18, 74, FloorY + 4, 18, FixtureWorld.Stone);
        world.Fill(66, FloorY + 5, 10, 74, FloorY + 5, 18, FixtureWorld.Stone);

        PlanningWorldView eagerView = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(70, FloorY + 1, 14), Margin);
        PlanningWorldView lazyView = world.CaptureOnDemand(new BlockPos(0, FloorY + 1, 0), new BlockPos(70, FloorY + 1, 14), Margin);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new GoalBlock(new BlockPos(70, FloorY + 1, 14));
        PathResult eager = PathPlanner.FindPath(eagerView, PathfinderOptions.Default, start, goal);
        PathResult lazy = PathPlanner.FindPath(lazyView, PathfinderOptions.Default, start, goal);

        Assert.NotEqual(PathStatus.Success, eager.Status);
        Assert.Equal(eager.Status, lazy.Status);
        Assert.Equal(eager.Cost, lazy.Cost);
        Assert.Equal(eager.Path.Count, lazy.Path.Count);
        Assert.Equal(eager.Diagnostics.NodesExplored, lazy.Diagnostics.NodesExplored);
        Assert.True(eager.Diagnostics.NodesExplored > 1000, $"the sealed shape only explored {eager.Diagnostics.NodesExplored} nodes");
    }

    /// <summary>A demand-faulted region is a copy from the moment it touches a section, not a window onto the world: once a section has been read, a later write to it is invisible, exactly as it is to a full capture. This is the property the executor depends on when it forward-simulates against the terrain the plan was made against.</summary>
    [Fact]
    public void ADemandFaultedRegion_FreezesEachSectionAtItsFirstRead()
    {
        FixtureWorld world = Runway(8, 8);
        PlanningWorldView view = world.CaptureOnDemand(new BlockPos(0, FloorY + 1, 0), new BlockPos(8, FloorY + 1, 8), Margin);

        Assert.True(view.GetBlock(new BlockPos(4, FloorY, 4)).BlocksMotion);
        Assert.Equal(1, view.Region.SectionsCaptured);

        world.Set(4, FloorY, 4, FixtureWorld.Air);

        Assert.True(view.GetBlock(new BlockPos(4, FloorY, 4)).BlocksMotion);
        Assert.Equal(1, view.Region.SectionsCaptured);
    }
}
