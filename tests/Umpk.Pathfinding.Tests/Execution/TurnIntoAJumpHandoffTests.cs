using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class TurnIntoAJumpHandoffTests
{
    private const int FloorY = 64;

    private const double SettledSpeedCap = 0.04;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private static PathNode Node(int x, int y, int z, MoveType move) => new(x, y, z) { MoveUsed = move };

    private static (FixtureWorld World, IReadOnlyList<PathSegment> Segments) TurnThenStepUp()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(1, FloorY + 1, 2, FixtureWorld.Stone);   // the riser the Ascend climbs

        PathNode[] nodes =
        [
            Node(0, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 1, MoveType.Traverse),
            Node(1, FloorY + 2, 2, MoveType.Ascend),
        ];

        return (world, PathSegmentBuilder.FromPath(nodes));
    }

    [Fact]
    public void TheJumpReadyTurnAsksForASpeedASettledBodyCannotHave()
    {
        (_, IReadOnlyList<PathSegment> segments) = TurnThenStepUp();

        Assert.Equal(PathTransitionType.Turn, segments[0].ExitTransition);
        Assert.True(segments[0].ExitHints.RequireJumpReady);
        Assert.False(segments[0].ExitHints.RequireStableFooting);
        Assert.True(
            segments[0].ExitHints.MinExitSpeed > SettledSpeedCap,
            $"the rolling turn demands {segments[0].ExitHints.MinExitSpeed} along the exit heading, "
            + $"which a body settled at {SettledSpeedCap} cannot produce");
    }

    [Fact]
    public void ATurnWhoseSecondMoveAwayIsAnAscendHandsOffInsteadOfBurningItsBudget()
    {
        (FixtureWorld world, IReadOnlyList<PathSegment> segments) = TurnThenStepUp();

        var failures = new List<string>();
        var observer = new DelegatePathExecutionObserver(line =>
        {
            if (line.Contains("FAILED", StringComparison.Ordinal))
                failures.Add(line);

        });

        PlanningWorldView view = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(1, FloorY + 2, 2));
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, observer: observer);

        PathExecutorState state = driver.Run(600);

        Assert.True(
            state == PathExecutorState.Complete,
            $"the rolling turn never handed off: state {state}, failures [{string.Join("; ", failures)}], "
            + $"final position {driver.State.Position}");
        Assert.Empty(failures);
    }

    [Fact]
    public void TheSameCornerWithNoJumpTwoMovesAwayWasNeverBroken()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);

        PathNode[] nodes =
        [
            Node(0, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 1, MoveType.Traverse),
            Node(1, FloorY + 1, 2, MoveType.Traverse),
        ];

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes);

        Assert.Equal(PathTransitionType.Turn, segments[0].ExitTransition);
        Assert.True(segments[0].ExitHints.RequireStableFooting);
        Assert.False(segments[0].ExitHints.RequireJumpReady);

        PlanningWorldView view = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(1, FloorY + 1, 2));
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start);

        Assert.Equal(PathExecutorState.Complete, driver.Run(600));
    }
}
