using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class BubbleColumnPlanningTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    private static FixtureWorld E12World()
    {
        var world = new FixtureWorld();
        world.Fill(712, 99, 712, 716, 117, 716, FixtureWorld.Stone);
        world.Fill(714, 100, 714, 714, 114, 714, FixtureWorld.BubbleColumnUp);
        world.Fill(715, 100, 714, 716, 101, 714, FixtureWorld.Air);
        world.Fill(715, 115, 714, 716, 116, 714, FixtureWorld.Air);
        world.Fill(714, 115, 714, 714, 116, 714, FixtureWorld.Air);
        world.Fill(717, 99, 713, 721, 99, 715, FixtureWorld.Stone);
        world.Fill(717, 100, 713, 721, 102, 713, FixtureWorld.Stone);
        world.Fill(717, 100, 715, 721, 102, 715, FixtureWorld.Stone);
        world.Fill(721, 100, 713, 721, 102, 715, FixtureWorld.Stone);
        world.Fill(717, 114, 713, 721, 114, 715, FixtureWorld.Stone);
        return world;
    }

    /// <summary>E13's world: the same shaft with a MAGMA base, i.e. <c>drag=true</c> all the way up.</summary>
    private static FixtureWorld E13World()
    {
        var world = new FixtureWorld();
        world.Fill(776, 99, 712, 780, 117, 716, FixtureWorld.Stone);
        world.Fill(778, 100, 714, 778, 114, 714, FixtureWorld.BubbleColumnDown);
        world.Fill(779, 100, 714, 780, 101, 714, FixtureWorld.Air);
        world.Fill(779, 115, 714, 780, 116, 714, FixtureWorld.Air);
        world.Fill(778, 115, 714, 778, 116, 714, FixtureWorld.Air);
        world.Fill(781, 99, 713, 785, 99, 715, FixtureWorld.Stone);
        world.Fill(781, 100, 713, 785, 102, 713, FixtureWorld.Stone);
        world.Fill(781, 100, 715, 785, 102, 715, FixtureWorld.Stone);
        world.Fill(785, 100, 713, 785, 102, 715, FixtureWorld.Stone);
        world.Fill(781, 114, 713, 785, 114, 715, FixtureWorld.Stone);
        return world;
    }

    /// <summary>A bubble column is water, and the plan may swim into it.</summary>
    [Fact]
    public void ABubbleColumn_IsWaterToThePlanner()
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.BubbleColumnUp);
        world.Set(0, 65, 0, FixtureWorld.BubbleColumnUp);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.True(MoveHelper.IsWater(ctx.GetBlock(0, 64, 0)));
        Assert.True(MoveHelper.CanWalkThrough(ctx, 0, 64, 0));
        Assert.True(MoveHelper.CanTraverseWater(ctx, 0, 64, 0));
        Assert.True(MoveHelper.IsUpwardBubbleColumn(ctx.GetBlock(0, 64, 0)));
    }

    /// <summary>The exemption, both ways round. A body whose eye cell is a bubble column is NOT submerged for the breath model; the same body one cell into plain water is.</summary>
    [Fact]
    public void ABubbleColumnEyeCell_DoesNotDrainAir()
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.BubbleColumnUp);
        world.Set(0, 65, 0, FixtureWorld.BubbleColumnUp);
        world.Set(4, 64, 0, FixtureWorld.Water);
        world.Set(4, 65, 0, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(0, 64, 0), new BlockPos(4, 65, 0), margin: 8);

        Assert.False(BreathModel.IsSubmerged(view, 0, 64, 0));
        Assert.True(BreathModel.IsSubmerged(view, 4, 64, 0));
    }

    /// <summary>A DOWNWARD column is still water and still gets no lift: the same exemption applies (vanilla's clause names the block, not the direction), and the ascent through it is priced as an ordinary swim.</summary>
    [Fact]
    public void ADownwardColumn_IsWaterWithNoLift()
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.BubbleColumnDown);
        world.Set(0, 65, 0, FixtureWorld.BubbleColumnDown);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.True(MoveHelper.IsWater(ctx.GetBlock(0, 64, 0)));
        Assert.True(MoveHelper.IsBubbleColumn(ctx.GetBlock(0, 64, 0)));
        Assert.False(MoveHelper.IsUpwardBubbleColumn(ctx.GetBlock(0, 64, 0)));
    }

    /// <summary>E12 plans: the elevator is a route.</summary>
    [Fact]
    public void E12_Plans()
    {
        FixtureWorld world = E12World();
        var start = new BlockPos(719, 100, 714);
        var goal = new BlockPos(719, 115, 714);
        PlanningWorldView view = world.Capture(start, goal, margin: 20);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((goal.X, goal.Y, goal.Z), (last.X, last.Y, last.Z));
    }

    /// <summary>E12 executes: the real engine rides the column and steps out at the top.</summary>
    [Fact]
    public void E12_Executes()
    {
        FixtureWorld world = E12World();
        var start = new BlockPos(719, 100, 714);
        var goal = new BlockPos(719, 115, 714);
        PlanningWorldView view = world.Capture(start, goal, margin: 20);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var exec = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(exec, segments, new Vec3d(719.5, 100, 714.5), 90f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 1500);

        Assert.True(
            state == PathExecutorState.Complete,
            $"E12 ended {state} at {Fmt(driver.State.Position)} after {driver.Trace.Count} ticks");
        Assert.True(
            Math.Abs(driver.State.Position.Y - 115.0) < 0.2,
            $"E12 ended at y = {driver.State.Position.Y:F4}, not on the y=114 platform");
    }

    [Fact]
    public void E12_ExecutesFromTheColumnEdge_WhereTheLiveRunPinnedIt()
    {
        FixtureWorld world = E12World();
        var start = new BlockPos(715, 100, 714);
        var goal = new BlockPos(719, 115, 714);
        PlanningWorldView view = world.Capture(new BlockPos(705, 95, 705), goal, margin: 20);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        PathSegment entry = segments[0];

        // The hints have to say what the transition label cannot: the body will be OFF THE GROUND at the end of this segment, because the column picks it up before its centre is inside the cell.
        Assert.True(entry.ExitHints.AllowUngrounded);
        Assert.False(entry.ExitHints.RequireGrounded);
        Assert.False(entry.ExitHints.RequireStableFooting);
        Assert.False(entry.ExitHints.AllowAirBrake);

        var exec = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(exec, segments, new Vec3d(715.2261, 100.2, 714.5), 90f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 1500);

        Assert.True(
            state == PathExecutorState.Complete,
            $"E12 ended {state} at {Fmt(driver.State.Position)} after {driver.Trace.Count} ticks");
        Assert.True(
            Math.Abs(driver.State.Position.Y - 115.0) < 0.2,
            $"E12 ended at y = {driver.State.Position.Y:F4}, not on the y=114 platform");
    }

    /// <summary>The entry never brakes. A body on the column's edge that is asked to hold <c>Back</c> is being pushed out of the cell it is trying to enter, and the anti-stall creep that exists to break a stalled handoff is short-circuited by the <c>HoldBack</c> arm, so nothing recovers it.</summary>
    [Fact]
    public void E12_TheColumnEntry_NeverHoldsBack()
    {
        FixtureWorld world = E12World();
        var start = new BlockPos(715, 100, 714);
        var goal = new BlockPos(719, 115, 714);
        PlanningWorldView view = world.Capture(new BlockPos(705, 95, 705), goal, margin: 20);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        var exec = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(exec, segments, new Vec3d(715.2261, 100.2, 714.5), 90f, seedAtStartPos: true);
        driver.Run(maxTicks: 1500);

        int back = 0;
        for (int i = 0; i < driver.EmittedInputs.Count && i < driver.Trace.Count; i++)
            if (driver.Trace[i].SegmentIndex == 0 && driver.EmittedInputs[i].Back)
                back++;

        Assert.True(back == 0, $"the column entry held Back on {back} ticks");
    }

    [Fact]
    public void E13_StillHasNoModelledDowndraft()
    {
        FixtureWorld world = E13World();
        var start = new BlockPos(783, 115, 714);
        var goal = new BlockPos(783, 100, 714);
        PlanningWorldView view = world.Capture(start, goal, margin: 20);

        var ctx = new CalculationContext(view, PathfinderOptions.Default);
        Assert.True(MoveHelper.IsBubbleColumn(ctx.GetBlock(778, 110, 714)));
        Assert.False(
            MoveHelper.IsUpwardBubbleColumn(ctx.GetBlock(778, 110, 714)),
            "the magma column is being treated as a lifting one, which item I did not model");

        // The descent's cost is the ordinary water sink, not a modelled downdraft: that is what "still deferred" means in numbers rather than in prose.
        var down = new Umpk.Pathfinding.Moves.Impl.MoveSwimVertical(up: false);
        var result = default(MoveResult);
        down.Calculate(ctx, 778, 110, 714, ref result);
        Assert.False(result.IsImpossible);
        Assert.Equal(ctx.SwimCostThrough(778, 110, 714, 0, -1, 0), result.Cost, 6);
    }

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");
}
