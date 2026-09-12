using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>A* returns the cheapest route only while its heuristic is ADMISSIBLE (never over-states the true remaining cost) and CONSISTENT (never drops across one edge by more than that edge costs). Both are properties of the pair (heuristic, cost model), so neither can be argued from the heuristic alone, and this file tests the pair: every edge the REAL expanders emit over real terrain, against the heuristic the real goals hand the search.</summary>
/// <remarks>
/// <para>Consistency is the stronger property and it implies admissibility for a heuristic that is zero at the goal, so it is what is tested. Because the heuristic is a function of the DISPLACEMENT to the goal and obeys the triangle inequality (<see cref="TheHeuristic_ObeysTheTriangleInequality"/>), the most it can fall across an edge is its own value on that edge's displacement. So consistency reduces to one inequality per edge: <c>h(displacement) &lt;= cost</c>.</para>
/// <para>Everything here goes through <see cref="GoalBlock.Heuristic"/>, the entry point the search itself calls, rather than through any internal helper, so the assertions cannot be satisfied by a refactor that leaves the search's own number where it was.</para>
/// </remarks>
public sealed class HeuristicAdmissibilityTests
{
    private const double Tolerance = 1.0E-9;

    /// <summary>The heuristic the search uses, for a displacement from a node to the goal.</summary>
    private static double H(int dx, int dy, int dz)
        => new GoalBlock(0, 0, 0).Heuristic(new BlockPos(dx, dy, dz));

    [Fact]
    public void ACardinalStepAscend_IsNotOverchargedByTheHeuristic()
    {
        double heuristic = H(1, 1, 0);
        double edge = ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty;

        Assert.True(
            heuristic <= edge + Tolerance,
            $"h({1},{1},{0}) = {heuristic:0.####} over-states a cardinal step-ascend edge of {edge:0.####}");
    }

    /// <summary>The same for the diagonal step-ascend, whose edge carries the same jump penalty over a <see cref="ActionCosts.DiagonalMultiplier"/> horizontal.</summary>
    [Fact]
    public void ADiagonalStepAscend_IsNotOverchargedByTheHeuristic()
    {
        double heuristic = H(1, 1, 1);
        double edge = (ActionCosts.SprintOneBlock * ActionCosts.DiagonalMultiplier) + ActionCosts.JumpPenalty;

        Assert.True(
            heuristic <= edge + Tolerance,
            $"h(1,1,1) = {heuristic:0.####} over-states a diagonal step-ascend edge of {edge:0.####}");
    }

    /// <summary>A parkour ascend over a diagonal (2, 1) shape: the heuristic's octile horizontal is already LONGER than the euclidean distance the move is priced on, and the separate vertical term is what pushes it past the two jump penalties as well.</summary>
    [Fact]
    public void ADiagonalParkourAscend_IsNotOverchargedByTheHeuristic()
    {
        double heuristic = H(2, 1, 1);
        double edge = (Math.Sqrt(5.0) * ActionCosts.SprintOneBlock) + (ActionCosts.JumpPenalty * 2);

        Assert.True(
            heuristic <= edge + Tolerance,
            $"h(2,1,1) = {heuristic:0.####} over-states a (2,1,+1) parkour edge of {edge:0.####}");
    }

    /// <summary>The property the reduction above rests on: the heuristic is a metric on displacements, so its value at a node can never exceed its value at a neighbour plus its value on the step between them. Swept over every displacement pair inside a 3-block cube.</summary>
    [Fact]
    public void TheHeuristic_ObeysTheTriangleInequality()
    {
        for (int ax = -3; ax <= 3; ax++)
            for (int ay = -3; ay <= 3; ay++)
                for (int az = -3; az <= 3; az++)
                    for (int bx = -3; bx <= 3; bx++)
                        for (int by = -3; by <= 3; by++)
                            for (int bz = -3; bz <= 3; bz++)
                            {
                                double whole = H(ax + bx, ay + by, az + bz);
                                double parts = H(ax, ay, az) + H(bx, by, bz);
                                Assert.True(
                                    whole <= parts + Tolerance,
                                    $"h({ax + bx},{ay + by},{az + bz}) = {whole:0.####} exceeds "
                                    + $"h({ax},{ay},{az}) + h({bx},{by},{bz}) = {parts:0.####}");
                            }

    }

