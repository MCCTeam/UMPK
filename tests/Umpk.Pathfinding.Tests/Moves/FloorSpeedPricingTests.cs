using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class FloorSpeedPricingTests
{
    private const int FloorY = 64;
    private const int FeetY = FloorY + 1;

    private const double MeasuredStoneTicksPerBlock = 3.62765;

    private const double MeasuredSoulSandTicksPerBlock = 6.13501;

    private static readonly PathfinderOptions Walking =
        PathfinderOptions.Default with { AllowParkour = false, AllowParkourAscend = false };

    /// <summary>The fixture mirrors the dataset's own scalars, which is what makes every row below a statement about the planner rather than about the fixture.</summary>
    [Theory]
    [InlineData(FixtureWorld.SoulSand, 0.4f)]
    [InlineData(FixtureWorld.Honey, 0.4f)]
    [InlineData(FixtureWorld.Stone, 1.0f)]
    [InlineData(FixtureWorld.Carpet, 1.0f)]
    [InlineData(FixtureWorld.BottomSlab, 1.0f)]
    public void TheFixtureCarriesTheDatasetsSpeedFactors(int stateId, float expected)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(expected, ctx.GetBlock(0, FloorY, 0).SpeedFactor, 3);
    }

    [Theory]
    [InlineData(FixtureWorld.Stone, FixtureWorld.Stone, 1.0, "open ground")]
    [InlineData(FixtureWorld.SoulSand, FixtureWorld.Stone, 1.691, "feet inside the soul sand at 0.875")]
    [InlineData(FixtureWorld.Honey, FixtureWorld.Stone, 1.691, "feet inside the honey at 0.9375")]
    [InlineData(FixtureWorld.Carpet, FixtureWorld.SoulSand, 1.691, "carpet reads 1.0, the fall-through finds the soul sand")]
    [InlineData(FixtureWorld.Carpet, FixtureWorld.Stone, 1.0, "carpet reads 1.0, and so does the stone under it")]
    [InlineData(FixtureWorld.BottomSlab, FixtureWorld.SoulSand, 1.691, "a slab is half a block, so the feet are still in its cell")]
    [InlineData(FixtureWorld.Air, FixtureWorld.Air, 1.0, "nothing underfoot at all")]
    public void FloorSpeedPenalty_ReadsTheFeetCellThenTheOneBelow(int floor, int under, double expected, string why)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY - 1, 0, under);
        world.Set(0, FloorY, 0, floor);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(expected, MoveHelper.FloorSpeedPenalty(ctx, 0, FeetY, 0), 3);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>The charge is the MEASURED ratio, not the first-principles <c>1 / speedFactor</c>.</summary>
    /// <remarks><c>ApplyBlockSpeedFactor</c> multiplies the velocity inside the friction loop rather than scaling a terminal speed, so a 0.4 factor does not make the walk 2.5 times as long. M6 measured 6.13501 ticks a block against stone's 3.62765, a ratio of 1.691 - so <c>1 / 0.4</c> over-charges by 48 percent.</remarks>
    [Fact]
    public void TheChargeIsTheMeasuredRatio_NotOneOverTheSpeedFactor()
    {
        Assert.Equal(
            MeasuredSoulSandTicksPerBlock / MeasuredStoneTicksPerBlock,
            ActionCosts.SpeedFactorCostMultiplier(0.4f),
            3);

        Assert.NotEqual(1.0 / 0.4, ActionCosts.SpeedFactorCostMultiplier(0.4f), 3);
        Assert.Equal(1.0, ActionCosts.SpeedFactorCostMultiplier(1.0f), 6);

        // Anything the dataset does not carry falls back to the first-principles upper bound, which is conservative rather than measured. Nothing in the game reaches this arm today.
        Assert.Equal(2.0, ActionCosts.SpeedFactorCostMultiplier(0.5f), 6);
    }

    /// <summary>Every ground arm the jump family emits carries the floor's charge.</summary>
    /// <remarks>Penalising the cardinal walk alone is what produced the zig-zag: it re-prices one arm of a pair whose whole purpose is to be compared. The airborne family is deliberately absent - a sprint jump's arc is not slowed by the floor it left, and the takeoff gate that DOES belong to a slow floor is the jump-factor one.</remarks>
    [Theory]
    [InlineData(MoveType.Traverse, 1, 0, "the cardinal walk")]
    [InlineData(MoveType.Diagonal, 1, 1, "the diagonal walk")]
    public void EveryLevelWalkArmIsChargedTheFloorPenalty(MoveType expected, int dx, int dz, string why)
    {
        double overStone = LevelStepCost(FixtureWorld.Stone, dx, dz, expected);
        double overSoulSand = LevelStepCost(FixtureWorld.SoulSand, dx, dz, expected);

        Assert.Equal(1.691, overSoulSand / overStone, 3);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>And both JUMPED ascend arms, for the same reason one level up: charging the cardinal jump and not the diagonal one moves the asymmetry rather than removing it. The jump penalty is additive, so only the walk term scales.</summary>
    [Theory]
    [InlineData(1, 0, "the cardinal ascend jump")]
    [InlineData(1, 1, "the diagonal ascend jump")]
    public void BothAscendJumpArmsAreChargedTheFloorPenalty(int dx, int dz, string why)
    {
        double overStone = AscendJumpCost(FixtureWorld.Stone, dx, dz);
        double overSoulSand = AscendJumpCost(FixtureWorld.SoulSand, dx, dz);

        Assert.Equal(1.691, (overSoulSand - ActionCosts.JumpPenalty) / (overStone - ActionCosts.JumpPenalty), 3);
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void ASoulSandField_IsCrossedCardinally()
    {
        PathResult result = CrossTheField(FixtureWorld.SoulSand);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.DoesNotContain(MoveType.Diagonal, result.Moves);
        Assert.Equal(10, result.Moves.Count);
        Assert.All(result.Path, node => Assert.Equal(0, node.Z));
    }

    /// <summary>The control: over plain stone the same field is crossed the same way, so the row above is about the floor and not about the goal geometry.</summary>
    [Fact]
    public void AStoneField_WasAlreadyCrossedCardinally()
    {
        PathResult result = CrossTheField(FixtureWorld.Stone);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.DoesNotContain(MoveType.Diagonal, result.Moves);
        Assert.Equal(10, result.Moves.Count);
    }

    /// <summary>And the two-cell read reaches the PLANNER, not just the helper: a lane of carpet laid over soul sand is priced at the soul-sand rate, where a one-cell query reads the carpet's own 1.0 and prices the whole lane as open ground.</summary>
    /// <remarks>The comparison lane is carpet over STONE, so the two worlds differ in exactly one cell per column - the one the body's feet are NOT in - and nothing about the geometry, the node Y or the move set moves between them. That is what makes the ratio a statement about the fall-through.</remarks>
    [Fact]
    public void ACarpetedSoulSandLane_IsPricedAtTheSoulSandRate()
    {
        PathResult overSoulSand = CrossCarpetOver(FixtureWorld.SoulSand);
        PathResult overStone = CrossCarpetOver(FixtureWorld.Stone);

        Assert.Equal(PathStatus.Success, overSoulSand.Status);
        Assert.Equal(PathStatus.Success, overStone.Status);
        Assert.Equal(1.691, overSoulSand.Cost / overStone.Cost, 3);
        Assert.DoesNotContain(MoveType.Diagonal, overSoulSand.Moves);
    }

    private static PathResult CrossTheField(int floorState)
    {
        var world = new FixtureWorld();
        world.Floor(-2, 12, -6, 6, FloorY);
        world.Fill(1, FloorY, -5, 9, FloorY, 5, floorState);

        return Cross(world);
    }

    private static PathResult CrossCarpetOver(int underState)
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY - 1, -6, 12, FloorY - 1, 6, underState);
        world.Fill(-2, FloorY, -6, 12, FloorY, 6, FixtureWorld.Carpet);

        return Cross(world);
    }

    private static PathResult Cross(FixtureWorld world)
    {
        var start = new BlockPos(0, FeetY, 0);
        var goal = new BlockPos(10, FeetY, 0);
        return PathPlanner.FindPath(
            world.Capture(start, goal, margin: 8), Walking, start, new GoalBlock(goal));
    }

    /// <summary>The emitted cost of one level step onto a floor of <paramref name="floorState"/>.</summary>
    private static double LevelStepCost(int floorState, int dx, int dz, MoveType expected)
    {
        var world = new FixtureWorld();
        world.Floor(-2, 2, -2, 2, FloorY);
        world.Set(dx, FloorY, dz, floorState);
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, Walking);

        return Emit(ctx, 0, FeetY, 0, expected, dx, dz, 0);
    }

    /// <summary>The emitted cost of one JUMPED ascend onto a floor of <paramref name="floorState"/>.</summary>
    private static double AscendJumpCost(int floorState, int dx, int dz)
    {
        // A one-block kerb, which is a rise of 0.875 or more and therefore JUMPED rather than walked (MoveHelper.IsWalkedStep refuses any shelf past the 0.6 auto-step). The kerb IS the floor under test, because it is the cell the body's feet land in.
        var world = new FixtureWorld();
        world.Floor(-2, 2, -2, 2, FloorY);
        world.Set(dx, FloorY + 1, dz, floorState);
        if (dx != 0 && dz != 0)
        {
            // EvaluateStepAscend offers the diagonal arm only when NEITHER cardinal leg is a walkable cell of its own, so both legs lose their floor while staying open to pass through.
            world.Set(dx, FloorY, 0, FixtureWorld.Air);
            world.Set(0, FloorY, dz, FixtureWorld.Air);
        }

        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0, Walking);
        return Emit(ctx, 0, FeetY, 0, MoveType.Ascend, dx, dz, 1);
    }

    private static double Emit(
        CalculationContext ctx, int x, int y, int z, MoveType expected, int dx, int dz, int dy)
    {
        var expander = new JumpExpander();
        Span<MoveNeighbor> buffer = new MoveNeighbor[expander.MaxNeighbors];
        int count = expander.Expand(ctx, x, y, z, buffer);

        for (int i = 0; i < count; i++)
        {
            MoveNeighbor neighbor = buffer[i];
            if (neighbor.MoveType == expected
                && neighbor.DestX == x + dx
                && neighbor.DestY == y + dy
                && neighbor.DestZ == z + dz)
                return neighbor.Cost;

        }

        Assert.Fail($"no {expected} to ({x + dx}, {y + dy}, {z + dz}) was emitted at all.");
        return 0.0;
    }
}
