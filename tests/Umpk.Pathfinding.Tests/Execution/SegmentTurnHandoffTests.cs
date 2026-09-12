using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SegmentTurnHandoffTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private static PathNode Node(int x, int y, int z, MoveType move) => new(x, y, z) { MoveUsed = move };

    /// <summary>Two diagonals meeting at ninety degrees: north-east then south-east. The corner is a stable-footing Turn, and the second leg's heading is 90 degrees off the first's.</summary>
    [Fact]
    public void ARightAngleTurnBetweenTwoDiagonalsCompletes()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);

        PathNode[] nodes =
        [
            Node(0, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 1, MoveType.Diagonal),
            Node(2, FloorY + 1, 0, MoveType.Diagonal),
        ];

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes);
        Assert.Equal(2, segments.Count);

        // the test pass by turning the corner into some other kind of handoff.
        Assert.Equal(PathTransitionType.Turn, segments[0].ExitTransition);
        Assert.True(segments[0].ExitHints.RequireStableFooting);
        Assert.False(segments[0].ExitHints.RequireJumpReady);
        Assert.Equal(90.0, HeadingAngleBetween(segments[0], segments[1]), 6);

        var failures = new List<string>();
        var observer = new DelegatePathExecutionObserver(line =>
        {
            if (line.Contains("FAILED", StringComparison.Ordinal))
                failures.Add(line);

        });

        PlanningWorldView view = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(2, FloorY + 1, 0));
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, observer: observer);

        PathExecutorState state = driver.Run(600);

        Assert.True(
            state == PathExecutorState.Complete,
            $"the right-angle turn never handed off: state {state}, failures [{string.Join("; ", failures)}], " +
            $"final position {driver.State.Position}");
        Assert.Empty(failures);
        Assert.True(
            Math.Abs(driver.State.Position.X - segments[1].End.X) < 1.0
            && Math.Abs(driver.State.Position.Z - segments[1].End.Z) < 1.0,
            $"ended away from the second leg's target: {driver.State.Position}");
    }

    /// <summary>A gentler cardinal-to-diagonal turn (45 degrees) has to keep working too.</summary>
    [Fact]
    public void AFortyFiveDegreeTurnStillCompletes()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);

        PathNode[] nodes =
        [
            Node(0, FloorY + 1, 0, MoveType.Traverse),
            Node(1, FloorY + 1, 0, MoveType.Traverse),
            Node(2, FloorY + 1, 1, MoveType.Diagonal),
        ];

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes);
        Assert.Equal(PathTransitionType.Turn, segments[0].ExitTransition);

        PlanningWorldView view = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(2, FloorY + 1, 1));
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start);

        Assert.Equal(PathExecutorState.Complete, driver.Run(600));
    }

    private static double HeadingAngleBetween(PathSegment a, PathSegment b)
    {
        double dot = (a.HeadingX * b.HeadingX) + (a.HeadingZ * b.HeadingZ);
        double lenA = Math.Sqrt((a.HeadingX * a.HeadingX) + (a.HeadingZ * a.HeadingZ));
        double lenB = Math.Sqrt((b.HeadingX * b.HeadingX) + (b.HeadingZ * b.HeadingZ));
        return Math.Acos(dot / (lenA * lenB)) * 180.0 / Math.PI;
    }
}
