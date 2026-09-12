using System.Globalization;
using System.Text;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

/// <summary>
/// Where a sprint jump LANDS, measured as a sweep over run-up phase rather than as a single scenario.
/// <para><see cref="SprintJumpTakeoffTests"/> pins when the bot leaves the ground; this suite pins where it comes down. Every case begins with a valid take-off so the assertions isolate landing control.</para>
/// <para>The mechanism, measured on the course's D7 pillar row (a 3-block hop onto a 1x1 pad). The bot leaves the ground at x = 454.5708 and must put its centre inside [456.3, 456.7] as Y crosses 100, so the arc it needs is 1.93 blocks, while an unbraked arc covers 3.57. The end plane is first reached at the apex (t26, y = 101.2522), 1.25 blocks above the pad, when the remaining 1.64 blocks cannot be recovered. Early air control is sufficient: Back at sprint sheds about 0.026 b/t per tick, removing 0.026*(1+..+10) = 1.43 blocks over ten airborne ticks in addition to the distance saved by releasing Forward.</para>
/// <para>One scenario cannot see this either. Ticks land 0.28062 blocks apart while sprinting and where they fall relative to the ledge depends on where the run-up began, so every case drives the same plan from 41 start positions 0.05 apart, spanning two whole blocks, and asserts on the count.</para>
/// </summary>
public sealed class SprintJumpLandingTests
{
    private const int FloorY = 64;

    private const float FacingPlusX = 270f;

    /// <summary>41 run-up phases 0.05 apart span 2.0 blocks, which is seven sprint ticks.</summary>
    private const int SweepPoints = 41;

