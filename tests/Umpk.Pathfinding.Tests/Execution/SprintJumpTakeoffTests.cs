using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SprintJumpTakeoffTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    /// <summary>41 run-up phases 0.05 apart span 2.0 blocks, which is seven sprint ticks.</summary>
    private const int SweepPoints = 41;

    private const double SweepStep = 0.05;
    private const double SweepStartX = -13.5;

    /// <summary>The four-block gap, which is the row the gate is for: 3 of 41 before, 33 of 41 after.</summary>
    /// <remarks>The bound is 30 rather than 41 because the remaining eight are not a gate problem. Their coyote tick lands at the far end of its own window (x = 1.30 to 1.35 against a ledge at 1.0, so the whole 0.6-wide footprint is already past it), and from there the arc finishes a tenth of a block short: they end at x = 4.700, which is the collider half-width short of the landing column's near face, having hit its side. Buying those back means a longer arc, and every knob that would do it (MaxJumpDistance, the arc-clearance margins) is one the descend and ascend packages calibrated against live refusal rows. Out of scope here.</remarks>
    [Fact]
    public void Gap4_IsCrossedFromMostRunUpPhases()
    {
        int crossed = Sweep(gap: 4, out string detail);
        Assert.True(crossed >= 30, $"gap-4 crossed from {crossed} of {SweepPoints} run-up phases: {detail}");
    }

    /// <summary>Short and middling gaps must succeed from all 41 run-up phases. On a one- or two-block gap the target is already within reach from the launch block, so a coyote-only gate would unnecessarily walk to the rim and shorten the arc.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ShortGaps_AreStillCrossedFromEveryRunUpPhase(int gap)
    {
        int crossed = Sweep(gap, out string detail);
        Assert.True(
            crossed == SweepPoints,
            $"gap-{gap} crossed from only {crossed} of {SweepPoints} run-up phases: {detail}");
    }

    [Fact]
    public void FootprintSupport_RunsOutWhenTheTrailingCornerClearsTheRim()
    {
        var world = new FixtureWorld();
        world.Fill(-8, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        PlanningWorldView view = world.Capture(
            new BlockPos(-4, FloorY + 1, 0), new BlockPos(0, FloorY + 1, 0), margin: 8);
        const double Feet = FloorY + 1;

        Assert.True(Supported(view, 0.5, Feet), "standing on the middle of the ledge block");
        Assert.True(Supported(view, 0.9, Feet), "leading corner over the void, trailing corner still on");
        Assert.True(Supported(view, 1.29, Feet), "the coyote tick: only a sliver of the footprint is left");
        Assert.False(Supported(view, 1.31, Feet), "the whole footprint has cleared the rim");
        Assert.False(Supported(view, 0.5, Feet + 1.0), "a player floating a block above the ledge");
    }

    private static bool Supported(PlanningWorldView view, double x, double feetY)
        => SegmentGeometry.HasSupportUnderFootprint(view, new Vec3d(x, feetY, 0.5));

    /// <summary>Drives the same parkour plan from <see cref="SweepPoints"/> run-up positions and counts how many land on the far platform.</summary>
    private static int Sweep(int gap, out string detail)
    {
        int landingX = gap + 1;
        int crossed = 0;
        var missed = new List<string>();
        for (int i = 0; i < SweepPoints; i++)
        {
            double startX = SweepStartX + (i * SweepStep);
            if (Cross(landingX, startX, out string outcome))
                crossed++;

            else if (missed.Count < 4)
                missed.Add(string.Create(CultureInfo.InvariantCulture, $"x0={startX:F2} {outcome}"));

        }

        detail = missed.Count == 0 ? "every phase landed" : string.Join(" | ", missed);
        return crossed;
    }

    private static bool Cross(int landingX, double startX, out string outcome)
    {
        var world = new FixtureWorld();
        // Take-off shelf up to x = 0, the gap, then a platform that carries on past the landing block so the parkour segment hands off into a walk rather than into a final stop.
        world.Fill(-20, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);
        world.Fill(landingX, FloorY, -4, landingX + 8, FloorY, 4, FixtureWorld.Stone);

        PlanningWorldView view = world.Capture(
            new BlockPos(-16, FloorY + 1, 0), new BlockPos(landingX + 2, FloorY + 1, 0), margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(BuildPath(landingX));
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(startX, FloorY + 1, 0.5), FacingPlusX, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 400);

        Vec3d end = driver.State.Position;
        outcome = string.Create(
            CultureInfo.InvariantCulture, $"{state} takeoffX={TakeoffX(driver):F4} end=({end.X:F3}, {end.Y:F3})");

        // Landed on the far platform, at the platform's own level rather than down in the gap.
        return state == PathExecutorState.Complete
            && end.X >= landingX
            && Math.Abs(end.Y - (FloorY + 1)) < 0.1;
    }

    /// <summary>Where the bot stood on the last tick it was still on the ground.</summary>
    private static double TakeoffX(ExecutionDriver driver)
    {
        for (int t = 1; t < driver.Trace.Count; t++)
            if (driver.Trace[t - 1].OnGround && !driver.Trace[t].OnGround)
                return driver.Trace[t - 1].Position.X;

        return double.NaN;
    }

    /// <summary>The node chain the real planner would produce: a run-up of one-block traverses to the ledge block at x = 0, the parkour leap to the landing block, then two traverses so the leap hands off into a walk. Built by hand and pushed through the real <c>PathSegmentBuilder</c>, so the transitions and exit hints are the ones execution actually sees. The planner itself will not offer a four-block leap, and widening its reach is out of scope: this is a test of the takeoff gate, not of what the search is willing to plan.</summary>
    private static List<PathNode> BuildPath(int landingX)
    {
        var nodes = new List<PathNode>();
        for (int x = -16; x <= 0; x++)
            nodes.Add(new PathNode(x, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });

        nodes.Add(new PathNode(landingX, FloorY + 1, 0) { MoveUsed = MoveType.Parkour });
        nodes.Add(new PathNode(landingX + 1, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });
        nodes.Add(new PathNode(landingX + 2, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });
        return nodes;
    }
}
