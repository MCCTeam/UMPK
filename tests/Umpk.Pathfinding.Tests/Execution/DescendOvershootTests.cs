using System.Globalization;
using System.Text;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class DescendOvershootTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    [Fact]
    public void SprintDescend_Drop3_CompletesOnTheLandingColumnWithoutOvershoot()
    {
        Run run = DriveSprintDescend(drop: 3, PathfinderOptions.Default);
        AssertLandsOnTarget(run, MoveType.Descend, maxSegmentTicks: 40, maxOvershoot: 1.5);
    }

    [Fact]
    public void SprintDescend_Drop4_CompletesOnTheLandingColumnWithoutOvershoot()
    {
        // MaxFallHeight 4 rather than PathfinderOptions.UnsafeFalls: the unsafe preset opens the descend scan to the whole world height, which makes the plan far slower without changing this shape.
        Run run = DriveSprintDescend(drop: 4, PathfinderOptions.Default with { MaxFallHeight = 4 });
        AssertLandsOnTarget(run, MoveType.Descend, maxSegmentTicks: 40, maxOvershoot: 1.5);
    }

    [Fact]
    public void SprintDescend_Drop2_SegmentTickCountIsPinned()
    {
        Run run = DriveSprintDescend(drop: 2, PathfinderOptions.Default);
        (int index, PathSegment segment) = FindSegment(run, MoveType.Descend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, "drop-2 descend segment failed", index));

        // gets faster, the landing margin is not allowed to move at all.
        //

        // the acceptance window, so the segment completes on the first grounded tick and the count is set by the walk-up plus the fall, not by the airborne policy. The walk-up is the part that moved: the approach runs at the sprint steady 0.28062 blocks a tick rather than the walk 0.21586,

        // it exactly is what proves the controller did not make the short drops slower to pay for the long ones, and that the extra speed did not cost a tick somewhere else.
        //

        // 0.4-wide window, which is the margin that vanished entirely on drops of three or more. The controller put it within 0.01 of the column centre and the faster approach keeps it there, on on the near side: 0.0056 short of centre. That margin, not the tick count, is what this test defends, and the sprint speed did not spend it.
        Assert.True(
            outcome.Ticks == 12,
            Describe(run, $"drop-2 descend segment took {outcome.Ticks} ticks, pinned at 12", index));
        Assert.True(
            Math.Abs(outcome.Position.X - segment.End.X) < 0.1,
            Describe(run, $"drop-2 landed at X={outcome.Position.X:F4}, not on the column centre {segment.End.X:F1}", index));
        Assert.True(
            MaxOvershoot(run, index, segment) <= 1.5,
            Describe(run, "drop-2 descend overshot the landing column", index));
    }

    [Fact]
    public void SprintDescend_Drop4_IntoA90DegreeTurn_Completes()
    {
        var world = new FixtureWorld();
        // Upper shelf ends at x=0; the landing strip is a single column at x=2 running away in +Z, so the

        // makes the descend's DesiredHeading differ from its own heading, which is the arm that the same-heading overshoot acceptance deliberately does not cover.
        world.Fill(-12, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(2, FloorY - 4, 0, 2, FloorY - 4, 10, FixtureWorld.Stone);

        var start = new BlockPos(-6, FloorY + 1, 0);
        var goal = new BlockPos(2, FloorY - 3, 8);
        Run run = Drive(world, PathfinderOptions.Default with { MaxFallHeight = 4 }, start, goal, FacingPlusX);

        (int index, PathSegment segment) = FindSegment(run, MoveType.Descend);
        SegmentOutcome outcome = OutcomeFor(run, index);
        Assert.False(outcome.Failed, Describe(run, "descend-into-turn segment failed", index));
        Assert.True(
            outcome.Ticks <= 60,
            Describe(run, $"descend-into-turn took {outcome.Ticks} ticks", index));
        Assert.True(
            Math.Abs(outcome.Position.X - segment.End.X) < 0.5 && Math.Abs(outcome.Position.Z - segment.End.Z) < 0.5,
            Describe(run, $"descend-into-turn handed off at {outcome.Position}, off the landing block", index));
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    [Fact]
    public void Descend_SingleStep_IntoA90DegreeTurn_Completes()
    {
        var world = new FixtureWorld();
        // The single-step counterpart of the turn case: a one-block drop onto a one-wide strip that runs

        //
        // This is the case the center-inside fallback exists for, and nothing else can complete it. The predicted-landing controller deliberately does not run on a single-step drop, so the bot lands at (1.9018, 5.9156) with the footprint hanging about 0.2 out of the destination column on both axes: the footprint test fails. The exit heading has turned 90 degrees, so the same-heading

        // one-wide strip and was 17 blocks into the void when the segment timed out.
        world.Fill(-12, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(1, FloorY - 1, 0, 1, FloorY - 1, 10, FixtureWorld.Stone);

        var start = new BlockPos(-6, FloorY + 1, 0);
        var goal = new BlockPos(1, FloorY, 8);
        Run run = Drive(world, PathfinderOptions.Default with { AllowSprint = false }, start, goal, FacingPlusX);

        (int index, PathSegment segment) = FindSegment(run, MoveType.Descend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.True(
            segment.Start.Y - segment.End.Y <= 1.0,
            Describe(run, "the plan did not use a single-step descend", index));
        Assert.True(
            segment.ExitHints.DesiredHeadingX != segment.HeadingX || segment.ExitHints.DesiredHeadingZ != segment.HeadingZ,
            Describe(run, "the segment after the descend does not turn", index));
        Assert.False(outcome.Failed, Describe(run, "single-step descend-into-turn segment failed", index));
        Assert.True(
            outcome.Ticks <= 20,
            Describe(run, $"single-step descend-into-turn took {outcome.Ticks} ticks", index));
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    [Fact]
    public void SprintJump_AcrossATwoBlockGap_LandsOnTheContinuingPlatform()
    {
        var world = new FixtureWorld();
        // Take-off shelf, a two-block gap (x=1,2), then a platform that carries on past the landing block so the parkour segment hands off into a walk rather than a final stop.
        world.Fill(-10, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(3, FloorY, -4, 14, FloorY, 4, FixtureWorld.Stone);

        var start = new BlockPos(-4, FloorY + 1, 0);
        var goal = new BlockPos(9, FloorY + 1, 0);
        Run run = Drive(world, PathfinderOptions.Default, start, goal, FacingPlusX);

        (int index, PathSegment segment) = FindSegment(run, MoveType.Parkour);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, "parkour segment failed", index));

        // parkour segment shares LandingRecovery completion with the descend segments, so this pins the arm they have in common.
        Assert.True(
            outcome.Position.X >= Math.Floor(segment.End.X) && outcome.Position.X <= Math.Floor(segment.End.X) + 1.0,
            Describe(run, $"parkour landed at X={outcome.Position.X:F3}, outside column {Math.Floor(segment.End.X)}", index));
        Assert.True(
            outcome.Ticks <= 40,
            Describe(run, $"parkour segment took {outcome.Ticks} ticks", index));
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    [Fact]
    public void Descend_Drop1_SingleForward_HoldsItsLineAndCompletesQuickly()
    {
        var world = new FixtureWorld();
        // A plain 1-forward, 1-down descend: upper shelf to x=0, lower floor from x=1 one block lower.
        world.Fill(-12, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(1, FloorY - 1, -4, 14, FloorY - 1, 4, FixtureWorld.Stone);

        var start = new BlockPos(-6, FloorY + 1, 0);
        var goal = new BlockPos(8, FloorY, 0);
        // AllowSprint false is what forces the ONE-forward descend. MoveSprintDescend requires CanSprint, so without it the only way down this ledge is MoveDescend(1,0); with it A* prefers the cheaper two-block leap and this test silently stops covering the single-step arm. The flag reaches both the search and executor through PathExecutionContext.AllowSprint, so the run is walking end to end: the ledge is left at the walk steady 0.21586 blocks a tick rather than the sprint 0.28062, which is why the tick bound below is generous.
        Run run = Drive(world, PathfinderOptions.Default with { AllowSprint = false }, start, goal, FacingPlusX);

        (int index, PathSegment segment) = FindSegment(run, MoveType.Descend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.True(
            Math.Abs(segment.End.X - segment.Start.X) < 1.5,
            Describe(run, "the plan did not use a one-forward descend", index));
        Assert.False(outcome.Failed, Describe(run, "drop-1 descend segment failed", index));
        AssertHoldsItsLine(run, index, segment);

        Assert.True(
            outcome.Ticks <= 20,
            Describe(run, $"drop-1 descend took {outcome.Ticks} ticks", index));
    }

    private static void AssertHoldsItsLine(Run run, int index, PathSegment segment)
    {
        double worstZ = 0.0;
        foreach (TickSample sample in run.Driver.Trace)
        {
            if (sample.SegmentIndex != index)
                continue;

            worstZ = Math.Max(worstZ, Math.Abs(sample.Position.Z - segment.End.Z));
        }

        // The airborne yaw on a single-step descend aims at the end POINT. Once the bot passes the end plane that vector inverts, SmoothYaw rotates through 180 degrees, and the air control steers the bot laterally off the segment line. Two bounds, because they fail to two different

        //

        //        |dZ| = 0.477 and burned all 200 ticks of the segment budget.
        //   0.05 is line-holding. The approach, the ledge and the landing are all on Z = 0.5 and the
        //        template presses nothing sideways, so the correct trajectory drifts by exactly 0.000.
        //        Restore the flip on its own, leaving the two-sided completion in place, and the segment
        //        still finishes (it completes on the landing tick) but it arrives 0.099 off the line,

        //        narrower than this fixture's, and only this bound catches it.
        Assert.True(worstZ < 0.3, Describe(run, $"drop-1 descend drifted |dZ|={worstZ:F3} off its line", index));
        Assert.True(worstZ < 0.05, Describe(run, $"drop-1 descend did not hold its line: |dZ|={worstZ:F3}", index));
    }

    private static Run DriveSprintDescend(int drop, PathfinderOptions options)
    {
        var world = new FixtureWorld();
        // Upper shelf x in [-12,0] (feet at FloorY+1); a one-block gap at x=1; the lower floor from x=2, 13 blocks long, so an overshoot has room to run instead of being stopped by a wall.
        world.Fill(-12, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(2, FloorY - drop, -4, 14, FloorY - drop, 4, FixtureWorld.Stone);

        var start = new BlockPos(-6, FloorY + 1, 0);
        var goal = new BlockPos(10, FloorY + 1 - drop, 0);
        return Drive(world, options, start, goal, FacingPlusX);
    }

    private static void AssertLandsOnTarget(Run run, MoveType moveType, int maxSegmentTicks, double maxOvershoot)
    {
        (int index, PathSegment segment) = FindSegment(run, moveType);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, $"{moveType} segment failed", index));
        Assert.True(
            outcome.Ticks <= maxSegmentTicks,
            Describe(run, $"{moveType} segment took {outcome.Ticks} ticks (budget {maxSegmentTicks})", index));

        double worst = MaxOvershoot(run, index, segment);
        Assert.True(
            worst <= maxOvershoot,
            Describe(run, $"{moveType} segment ran {worst:F3} past End.X while executing", index));

        // Landing ON the column, not merely being accepted somewhere along the line past it. The player is 0.6 wide in a 1-wide column, so the footprint is inside exactly while the center is within 0.2 of the column center. Two-sided completion alone may accept a landing on the walk beyond the column, so the controller must hand off from the block rather than its rim.
        Assert.True(
            Math.Abs(outcome.Position.X - segment.End.X) <= 0.2 && Math.Abs(outcome.Position.Z - segment.End.Z) <= 0.2,
            Describe(run, $"{moveType} handed off at {outcome.Position}, footprint not inside column {segment.End}", index));
    }

    private static double MaxOvershoot(Run run, int segmentIndex, PathSegment segment)
    {
        double worst = double.NegativeInfinity;
        foreach (TickSample sample in run.Driver.Trace)
        {
            if (sample.SegmentIndex != segmentIndex)
                continue;

            worst = Math.Max(worst, sample.Position.X - segment.End.X);
        }

        return double.IsNegativeInfinity(worst) ? 0.0 : worst;
    }

    private static (int Index, PathSegment Segment) FindSegment(Run run, MoveType moveType)
    {
        for (int i = 0; i < run.Segments.Count; i++)
            if (run.Segments[i].MoveType == moveType)
                return (i, run.Segments[i]);

        Assert.Fail(Describe(run, $"the plan contains no {moveType} segment", -1));
        return default;
    }

    private static SegmentOutcome OutcomeFor(Run run, int segmentIndex)
    {
        foreach (SegmentOutcome outcome in run.Recorder.Outcomes)
            if (outcome.Index == segmentIndex)
                return outcome;

        Assert.Fail(Describe(run, $"segment {segmentIndex} neither completed nor failed", segmentIndex));
        return default;
    }

    private static Run Drive(FixtureWorld world, PathfinderOptions options, BlockPos start, BlockPos goal, float startYaw)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        // The executor gets the SAME AllowSprint the search did. Planning with it off and executing with it on is the divergence PathExecutionContext.AllowSprint exists to close, and this file is the one that plans with it off (Descend_Drop1_SingleForward, to force the one-forward arm).
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default, options.AllowSprint);
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);

        var recorder = new SegmentRecorder();
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5), startYaw, recorder);
        PathExecutorState state = driver.Run(maxTicks: 1200);
        return new Run(segments, recorder, driver, state);
    }

    private static string Describe(Run run, string headline, int focusIndex)
    {
        var sb = new StringBuilder();
        sb.Append(headline).Append(" | executor=").Append(run.State).AppendLine();
        for (int i = 0; i < run.Segments.Count; i++)
        {
            PathSegment s = run.Segments[i];
            sb.Append(i == focusIndex ? "  >> " : "     ")
                .Append(CultureInfo.InvariantCulture, $"seg[{i}] {s.MoveType,-8} {s.Start}->{s.End} exit={s.ExitTransition} h=({s.HeadingX},{s.HeadingZ}) desired=({s.ExitHints.DesiredHeadingX},{s.ExitHints.DesiredHeadingZ})");
            foreach (SegmentOutcome outcome in run.Recorder.Outcomes)
                if (outcome.Index == i)
                    sb.Append(CultureInfo.InvariantCulture, $" -> {(outcome.Failed ? "FAILED" : "done")} in {outcome.Ticks} ticks at {outcome.Position}");

            sb.AppendLine();
        }

        if (focusIndex >= 0)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
            int samples = 0;
            foreach (TickSample sample in run.Driver.Trace)
            {
                if (sample.SegmentIndex != focusIndex)
                    continue;

                samples++;
                minX = Math.Min(minX, sample.Position.X);
                maxX = Math.Max(maxX, sample.Position.X);
                minZ = Math.Min(minZ, sample.Position.Z);
                maxZ = Math.Max(maxZ, sample.Position.Z);
            }

            if (samples > 0)
                sb.Append(CultureInfo.InvariantCulture, $"     focus segment path: {samples} ticks, X in [{minX:F3}..{maxX:F3}], Z in [{minZ:F3}..{maxZ:F3}]");

        }

        return sb.ToString();
    }

    private readonly record struct SegmentOutcome(int Index, int Ticks, bool Failed, Vec3d Position);

    private sealed record Run(
        IReadOnlyList<PathSegment> Segments,
        SegmentRecorder Recorder,
        ExecutionDriver Driver,
        PathExecutorState State);

    private sealed class SegmentRecorder : IPathExecutionObserver
    {
        private readonly List<SegmentOutcome> _outcomes = [];

        internal IReadOnlyList<SegmentOutcome> Outcomes => _outcomes;

        public void OnNavigationStarted(IReadOnlyList<PathSegment> segments)
        {
        }

        public void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment)
        {
        }

        public void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
            => _outcomes.Add(new SegmentOutcome(segmentIndex, elapsedTicks, false, position));

        public void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
            => _outcomes.Add(new SegmentOutcome(segmentIndex, elapsedTicks, true, position));

        public void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance)
        {
        }

        public void OnNavigationCompleted(int totalTicks)
        {
        }
    }
}
