using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ExecutorDeviationTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private static (PathExecutionContext Ctx, IReadOnlyList<PathSegment> Segments, Vec3d Start) Setup()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 16, -8, 8, FloorY);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(6, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        return (ctx, segments, new Vec3d(0.5, FloorY + 1, 0.5));
    }

    [Fact]
    public void NoDeviation_WhenStateFollowsThePlan()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Setup();
        var driver = new ExecutionDriver(ctx, segments, start);

        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.False(driver.DeviationSeen);
    }

    [Fact]
    public void Deviation_FlaggedWhenStateJumpsAwayFromExpected()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Setup();
        var executor = new PathExecutor(ctx, segments, deviationThreshold: 1.0);

        var engine = new PlayerPhysics(ctx.World, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(segments[0].Start, 0f, 0f);
        engine.Step(MovementInput.None);

        // First tick establishes an expected next state.
        PathExecutorTick first = executor.Tick(engine.State);
        Assert.False(first.DeviationExceeded);

        // Feed a wildly teleported state (as if the server yanked the bot) far from the expected position.
        PhysicsState teleported = engine.State with { Position = engine.State.Position.Add(10, 0, 10) };
        PathExecutorTick second = executor.Tick(teleported);

        Assert.True(second.DeviationExceeded);
    }

    [Fact]
    public void Observer_ReceivesSegmentAndCompletionEvents()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Setup();
        var observer = new RecordingObserver();
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, observer: observer);

        PathExecutorState state = driver.Run();

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(observer.Started);
        Assert.True(observer.SegmentsCompleted > 0);
        Assert.True(observer.NavigationCompleted);
    }

    [Fact]
    public void DelegateObserver_FormatsEventLines()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Setup();
        var lines = new List<string>();
        var observer = new DelegatePathExecutionObserver(lines.Add);
        var driver = new ExecutionDriver(ctx, segments, segments[0].Start, observer: observer);

        driver.Run();

        Assert.Contains(lines, l => l.Contains("navigation started"));
        Assert.Contains(lines, l => l.Contains("complete"));
    }

    private sealed class RecordingObserver : IPathExecutionObserver
    {
        public bool Started { get; private set; }

        public int SegmentsCompleted { get; private set; }

        public bool NavigationCompleted { get; private set; }

        public void OnNavigationStarted(IReadOnlyList<PathSegment> segments) => Started = true;

        public void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment)
        {
        }

        public void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
            => SegmentsCompleted++;

        public void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
        {
        }

        public void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance)
        {
        }

        public void OnNavigationCompleted(int totalTicks) => NavigationCompleted = true;
    }
}