    /// <summary>The whole cost model at once: expand every standable cell of a shape with the REAL default expander set and assert that no edge it emits costs less than the heuristic drops across it. A single violating edge is a route A* is licensed to walk past.</summary>
    [Theory]
    [InlineData(ShapeKind.DryPlain)]
    [InlineData(ShapeKind.FullBlockStaircase)]
    [InlineData(ShapeKind.SlabStreet)]
    [InlineData(ShapeKind.StairTreadRun)]
    [InlineData(ShapeKind.LedgeAndGap)]
    [InlineData(ShapeKind.LadderShaft)]
    [InlineData(ShapeKind.Pool)]
    [InlineData(ShapeKind.FlowingChannel)]
    [InlineData(ShapeKind.BambooGrove)]
    public void NoEmittedEdge_CostsLessThanTheHeuristicItRemoves(ShapeKind kind)
    {
        (CalculationContext ctx, BlockPos min, BlockPos max) = Build(kind);
        var violations = new List<string>();

        Sweep(ctx, min, max, (from, neighbor) =>
        {
            double h = H(
                neighbor.DestX - from.X,
                neighbor.DestY - from.Y,
                neighbor.DestZ - from.Z);

            if (h > neighbor.Cost + Tolerance)
                violations.Add(
                    $"{neighbor.MoveType} ({from.X},{from.Y},{from.Z}) -> "
                    + $"({neighbor.DestX},{neighbor.DestY},{neighbor.DestZ}): h {h:0.####} > cost {neighbor.Cost:0.####}");

        });

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} inadmissible edges on {kind}; first 5:{Environment.NewLine}"
                + string.Join(Environment.NewLine, violations.Take(5).Select(v => "  " + v)));
    }

    /// <summary>The sweep above is only worth its assertion if it actually reaches every move family, so this pins what the nine shapes between them emit. A family that stops appearing here is a family the admissibility sweep silently stopped covering.</summary>
    [Fact]
    public void TheSweptShapes_CoverEveryMoveFamily()
    {
        var seen = new HashSet<MoveType>();
        foreach (ShapeKind kind in Enum.GetValues<ShapeKind>())
        {
            (CalculationContext ctx, BlockPos min, BlockPos max) = Build(kind);
            Sweep(ctx, min, max, (_, neighbor) => seen.Add(neighbor.MoveType));
        }

        Assert.Equal(
            [
                MoveType.Traverse,
                MoveType.Diagonal,
                MoveType.Ascend,
                MoveType.Descend,
                MoveType.Fall,
                MoveType.Climb,
                MoveType.Parkour,
                MoveType.Swim,
            ],
            seen.Order().ToArray());
    }

    [Fact]
    public void TheBambooGroveShape_ReallyEmitsSqueezeEdges()
    {
        (CalculationContext ctx, BlockPos min, BlockPos max) = Build(ShapeKind.BambooGrove);
        int squeezes = 0;
        double worstMargin = double.PositiveInfinity;

        Sweep(ctx, min, max, (from, neighbor) =>
        {
            if (!neighbor.Squeezed)
                return;

            squeezes++;
            worstMargin = Math.Min(
                worstMargin,
                neighbor.Cost - H(neighbor.DestX - from.X, neighbor.DestY - from.Y, neighbor.DestZ - from.Z));
        });

        Assert.True(squeezes > 0, "the grove shape emits no squeeze edges, so sweeping it proves nothing");
        Assert.True(
            worstMargin > 0.0,
            $"a squeeze must be a PENALTY against the heuristic, not a discount; worst margin {worstMargin:0.####}");
        Assert.Equal(ActionCosts.WalkOneBlock * ActionCosts.SqueezeMultiplier - ActionCosts.SprintOneBlock, worstMargin, 9);
    }

    /// <summary>The inequality the row above measures, stated as the constants themselves so a future change to the multiplier is caught at the source rather than only through a fixture.</summary>
    [Fact]
    public void TheSqueezeMultiplier_CannotPutAnEdgeUnderTheSprintRateTheHeuristicAssumes()
    {
        Assert.True(
            ActionCosts.SqueezeMultiplier >= 1.0,
            "a squeeze is walked and turns twice; a multiplier under 1 would make it cheaper than a walk");
        Assert.True(
            ActionCosts.WalkOneBlock * ActionCosts.SqueezeMultiplier > ActionCosts.SprintOneBlock,
            $"squeeze {ActionCosts.WalkOneBlock * ActionCosts.SqueezeMultiplier:0.####} must exceed the "
                + $"{ActionCosts.SprintOneBlock:0.####} a block that GoalBlock.DistanceHeuristic promises; "
                + "the Diagonal family already sits at exactly 0.000000 margin and a discount here would "
                + "make A* walk past a cheaper route");
    }

    /// <summary>The terrain shapes the sweep runs over.</summary>
    public enum ShapeKind
    {
        /// <summary>A flat stone plane: cardinal and diagonal walks.</summary>
        DryPlain,

        /// <summary>Six full-block treads: ascends up, descends and falls down.</summary>
        FullBlockStaircase,

        /// <summary>Bottom slabs on alternate cells: the M5c auto-step street.</summary>
        SlabStreet,

        /// <summary>Six bottom-half stair treads: the two-shelf entry ladder.</summary>
        StairTreadRun,

        /// <summary>A plane with a ledge and a three-block gap: falls, sprint descends, parkour.</summary>
        LedgeAndGap,

        /// <summary>A ladder against a wall: climbs.</summary>
        LadderShaft,

        /// <summary>A pool sunk into the plane: swims, vertical swims and swim exits.</summary>
        Pool,

        FlowingChannel,

        BambooGrove,
    }

    private static void Sweep(
        CalculationContext ctx, BlockPos min, BlockPos max, Action<BlockPos, MoveNeighbor> onEdge)
    {
        IMoveExpander[] expanders = AStarPathFinder.BuildDefaultExpanders();
        int capacity = expanders.Sum(e => e.MaxNeighbors);
        var buffer = new MoveNeighbor[capacity];

        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                {
                    if (!MoveHelper.CanStandAt(ctx, x, y, z))
                        continue;

                    var from = new BlockPos(x, y, z);
                    int offset = 0;
                    foreach (IMoveExpander expander in expanders)
                    {
                        Span<MoveNeighbor> slot = buffer.AsSpan(offset, expander.MaxNeighbors);
                        int produced = expander.Expand(ctx, x, y, z, slot);
                        offset += expander.MaxNeighbors;
                        for (int i = 0; i < produced; i++)
                            onEdge(from, slot[i]);

                    }
                }

    }

    private static (CalculationContext Ctx, BlockPos Min, BlockPos Max) Build(ShapeKind kind)
    {
        var world = new FixtureWorld();
        BlockPos min;
        BlockPos max;

        switch (kind)
        {
            case ShapeKind.DryPlain:
                world.Floor(-2, 8, -2, 4, 60);
                min = new BlockPos(-1, 61, -1);
                max = new BlockPos(7, 61, 3);
                break;

            case ShapeKind.FullBlockStaircase:
                world.Floor(-2, 10, -2, 2, 60);
                for (int k = 0; k < 6; k++)
                    world.Fill(2 + k, 61, -2, 2 + k, 61 + k, 2, FixtureWorld.Stone);

                world.Fill(8, 61, -2, 10, 66, 2, FixtureWorld.Stone);
                min = new BlockPos(-1, 61, -1);
                max = new BlockPos(9, 68, 1);
                break;

            case ShapeKind.SlabStreet:
                world.Floor(-2, 14, -2, 2, 60);
                for (int x = 2; x <= 12; x += 2)
                    world.Fill(x, 61, -2, x, 61, 2, FixtureWorld.BottomSlab);

                min = new BlockPos(-1, 61, -1);
                max = new BlockPos(13, 62, 1);
                break;

            case ShapeKind.StairTreadRun:
                world.Floor(-2, 12, -2, 2, 60);
                for (int k = 0; k < 6; k++)
                {
                    world.Fill(2 + k, 61, -2, 2 + k, 60 + k, 2, FixtureWorld.Stone);
                    world.Fill(2 + k, 61 + k, -2, 2 + k, 61 + k, 2, FixtureWorld.StairsBottom);
                }

                world.Fill(8, 61, -2, 12, 66, 2, FixtureWorld.Stone);
                min = new BlockPos(-1, 61, -1);
                max = new BlockPos(11, 68, 1);
                break;

            case ShapeKind.LedgeAndGap:
                world.Floor(-2, 4, -2, 4, 60);
                world.Floor(8, 14, -2, 4, 60);
                world.Floor(-2, 4, -2, 4, 56);
                world.Floor(8, 14, -2, 4, 56);
                world.Fill(5, 57, -2, 7, 60, 4, FixtureWorld.Air);
                min = new BlockPos(-1, 57, -1);
                max = new BlockPos(13, 61, 3);
                break;

            case ShapeKind.BambooGrove:
                // The lane runs along X at z = 0 with walls at z = -1 and z = +1; the post at (5, 0) hashes to a Z offset of +0.216667, so its box is [0.622917, 0.810417] and only a body flush with the low face clears it.
                world.Floor(-1, 11, -1, 1, 60);
                world.Fill(-1, 61, -1, 11, 63, -1, FixtureWorld.Stone);
                world.Fill(-1, 61, 1, 11, 63, 1, FixtureWorld.Stone);
                world.Fill(5, 61, 0, 5, 63, 0, FixtureWorld.Bamboo);
                min = new BlockPos(0, 61, 0);
                max = new BlockPos(10, 61, 0);
                break;

            case ShapeKind.LadderShaft:
                world.Floor(-2, 6, -2, 2, 60);
                world.Fill(4, 61, -2, 4, 70, 2, FixtureWorld.Stone);
                world.Fill(3, 61, 0, 3, 70, 0, FixtureWorld.Ladder);
                world.Fill(2, 71, -1, 3, 71, 1, FixtureWorld.Stone);
                min = new BlockPos(0, 61, -1);
                max = new BlockPos(5, 72, 1);
                break;

            case ShapeKind.Pool:
                world.Floor(-2, 12, -4, 4, 60);
                world.Fill(3, 58, -2, 9, 61, 2, FixtureWorld.Air);
                world.Floor(3, 9, -2, 2, 57);
                world.Fill(3, 58, -2, 9, 60, 2, FixtureWorld.Water);
                min = new BlockPos(0, 58, -3);
                max = new BlockPos(11, 62, 3);
                break;

            case ShapeKind.FlowingChannel:
                // A walled trench, its floor one cell down from the plain, flooded with a real level gradient running +x: level 0 (a source) at x=3 stepping to level 7 at x=10. That is exactly what a vanilla server spreads into a sealed channel, and it is what makes GetWaterFlow read a non-zero vector rather than the Pool's zero.
                //
                // Deliberately ONE deep, with the cells above it open. A one-deep flooded trench is a WADE - the jump family emits it as an ordinary Traverse and Diagonal - which is the family this shape exists to sweep, while the swim family reaches its cells from the ends. Both current models therefore run over a real current here.
                world.Floor(-2, 14, -4, 4, 60);
                world.Fill(2, 61, -1, 11, 61, 1, FixtureWorld.Air);
                world.Floor(2, 11, -1, 1, 59);
                world.Fill(2, 60, -2, 11, 63, -2, FixtureWorld.Stone);
                world.Fill(2, 60, 2, 11, 63, 2, FixtureWorld.Stone);
                world.FlowingRun(3, 60, 0, 8, 1, 0, layers: 1);
                min = new BlockPos(0, 59, -2);
                max = new BlockPos(13, 62, 2);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        CalculationContext ctx = FixtureContext.Build(world, min, max, PathfinderOptions.Default);
        return (ctx, min, max);
    }
}
