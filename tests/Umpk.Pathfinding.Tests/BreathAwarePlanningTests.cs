using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

/// <summary>Air as a banded dimension of the A* node key.</summary>
/// <remarks>
/// <para>The validator can only judge the one route the search returned, so it gets a bore with air bells right by accident when the bells sit on the shortest line and wrong when they do not. Choosing where to detour belongs in the search; the third field of the node key makes that choice possible.</para>
/// <para>The deficit is priced at the SLOWEST water rate on every era - 10.204 ticks a block - which is at least what <c>BreathValidator</c> charges. That inequality is deliberate: it makes every route this search approves a route the validator also approves, which turns the validator from a second opinion into a runtime assertion that should never fire.</para>
/// </remarks>
public sealed class BreathAwarePlanningTests
{
    private const int FloorY = 64;

    private static readonly PathfinderOptions NotBreathAware = PathfinderOptions.Default with { BreathAware = false };

    private readonly ITestOutputHelper _output;

    public BreathAwarePlanningTests(ITestOutputHelper output) => _output = output;

    /// <summary>A lidded flooded bore too long for one lung, with an air bell OFF the direct line: a six-cell side branch at the halfway point whose head cells are air, so a body in it is breathing.</summary>
    private static FixtureWorld BoreWithAnOffLineBell(int length, int bellAt, int bellDepth)
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 1, -2, length + 3, FloorY + 4, bellDepth + 2, FixtureWorld.Stone);
        world.Fill(-3, FloorY + 1, 0, length + 2, FloorY + 2, 0, FixtureWorld.Air);
        world.Fill(0, FloorY + 1, 0, length - 1, FloorY + 2, 0, FixtureWorld.Water);

        // The bell: feet in water, head in air, reachable only by leaving the bore's own line.
        world.Fill(bellAt, FloorY + 1, 1, bellAt, FloorY + 1, bellDepth, FixtureWorld.Water);
        world.Fill(bellAt, FloorY + 2, 1, bellAt, FloorY + 2, bellDepth, FixtureWorld.Air);
        return world;
    }

    /// <summary>A big open body of water: the node-explosion case, where every cell is submerged.</summary>
    private static FixtureWorld OpenWater(int span, int depth)
    {
        var world = new FixtureWorld();
        world.Fill(-span - 2, FloorY - depth - 2, -span - 2, span + 2, FloorY + 4, span + 2, FixtureWorld.Stone);
        world.Fill(-span, FloorY - depth, -span, span, FloorY + 3, span, FixtureWorld.Air);
        world.Fill(-span, FloorY - depth, -span, span, FloorY + 1, span, FixtureWorld.Water);
        return world;
    }

    /// <summary>An ordinary dry plain with a wall to route round: no water anywhere.</summary>
    private static FixtureWorld DryPlain()
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY, -12, 40, FloorY, 12, FixtureWorld.Stone);
        world.Fill(16, FloorY + 1, -6, 16, FloorY + 4, 6, FixtureWorld.Stone);
        return world;
    }

    private static PathResult Plan(FixtureWorld world, BlockPos start, BlockPos goal, PathfinderOptions options)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        return PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
    }

    /// <summary>The forty-block bore is 408 real ticks of a 300-tick lung on the straight line, and the bell is three cells off it. A post-hoc validator can only refuse; the dimension routes.</summary>
    [Fact]
    public void BoreWithAnOffLineBell_RoutesThroughTheBell()
    {
        FixtureWorld world = BoreWithAnOffLineBell(length: 40, bellAt: 20, bellDepth: 6);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(41, FloorY + 1, 0);

        PathResult flat = Plan(world, start, goal, NotBreathAware);
        PathResult aware = Plan(world, start, goal, PathfinderOptions.Default);

        // Without the dimension the search takes the straight line, which is exactly the route the validator then has to refuse.
        Assert.Equal(PathStatus.Success, flat.Status);
        Assert.DoesNotContain(flat.Path, node => node.Z != 0);

        _output.WriteLine(
            $"off-line bell: flat {flat.NodesExplored} nodes / cost {flat.Cost:F1} / {flat.Path.Count} path nodes, "
            + $"breath-aware {aware.NodesExplored} nodes / cost {aware.Cost:F1} / {aware.Path.Count} path nodes "
            + $"({(double)aware.NodesExplored / Math.Max(1, flat.NodesExplored):F2}x)");

        Assert.Equal(PathStatus.Success, aware.Status);
        Assert.Contains(aware.Path, node => node.Z > 0);
    }

    /// <summary>The goal heuristic charges <see cref="ActionCosts.SprintOneBlock"/> a block and a swim costs <see cref="ActionCosts.SwimOneBlock"/>, so in water the heuristic under-charges by 2.55 and the search degenerates toward Dijkstra exactly where the branching is highest.</summary>
    /// <remarks>Raising the heuristic in water is an admissibility question: it has to stay under the CHEAPEST swim, which the flow-aware cost has just made 5.303 ticks a block dead downstream rather than a flat 9.0909. The printed measurements provide the evidence needed for a separate admissibility analysis.</remarks>
    [Fact]
    public void WaterHeuristicUnderCharge_IsRecordedForPhaseFive()
    {
        FixtureWorld wet = OpenWater(span: 14, depth: 8);
        FixtureWorld dry = DryPlain();

        PathResult wetPlan = Plan(wet, new BlockPos(-13, FloorY + 1, -13), new BlockPos(13, FloorY + 1, 13), NotBreathAware);
        PathResult dryPlan = Plan(dry, new BlockPos(0, FloorY + 1, 0), new BlockPos(38, FloorY + 1, 0), NotBreathAware);

        double perPathNodeWet = (double)wetPlan.NodesExplored / Math.Max(1, wetPlan.Path.Count);
        double perPathNodeDry = (double)dryPlan.NodesExplored / Math.Max(1, dryPlan.Path.Count);

        _output.WriteLine(
            $"heuristic under-charge in water: {ActionCosts.SwimOneBlock / ActionCosts.SprintOneBlock:F3}x; "
            + $"open water {wetPlan.NodesExplored} nodes over a {wetPlan.Path.Count}-node route "
            + $"({perPathNodeWet:F1} explored per path node), dry plain {dryPlan.NodesExplored} over "
            + $"{dryPlan.Path.Count} ({perPathNodeDry:F1} per path node)");

        Assert.Equal(2.551, ActionCosts.SwimOneBlock / ActionCosts.SprintOneBlock, 3);
        Assert.True(
            perPathNodeWet > perPathNodeDry,
            $"water explored {perPathNodeWet:F1} nodes per path node against dry's {perPathNodeDry:F1}");
    }

    /// <summary>Every route returned by the breath-aware search must satisfy the independent validator.</summary>
    [Fact]
    public void BreathAwarePlan_NeverFailsTheValidator()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal)[] cases =
        [
            (BoreWithAnOffLineBell(40, 20, 6), new BlockPos(-2, FloorY + 1, 0), new BlockPos(41, FloorY + 1, 0)),
            (BoreWithAnOffLineBell(20, 10, 4), new BlockPos(-2, FloorY + 1, 0), new BlockPos(21, FloorY + 1, 0)),
            (OpenWater(12, 6), new BlockPos(-11, FloorY + 1, -11), new BlockPos(11, FloorY + 1, 11)),
            (DryPlain(), new BlockPos(0, FloorY + 1, 0), new BlockPos(38, FloorY + 1, 0)),
        ];

        foreach ((FixtureWorld world, BlockPos start, BlockPos goal) in cases)
        {
            PlanningWorldView view = world.Capture(start, goal, margin: 8);
            PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
            if (result.Status != PathStatus.Success)
                continue;

            IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
            BreathValidation validation = BreathValidator.Validate(
                segments, view, PhysicsProfile.ForProtocol(772), allowSprint: true, (int)BreathModel.FullLungTicks);

            Assert.True(
                validation.IsSurvivable,
                $"a breath-aware plan from {start} to {goal} peaked at "
                + $"{validation.PeakDeficitTicks:F1} ticks of deficit, violating at segment "
                + $"{validation.FirstViolationSegment}");
        }
    }

    /// <summary>The dimension multiplies states only where they are submerged, so the honest risk is an ocean. Bound it: under four times the nodes the same search explores without it.</summary>
    [Fact]
    public void NodeExplosion_StaysUnderBudget()
    {
        FixtureWorld world = OpenWater(span: 14, depth: 8);
        var start = new BlockPos(-13, FloorY + 1, -13);
        var goal = new BlockPos(13, FloorY + 1, 13);

        PathResult flat = Plan(world, start, goal, NotBreathAware);
        PathResult aware = Plan(world, start, goal, PathfinderOptions.Default);

        _output.WriteLine(
            $"open water 29x29x9: flat {flat.NodesExplored} nodes / cost {flat.Cost:F1}, "
            + $"breath-aware {aware.NodesExplored} nodes / cost {aware.Cost:F1} "
            + $"({(double)aware.NodesExplored / Math.Max(1, flat.NodesExplored):F2}x)");

        Assert.Equal(PathStatus.Success, aware.Status);
        Assert.True(
            aware.NodesExplored < flat.NodesExplored * 4,
            $"the air dimension explored {aware.NodesExplored} nodes against {flat.NodesExplored}");
    }

    /// <summary>A plan over terrain with no water in it must be BIT-IDENTICAL with the dimension on and off: same node count, same cost, same route. The deficit is 0 at every node, every node bands to 0, and the key is the key it always was.</summary>
    [Fact]
    public void ADryPlan_IsUnchangedByTheDimension()
    {
        FixtureWorld world = DryPlain();
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(38, FloorY + 1, 0);

        PathResult flat = Plan(world, start, goal, NotBreathAware);
        PathResult aware = Plan(world, start, goal, PathfinderOptions.Default);

        _output.WriteLine($"dry plain: flat {flat.NodesExplored} nodes, breath-aware {aware.NodesExplored} nodes");

        Assert.Equal(PathStatus.Success, flat.Status);
        Assert.Equal(flat.NodesExplored, aware.NodesExplored);
        Assert.Equal(flat.Cost, aware.Cost, 10);
        Assert.Equal(flat.Path.Count, aware.Path.Count);
        for (int i = 0; i < flat.Path.Count; i++)
        {
            Assert.Equal(flat.Path[i].X, aware.Path[i].X);
            Assert.Equal(flat.Path[i].Y, aware.Path[i].Y);
            Assert.Equal(flat.Path[i].Z, aware.Path[i].Z);
        }
    }

    /// <summary>The band rounds UP, which is the whole soundness argument: two routes that share a key are credited with the WORSE of their two deficits, so the search can refuse a survivable route and can never approve an unsurvivable one.</summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-5.0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(40.0, 1)]
    [InlineData(40.1, 2)]
    [InlineData(299.0, 8)]
    [InlineData(300.0, 8)]
    public void Band_RoundsUp(double deficit, int expected)
        => Assert.Equal(expected, BreathModel.Band(deficit));
}
