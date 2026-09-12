using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class SwimFamilyHazardGateTests
{
    private static MoveResult Calculate(IMove move, CalculationContext ctx, int x, int y, int z)
    {
        var result = default(MoveResult);
        move.Calculate(ctx, x, y, z, ref result);
        return result;
    }

    private static CalculationContext Context(
        FixtureWorld world, BlockPos around, bool fireHazardsCleared = false, int margin = 8)
        => new(
            world.Capture(around, around, margin),
            PathfinderOptions.Default,
            capabilities: null,
            fireHazardsCleared);

    // The swim destination's own floor

    /// <summary>The bed of the shallow lane, and the lid two cells over it.</summary>
    private const int BedY = 59;

    /// <summary>The lane's feet cell: water here and at <c>BedY + 2</c>, stone lid at <c>BedY + 3</c>.</summary>
    private const int LaneY = 60;

    private static FixtureWorld ShallowLane(int bedState)
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 14, BedY + 7, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 11, LaneY + 1, 0, FixtureWorld.Water);
        world.Fill(5, BedY, 0, 8, BedY, 0, bedState);
        return world;
    }

    /// <summary>The gap itself, at the move that owns it: a swim INTO the first cell whose bed is magma. The destination is a perfectly passable water column and the move has to refuse it anyway, because the body it plans there is resting on the hazard.</summary>
    [Fact]
    public void Swim_RefusesAShallowLaneCellWhoseBedIsMagma()
    {
        FixtureWorld world = ShallowLane(FixtureWorld.MagmaBlock);
        CalculationContext ctx = Context(world, new BlockPos(4, LaneY, 0));

        MoveResult result = Calculate(new MoveSwim(1, 0), ctx, 4, LaneY, 0);

        Assert.True(
            result.IsImpossible,
            $"the swim was admitted to ({result.DestX},{result.DestY},{result.DestZ}) at cost "
            + $"{result.Cost:F4}, with the body resting on "
            + $"{ctx.GetBlock(result.DestX, result.DestY - 1, result.DestZ).Block.Id}");
    }

    /// <summary>The control: the same lane, the same move, one cell earlier, over ordinary stone.</summary>
    [Fact]
    public void Swim_StillCrossesAShallowLaneOverStone()
    {
        FixtureWorld world = ShallowLane(FixtureWorld.MagmaBlock);
        CalculationContext ctx = Context(world, new BlockPos(2, LaneY, 0));

        MoveResult result = Calculate(new MoveSwim(1, 0), ctx, 2, LaneY, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(3, result.DestX);
        Assert.Equal(LaneY, result.DestY);
    }

    /// <summary>DEPTH is what makes the difference, and it is the whole of the difference. Nine cells of water over the same magma bed: the cell below the swim node is water, nothing holds the body up there, and A step hazard applies only against the block supporting a resting body, and a sprinting swimmer does not sink toward one because fluid movement leaves velocity unchanged while <c>SwimTemplate</c> holds sprint.</summary>
    [Fact]
    public void Swim_StillCrossesDeepWaterOverTheSameMagmaBed()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 14, BedY + 20, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 11, LaneY + 6, 0, FixtureWorld.Water);
        world.Fill(0, LaneY + 7, 0, 11, LaneY + 14, 0, FixtureWorld.Air);
        world.Fill(5, BedY, 0, 8, BedY, 0, FixtureWorld.MagmaBlock);

        CalculationContext ctx = Context(world, new BlockPos(4, LaneY + 4, 0));

        MoveResult result = Calculate(new MoveSwim(1, 0), ctx, 4, LaneY + 4, 0);

        Assert.False(result.IsImpossible, "four cells of water below the body is not a hot floor");
        Assert.Equal(5, result.DestX);
        Assert.Equal(LaneY + 4, result.DestY);
    }

    /// <summary>Fire-resistance clearance reaches this gate through the shared hazard predicate: <see cref="MoveHelper.IsHazard"/> is what consults <see cref="CalculationContext.FireHazardsCleared"/>, so a magma bed under a swimmer costs a plan exactly what a magma floor under a walker costs it.</summary>
    [Fact]
    public void Swim_OverAMagmaBed_IsAdmittedUnderFireResistance()
    {
        FixtureWorld world = ShallowLane(FixtureWorld.MagmaBlock);
        CalculationContext ctx = Context(world, new BlockPos(4, LaneY, 0), fireHazardsCleared: true);

        MoveResult result = Calculate(new MoveSwim(1, 0), ctx, 4, LaneY, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(5, result.DestX);
    }

    [Fact]
    public void Swim_OverACactusBed_IsRefusedEvenUnderFireResistance()
    {
        FixtureWorld world = ShallowLane(FixtureWorld.Cactus);
        CalculationContext ctx = Context(world, new BlockPos(4, LaneY, 0), fireHazardsCleared: true);

        MoveResult result = Calculate(new MoveSwim(1, 0), ctx, 4, LaneY, 0);

        Assert.True(result.IsImpossible, "fire resistance does not answer a cactus");
    }

    /// <summary>The vertical arm is the same graph and needs the same gate, or it is simply the loophole that re-admits the node <see cref="Swim_RefusesAShallowLaneCellWhoseBedIsMagma"/> refuses: a dive into the cell whose floor is the hazard.</summary>
    [Fact]
    public void SwimDown_RefusesADiveOntoAMagmaBed()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 14, BedY + 10, 2, FixtureWorld.Stone);
        world.Fill(5, LaneY, 0, 5, LaneY + 4, 0, FixtureWorld.Water);
        world.Set(5, BedY, 0, FixtureWorld.MagmaBlock);

        CalculationContext ctx = Context(world, new BlockPos(5, LaneY + 1, 0));

        MoveResult result = Calculate(new MoveSwimVertical(up: false), ctx, 5, LaneY + 1, 0);

        Assert.True(result.IsImpossible, "the dive lands the body resting on the magma");
    }

    /// <summary>The planner-level form, which is course row M8's own contract: a flooded bore whose bed turns to magma part-way along has no route across it at all. The safe arm (a submerged bottom-walk, emitted as <c>Traverse</c> and gated on <see cref="MoveHelper.CanWalkOn"/>) is deleted by the hazard, and the swim arm has to be deleted by it too or the refusal never happens.</summary>
    [Fact]
    public void TheM8Bore_WithAMagmaBed_HasNoRouteAcross()
    {
        static PathResult Plan(int bedState)
        {
            var world = new FixtureWorld();
            world.Fill(-2, BedY - 2, -2, 18, BedY + 7, 2, FixtureWorld.Stone);
            world.Fill(0, LaneY, 0, 11, LaneY + 1, 0, FixtureWorld.Water);
            world.Fill(12, LaneY, 0, 15, LaneY + 1, 0, FixtureWorld.Air);
            world.Fill(5, BedY, 0, 8, BedY, 0, bedState);

            var start = new BlockPos(0, LaneY, 0);
            var goal = new BlockPos(13, LaneY, 0);
            return PathPlanner.FindPath(
                world.Capture(start, goal, margin: 6), PathfinderOptions.Default, start, new GoalBlock(goal));
        }

        Assert.Equal(PathStatus.Success, Plan(FixtureWorld.Stone).Status);
        Assert.NotEqual(PathStatus.Success, Plan(FixtureWorld.MagmaBlock).Status);
    }

    /// <summary>And the same bore flooded deep enough to swim clear of its own bed still has one. This is the row that keeps the gate from being "no swimming over hazards": the plan is refused because the body would be TOUCHING the hazard, so where it would not be, nothing changes.</summary>
    [Fact]
    public void ADeepPoolOverAMagmaBed_IsStillCrossed()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 18, BedY + 20, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 11, LaneY + 6, 0, FixtureWorld.Water);
        world.Fill(0, LaneY + 7, 0, 11, LaneY + 14, 0, FixtureWorld.Air);
        world.Fill(5, BedY, 0, 8, BedY, 0, FixtureWorld.MagmaBlock);

        var start = new BlockPos(0, LaneY + 4, 0);
        var goal = new BlockPos(11, LaneY + 4, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        foreach (PathNode node in result.Path)
            Assert.NotEqual(
                Identifier.Minecraft("magma_block"),
                view.GetBlock(new BlockPos(node.X, node.Y - 1, node.Z)).Block.Id);

    }

    // The climb family's descent-path cell

    /// <summary>A seven-rung ladder column in a stone casing, with one rung swapped for something else.</summary>
    private static FixtureWorld LadderColumn(int rungState)
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 14, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 6, 0, FixtureWorld.Ladder);
        world.Set(0, LaneY + 2, 0, rungState);
        return world;
    }

    [Theory]
    [InlineData(FixtureWorld.Lava)]
    [InlineData(FixtureWorld.Fire)]
    [InlineData(FixtureWorld.SoulFire)]
    [InlineData(FixtureWorld.Cactus)]
    [InlineData(FixtureWorld.Campfire)]
    [InlineData(FixtureWorld.PowderSnow)]
    public void ClimbDown_RefusesAHazardousRung(int rungState)
    {
        FixtureWorld world = LadderColumn(rungState);
        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 3, 0));

        MoveResult result = Calculate(new MoveClimb(up: false), ctx, 0, LaneY + 3, 0);

        Assert.True(
            result.IsImpossible,
            $"the climb dropped the body into {ctx.GetBlock(0, LaneY + 2, 0).Block.Id}");
    }

    /// <summary>The controls: a rung, air under the ladder's end, and water are all still descended into.</summary>
    [Theory]
    [InlineData(FixtureWorld.Ladder)]
    [InlineData(FixtureWorld.Air)]
    [InlineData(FixtureWorld.Water)]
    public void ClimbDown_StillDescendsIntoAHarmlessCell(int rungState)
    {
        FixtureWorld world = LadderColumn(rungState);
        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 3, 0));

        MoveResult result = Calculate(new MoveClimb(up: false), ctx, 0, LaneY + 3, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(LaneY + 2, result.DestY);
    }

    [Fact]
    public void ClimbUp_RefusesAHazardousDestination()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 14, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 2, 0, FixtureWorld.Scaffolding);
        world.Set(0, LaneY + 3, 0, FixtureWorld.Fire);
        world.Fill(0, LaneY + 4, 0, 0, LaneY + 6, 0, FixtureWorld.Air);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 2, 0));

        MoveResult result = Calculate(new MoveClimb(up: true), ctx, 0, LaneY + 2, 0);

        Assert.True(result.IsImpossible, "the climb planned the body up into the fire");
    }

    // The water arms and the solid that holds water

    /// <summary><see cref="MoveSwimVertical"/>'s down arm asked <c>IsWater(below)</c> and nothing else, so under S1b's wide predicate a waterlogged fence - <c>BlocksMotion</c> and <c>Waterlogged</c> at once - was a legal dive destination. The ascend arm has asked <see cref="MoveHelper.CanTraverseWater"/> all along, which carries the guard; the two arms of the same graph disagreed about the same cell.</summary>
    [Fact]
    public void SwimDown_RefusesAWaterloggedSolid()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 10, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 4, 0, FixtureWorld.Water);
        world.Set(0, LaneY + 1, 0, FixtureWorld.WaterloggedFence);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 2, 0));

        MoveResult result = Calculate(new MoveSwimVertical(up: false), ctx, 0, LaneY + 2, 0);

        Assert.True(result.IsImpossible, "a swimmer occupies neither half of a waterlogged fence");
    }

    /// <summary>The control: a plain water cell below is still a dive.</summary>
    [Fact]
    public void SwimDown_StillDivesIntoPlainWater()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 10, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 4, 0, FixtureWorld.Water);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 2, 0));

        MoveResult result = Calculate(new MoveSwimVertical(up: false), ctx, 0, LaneY + 2, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(LaneY + 1, result.DestY);
    }

    /// <summary><see cref="MoveFall"/>'s water arm stops the scan at the first <c>IsWater</c> cell and lands the body IN it. A waterlogged stair is water and a collision box at once, so the body was planned inside a solid - and paid the splash price for it, which <see cref="MoveHelper.AbsorbsFallDamage"/> already refuses to grant (S1b). The landing is on TOP of the block, at the ordinary fall price.</summary>
    [Fact]
    public void Fall_LandsOnAWaterloggedSolidRatherThanInsideIt()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 12, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 6, 0, FixtureWorld.Air);
        world.Set(0, LaneY + 1, 0, FixtureWorld.WaterloggedStairs);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 3, 0));

        MoveResult result = Calculate(new MoveFall(), ctx, 0, LaneY + 3, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(LaneY + 2, result.DestY);
    }

    /// <summary>The control: plain water at the same depth is still a plunge INTO the water cell.</summary>
    [Fact]
    public void Fall_StillPlungesIntoPlainWater()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 2, -2, 4, BedY + 12, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY, 0, 0, LaneY + 6, 0, FixtureWorld.Air);
        world.Set(0, LaneY + 1, 0, FixtureWorld.Water);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 3, 0));

        MoveResult result = Calculate(new MoveFall(), ctx, 0, LaneY + 3, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(LaneY + 1, result.DestY);
    }

    /// <summary><see cref="MoveDescend"/>'s dynamic scan carries the identical arm, and the identical gap. Four blocks down, which is past the three-cell fast path and inside the scan.</summary>
    [Fact]
    public void Descend_LandsOnAWaterloggedSolidRatherThanInsideIt()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 6, -2, 6, BedY + 14, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY + 4, 0, 0, LaneY + 8, 0, FixtureWorld.Air);
        world.Fill(1, LaneY - 2, 0, 1, LaneY + 8, 0, FixtureWorld.Air);
        world.Set(1, LaneY, 0, FixtureWorld.WaterloggedStairs);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 4, 0));

        MoveResult result = Calculate(new MoveDescend(1, 0), ctx, 0, LaneY + 4, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(1, result.DestX);
        Assert.Equal(LaneY + 1, result.DestY);
    }

    /// <summary>The control: plain water four blocks down is still the water landing it always was.</summary>
    [Fact]
    public void Descend_StillLandsInPlainWater()
    {
        var world = new FixtureWorld();
        world.Fill(-2, BedY - 6, -2, 6, BedY + 14, 2, FixtureWorld.Stone);
        world.Fill(0, LaneY + 4, 0, 0, LaneY + 8, 0, FixtureWorld.Air);
        world.Fill(1, LaneY - 2, 0, 1, LaneY + 8, 0, FixtureWorld.Air);
        world.Set(1, LaneY, 0, FixtureWorld.Water);

        CalculationContext ctx = Context(world, new BlockPos(0, LaneY + 4, 0));

        MoveResult result = Calculate(new MoveDescend(1, 0), ctx, 0, LaneY + 4, 0);

        Assert.False(result.IsImpossible);
        Assert.Equal(1, result.DestX);
        Assert.Equal(LaneY, result.DestY);
    }
}
