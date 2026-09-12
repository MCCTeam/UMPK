using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class PushedSegmentRegressionTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public PushedSegmentRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AnAscendFallingAwayFromItsEnd_IsAbandonedBeforeItRidesOutOfTheWorld()
    {
        var world = new FixtureWorld();
        // A floating island: stone y=90..96 over x=-4..4, z=-4..4, and nothing at all beyond it.
        world.Fill(-4, 90, -4, 4, 96, 4, FixtureWorld.Stone);
        world.Set(0, 96, 0, FixtureWorld.Water);
        world.Fill(1, 97, -4, 4, 97, 4, FixtureWorld.Stone);

        PlanningWorldView view = world.Capture(new BlockPos(-8, -64, -8), new BlockPos(12, 110, 8), margin: 2);
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = new Vec3d(0.5, 96, 0.5),
                End = new Vec3d(1.5, 98, 0.5),
                MoveType = MoveType.Ascend,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        int budget = ctx.Budget.BudgetFor(segments[0]);
        Assert.True(budget > 90, $"the fixture must reproduce the row's long wet budget; it got {budget}");

        // Seeded where the body actually was, not where the plan says the segment starts.
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(7.5, 96.0, 0.5), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(600);

        double fell = 96.0 - driver.State.Position.Y;
        _output.WriteLine(
            $"{state} after {driver.Trace.Count} ticks at {driver.State.Position}, budget {budget}, fell {fell:F2}");

        Assert.Equal(PathExecutorState.Failed, state);
        Assert.True(
            fell < 40.0,
            $"the ascend rode {fell:F2} blocks down to y={driver.State.Position.Y:F2} over "
            + $"{driver.Trace.Count} ticks before its budget cut it off");
        Assert.True(
            driver.Trace.Count < budget,
            $"the ascend ran its whole {budget}-tick budget ({driver.Trace.Count} ticks), so nothing but "
            + "the budget stopped it");
    }

    [Fact]
    public void ATraverseAcrossASheet_IsHeldOnItsLineInsteadOfBeingCarriedOffIt()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, int budget) = CrossSheetTraverse();

        var driver = new ExecutionDriver(ctx, segments, new Vec3d(4.5, 97, 0.5), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(600);

        double carried = driver.State.Position.X - 4.5;
        _output.WriteLine(
            $"{state} after {driver.Trace.Count} ticks at {driver.State.Position}, budget {budget}, "
            + $"carried {carried:F2}");

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            carried < 0.5,
            $"the traverse was carried {carried:F2} blocks off its own column even with the station "
            + "controller holding it");
    }

    [Fact]
    public void WithTheStationControllerAblated_TheSameTraverseIsCarriedOffAndCutOff()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, int budget) = CrossSheetTraverse();

        var driver = new ExecutionDriver(
            ctx,
            segments,
            new Vec3d(4.5, 97, 0.5),
            0f,
            seedAtStartPos: true,
            station: StationControllerOptions.Disabled);
        PathExecutorState state = driver.Run(600);

        double carried = driver.State.Position.X - 4.5;
        _output.WriteLine(
            $"ABLATED: {state} after {driver.Trace.Count} ticks at {driver.State.Position}, "
            + $"budget {budget}, carried {carried:F2}");

        Assert.Equal(PathExecutorState.Failed, state);
        Assert.True(
            carried > 1.0,
            $"the ablation only let the body drift {carried:F2}, so it did not actually ablate anything");
        Assert.True(
            carried < 3.0,
            $"the traverse was carried {carried:F2} blocks off its own column over {driver.Trace.Count} "
            + "ticks before its budget cut it off");
        Assert.True(
            driver.Trace.Count < budget,
            $"the traverse ran its whole {budget}-tick budget ({driver.Trace.Count} ticks), so nothing "
            + "but the budget stopped it");
    }

    /// <summary>E8's shape: a one-block <c>Traverse</c> in +Z across a two-deep sheet running in +X.</summary>
    private static (PathExecutionContext Ctx, IReadOnlyList<PathSegment> Segments, int Budget) CrossSheetTraverse()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 96, -6, 30, 96, 6, FixtureWorld.Stone);
        world.Fill(-4, 97, -6, 30, 101, 6, FixtureWorld.Air);
        for (int z = -6; z <= 6; z++)
            world.FlowingRun(0, 98, z, length: 26, stepX: 1, stepZ: 0, layers: 2);

        PlanningWorldView view = world.Capture(new BlockPos(-6, 92, -8), new BlockPos(32, 104, 8), margin: 2);
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = new Vec3d(4.5, 97, 0.5),
                End = new Vec3d(4.5, 97, 1.5),
                MoveType = MoveType.Traverse,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        return (ctx, segments, ctx.Budget.BudgetFor(segments[0]));
    }

    [Theory]
    [InlineData("walk", 29)]
    [InlineData("ascend", 37)]
    [InlineData("descend", 30)]
    [InlineData("swim", 54)]
    public void DryAndStillCourse_TickCountsDoNotMove(string shape, int expectedTicks)
    {
        const int floorY = 64;
        var profile = PhysicsProfile.ForProtocol(770);
        var world = new FixtureWorld();
        BlockPos start;
        BlockPos goal;
        float yaw = 0f;
        switch (shape)
        {
            case "walk":
                world.Floor(-8, 16, -8, 8, floorY);
                start = new BlockPos(0, floorY + 1, 0);
                goal = new BlockPos(6, floorY + 1, 0);
                break;
            case "ascend":
                world.Floor(-4, 12, -4, 4, floorY);
                world.Fill(3, floorY + 1, -4, 8, floorY + 1, 4, FixtureWorld.Stone);
                start = new BlockPos(0, floorY + 1, 0);
                goal = new BlockPos(6, floorY + 2, 0);
                break;
            case "descend":
                world.Fill(-4, floorY + 1, -4, 2, floorY + 1, 4, FixtureWorld.Stone);
                world.Fill(3, floorY, -4, 12, floorY, 4, FixtureWorld.Stone);
                start = new BlockPos(0, floorY + 2, 0);
                goal = new BlockPos(6, floorY + 1, 0);
                break;
            default:
                world.Floor(-4, 14, -4, 4, floorY);
                world.Fill(1, floorY + 1, 0, 6, floorY + 3, 0, FixtureWorld.Water);
                start = new BlockPos(1, floorY + 2, 0);
                goal = new BlockPos(6, floorY + 2, 0);
                yaw = 90f;
                break;
        }

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        var ctx = new PathExecutionContext(view, profile);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5), yaw);
        PathExecutorState state = driver.Run();

        _output.WriteLine($"{shape}: {state} in {driver.Trace.Count} ticks at {driver.State.Position}");

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.Equal(expectedTicks, driver.Trace.Count);
    }
}
