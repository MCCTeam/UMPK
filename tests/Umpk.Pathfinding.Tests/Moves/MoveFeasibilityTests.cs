using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class MoveFeasibilityTests
{
    private const int FloorY = 79;

    [Fact]
    public void Descend_Accepts1BlockStepDown()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone); // raise the source column by one

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 2, 0);
        var move = new MoveDescend(1, 0);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, FloorY + 2, 0, ref result);

        Assert.False(result.IsImpossible);
        Assert.Equal(1, result.DestX);
        Assert.Equal(FloorY + 1, result.DestY);
    }

    [Fact]
    public void Descend_Rejects1BlockDescendIntoSolidLandingColumn()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);
        world.Set(1, FloorY + 1, 0, FixtureWorld.Stone); // destination landing column is solid

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 2, 0);
        var move = new MoveDescend(1, 0);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, FloorY + 2, 0, ref result);

        Assert.True(result.IsImpossible);
    }

    [Fact]
    public void Descend_Accepts2BlockDrop()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);
        world.Set(0, FloorY + 2, 0, FixtureWorld.Stone);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 3, 0);
        var move = new MoveDescend(1, 0);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, FloorY + 3, 0, ref result);

        Assert.False(result.IsImpossible);
        Assert.Equal(1, result.DestX);
        Assert.Equal(FloorY + 1, result.DestY);
    }

    [Fact]
    public void Descend_RejectsMultiBlockDropWhenColumnBlocked()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);
        world.Set(0, FloorY + 2, 0, FixtureWorld.Stone);
        world.Set(1, FloorY + 2, 0, FixtureWorld.Stone); // blocker in the destination column

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 3, 0);
        var move = new MoveDescend(1, 0);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, FloorY + 3, 0, ref result);

        Assert.True(result.IsImpossible);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void SprintDescend_DiagonalNeedsOneShoulderColumnClear(bool cornerXOpen, bool cornerZOpen, bool expectPossible)
    {
        const int SourceY = FloorY + 2;

        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);                // the lower corridor's floor
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);  // the branch cell, one block higher

        if (!cornerXOpen)
            world.Fill(-1, SourceY - 1, 0, -1, SourceY + 1, 0, FixtureWorld.Stone);

        if (!cornerZOpen)
            world.Fill(0, SourceY - 1, 1, 0, SourceY + 1, 1, FixtureWorld.Stone);

        CalculationContext ctx = FixtureContext.Around(world, 0, SourceY, 0);
        var move = new MoveSprintDescend(-1, 1);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, SourceY, 0, ref result);

        Assert.Equal(expectPossible, !result.IsImpossible);

        if (expectPossible)
        {
            Assert.Equal(-1, result.DestX);
            Assert.Equal(SourceY - 1, result.DestY);
            Assert.Equal(1, result.DestZ);
        }
    }

    /// <summary>The gate is on the DIAGONAL arm only. The cardinal <c>(2, 0)</c> sprint-descend keeps its own intermediate-cell rule, which is a different question (the cell the body flies OVER, not the corner it is squeezed between), and a body sprinting off a ledge along a wall must still be able to do it.</summary>
    [Fact]
    public void SprintDescend_CardinalIsUnaffectedByAWallAlongTheRun()
    {
        const int SourceY = FloorY + 2;

        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Stone);                    // the ledge, one cell
        world.Fill(-2, SourceY, 1, 0, SourceY + 1, 1, FixtureWorld.Stone);  // a wall along the whole run

        CalculationContext ctx = FixtureContext.Around(world, 0, SourceY, 0);
        var move = new MoveSprintDescend(-2, 0);
        var result = default(MoveResult);

        move.Calculate(ctx, 0, SourceY, 0, ref result);

        Assert.False(result.IsImpossible);
        Assert.Equal(-2, result.DestX);
        Assert.Equal(0, result.DestZ);
    }
}