    private const double SweepStep = 0.05;
    private const double SweepStartX = -13.5;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void NarrowPad_IsHitFromEveryRunUpPhase(int gap, int depth)
    {
        int landed = SweepLedge(gap, depth, out string detail);
        Assert.True(
            landed == SweepPoints,
            $"gap-{gap} depth-{depth} landed from only {landed} of {SweepPoints} run-up phases: {detail}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ChainedPillars_AllHandOffInsideTheirColumn(int gap)
    {
        int landed = 0;
        var missed = new List<string>();
        for (int i = 0; i < SweepPoints; i++)
        {
            double startX = SweepStartX + (i * SweepStep);
            if (RunChain(gap, startX, out string outcome))
                landed++;

            else if (missed.Count < 3)
                missed.Add(string.Create(CultureInfo.InvariantCulture, $"x0={startX:F2} {outcome}"));

        }

        Assert.True(
            landed == SweepPoints,
            $"chained gap-{gap} pillars: {landed} of {SweepPoints} run-up phases kept every handoff on its "
                + $"pillar: {string.Join(" | ", missed)}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    public void Gap4Residue_IsUndisturbed(int depth)
    {
        int landed = SweepLedge(gap: 4, depth, out string detail);
        Assert.True(landed >= 30, $"gap-4 depth-{depth} fell to {landed} of {SweepPoints}: {detail}");
    }

    [Fact]
    public void MaxRangeLeapOntoAOneWidePad_IsNotClosedByAirControl()
    {
        int landed = SweepLedge(gap: 3, depth: 1, out string detail);
        Assert.True(landed > 0, $"the 4-block leap onto a 1x1 pad landed from no phase at all: {detail}");
        Assert.True(landed < SweepPoints, $"gap-3 depth-1 unexpectedly reached {landed}/{SweepPoints}: {detail}");
    }

    /// <summary>The airborne objective's elevation key, tested directly because this fixture cannot exercise it. Instrumented over the whole 656-run sweep the key flipped the winner ZERO times (593/656 with it and 593/656 without, byte-identical in every one of the sixteen cells), and that is stated here rather than glossed. It is kept for the reason <see cref="AscendTemplate"/> keeps its own: it is what the objective MEANS, and it is what makes an arc that falls back onto the launch shelf lose to a real landing on the pad. A horizontal-distance-only objective prefers exactly that failure, because a fall-back can be nearer the destination than a scruffy arrival on it.</summary>
    /// <remarks>The objective is <see cref="AscendTemplate.IsBetterLanding"/> itself, called rather than copied so the parkour and the ascend cannot drift apart; the numbers below are a parkour arc's, not an ascend's (a 3-block hop from a shelf at y = 65 onto a pad at y = 65, two blocks out).</remarks>
    [Fact]
    public void AirborneObjective_RefusesAnArcThatFallsBackOntoTheLaunchShelf()
    {
        var pad = new Vec3d(2.5, 65.0, 0.5);

        // Shortened so hard it never left the shelf: horizontally nearer the pad than a rim landing, but a block low, so it is a failed jump.
        LandingPrediction fellShort = Landing(true, new Vec3d(2.2, 64.0, 0.5));

        // On the pad, but near its far rim: the scruffy arrival that still counts.
        LandingPrediction onTheRim = Landing(true, new Vec3d(2.95, 65.0, 0.5));

        Assert.True(AscendTemplate.IsFailedClimb(fellShort, pad));
        Assert.False(AscendTemplate.IsFailedClimb(onTheRim, pad));
        Assert.True(
            AscendTemplate.IsBetterLanding(onTheRim, fellShort, pad),
            "a landing on the pad must beat one that fell back to the shelf, however near it fell");
        Assert.False(
            AscendTemplate.IsBetterLanding(fellShort, onTheRim, pad),
            "a fall back to the shelf must never win on horizontal distance alone");
    }

    private static LandingPrediction Landing(bool landed, Vec3d position)
        => new() { Landed = landed, TicksSimulated = 8, LandingPosition = position };

    /// <summary>A take-off shelf x in [-20, 0] three wide in z, <paramref name="gap"/> empty columns, then a landing pad <paramref name="depth"/> blocks deep along x. Depth 1 is D7's 1x1 pillar, depth 2 a 2x1 ledge, depth 6 a 6x3 platform. Swept over every run-up phase.</summary>
    private static int SweepLedge(int gap, int depth, out string detail)
    {
        int landed = 0;
        var missed = new List<string>();
        for (int i = 0; i < SweepPoints; i++)
        {
            double startX = SweepStartX + (i * SweepStep);
            if (RunLedge(gap, depth, startX, out string outcome))
                landed++;

            else if (missed.Count < 3)
                missed.Add(string.Create(CultureInfo.InvariantCulture, $"x0={startX:F2} {outcome}"));

        }

        detail = missed.Count == 0 ? "every phase landed" : string.Join(" | ", missed);
        return landed;
    }

    private static bool RunLedge(int gap, int depth, double startX, out string outcome)
    {
        var world = new FixtureWorld();
        world.Fill(-20, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);

        int padStart = gap + 1;
        int padEnd = padStart + depth - 1;
        int zHalf = depth >= 6 ? 1 : 0;
        world.Fill(padStart, FloorY, -zHalf, padEnd, FloorY, zHalf, FixtureWorld.Stone);

        List<PathNode> nodes = RunUp();
        nodes.Add(Parkour(padStart));
        for (int x = padStart + 1; x <= padEnd; x++)
            nodes.Add(new PathNode(x, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });

        Run run = Drive(world, nodes, startX, observe: false);
        Vec3d end = run.Driver.State.Position;
        outcome = string.Create(
            CultureInfo.InvariantCulture, $"{run.State} end=({end.X:F3}, {end.Y:F3}, {end.Z:F3})");
        return run.State == PathExecutorState.Complete
            && end.X >= padEnd - 0.4
            && Math.Abs(end.Y - (FloorY + 1)) < 0.1;
    }

    /// <summary>D9's chain: pillar, pillar, then a wide platform, each leap the same length.</summary>
    private static bool RunChain(int gap, double startX, out string outcome)
    {
        var world = new FixtureWorld();
        world.Fill(-20, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);

        int pad1 = gap + 1;
        int pad2 = pad1 + gap + 1;
        int landing = pad2 + gap + 1;
        world.Set(pad1, FloorY, 0, FixtureWorld.Stone);
        world.Set(pad2, FloorY, 0, FixtureWorld.Stone);
        world.Fill(landing, FloorY, -4, landing + 8, FloorY, 4, FixtureWorld.Stone);

        List<PathNode> nodes = RunUp();
        nodes.Add(Parkour(pad1));
        nodes.Add(Parkour(pad2));
        nodes.Add(Parkour(landing));
        nodes.Add(new PathNode(landing + 1, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });
        nodes.Add(new PathNode(landing + 2, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });

        Run run = Drive(world, nodes, startX, observe: true);
        Vec3d end = run.Driver.State.Position;

        for (int i = 0; i < run.Segments.Count; i++)
        {
            if (run.Segments[i].MoveType != MoveType.Parkour)
                continue;

            SegmentOutcome? outcomeFor = OutcomeFor(run, i);
            if (outcomeFor is null || outcomeFor.Value.Failed
                || !SegmentGeometry.IsCenterInsideTargetBlock(outcomeFor.Value.Position, run.Segments[i].End))
            {
                outcome = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{run.State} leap {i} handed off at {Describe(outcomeFor)} for column {run.Segments[i].End}");
                return false;
            }
        }

        outcome = string.Create(
            CultureInfo.InvariantCulture, $"{run.State} end=({end.X:F3}, {end.Y:F3}, {end.Z:F3})");
        return run.State == PathExecutorState.Complete
            && end.X >= landing + 2 - 0.4
            && Math.Abs(end.Y - (FloorY + 1)) < 0.1;
    }

    private static string Describe(SegmentOutcome? outcome)
        => outcome is null
            ? "(never resolved)"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(outcome.Value.Failed ? "FAILED" : "done")} at {outcome.Value.Position}");

    private static List<PathNode> RunUp()
    {
        var nodes = new List<PathNode>();
        for (int x = -16; x <= 0; x++)
            nodes.Add(new PathNode(x, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });

        return nodes;
    }

    private static PathNode Parkour(int x)
        => new(x, FloorY + 1, 0) { MoveUsed = MoveType.Parkour, ParkourProfile = ParkourProfile.Default };

    /// <summary>Drives a hand-built node chain through the real <c>PathSegmentBuilder</c>, the real <c>PathExecutor</c> and the real <c>PlayerPhysics</c>, so the transitions and exit hints are the ones execution actually sees. Hand-built rather than planned because the planner will not offer most of these leaps, and widening its reach is not what is under test.</summary>
    private static Run Drive(FixtureWorld world, List<PathNode> nodes, double startX, bool observe)
    {
        PathNode last = nodes[^1];
        PlanningWorldView view = world.Capture(
            new BlockPos(-16, FloorY + 1, 0), new BlockPos(last.X + 2, FloorY + 1, 0), margin: 10);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes);

        var recorder = observe ? new SegmentRecorder() : null;
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(startX, FloorY + 1, 0.5), FacingPlusX, recorder, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 800);
        return new Run(segments, recorder, driver, state);
    }

    private static SegmentOutcome? OutcomeFor(Run run, int segmentIndex)
    {
        if (run.Recorder is null)
            return null;

        foreach (SegmentOutcome outcome in run.Recorder.Outcomes)
            if (outcome.Index == segmentIndex)
                return outcome;

        return null;
    }

    private readonly record struct SegmentOutcome(int Index, int Ticks, bool Failed, Vec3d Position);

    private sealed record Run(
        IReadOnlyList<PathSegment> Segments,
        SegmentRecorder? Recorder,
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
