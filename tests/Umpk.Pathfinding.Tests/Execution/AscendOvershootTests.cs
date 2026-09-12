using System.Globalization;
using System.Text;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class AscendOvershootTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Modern = PhysicsProfile.ForProtocol(770);

    [Fact]
    public void FlushDiagonalAscend_LandsOnTheTargetColumn()
    {
        Run run = DriveDiagonalPad(new Vec3d(-0.3, FloorY + 1, -0.3));
        AssertAscendLandsOnTheColumn(run, maxTicks: 20, tolerance: 0.35);
    }

    [Fact]
    public void DiagonalAscend_FromTheBlockCentre_LandsOnTheTargetColumn()
    {
        Run run = DriveDiagonalPad(new Vec3d(-0.5, FloorY + 1, -0.5));
        AssertAscendLandsOnTheColumn(run, maxTicks: 20, tolerance: 0.35);
    }

    [Fact]
    public void DiagonalAscend_OntoAnIsolatedPillar_DoesNotHandOffFromTheRim()
    {
        var world = new FixtureWorld();
        world.Set(-1, FloorY, -1, FixtureWorld.Stone);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);

        Run run = Drive(
            world, Modern, new BlockPos(-1, FloorY + 1, -1), new BlockPos(0, FloorY + 2, 0),
            new Vec3d(-0.3, FloorY + 1, -0.3));

        (int index, PathSegment segment) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);
        Assert.False(outcome.Failed, Describe(run, "the pillar ascend failed", index));
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, segment.End),
            Describe(run, $"the pillar ascend handed off at {outcome.Position}, off the pillar", index));
    }

    [Fact]
    public void DiagonalAscend_IntoATurn_Completes()
    {
        var world = new FixtureWorld();
        world.Set(-1, FloorY, -1, FixtureWorld.Stone);
        world.Fill(0, FloorY + 1, 0, 5, FloorY + 1, 0, FixtureWorld.Stone);

        Run run = Drive(
            world, Modern, new BlockPos(-1, FloorY + 1, -1), new BlockPos(4, FloorY + 2, 0),
            new Vec3d(-0.3, FloorY + 1, -0.3));

        (int index, PathSegment segment) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);
        Assert.False(outcome.Failed, Describe(run, "the ascend into a turn failed", index));
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, segment.End),
            Describe(run, $"the ascend into a turn handed off at {outcome.Position}, off the walkway", index));
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    [Fact]
    public void ChainedDiagonalAscends_KeepEveryHandoffOnItsTread()
    {
        Run run = DriveStairs();

        int ascends = 0;
        for (int i = 0; i < run.Segments.Count; i++)
        {
            if (run.Segments[i].MoveType != MoveType.Ascend)
                continue;

            ascends++;
            SegmentOutcome outcome = OutcomeFor(run, i);
            Assert.False(outcome.Failed, Describe(run, $"tread {ascends} of the staircase failed", i));
            Assert.True(
                SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, run.Segments[i].End),
                Describe(run, $"tread {ascends} handed off at {outcome.Position}, off the tread", i));
        }

        Assert.True(ascends == 6, Describe(run, $"the plan has {ascends} ascends, expected 6", -1));
        Assert.Equal(PathExecutorState.Complete, run.State);
        Assert.True(
            run.Driver.Trace.Count <= 100,
            Describe(run, $"the staircase took {run.Driver.Trace.Count} ticks", -1));
    }

    [Fact]
    public void FlushCardinalAscend_IsUnchanged()
    {
        Run run = DrivePlusShape();
        (int index, _) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, "the cardinal ascend failed", index));
        Assert.True(outcome.Ticks == 11, Describe(run, $"cardinal ascend took {outcome.Ticks} ticks, pinned at 11", index));
        Assert.True(
            Math.Abs(outcome.Position.X - 0.2985) < 1e-3 && Math.Abs(outcome.Position.Z - 0.5) < 1e-3,
            Describe(run, $"cardinal ascend landed at {outcome.Position}, pinned at (0.2985, 0.5)", index));
    }

    [Fact]
    public void FlushCardinalAscend_PositionTraceIsUnchanged()
    {
        // Every line pins one tick of the ascend segment as "x y z onGround".
        string[] expected =
        [
            "-0.3000 65.0000 0.5000 -",
            "-0.3000 65.0000 0.5000 G",
            "-0.3000 65.4200 0.5000 -",
            "-0.3000 65.7532 0.5000 -",
            "-0.2745 66.0013 0.5000 -",
            "-0.2259 66.1661 0.5000 -",
            "-0.1561 66.2492 0.5000 -",
            "-0.0671 66.2522 0.5000 -",
            "0.0393 66.1768 0.5000 -",
            "0.1617 66.0244 0.5000 -",
            "0.2985 66.0000 0.5000 G",
        ];

        Run run = DrivePlusShape();
        (int index, _) = FindSegment(run, MoveType.Ascend);
        Assert.Equal(string.Join("\n", expected), TraceOf(run, index));
    }

    [Fact]
    public void Staircase_TenCardinalAscends_IsUnchanged()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 2, FloorY, 2, FixtureWorld.Stone);
        for (int n = 1; n <= 9; n++)
            world.Fill((2 * n) + 1, FloorY + n, 0, (2 * n) + 2, FloorY + n, 2, FixtureWorld.Stone);

        world.Fill(21, FloorY + 10, 0, 23, FloorY + 10, 2, FixtureWorld.Stone);

        Run run = Drive(
            world, Modern, new BlockPos(1, FloorY + 1, 1), new BlockPos(22, FloorY + 11, 1),
            new Vec3d(1.5, FloorY + 1, 1.5), seedAtStartPos: false, margin: 12);

        int ascends = 0;
        foreach (PathSegment segment in run.Segments)
            if (segment.MoveType == MoveType.Ascend)
                ascends++;

        (int index, _) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.Equal(PathExecutorState.Complete, run.State);
        Assert.True(ascends == 10, Describe(run, $"the staircase plan has {ascends} ascends, pinned at 10", index));
        Assert.True(outcome.Ticks == 10, Describe(run, $"the first ascend took {outcome.Ticks} ticks, pinned at 10", index));
        Assert.True(
            Math.Abs(outcome.Position.X - 3.2985) < 1e-3,
            Describe(run, $"the first ascend landed at X={outcome.Position.X:F4}, pinned at 3.2985", index));
        Assert.True(
            run.Driver.Trace.Count == 134,
            Describe(run, $"the staircase route took {run.Driver.Trace.Count} ticks, pinned at 134", index));
    }

    [Fact]
    public void OffCentreCorridorAscend_IsUnchanged()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 10, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, FloorY + 1, 0, 10, FloorY + 2, 0, FixtureWorld.Stone);
        world.Fill(0, FloorY + 1, 2, 10, FloorY + 2, 2, FixtureWorld.Stone);
        world.Fill(8, FloorY + 1, 1, 10, FloorY + 1, 1, FixtureWorld.Stone);

        Run run = Drive(
            world, Modern, new BlockPos(1, FloorY + 1, 1), new BlockPos(8, FloorY + 2, 1),
            new Vec3d(1.5, FloorY + 1, 1.3), margin: 12);

        (int index, _) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, "the off-centre corridor ascend failed", index));
        Assert.True(outcome.Ticks == 10, Describe(run, $"the corridor ascend took {outcome.Ticks} ticks, pinned at 10", index));
        Assert.True(
            Math.Abs(outcome.Position.X - 8.2985) < 1e-3,
            Describe(run, $"the corridor ascend landed at X={outcome.Position.X:F4}, pinned at 8.2985", index));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(578)]
    [InlineData(767)]
    [InlineData(774)]
    public void FlushDiagonalAscend_LandsOnTheTargetColumn_OnEveryProtocol(int protocol)
    {
        Run run = DriveDiagonalPad(new Vec3d(-0.3, FloorY + 1, -0.3), PhysicsProfile.ForProtocol(protocol));

        (int index, PathSegment segment) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);
        Assert.False(outcome.Failed, Describe(run, $"the flush diagonal ascend failed on protocol {protocol}", index));
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, segment.End),
            Describe(run, $"protocol {protocol} handed off at {outcome.Position}, off the column", index));
    }

    [Theory]
    [InlineData(0.30)]
    [InlineData(0.40)]
    [InlineData(0.50)]
    [InlineData(0.60)]
    [InlineData(0.70)]
    public void DiagonalAscend_LandsOnTheTargetColumn_FromEveryApproachPhase(double offset)
    {
        Run run = DriveDiagonalPad(new Vec3d(-1.0 + offset, FloorY + 1, -1.0 + offset));

        (int index, PathSegment segment) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);
        Assert.False(outcome.Failed, Describe(run, $"the diagonal ascend failed from offset {offset:F2}", index));
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, segment.End),
            Describe(run, $"offset {offset:F2} handed off at {outcome.Position}, off the column", index));
    }

    /// <summary>The airborne objective's first key, tested directly because the geometry of a ONE-BLOCK ascend cannot exercise it: any predicted landing within ~0.3 of a one-block column's centre is on that column and therefore at its elevation, so on every fixture in this file the key never changes a decision (instrumented: 0 flips over 22 geometries and 32 approach phases). It still has to be right, because it is what lets the controller refuse a jump that fell back down without any completion gate being loosened - and a horizontal-distance-only objective prefers exactly that failure, since a fall-back onto the take-off block can be nearer the destination than a real landing on it.</summary>
    [Fact]
    public void AirborneObjective_RefusesALandingAtTheWrongElevation_HoweverNearItIs()
    {
        var end = new Vec3d(0.5, 66.0, 0.5);

        // Dead on the destination horizontally, but a block low: the climb failed and fell back.
        LandingPrediction fellBack = Landing(true, new Vec3d(0.5, 65.0, 0.5));

        // At the destination's elevation but most of a block away: a real, if scruffy, arrival.
        LandingPrediction scruffy = Landing(true, new Vec3d(1.4, 66.0, 1.4));

        Assert.True(AscendTemplate.IsFailedClimb(fellBack, end));
        Assert.False(AscendTemplate.IsFailedClimb(scruffy, end));
        Assert.True(
            AscendTemplate.IsBetterLanding(scruffy, fellBack, end),
            "a landing at the destination's elevation must beat one that fell back, however near it fell");
        Assert.False(
            AscendTemplate.IsBetterLanding(fellBack, scruffy, end),
            "a candidate that fell back must never win on horizontal distance alone");
    }

    /// <summary>The objective's second key, and the two edges of the first. A candidate that never lands inside the prediction budget is a failed climb too - on an isolated pillar that is a candidate that flew into the void - and among candidates that all failed the nearer one still wins, so the controller degrades to plain horizontal distance rather than to whichever candidate happens to be listed first.</summary>
    [Fact]
    public void AirborneObjective_TreatsANonLandingCandidateAsAFailedClimb_AndStillOrdersTheFailures()
    {
        var end = new Vec3d(0.5, 66.0, 0.5);

        LandingPrediction neverLanded = Landing(false, new Vec3d(0.5, 55.0, 0.5));
        LandingPrediction onTarget = Landing(true, new Vec3d(0.675, 66.0, 0.2985));
        LandingPrediction farFailure = Landing(true, new Vec3d(3.0, 65.0, 3.0));

        Assert.True(AscendTemplate.IsFailedClimb(neverLanded, end));
        Assert.True(AscendTemplate.IsBetterLanding(onTarget, neverLanded, end));
        Assert.False(AscendTemplate.IsBetterLanding(neverLanded, onTarget, end));

        // Both failed: the nearer failure is still the better one to hold.
        Assert.True(AscendTemplate.IsBetterLanding(neverLanded, farFailure, end));
        Assert.False(AscendTemplate.IsBetterLanding(farFailure, neverLanded, end));

        // Inside the tolerance is an arrival; past it is not. The tolerance is the completion gate's own
        // |dy| < 0.2, so a landing the gate would accept must not be scored as a failed climb.
        Assert.False(AscendTemplate.IsFailedClimb(Landing(true, new Vec3d(0.5, 66.19, 0.5)), end));
        Assert.True(AscendTemplate.IsFailedClimb(Landing(true, new Vec3d(0.5, 66.21, 0.5)), end));
    }

    private static LandingPrediction Landing(bool landed, Vec3d position)
        => new() { Landed = landed, TicksSimulated = 8, LandingPosition = position };

    /// <summary>The course's B4: a 5x5 pad, the centre raised one block, and the centre's four CARDINAL neighbours deleted so the only approach is diagonal.</summary>
    private static Run DriveDiagonalPad(Vec3d startPos, PhysicsProfile? profile = null)
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY, -2, 2, FloorY, 2, FixtureWorld.Stone);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);
        world.Set(1, FloorY, 0, FixtureWorld.Air);
        world.Set(-1, FloorY, 0, FixtureWorld.Air);
        world.Set(0, FloorY, 1, FixtureWorld.Air);
        world.Set(0, FloorY, -1, FixtureWorld.Air);

        return Drive(
            world, profile ?? Modern, new BlockPos(-1, FloorY + 1, -1), new BlockPos(0, FloorY + 2, 0), startPos);
    }

    /// <summary>The cardinal control: a plus shape with one-wide arms and the centre raised.</summary>
    private static Run DrivePlusShape()
    {
        var world = new FixtureWorld();
        world.Fill(1, FloorY, 0, 6, FloorY, 0, FixtureWorld.Stone);
        world.Fill(-6, FloorY, 0, -1, FloorY, 0, FixtureWorld.Stone);
        world.Fill(0, FloorY, 1, 0, FloorY, 6, FixtureWorld.Stone);
        world.Fill(0, FloorY, -6, 0, FloorY, -1, FixtureWorld.Stone);
        world.Set(0, FloorY, 0, FixtureWorld.Stone);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);

        return Drive(
            world, Modern, new BlockPos(-1, FloorY + 1, 0), new BlockPos(0, FloorY + 2, 0),
            new Vec3d(-0.3, FloorY + 1, 0.5), margin: 12);
    }

    /// <summary>Six chained diagonal ascends, each onto a 1x1 tread.</summary>
    private static Run DriveStairs()
    {
        var world = new FixtureWorld();
        world.Fill(-3, FloorY, -3, -1, FloorY, -1, FixtureWorld.Stone);
        for (int n = 0; n <= 5; n++)
            world.Set(n, FloorY + n + 1, n, FixtureWorld.Stone);

        return Drive(
            world, Modern, new BlockPos(-2, FloorY + 1, -2), new BlockPos(5, FloorY + 7, 5),
            new Vec3d(-1.5, FloorY + 1, -1.5), seedAtStartPos: false, margin: 12);
    }

    private static void AssertAscendLandsOnTheColumn(Run run, int maxTicks, double tolerance)
    {
        (int index, PathSegment segment) = FindSegment(run, MoveType.Ascend);
        SegmentOutcome outcome = OutcomeFor(run, index);

        Assert.False(outcome.Failed, Describe(run, "the diagonal ascend segment failed", index));
        Assert.True(
            outcome.Ticks <= maxTicks,
            Describe(run, $"the diagonal ascend took {outcome.Ticks} ticks (budget {maxTicks})", index));
        Assert.True(
            Math.Abs(outcome.Position.X - segment.End.X) <= tolerance
                && Math.Abs(outcome.Position.Z - segment.End.Z) <= tolerance,
            Describe(run, $"the diagonal ascend handed off at {outcome.Position}, not on column {segment.End}", index));
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(outcome.Position, segment.End),
            Describe(run, $"the diagonal ascend handed off at {outcome.Position}, centre outside the column", index));

        // Having reached the destination's elevation, the bot must not come back down off it. This is what separates a steered arc from an arc that overshot the column and fell to the pad below, which is

        bool reached = false;
        foreach (TickSample sample in run.Driver.Trace)
        {
            if (sample.SegmentIndex != index)
                continue;

            if (sample.Position.Y >= segment.End.Y)
            {
                reached = true;
                continue;
            }

            Assert.True(
                !reached || sample.Position.Y >= segment.End.Y - 0.1,
                Describe(run, $"the ascend dropped back to Y={sample.Position.Y:F4} after reaching {segment.End.Y:F1}", index));
        }

        Assert.True(reached, Describe(run, "the ascend never reached the destination's elevation", index));
    }

    private static string TraceOf(Run run, int segmentIndex)
    {
        var sb = new StringBuilder();
        foreach (TickSample sample in run.Driver.Trace)
        {
            if (sample.SegmentIndex != segmentIndex)
                continue;

            if (sb.Length > 0)
                sb.Append('\n');

            sb.Append(CultureInfo.InvariantCulture,
                $"{sample.Position.X:0.0000} {sample.Position.Y:0.0000} {sample.Position.Z:0.0000} {(sample.OnGround ? "G" : "-")}");
        }

        return sb.ToString();
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

    private static Run Drive(
        FixtureWorld world,
        PhysicsProfile profile,
        BlockPos start,
        BlockPos goal,
        Vec3d startPos,
        bool seedAtStartPos = true,
        int margin = 8)
    {
        PlanningWorldView view = world.Capture(start, goal, margin);
        var ctx = new PathExecutionContext(view, profile);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);

        var recorder = new SegmentRecorder();
        var driver = new ExecutionDriver(
            ctx, segments, startPos, startYaw: 0f, recorder, deviationThreshold: 1.5, seedAtStartPos);
        PathExecutorState state = driver.Run(maxTicks: 1500);
        return new Run(segments, recorder, driver, state);
    }

    private static string Describe(Run run, string headline, int focusIndex)
    {
        var sb = new StringBuilder();
        sb.Append(headline).Append(" | executor=").Append(run.State)
            .Append(" | ticks=").Append(run.Driver.Trace.Count)
            .Append(" | final=").Append(run.Driver.State.Position).AppendLine();
        for (int i = 0; i < run.Segments.Count; i++)
        {
            PathSegment s = run.Segments[i];
            sb.Append(i == focusIndex ? "  >> " : "     ")
                .Append(CultureInfo.InvariantCulture, $"seg[{i}] {s.MoveType,-8} {s.Start}->{s.End} exit={s.ExitTransition} h=({s.HeadingX},{s.HeadingZ})");
            foreach (SegmentOutcome outcome in run.Recorder.Outcomes)
                if (outcome.Index == i)
                    sb.Append(CultureInfo.InvariantCulture, $" -> {(outcome.Failed ? "FAILED" : "done")} in {outcome.Ticks} ticks at {outcome.Position}");

            sb.AppendLine();
        }

        if (focusIndex >= 0)
        {
            sb.Append("     focus segment trace:").AppendLine();
            sb.Append(TraceOf(run, focusIndex));
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
