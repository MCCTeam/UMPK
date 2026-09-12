using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class AutoStepStreetTests
{
    private const int FloorY = 60;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    /// <summary>The M5c street: floor at <c>FloorY</c>, a slab on every odd cell from x=2 to x=14.</summary>
    private static FixtureWorld Street()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 20, -3, 3, FloorY);
        for (int x = 2; x <= 14; x++)
            if (x % 2 == 1)
                world.Fill(x, FloorY + 1, -3, x, FloorY + 1, 3, FixtureWorld.BottomSlab);

        return world;
    }

    [Fact]
    public void TheSlabStreet_PlansAsOneFlatTraverseChain()
    {
        FixtureWorld world = Street();
        var start = new BlockPos(1, FloorY + 1, 0);
        var goal = new BlockPos(9, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(new BlockPos(-6, FloorY - 1, -3), new BlockPos(20, FloorY + 6, 3), margin: 2);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);

        Assert.Equal(8, segments.Count);
        Assert.All(segments, s => Assert.Equal(MoveType.Traverse, s.MoveType));

        // Eight walks, and nothing else: the chain is priced as the flat street it is.
        Assert.Equal(8 * ActionCosts.SprintOneBlock, result.Cost, 1.0E-6);

        // No segment hands off into a jump; every transition remains an ordinary walk.
        Assert.DoesNotContain(segments, s => s.ExitTransition == PathTransitionType.PrepareJump);
    }

    [Fact]
    public void TheSlabStreet_IsWalkedEndToEnd()
    {
        FixtureWorld world = Street();
        var start = new BlockPos(1, FloorY + 1, 0);
        var goal = new BlockPos(9, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(new BlockPos(-6, FloorY - 1, -3), new BlockPos(20, FloorY + 6, 3), margin: 2);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, startYaw: -90f);

        PathExecutorState state = driver.Run(600);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            driver.State.Position.X >= 9.0,
            $"the street ended at x={driver.State.Position.X:0.####}, y={driver.State.Position.Y:0.####}");
    }

    /// <summary>A six-tread staircase of bottom-half stairs, entered from its low side. The raw body walks it in 33 ticks with one airborne tick, compared with 72 ticks and 60 airborne ticks while holding jump. This runs the same route through the planner and executor.</summary>
    [Fact]
    public void AStairRun_IsWalkedEndToEnd()
    {
        var world = new FixtureWorld();
        // Approach from high Z, climbing toward low Z, because the fixture's bottom stair carries its raised half against the low-Z face and is therefore walkable only from the other side.
        world.Fill(-3, FloorY, 0, 3, FloorY, 12, FixtureWorld.Stone);
        for (int k = 0; k < 6; k++)
        {
            int z = 5 - k;
            world.Fill(-3, FloorY, z, 3, FloorY + k, z, FixtureWorld.Stone);
            world.Fill(-3, FloorY + 1 + k, z, 3, FloorY + 1 + k, z, FixtureWorld.StairsBottom);
        }

        world.Fill(-3, FloorY, -6, 3, FloorY + 6, -1, FixtureWorld.Stone);

        var start = new BlockPos(0, FloorY + 1, 8);
        var goal = new BlockPos(0, FloorY + 7, -3);
        PlanningWorldView view = world.Capture(new BlockPos(-3, FloorY - 1, -6), new BlockPos(3, FloorY + 12, 12), margin: 2);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);
        Assert.All(segments, s => Assert.Equal(MoveType.Traverse, s.MoveType));

        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, startYaw: 180f);
        PathExecutorState state = driver.Run(600);

        Assert.Equal(PathExecutorState.Complete, state);
        int airborne = driver.Trace.Count(t => !t.OnGround);
        Assert.True(
            airborne <= 4,
            $"a walked stair run spent {airborne} of {driver.Trace.Count} ticks airborne");
    }

    /// <summary>The watch item, end to end: the same six-tread staircase built out of FULL blocks is still climbed as a chain of <c>Ascend</c>s, because a 1.0 rise is above the auto-step. Course rows B2 <c>stairs10</c> and C9 <c>stairdown</c> are that shape.</summary>
    [Fact]
    public void AFullBlockStaircase_IsStillAChainOfAscends()
    {
        var world = new FixtureWorld();
        world.Fill(-3, FloorY, 0, 3, FloorY, 12, FixtureWorld.Stone);
        for (int k = 0; k < 6; k++)
        {
            int z = 5 - k;
            world.Fill(-3, FloorY, z, 3, FloorY + 1 + k, z, FixtureWorld.Stone);
        }

        world.Fill(-3, FloorY, -6, 3, FloorY + 6, -1, FixtureWorld.Stone);

        var start = new BlockPos(0, FloorY + 1, 8);
        var goal = new BlockPos(0, FloorY + 7, -3);
        PlanningWorldView view = world.Capture(new BlockPos(-3, FloorY - 1, -6), new BlockPos(3, FloorY + 12, 12), margin: 2);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);
        Assert.Equal(6, segments.Count(s => s.MoveType == MoveType.Ascend));
    }
}
