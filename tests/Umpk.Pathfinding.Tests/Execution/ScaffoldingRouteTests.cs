using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ScaffoldingRouteTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    private static FixtureWorld B11World()
    {
        var world = new FixtureWorld();
        world.Fill(646, 99, 70, 653, 99, 77, FixtureWorld.Stone);
        world.Fill(650, 100, 74, 650, 104, 74, FixtureWorld.Scaffolding);
        return world;
    }

    private static FixtureWorld K4World()
    {
        var world = new FixtureWorld();
        world.Fill(196, 99, 902, 205, 99, 909, FixtureWorld.Stone);
        world.Fill(196, 100, 904, 200, 103, 908, FixtureWorld.Stone);
        world.Fill(202, 100, 906, 202, 103, 906, FixtureWorld.Scaffolding);
        return world;
    }

    /// <summary>B11's plan. The goal stands on the column's top cell, so the whole row turns on whether that cell is a support.</summary>
    [Fact]
    public void B11_ClimbFromTheBase_Plans()
    {
        FixtureWorld world = B11World();
        var start = new BlockPos(647, 100, 74);
        var goal = new BlockPos(650, 105, 74);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((goal.X, goal.Y, goal.Z), (last.X, last.Y, last.Z));
    }

    /// <summary>B11's plan, walked by the real engine.</summary>
    /// <remarks>The arrival band is <b>at or just above</b> the pad rather than exactly on it. The last leg is a climb, and vanilla's climbable lift (<c>(horizontalCollision || jumping) &amp;&amp; onClimbable -&gt; deltaMovement.y = 0.2</c>) is still in the body when the segment completes: measured <c>y = 105.0656</c>, i.e. 0.0656 of residual rise over a pad at 105, with nothing under the feet to fall onto but the scaffolding's own top plate. What must never happen is ending BELOW the pad, which is the shape a body that fell back down the column would have.</remarks>
    [Fact]
    public void B11_ClimbFromTheBase_Executes()
    {
        Run run = Drive(B11World(), new BlockPos(647, 100, 74), new BlockPos(650, 105, 74), new Vec3d(647.5, 100, 74.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"B11 ended {run.State} at {Fmt(run.End)}");
        Assert.True(
            run.End.Y >= 105.0 - 0.01 && run.End.Y <= 105.0 + 0.1,
            $"B11 ended at y = {run.End.Y:F4}, outside the 105.00-105.10 arrival band (measured 105.0656)");
    }

    /// <summary>K4's plan: entry at height across a one-block gap onto the column's top face.</summary>
    [Fact]
    public void K4_LateralEntryAtHeight_Plans()
    {
        FixtureWorld world = K4World();
        var start = new BlockPos(197, 104, 906);
        var goal = new BlockPos(202, 104, 906);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((goal.X, goal.Y, goal.Z), (last.X, last.Y, last.Z));
    }

    /// <summary>K4's plan, walked by the real engine.</summary>
    [Fact]
    public void K4_LateralEntryAtHeight_Executes()
    {
        Run run = Drive(K4World(), new BlockPos(197, 104, 906), new BlockPos(202, 104, 906), new Vec3d(197.5, 104, 906.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"K4 ended {run.State} at {Fmt(run.End)}");
        Assert.True(Math.Abs(run.End.Y - 104.0) < 0.01, $"K4 ended at y = {run.End.Y:F4}, not 104");
    }

    /// <summary>The classification, stated one block at a time so the widening is bounded by evidence rather than by hope. Scaffolding is the ONLY climbable a plan may stand on, and it is standable because of its shape, not because of its name.</summary>
    [Theory]
    [InlineData(FixtureWorld.Scaffolding, true, "the top plate holds a centred footprint at 1.0")]
    [InlineData(FixtureWorld.Ladder, false, "the fixture ladder has no collision shape at all")]
    [InlineData(FixtureWorld.LadderWest, false, "a 3/16 wall plate at x 0.8125-1.0 misses a footprint at 0.2-0.8")]
    public void OnlyScaffolding_IsAStandableClimbable(int stateId, bool standable, string why)
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.True(ctx.GetBlock(0, 64, 0).IsClimbable, "the row's block is not climbable");
        Assert.Equal(standable, MoveHelper.CanWalkOn(ctx, 0, 64, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>A scaffolding cell stays a corridor as well as a floor. Standing ON a scaffold must not turn its column into a wall, or the climb the row's first half needs is refused at the cell it starts in.</summary>
    [Fact]
    public void AScaffoldingCell_IsStillWalkedThrough()
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.Scaffolding);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.True(MoveHelper.CanWalkThrough(ctx, 0, 64, 0));
    }

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");

    private static Run Drive(FixtureWorld world, BlockPos start, BlockPos goal, Vec3d startPos)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, startPos, 270f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 900);
        return new Run(state, driver.State.Position, driver.Trace.Count);
    }

    private readonly record struct Run(PathExecutorState State, Vec3d End, int Ticks);
}
