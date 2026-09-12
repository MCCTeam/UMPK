using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class FlowAwareSwimCostTests
{
    private static MoveResult Calculate(IMove move, CalculationContext ctx, int x, int y, int z)
    {
        var result = default(MoveResult);
        move.Calculate(ctx, x, y, z, ref result);
        return result;
    }

    /// <summary>A walled channel at z=4 whose water runs +x over x=0..11 and is deep enough (y=51..55) that a body inside it is submerged rather than wading.</summary>
    private static FixtureWorld Channel()
    {
        var world = new FixtureWorld();
        world.Fill(-2, 48, 2, 14, 62, 6, FixtureWorld.Stone);
        world.Fill(-1, 49, 4, 13, 60, 4, FixtureWorld.Air);
        world.FlowingRun(0, 55, 4, 12, 1, 0, layers: 5);
        return world;
    }

    /// <summary>A wide flowing sheet, so a move ACROSS it is a real crossing rather than a wall hug.</summary>
    private static FixtureWorld Sheet()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 48, -4, 20, 66, 20, FixtureWorld.Stone);
        world.Fill(-3, 49, -3, 19, 64, 19, FixtureWorld.Air);
        for (int z = 0; z <= 16; z++)
            world.FlowingRun(0, 55, z, 12, 1, 0, layers: 5);

        return world;
    }

    /// <summary>An enclosed falling-water shaft: the deep-flow case, where the current is vertical.</summary>
    private static FixtureWorld FallingShaft()
    {
        var world = new FixtureWorld();
        world.Fill(-2, 40, -2, 2, 92, 2, FixtureWorld.Stone);
        world.Fill(0, 41, 0, 0, 91, 0, FixtureWorld.Air);
        world.FallingColumn(0, 42, 0, height: 48);
        return world;
    }

    /// <summary>The same swim, both ways down a channel, is not the same swim.</summary>
    [Fact]
    public void UpstreamSwim_CostsMoreThanDownstream()
    {
        FixtureWorld world = Channel();
        CalculationContext ctx = FixtureContext.Around(world, 6, 53, 4);

        MoveResult downstream = Calculate(new MoveSwim(1, 0), ctx, 6, 53, 4);
        MoveResult upstream = Calculate(new MoveSwim(-1, 0), ctx, 6, 53, 4);

        Assert.False(downstream.IsImpossible);
        Assert.False(upstream.IsImpossible);
        Assert.True(
            upstream.Cost > downstream.Cost * 2.0,
            $"upstream {upstream.Cost:F4} against downstream {downstream.Cost:F4}: the planner cannot "
            + "tell the two apart");
    }

    /// <summary>A 90-degree crossing is NOT free. The naive along-track model prices it at the flat cost because the current's component along the move is zero; the crab that holds the line spends <c>k sin(phi)</c> of the swimmer's thrust and leaves less than the whole of it for the track.</summary>
    [Fact]
    public void CrossCurrentSwim_IsPricedAboveStillWater()
    {
        FixtureWorld world = Sheet();
        CalculationContext ctx = FixtureContext.Around(world, 6, 53, 8);

        MoveResult across = Calculate(new MoveSwim(0, 1), ctx, 6, 53, 8);

        Assert.False(across.IsImpossible);
        Assert.True(
            across.Cost > ActionCosts.SwimOneBlock * 1.2,
            $"a dead-90-degree crossing was charged {across.Cost:F4} against the flat "
            + $"{ActionCosts.SwimOneBlock:F4}, so the crossing is free");
    }

    [Fact]
    public void UpstreamSwim_IsStillPlannable()
    {
        FixtureWorld world = Channel();
        CalculationContext ctx = FixtureContext.Around(world, 6, 53, 4);

        MoveResult upstream = Calculate(new MoveSwim(-1, 0), ctx, 6, 53, 4);

        Assert.False(upstream.IsImpossible);
        Assert.True(
            upstream.Cost < ActionCosts.SwimOneBlock * ActionCosts.MaxCurrentCostMultiplier + 0.001,
            $"the upstream swim was charged {upstream.Cost:F4}, past the 3.5x dead-upstream bound");
    }

    /// <summary>GUARD, passes before and after: still water is still charged the flat cost, exactly.</summary>
    [Fact]
    public void StillWaterSwim_IsChargedTheFlatCost()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 48, -4, 20, 66, 20, FixtureWorld.Stone);
        world.Fill(-3, 49, -3, 19, 64, 19, FixtureWorld.Air);
        world.Fill(-3, 51, -3, 19, 55, 19, FixtureWorld.Water);
        CalculationContext ctx = FixtureContext.Around(world, 6, 53, 8);

        MoveResult move = Calculate(new MoveSwim(1, 0), ctx, 6, 53, 8);

        Assert.Equal(ActionCosts.SwimOneBlock, move.Cost, 10);
    }

    [Fact]
    public void SwimUpAFallingColumn_CostsMoreThanSwimmingDownIt()
    {
        FixtureWorld world = FallingShaft();
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, 42, 0), new BlockPos(0, 89, 0));

        MoveResult up = Calculate(new MoveSwimVertical(up: true), ctx, 0, 60, 0);
        MoveResult down = Calculate(new MoveSwimVertical(up: false), ctx, 0, 60, 0);

        Assert.False(up.IsImpossible);
        Assert.False(down.IsImpossible);
        Assert.True(
            up.Cost > down.Cost,
            $"climbing the falling column was charged {up.Cost:F4} and riding it down {down.Cost:F4}");
    }

    /// <summary>A falling column is priced at the rate the executor actually achieves in it, not at the rate a HORIZONTAL current would justify.</summary>
    /// <remarks>
    /// <para>Measured by driving eight one-block vertical swim segments through the real <c>PathExecutor</c> over the real <c>PlayerPhysics</c> on protocol 772, in a 1x1 shaft, still water against a falling column whose flow reads exactly <c>(0, -1, 0)</c>:</para>
    /// <code>
    /// still   up  : 24 ticks / 8 blocks = 3.000 t/blk still   down: 29 ticks / 8 blocks = 3.625 t/blk falling up  : 27 ticks / 8 blocks = 3.375 t/blk   -> 1.125x still water falling down: 26 ticks / 8 blocks = 3.250 t/blk   -> 0.897x still water
    /// </code>
    /// <para>The axis-blind model charged 3.500x and 0.5833x for those two, over-charging the climb by 3.11 and under-charging the ride by 1.54, because it applied <see cref="ActionCosts.CurrentToThrustRatio"/> - a HORIZONTAL number, the water push over the horizontal swim thrust - to a purely vertical flow. Course row E10 (waterfall) is built entirely out of this move.</para>
    /// <para>The band is +/-8 percent of the measured ratio, which is wider than the model's own residual (1.1274 against 1.125, 0.8985 against 0.8966, both under 0.3 percent) and narrow enough that the 3.11x error cannot hide in it.</para>
    /// </remarks>
    [Theory]
    [InlineData(true, 1.125)]
    [InlineData(false, 0.8966)]
    public void AFallingColumn_IsPricedAtTheRateTheExecutorMeasures(bool up, double measuredRatio)
    {
        FixtureWorld world = FallingShaft();
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, 42, 0), new BlockPos(0, 89, 0));

        MoveResult move = Calculate(new MoveSwimVertical(up), ctx, 0, 60, 0);

        Assert.False(move.IsImpossible);
        double charged = move.Cost / ActionCosts.SwimOneBlock;
        Assert.True(
            Math.Abs(charged - measuredRatio) <= measuredRatio * 0.08,
            $"a vertical swim {(up ? "up" : "down")} a falling column was charged {charged:F4} times the "
                + $"flat swim cost, against the {measuredRatio:F4} the executor measures");
    }

    /// <summary>The memo pays <c>GetFlow</c> once per cell, not once per move evaluation. The snapshot is immutable for the life of the search, so a cached flow can never be stale.</summary>
    [Fact]
    public void FlowMemo_EvaluatesEachCellOnce()
    {
        FixtureWorld world = Channel();
        CalculationContext ctx = FixtureContext.Around(world, 6, 53, 4);

        for (int repeat = 0; repeat < 5; repeat++)
        {
            Calculate(new MoveSwim(1, 0), ctx, 6, 53, 4);
            Calculate(new MoveSwim(-1, 0), ctx, 6, 53, 4);
            Calculate(new MoveSwimVertical(up: true), ctx, 6, 53, 4);
            Calculate(new MoveSwimVertical(up: false), ctx, 6, 53, 4);
        }

        Assert.Equal(1, ctx.FlowSamplesTaken);
    }

    /// <summary>The cost model itself, at the three angles the whole design is quoted at. These are the numbers the budget calibration and the course row both read.</summary>
    [Theory]
    [InlineData(1.0, 0.0, 0.5833333333333334)]   // dead downstream
    [InlineData(0.0, 1.0, 1.4288690166235205)]   // dead 90-degree crossing
    [InlineData(-1.0, 0.0, 3.5)]                 // dead upstream
    [InlineData(0.0, 0.0, 1.0)]                  // still water
    public void CurrentCostMultiplier_MatchesTheMeasuredRange(double along, double cross, double expected)
        => Assert.Equal(expected, ActionCosts.CurrentCostMultiplier(along, cross), 10);
}
