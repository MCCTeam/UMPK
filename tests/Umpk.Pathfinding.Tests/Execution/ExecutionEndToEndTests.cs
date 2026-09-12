using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ExecutionEndToEndTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private static IReadOnlyList<PathSegment> Plan(PlanningWorldView view, PathfinderOptions options, BlockPos start, BlockPos goal)
    {
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        return PathSegmentBuilder.FromPath(result.Path);
    }

    private static Vec3d Center(BlockPos p) => new(p.X + 0.5, p.Y, p.Z + 0.5);

    [Fact]
    public void Walk_DrivesEngineToTheGoal()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 16, -8, 8, FloorY);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(6, FloorY + 1, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start));
        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        AssertNear(driver.State.Position, Center(goal), horiz: 1.0, vert: 1.0);
    }

    [Fact]
    public void Ascend_ClimbsAStaircaseToTheGoal()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 4, FloorY);
        // A one-block step up at x=3.
        world.Fill(3, FloorY + 1, -4, 8, FloorY + 1, 4, FixtureWorld.Stone);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(6, FloorY + 2, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start));
        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        AssertNear(driver.State.Position, Center(goal), horiz: 1.2, vert: 1.2);
    }

    [Fact]
    public void Descend_DropsToTheLowerGoal()
    {
        var world = new FixtureWorld();
        // Upper shelf x<=2, lower floor x>=3.
        world.Fill(-4, FloorY + 1, -4, 2, FloorY + 1, 4, FixtureWorld.Stone);
        world.Fill(3, FloorY, -4, 12, FloorY, 4, FixtureWorld.Stone);

        var start = new BlockPos(0, FloorY + 2, 0);
        var goal = new BlockPos(6, FloorY + 1, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start));
        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        AssertNear(driver.State.Position, Center(goal), horiz: 1.5, vert: 1.5);
    }

    [Fact]
    public void Swim_DrivesThroughDeepWaterToTheGoal()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, 0, 6, FloorY + 3, 0, FixtureWorld.Water);

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(6, FloorY + 2, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);
        Assert.Contains(segments, s => s.MoveType == MoveType.Swim);

        var driver = new ExecutionDriver(ctx, segments, Center(start), startYaw: 90f);
        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        // Swimmers complete on a position envelope; the horizontal target must be met.
        double dx = driver.State.Position.X - Center(goal).X;
        double dz = driver.State.Position.Z - Center(goal).Z;
        Assert.True((dx * dx) + (dz * dz) < 2.25, $"swimmer ended at {driver.State.Position}");
    }

    private static void AssertNear(Vec3d actual, Vec3d expected, double horiz, double vert)
    {
        double dx = actual.X - expected.X;
        double dz = actual.Z - expected.Z;
        Assert.True((dx * dx) + (dz * dz) <= horiz * horiz, $"horizontal miss: actual {actual}, expected {expected}");
        Assert.True(Math.Abs(actual.Y - expected.Y) <= vert, $"vertical miss: actual {actual}, expected {expected}");
    }
}
