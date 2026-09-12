using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ResolvedSegmentElevationTests
{
    private const int FloorY = 64;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public ResolvedSegmentElevationTests(ITestOutputHelper output) => _output = output;

    /// <summary>M5a's thirteen supports, with the height a centred body rests at and whether the current <c>|dy| &lt; 0.2</c> gate can see that landing when the endpoint carries the integer node Y (<c>1 - h &lt;= 0.125</c>, i.e. everything at or above 0.875).</summary>
    public static TheoryData<int, double, bool> M5aSupports() => new()
    {
        { FixtureWorld.Stone, 1.0, true },
        { FixtureWorld.DirtPath, 0.9375, true },
        { FixtureWorld.Honey, 0.9375, true },
        { FixtureWorld.SoulSand, 0.875, true },
        { FixtureWorld.TopSlab, 1.0, true },
        { FixtureWorld.StairsBottom, 1.0, true },
        { FixtureWorld.StairsTop, 1.0, true },
        { FixtureWorld.BottomSlab, 0.5, false },
        { FixtureWorld.SnowLayerFive, 0.5, false },
        { FixtureWorld.SnowLayer, 0.375, false },
        { FixtureWorld.SnowLayerTwo, 0.125, false },
        { FixtureWorld.LilyPad, 0.09375, false },
        { FixtureWorld.Carpet, 0.0625, false },
    };

    /// <summary>The endpoint carries the elevation the body rests at, not the cell index above the support.</summary>
    [Theory]
    [MemberData(nameof(M5aSupports))]
    public void ResolvedEndpoint_CarriesTheSupportsOwnTop(int support, double height, bool reachableUnderTheGate)
    {
        FixtureWorld world = Step(support);
        PlanningWorldView view = Capture(world);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(AscendPath(), view);

        PathSegment ascend = segments[0];
        Assert.Equal(FloorY + 1, ascend.Start.Y);
        Assert.Equal(FloorY + 1 + height, ascend.End.Y);

        // The logical cells are untouched by the resolution: that is what S0's consumers read.
        Assert.Equal(FloorY + 1, ascend.StartFeetY);
        Assert.Equal(FloorY + 2, ascend.EndFeetY);
        Assert.Equal(reachableUnderTheGate, Math.Abs((FloorY + 2) - ascend.End.Y) < 0.2);
    }

    /// <summary>M5a itself: the ascend onto each support, executed.</summary>
    [Theory]
    [MemberData(nameof(M5aSupports))]
    public void AnAscendOntoEverySupport_CompletesWithTheResolvedEndpoint(
        int support, double height, bool reachableUnderTheGate)
    {
        FixtureWorld world = Step(support);
        PlanningWorldView view = Capture(world);
        var ctx = new PathExecutionContext(view, Profile);

        var driver = new ExecutionDriver(
            ctx, PathSegmentBuilder.FromPath(AscendPath(), view), new Vec3d(0.5, FloorY + 1, 0.5), 270f);
        PathExecutorState state = driver.Run(400);

        _output.WriteLine(
            $"h={height} gateSeesItWithAnIntegerEndpoint={reachableUnderTheGate}: "
            + $"{state} in {driver.Trace.Count} ticks at {driver.State.Position}");

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.Equal(FloorY + 1 + height, driver.State.Position.Y, 9);
    }

    [Theory]
    [MemberData(nameof(M5aSupports))]
    public void TheSameAscend_WithAnIntegerEndpoint_FailsForEverySupportUnderZeroPointEight(
        int support, double height, bool reachableUnderTheGate)
    {
        FixtureWorld world = Step(support);
        PlanningWorldView view = Capture(world);
        var ctx = new PathExecutionContext(view, Profile);

        var driver = new ExecutionDriver(
            ctx, PathSegmentBuilder.FromPath(AscendPath()), new Vec3d(0.5, FloorY + 1, 0.5), 270f);
        PathExecutorState state = driver.Run(400);

        _output.WriteLine($"h={height}: integer endpoint -> {state} in {driver.Trace.Count} ticks");
        Assert.Equal(
            reachableUnderTheGate ? PathExecutorState.Complete : PathExecutorState.Failed, state);
    }

    /// <summary>A support cell with nothing to stand on leaves the endpoint alone. A swim node's feet cell is water over water, a climb node's is a ladder shaft, and neither rests on a box; resolving those to <c>feetY - 1</c> would drop every such endpoint a whole block.</summary>
    [Theory]
    [InlineData(FixtureWorld.Air, "nothing under the feet at all")]
    [InlineData(FixtureWorld.Water, "a swim node: water has no collision shape")]
    [InlineData(FixtureWorld.Ladder, "a rung: the fixture's ladder carries no collision plate")]
    [InlineData(FixtureWorld.SnowLayerOne, "the empty shape: a support of exactly zero")]
    [InlineData(FixtureWorld.Fence, "1.5, past the cell's own top")]
    public void AnUnsupportedColumn_KeepsTheIntegerFeetCell(int stateId, string why)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 8, -4, 4, FloorY - 2);
        world.Fill(-4, FloorY, -4, 8, FloorY, 4, stateId);
        PlanningWorldView view = Capture(world);

        Assert.Equal(FloorY + 1, PathSegmentBuilder.ResolveElevation(view, 2, FloorY + 1, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void TheAbuttingSlab_SplitsItsLandingsBetweenTwoElevations()
    {
        PlanningWorldView view = Capture(AbuttingStep());
        int onTheSlab = 0;
        int onTheStone = 0;
        int landings = 0;

        for (int i = 0; i < 24; i++)
        {
            double startX = 1.5 - (i * 0.05);
            if (!TryLand(view, startX, out double landY))
                continue;

            landings++;
            onTheSlab += Math.Abs(landY - (FloorY + 1.5)) < 0.2 ? 1 : 0;
            onTheStone += Math.Abs(landY - (FloorY + 2.0)) < 0.2 ? 1 : 0;
        }

        _output.WriteLine($"{landings} landings inside the column: {onTheSlab} on the slab, {onTheStone} on the stone");

        Assert.Equal(11, landings);
        Assert.Equal(5, onTheSlab);
        Assert.Equal(6, onTheStone);
        Assert.Equal(landings, onTheSlab + onTheStone);
    }

    /// <summary>The endpoint itself stays the DESTINATION column's own elevation through all of that, because it is what the templates steer at. R4e's own numbers: the body rests at 1.5 while its centre is at or below x = 2.7 and at 2.0 from x = 2.75, and the endpoint names 1.5 either way.</summary>
    [Fact]
    public void TheAbuttingSlabsEndpoint_IsTheSlabsOwnElevation()
    {
        PlanningWorldView view = Capture(AbuttingStep());

        // The slab column names 1.5; the stone next door, one whole cell higher in the plan, names 2.0.
        Assert.Equal(FloorY + 1.5, PathSegmentBuilder.ResolveElevation(view, 2, FloorY + 2, 0));
        Assert.Equal(FloorY + 2.0, PathSegmentBuilder.ResolveElevation(view, 3, FloorY + 2, 0));
    }

    /// <summary>The M5b/M5c shape: a whole street of one support family, planned and executed end to end. The integer endpoint fails the street at its first partial step; the resolved one walks it.</summary>
    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.Carpet)]
    [InlineData(FixtureWorld.SnowLayerTwo)]
    [InlineData(FixtureWorld.LilyPad)]
    public void ASupportStreet_IsWalkedEndToEnd(int support)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, 0, 6, FloorY + 1, 0, support);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(6, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(
            ctx, PathSegmentBuilder.FromPath(result.Path, view), new Vec3d(0.5, FloorY + 1, 0.5), 270f);
        PathExecutorState state = driver.Run(1200);

        _output.WriteLine($"street of {support}: {state} in {driver.Trace.Count} ticks at {driver.State.Position}");
        Assert.Equal(PathExecutorState.Complete, state);
    }

    /// <summary>A bare floor plane with the support laid on top of it from x = 1 onward.</summary>
    private static FixtureWorld Step(int support)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, -4, 14, FloorY + 1, 4, support);
        return world;
    }

    /// <summary>R1's geometry: one slab column at x = 2 with a full block one higher from x = 3.</summary>
    private static FixtureWorld AbuttingStep()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 14, -3, 3, FloorY);
        world.Fill(2, FloorY + 1, -3, 2, FloorY + 1, 3, FixtureWorld.BottomSlab);
        world.Fill(3, FloorY + 1, -3, 14, FloorY + 1, 3, FixtureWorld.Stone);
        return world;
    }

    private static PlanningWorldView Capture(FixtureWorld world)
        => world.Capture(new BlockPos(-6, FloorY - 4, -4), new BlockPos(14, FloorY + 8, 4), margin: 2);

    /// <summary>One Ascend from the bare plane at x = 0 onto the support at x = 1.</summary>
    private static IReadOnlyList<PathNode> AscendPath() =>
    [
        new PathNode(0, FloorY + 1, 0) { GCost = 0.0 },
        new PathNode(1, FloorY + 2, 0) { GCost = ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty, MoveUsed = MoveType.Ascend },
    ];

    /// <summary>R1d's sweep body: sprint +X from <paramref name="startX"/>, jumping while the slab's own elevation is more than 0.1 above the feet, and report the first grounded tick with the centre inside the slab's column.</summary>
    private static bool TryLand(PlanningWorldView view, double startX, out double landingY)
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
}
