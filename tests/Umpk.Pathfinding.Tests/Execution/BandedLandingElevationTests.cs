using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class BandedLandingElevationTests
{
    private const int FloorY = 64;
    private const double Tolerance = 0.2;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public BandedLandingElevationTests(ITestOutputHelper output) => _output = output;

    /// <summary>The slack is the neighbours' reach and nothing else: half a block over the abutting slab, zero over a uniform field of the same slab, zero on a staircase tread whose coplanar neighbours are air, and zero for the stone column that IS the high neighbour.</summary>
    [Fact]
    public void TheSlack_IsWhatTheCoplanarNeighboursReach()
    {
        PlanningWorldView abutting = Capture(AbuttingStep());
        Assert.Equal(0.5, PathSegmentBuilder.ResolveNeighbourSlack(abutting, 2, FloorY + 2, 0));
        Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(abutting, 3, FloorY + 2, 0));

        PlanningWorldView uniform = Capture(UniformSlab());
        Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(uniform, 2, FloorY + 2, 0));

        // One 1x1 stone tread over a bare plane: the coplanar neighbours are air, so nothing widens.
        var tread = new FixtureWorld();
        tread.Floor(-4, 8, -4, 4, FloorY);
        tread.Set(2, FloorY + 1, 0, FixtureWorld.Stone);
        Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(Capture(tread), 2, FloorY + 2, 0));

        // A fence next door reaches 1.5, past a cell's own top, and is not a floor a body settles on.
        var fenced = new FixtureWorld();
        fenced.Floor(-4, 8, -4, 4, FloorY);
        fenced.Fill(2, FloorY + 1, -4, 2, FloorY + 1, 4, FixtureWorld.BottomSlab);
        fenced.Set(3, FloorY + 1, 0, FixtureWorld.Fence);
        Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(Capture(fenced), 2, FloorY + 2, 0));
    }

    /// <summary>R1d, as an assertion on the gate rather than on the sweep. Every landing the sweep produced is classified three ways: by the band, by <c>|dy| &lt; 0.2</c> against the slab, and by <c>|dy| &lt; 0.2</c> against the stone.</summary>
    [Fact]
    public void TheBandAcceptsEveryLandingTheSweepProduces_WhereEitherAnchorAloneAcceptsAboutHalf()
    {
        PlanningWorldView view = Capture(AbuttingStep());
        double slack = PathSegmentBuilder.ResolveNeighbourSlack(view, 2, FloorY + 2, 0);
        var slabEnd = new Vec3d(2.5, FloorY + 1.5, 0.5);
        var stoneEnd = new Vec3d(2.5, FloorY + 2.0, 0.5);

        int landings = 0;
        int banded = 0;
        int slabAnchor = 0;
        int stoneAnchor = 0;
        foreach (double landY in Sweep(view))
        {
            landings++;
            banded += SegmentGeometry.IsAtEndElevation(landY, slabEnd, slack, Tolerance) ? 1 : 0;
            slabAnchor += SegmentGeometry.IsAtEndElevation(landY, slabEnd, 0.0, Tolerance) ? 1 : 0;
            stoneAnchor += SegmentGeometry.IsAtEndElevation(landY, stoneEnd, 0.0, Tolerance) ? 1 : 0;
        }

        _output.WriteLine(
            $"slack={slack}: {banded}/{landings} banded, {slabAnchor}/{landings} anchored on the slab, "
            + $"{stoneAnchor}/{landings} anchored on the stone");

        Assert.Equal(11, landings);
        Assert.Equal(5, slabAnchor);
        Assert.Equal(6, stoneAnchor);
        Assert.Equal(11, banded);
    }

    /// <summary>All 24 approach phases through the real <c>PathExecutor</c>, on a segment built by <c>PathSegmentBuilder</c> against the fixture. Every one completes, and every one completes ON THE SLAB.</summary>
    /// <remarks>
    /// <para><b>An honest boundary, measured here rather than assumed.</b> With the slack cleared these same 24 also complete, on the same slab, in the same 4 to 12 ticks. The straddle R1d measured does not reach the executor on this fixture, because <c>AscendTemplate</c>'s airborne controller is STEERING: every airborne tick it re-picks its input from the predicted landing against <c>ExpectedEnd</c>, which since the endpoint was resolved is the slab, so the arc is shortened onto the slab instead of running out onto the stone. R1d's sweep is open loop - a held Forward+Sprint+Jump - and open loop is what 6 of its 11 phases do.</para>
    /// <para>So the band is not what rescues this fixture, and saying it is would be a claim the evidence does not carry. What it does is make the GATE agree with the physics instead of depending on the controller winning every time: the landings the body really produces are the two elevations of <see cref="TheBandAcceptsEveryLandingTheSweepProduces_WhereEitherAnchorAloneAcceptsAboutHalf"/>, and an arrival gate that only recognises one of them is wrong about the other whatever the controller happens to achieve.</para>
    /// <para>The integer endpoint is the control that still discriminates: it completes by hopping the bot onto the stone, because <c>dy = 0.5 &gt; 0.1</c> keeps the jump gate open at every landing on the slab. That is arrival at the wrong elevation, reached by refusing the right one.</para>
    /// </remarks>
    [Fact]
    public void EveryApproachPhase_CompletesTheAscendOntoTheAbuttingSlab()
    {
        PlanningWorldView view = Capture(AbuttingStep());
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> resolved = PathSegmentBuilder.FromPath(AscendPath(), view);
        IReadOnlyList<PathSegment> unbanded = [resolved[0] with { EndElevationSlack = 0.0 }];
        IReadOnlyList<PathSegment> integer = PathSegmentBuilder.FromPath(AscendPath());

        int complete = 0;
        int completeUnbanded = 0;
        int onTheStoneWithAnIntegerEndpoint = 0;
        for (int i = 0; i < 24; i++)
        {
            double startX = 1.5 - (i * 0.05);

            (PathExecutorState banded, Vec3d at) = Execute(ctx, resolved, startX);
            complete += banded == PathExecutorState.Complete ? 1 : 0;
            Assert.Equal(FloorY + 1.5, at.Y, 9);

            completeUnbanded += Execute(ctx, unbanded, startX).State == PathExecutorState.Complete ? 1 : 0;
            onTheStoneWithAnIntegerEndpoint +=
                Math.Abs(Execute(ctx, integer, startX).Position.Y - (FloorY + 2.0)) < 1.0E-9 ? 1 : 0;
        }

        _output.WriteLine(
            $"{complete}/24 banded, {completeUnbanded}/24 with the slack cleared, "
            + $"{onTheStoneWithAnIntegerEndpoint}/24 finished on the STONE with an integer endpoint");

        Assert.Equal(24, complete);
        Assert.Equal(24, completeUnbanded);
        Assert.Equal(24, onTheStoneWithAnIntegerEndpoint);
    }

    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.Carpet)]
    [InlineData(FixtureWorld.LilyPad)]
    [InlineData(FixtureWorld.SnowLayerTwo)]
    public void TheBandNeverReachesTheCellAbove(int support)
    {
        var world = new FixtureWorld();
        world.Floor(-6, 14, -3, 3, FloorY);
        world.Fill(2, FloorY + 1, -3, 2, FloorY + 1, 3, support);
        world.Fill(3, FloorY + 1, -3, 14, FloorY + 1, 3, FixtureWorld.Stone);
        PlanningWorldView view = Capture(world);

        double end = PathSegmentBuilder.ResolveElevation(view, 2, FloorY + 2, 0);
        double slack = PathSegmentBuilder.ResolveNeighbourSlack(view, 2, FloorY + 2, 0);
        double bandTop = end + slack + Tolerance;

        _output.WriteLine($"end={end - FloorY} slack={slack} bandTop={bandTop - FloorY}");

        // The feet cell is FloorY + 2, so the band tops out at feetY + 0.2 whatever the support is, and the shallowest landing one whole cell too high sits at feetY + 1.
        Assert.Equal(FloorY + 2.2, bandTop, 9);
        Assert.True(
            FloorY + 3.0 - bandTop >= 0.8 - 1.0E-9,
            $"a cell-high overshoot must stay outside the band; the margin is {FloorY + 3.0 - bandTop}");
        Assert.False(SegmentGeometry.IsAtEndElevation(FloorY + 3.0, new Vec3d(2.5, end, 0.5), slack, Tolerance));
    }

    /// <summary>The candidate sort keeps its elevation key. With no slack, a landing that falls back to the take-off pad remains a failed climb and loses to any landing at the destination elevation, regardless of horizontal offset.</summary>
    [Fact]
    public void WithNoSlack_TheElevationKeyIsExactlyWhatItWas()
    {
        var end = new Vec3d(0.5, FloorY + 2, 0.5);
        LandingPrediction fellBack = Landing(new Vec3d(0.55, FloorY + 1, 0.55));
        LandingPrediction scruffy = Landing(new Vec3d(1.2, FloorY + 2, 0.5));

        Assert.True(AscendTemplate.IsFailedClimb(fellBack, end));
        Assert.False(AscendTemplate.IsFailedClimb(scruffy, end));
        Assert.True(AscendTemplate.IsBetterLanding(scruffy, fellBack, end));
        Assert.False(AscendTemplate.IsBetterLanding(fellBack, scruffy, end));
    }

    /// <summary>And with the abutting slab's slack it still discriminates in the direction that matters. The band opens UPWARD only: a landing on the neighbour's higher support counts as arrival, a fall back to the take-off pad below still does not, and neither does an overshoot onto a cell above the neighbour.</summary>
    [Fact]
    public void WithTheStraddleSlack_TheKeyStillRefusesAFallBackAndAnOvershoot()
    {
        var end = new Vec3d(2.5, FloorY + 1.5, 0.5);
        const double Slack = 0.5;

        LandingPrediction onTheSlab = Landing(new Vec3d(2.5, FloorY + 1.5, 0.5));
        LandingPrediction onTheStone = Landing(new Vec3d(2.9, FloorY + 2.0, 0.5));
        LandingPrediction fellBack = Landing(new Vec3d(1.6, FloorY + 1.0, 0.5));
        LandingPrediction overshot = Landing(new Vec3d(2.5, FloorY + 3.0, 0.5));

        Assert.False(AscendTemplate.IsFailedClimb(onTheSlab, end, Slack));
        Assert.False(AscendTemplate.IsFailedClimb(onTheStone, end, Slack));
        Assert.True(AscendTemplate.IsFailedClimb(fellBack, end, Slack));
        Assert.True(AscendTemplate.IsFailedClimb(overshot, end, Slack));

        // The stone landing is 0.4 further from the column centre than the slab one, and the key does not reorder them on that: both reached the destination, so the horizontal key decides.
        Assert.True(AscendTemplate.IsBetterLanding(onTheSlab, onTheStone, end, Slack));
        Assert.True(AscendTemplate.IsBetterLanding(onTheStone, fellBack, end, Slack));
        Assert.False(AscendTemplate.IsBetterLanding(fellBack, onTheStone, end, Slack));
    }

    /// <summary><c>SprintJumpTemplate</c>'s already-there arrival reads the same band. Its own fixtures are uniform pads where the slack is zero, so the arm is unchanged there; over the abutting slab a body that walked the gap and settled on the stone next door has arrived.</summary>
    [Fact]
    public void TheSprintJumpArrival_ReadsTheSameBand()
    {
        var end = new Vec3d(2.5, FloorY + 1.5, 0.5);

        Assert.True(SegmentGeometry.IsAtEndElevation(FloorY + 1.5, end, 0.0, Tolerance));
        Assert.False(SegmentGeometry.IsAtEndElevation(FloorY + 2.0, end, 0.0, Tolerance));
        Assert.True(SegmentGeometry.IsAtEndElevation(FloorY + 2.0, end, 0.5, Tolerance));
        Assert.False(SegmentGeometry.IsAtEndElevation(FloorY + 1.0, end, 0.5, Tolerance));
    }

    [Fact]
    public void TheDiagonalStaircase_CarriesNoSlackOnAnyTread()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 12, FloorY);
        for (int i = 1; i <= 6; i++)
            world.Fill(i, FloorY + 1, i, 12, FloorY + i, 12, FixtureWorld.Stone);

        PlanningWorldView view = world.Capture(
            new BlockPos(-4, FloorY - 2, -4), new BlockPos(12, FloorY + 10, 12), margin: 2);

        for (int i = 1; i <= 6; i++)
            Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(view, i, FloorY + i + 1, i));

    }

    private static FixtureWorld AbuttingStep()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 14, -3, 3, FloorY);
        world.Fill(2, FloorY + 1, -3, 2, FloorY + 1, 3, FixtureWorld.BottomSlab);
        world.Fill(3, FloorY + 1, -3, 14, FloorY + 1, 3, FixtureWorld.Stone);
        return world;
    }

    private static FixtureWorld UniformSlab()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 14, -3, 3, FloorY);
        world.Fill(2, FloorY + 1, -3, 14, FloorY + 1, 3, FixtureWorld.BottomSlab);
        return world;
    }

    private static PlanningWorldView Capture(FixtureWorld world)
        => world.Capture(new BlockPos(-6, FloorY - 4, -4), new BlockPos(14, FloorY + 10, 12), margin: 2);

    private static IReadOnlyList<PathNode> AscendPath() =>
    [
        new PathNode(1, FloorY + 1, 0) { GCost = 0.0 },
        new PathNode(2, FloorY + 2, 0)
        {
            GCost = ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty,
            MoveUsed = MoveType.Ascend,
        },
    ];

    private static (PathExecutorState State, Vec3d Position) Execute(
        PathExecutionContext ctx, IReadOnlyList<PathSegment> segments, double startX)
    {
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(startX, FloorY + 1, 0.5), 270f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(400);
        return (state, driver.State.Position);
    }

    /// <summary>R1d's sweep, over the phases that land with the centre inside the slab's column.</summary>
    private static IEnumerable<double> Sweep(PlanningWorldView view)
    {
        for (int i = 0; i < 24; i++)
            if (Landed(view, 1.5 - (i * 0.05), out double landY))
                yield return landY;

    }

    private static bool Landed(PlanningWorldView view, double startX, out double landingY)
    {
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(startX, FloorY + 1, 0.5), 270f, 0f);

        bool airborne = false;
        for (int t = 0; t < 80; t++)
        {
            PhysicsState before = engine.State;
            var input = new MovementInput { Forward = true, Sprint = true };
            if (before.OnGround && (FloorY + 1.5) - before.Position.Y > 0.1)
                input = input with { Jump = true };

            engine.Step(input);
            PhysicsState after = engine.State;
            if (!after.OnGround)
            {
                airborne = true;
                continue;
            }

            if (airborne && after.Position.X >= 2.0 && after.Position.X <= 3.0)
            {
                landingY = after.Position.Y;
                return true;
            }
        }

        landingY = double.NaN;
        return false;
    }

    private static LandingPrediction Landing(Vec3d at) => new() { Landed = true, LandingPosition = at };
}
