using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class CrossTrackStationTests
{
    private const int FloorY = 64;
    private const int FeetY = FloorY + 1;

    /// <summary>The corridor's lane centre in Z; the whole route is planned along it.</summary>
    private const double LaneZ = 0.5;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public CrossTrackStationTests(ITestOutputHelper output) => _output = output;

    /// <summary>A body walking a straight corridor through a crossflow stays on the line its segments were planned along.</summary>
    /// <remarks>The bound is 0.5 of a block, which is the widest cross-track error that still leaves the body's centre in its own lane of cells: past it the body is in the NEIGHBOURING column and every completion predicate in <c>GroundedSegmentController</c> is being asked about a cell the body is not in. It is the same 0.5 <c>ShouldComplete</c>'s landing arm already uses for <c>LateralOffsetFromSegmentLine</c> when it decides whether an overshoot counts as an arrival.</remarks>
    [Fact]
    public void ABodyCrossingASheet_StaysOnTheLineItsSegmentsWerePlannedAlong()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Crossflow();
        var driver = new ExecutionDriver(ctx, segments, start);

        PathExecutorState state = driver.Run(600);

        double worst = 0;
        foreach (TickSample sample in driver.Trace)
            worst = Math.Max(worst, Math.Abs(sample.Position.Z - LaneZ));

        _output.WriteLine(
            $"{state} after {driver.Trace.Count} ticks at {driver.State.Position}, worst cross-track {worst:F4}");
        _output.WriteLine(
            $"medium: ticks={driver.Trace.Count} grounded={driver.GroundedTicks} "
            + $"wade={driver.WadeTicks} buoyant={driver.BuoyantTicks} submerged={driver.SubmergedTicks}");

        // The fixture's PREMISE, pinned rather than assumed, because it is what decides the scope gate. A one-deep sheet leaves the body BUOYANT for nearly the whole crossing (measured wade=1 against buoyant=19), so OnGround cannot be what separates a wade from a swim - gating on it would disarm the controller on these rows. The head is what separates them: a one-layer wade never submerges. See StationController.IsInScope.
        Assert.True(
            driver.BuoyantTicks > driver.WadeTicks,
            $"the fixture was grounded for {driver.WadeTicks} of its {driver.WadeTicks + driver.BuoyantTicks} "
            + "wet ticks, so the premise that a one-deep wade is mostly buoyant no longer holds");
        Assert.Equal(0, driver.SubmergedTicks);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            worst <= 0.5,
            $"the body was washed {worst:F4} blocks off the line its segments were planned along, "
            + "and nothing in the executor pressed back");
    }

    [Fact]
    public void ThePerTickPredictionErrorNeverSeesTheDrift_SoTheDeviationThresholdIsTheWrongInstrument()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Crossflow();

        var engine = new PlayerPhysics(ctx.World, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(segments[0].Start, 0f, 0f);
        engine.Step(MovementInput.None);
        var executor = new PathExecutor(ctx, segments);

        double worstPrediction = 0;
        double worstCrossTrack = 0;
        PhysicsState? expected = null;
        for (int tick = 0; tick < 600; tick++)
        {
            PhysicsState actual = engine.State;
            if (expected is { } previous)
                worstPrediction = Math.Max(worstPrediction, actual.Position.Subtract(previous.Position).Length());

            worstCrossTrack = Math.Max(worstCrossTrack, Math.Abs(actual.Position.Z - LaneZ));

            PathExecutorTick step = executor.Tick(actual);
            if (step.State != PathExecutorState.InProgress)
                break;

            expected = step.Output.ExpectedState;
            engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            engine.Step(step.Output.Input);
        }

        _output.WriteLine(
            $"worst per-tick prediction error {worstPrediction:F4}, worst cross-track {worstCrossTrack:F4}, "
            + "threshold 1.5");

        Assert.True(
            worstPrediction < 0.1,
            $"the per-tick prediction error reached {worstPrediction:F4}, so it is not the near-zero "
            + "quantity the argument depends on");
    }

    /// <summary>Twice as much sheet to cross is still held inside the lane. The narrow row above proves the controller fires; this one proves it has authority to spare rather than only just enough.</summary>
    /// <remarks>Six blocks of crossflow instead of three. Uncontrolled this shape produced <c>1.2000</c> blocks of drift, which is past the point where the body's centre is in the neighbouring column.</remarks>
    [Fact]
    public void ASheetTwiceAsWide_IsStillHeldInsideTheLane()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Crossflow(strong: true);
        var driver = new ExecutionDriver(ctx, segments, start);

        PathExecutorState state = driver.Run(600);

        double worst = 0;
        foreach (TickSample sample in driver.Trace)
            worst = Math.Max(worst, Math.Abs(sample.Position.Z - LaneZ));

        _output.WriteLine($"{state} after {driver.Trace.Count} ticks, worst cross-track {worst:F4}");

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(worst <= 0.5, $"six blocks of crossflow washed the body {worst:F4} off its lane");
    }

    [Fact]
    public void ADriftPastWhatTheControllerCanRecover_IsReportedAsADeviation()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Crossflow();
        var observer = new CrossTrackObserver();
        var executor = new PathExecutor(ctx, segments, observer);

        PhysicsState offLane = OffLane(segments[0], 0.9, inWater: true);
        var firing = new List<int>();
        for (int tick = 1; tick <= StationController.CrossTrackReplanTicks; tick++)
            if (executor.Tick(offLane).DeviationExceeded)
                firing.Add(tick);

        _output.WriteLine($"reported on ticks [{string.Join(", ", firing)}], observer saw {observer.Deviations}");

        Assert.Equal([StationController.CrossTrackReplanTicks], firing);
        Assert.Equal(1, observer.Deviations);
    }

    /// <summary>The scope gate, asserted rather than assumed: the same displacement on dry land is reported by nobody, because on dry land the drift is the template's own steering and the controller has no business in it.</summary>
    [Fact]
    public void TheReplanArmIsSilentOnDryLand()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Crossflow();
        var observer = new CrossTrackObserver();
        var executor = new PathExecutor(ctx, segments, observer);

        PhysicsState offLane = OffLane(segments[0], 0.9, inWater: false);
        for (int tick = 0; tick < StationController.CrossTrackReplanTicks * 3; tick++)
            Assert.False(executor.Tick(offLane).DeviationExceeded);

        Assert.Equal(0, observer.Deviations);
    }

    [Fact]
    public void WithTheDeadbandAblated_TheDriftComesBack()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Crossflow();
        var driver = new ExecutionDriver(
            ctx, segments, start, station: StationControllerOptions.Disabled);

        PathExecutorState state = driver.Run(600);

        double worst = 0;
        foreach (TickSample sample in driver.Trace)
            worst = Math.Max(worst, Math.Abs(sample.Position.Z - LaneZ));

        _output.WriteLine($"ABLATED: {state} after {driver.Trace.Count} ticks, worst cross-track {worst:F4}");

        Assert.True(
            worst > 0.9,
            $"ablating the controller only produced {worst:F4} of drift against the 1.0037 the same "
            + "fixture measured before it existed, so the ablation did not ablate");
    }

    [Fact]
    public void OnDryLandTheControllerIsInvisible_ByteForByte()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, Vec3d start) = Crossflow(dry: true);

        var live = new ExecutionDriver(ctx, segments, start);
        PathExecutorState liveState = live.Run(600);

        (PathExecutionContext ablatedCtx, IReadOnlyList<PathSegment> ablatedSegments, Vec3d ablatedStart) =
            Crossflow(dry: true);
        var ablated = new ExecutionDriver(
            ablatedCtx, ablatedSegments, ablatedStart, station: StationControllerOptions.Disabled);
        PathExecutorState ablatedState = ablated.Run(600);

        _output.WriteLine(
            $"dry: {liveState} in {live.EmittedInputs.Count} ticks, "
            + $"ablated {ablatedState} in {ablated.EmittedInputs.Count} ticks");

        Assert.Equal(PathExecutorState.Complete, liveState);
        Assert.Equal(ablatedState, liveState);
        Assert.Equal(ablated.EmittedInputs, live.EmittedInputs);
        Assert.Equal(ablated.State.Position, live.State.Position);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(-0.9)]
    public void ASwimmerIsNotCorrected(double offset)
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Crossflow();
        PathSegment segment = segments[0];
        float yaw = SegmentGeometry.CalculateYaw(segment.HeadingX, segment.HeadingZ);
        var controller = new StationController();

        PhysicsState wading = OffLane(segment, offset, inWater: true);
        TemplateOutput wadeIn = TemplateOutput.From(new MovementInput { Forward = true }, yaw, 0f, ctx, wading);
        controller.Correct(segment, wading, wadeIn, ctx);
        Assert.True(controller.LastPressed, "the wading control did not arm, so the swim row asserts nothing");

        PhysicsState swimming = wading with { IsUnderWater = true };
        TemplateOutput swimIn = TemplateOutput.From(new MovementInput { Forward = true }, yaw, 0f, ctx, swimming);
        TemplateOutput corrected = controller.Correct(segment, swimming, swimIn, ctx);

        Assert.False(controller.LastPressed);
        Assert.Equal(swimIn.Input, corrected.Input);

        Assert.False(controller.CheckCrossTrack(segment, swimming, out double reported));
        Assert.Equal(0, reported);
    }

    [Fact]
    public void TheWaterHalfOfTheScopeGateIsLoadBearing()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Crossflow(dry: true);
        PathSegment segment = segments[0];
        float yaw = SegmentGeometry.CalculateYaw(segment.HeadingX, segment.HeadingZ);
        PhysicsState dryOffLane = OffLane(segment, 0.9, inWater: false);
        TemplateOutput input = TemplateOutput.From(new MovementInput { Forward = true }, yaw, 0f, ctx, dryOffLane);

        var shipped = new StationController(StationControllerOptions.Default);
        shipped.Correct(segment, dryOffLane, input, ctx);
        Assert.False(shipped.LastPressed);

        var ablated = new StationController(StationControllerOptions.Default with { RequireSurfaceWater = false });
        ablated.Correct(segment, dryOffLane, input, ctx);
        Assert.True(
            ablated.LastPressed,
            "ablating the water gate changed nothing, so the water gate is not what keeps dry land clear");
    }

    /// <summary>The controller may add a strafe and NOTHING else. Asserted on the correction itself rather than on a trajectory, because a trajectory that diverges cannot answer the question.</summary>
    /// <remarks>This is the invariant the whole design rests on: along-track is the segment's, so <see cref="MovementInput.Forward"/>, <see cref="MovementInput.Back"/>, <see cref="MovementInput.Jump"/>, <see cref="MovementInput.Sneak"/> and <see cref="MovementInput.Sprint"/> must survive the correction unchanged for every input a template can emit and every side the body can be off on.</remarks>
    [Theory]
    [InlineData(0.9)]
    [InlineData(-0.9)]
    public void TheCorrectionAddsAStrafeAndTouchesNothingElse(double offset)
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, _) = Crossflow();
        PathSegment segment = segments[0];
        PhysicsState physics = OffLane(segment, offset, inWater: true);
        var controller = new StationController();

        // The segment's own heading, which is the yaw a template on this segment emits. It matters: the correction is resolved in the BODY frame, so a body facing straight down the correction has no strafe axis to press and the controller presses nothing - which is the design, not a gap.
        float yaw = SegmentGeometry.CalculateYaw(segment.HeadingX, segment.HeadingZ);

        foreach (MovementInput input in EveryInput())
        {
            TemplateOutput before = TemplateOutput.From(input, yaw, 0f, ctx, physics);
            TemplateOutput after = controller.Correct(segment, physics, before, ctx);

            Assert.Equal(before.Input.Forward, after.Input.Forward);
            Assert.Equal(before.Input.Back, after.Input.Back);
            Assert.Equal(before.Input.Jump, after.Input.Jump);
            Assert.Equal(before.Input.Sneak, after.Input.Sneak);
            Assert.Equal(before.Input.Sprint, after.Input.Sprint);
            Assert.Equal(before.TargetYaw, after.TargetYaw);
            Assert.Equal(before.TargetPitch, after.TargetPitch);
        }

        Assert.True(controller.LastPressed, "the fixture never armed the controller, so it asserted nothing");
    }

    /// <summary>Every combination of the five bits the controller must not touch.</summary>
    private static IEnumerable<MovementInput> EveryInput()
    {
        for (int bits = 0; bits < 32; bits++)
            yield return new MovementInput
            {
                Forward = (bits & 1) != 0,
                Back = (bits & 2) != 0,
                Jump = (bits & 4) != 0,
                Sneak = (bits & 8) != 0,
                Sprint = (bits & 16) != 0,
            };

    }

    /// <summary>A state sitting <paramref name="offset"/> blocks off <paramref name="segment"/>'s line, on the ground, and close enough to the segment that no other executor bound fires first.</summary>
    private static PhysicsState OffLane(PathSegment segment, double offset, bool inWater)
    {
        Vec3d position = segment.Start.Add(0, 0, offset);
        return new PhysicsState
        {
            Position = position,
            Velocity = Vec3d.Zero,
            OnGround = true,
            InWater = inWater,
            BoundingBox = Aabb.OfSize(position.X, position.Y, position.Z, 0.6, 1.8),
        };
    }

    /// <summary>E20's shape offline: a walled corridor along +X at <see cref="FeetY"/>, with a sheet flowing across it in +Z.</summary>
    /// <remarks>The corridor is three cells wide in Z so the sheet has a gradient to run down and the body has room to be pushed off its lane without immediately meeting a wall; the walls at z = -2 and z = +2 are what keep the planner from simply routing around the water, so the route under test is the crossing itself.</remarks>
    private static (PathExecutionContext Ctx, IReadOnlyList<PathSegment> Segments, Vec3d Start) Crossflow(
        bool strong = false, bool dry = false)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 16, -2, 2, FloorY);
        world.Fill(-4, FeetY, -2, 16, FeetY + 2, -2, FixtureWorld.Stone);
        world.Fill(-4, FeetY, 2, 16, FeetY + 2, 2, FixtureWorld.Stone);

        int width = strong ? 6 : 3;
        for (int x = 4; x < 4 + width && !dry; x++)
            world.FlowingRun(x, FeetY, -1, length: 3, stepX: 0, stepZ: 1);

        var startCell = new BlockPos(0, FeetY, 0);
        var goalCell = new BlockPos(4 + width + 4, FeetY, 0);
        PlanningWorldView view = world.Capture(startCell, goalCell, margin: 6);
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, startCell, new GoalBlock(goalCell));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        Assert.NotEmpty(segments);
        return (ctx, segments, new Vec3d(0.5, FeetY, LaneZ));
    }

    private sealed class CrossTrackObserver : IPathExecutionObserver
    {
        public int Deviations { get; private set; }

        public void OnNavigationStarted(IReadOnlyList<PathSegment> segments)
        {
        }

        public void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment)
        {
        }

        public void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
        {
        }

        public void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
        {
        }

        public void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance)
            => Deviations++;

        public void OnNavigationCompleted(int totalTicks)
        {
        }
    }
}
