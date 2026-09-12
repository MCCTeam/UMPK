using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class WaterloggedPassabilityTests
{
    private const int FloorY = 64;

    private static BlockState State(int stateId) => new(Data, stateId);

    private static readonly FixtureBlockData Data = new();

    // The predicate itself

    [Fact]
    public void IsWater_AcceptsWaterloggedStates()
    {
        Assert.True(MoveHelper.IsWater(State(FixtureWorld.WaterloggedStairs)));
        Assert.True(MoveHelper.IsWater(State(FixtureWorld.WaterloggedFence)));
    }

    /// <summary>The flattening split <c>minecraft:water</c> into a source block and <c>minecraft:flowing_water</c>; eleven of the dataset's protocol bands are on the far side of that split, and the narrow predicate read every flowing cell on them as dry land.</summary>
    [Fact]
    public void IsWater_AcceptsPreFlatteningFlowingWater()
        => Assert.True(MoveHelper.IsWater(State(FixtureWorld.FlowingWater)));

    [Fact]
    public void IsWater_StillRejectsLavaAndDryBlocks()
    {
        Assert.False(MoveHelper.IsWater(State(FixtureWorld.Lava)));
        Assert.False(MoveHelper.IsWater(State(FixtureWorld.Stone)));
        Assert.False(MoveHelper.IsWater(State(FixtureWorld.Air)));
        Assert.False(MoveHelper.IsWater(State(FixtureWorld.Fence)));
        Assert.False(MoveHelper.IsWater(default));
    }

    // Passability under the widening

    [Fact]
    public void WaterloggedFence_IsNotWalkThrough()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(1, FloorY + 1, 0, FixtureWorld.WaterloggedFence);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.False(ctx.CanWalkThrough(1, FloorY + 1, 0));
    }

    [Fact]
    public void WaterloggedStair_IsNotWalkThrough()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(1, FloorY + 1, 0, FixtureWorld.WaterloggedStairs);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.False(ctx.CanWalkThrough(1, FloorY + 1, 0));
    }

    /// <summary>Plain water stays passable, and stays gated on <c>AllowSwim</c>.</summary>
    [Fact]
    public void PlainWater_IsStillWalkThroughOnlyWhenSwimIsAllowed()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(1, FloorY + 1, 0, FixtureWorld.Water);

        Assert.True(FixtureContext.Around(world, 0, FloorY + 1, 0).CanWalkThrough(1, FloorY + 1, 0));
        Assert.False(FixtureContext
            .Around(world, 0, FloorY + 1, 0, PathfinderOptions.Default with { AllowSwim = false })
            .CanWalkThrough(1, FloorY + 1, 0));
    }

    /// <summary><c>CanTraverseWater</c>'s head clause read "water, or air, or anything that neither blocks motion nor is a fluid". Under the widening its first arm newly accepted a waterlogged solid, so a swim column would have been declared open with a waterlogged stair for a ceiling.</summary>
    [Fact]
    public void CanTraverseWater_RefusesAWaterloggedSolidHeadCell()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.Water);
        world.Set(0, FloorY + 2, 0, FixtureWorld.WaterloggedStairs);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.False(ctx.CanTraverseWater(0, FloorY + 1, 0));
    }

    /// <summary>And the feet cell: a waterlogged fence is not a cell a swimmer occupies.</summary>
    [Fact]
    public void CanTraverseWater_RefusesAWaterloggedSolidFeetCell()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Set(0, FloorY + 1, 0, FixtureWorld.WaterloggedFence);
        world.Set(0, FloorY + 2, 0, FixtureWorld.Water);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.False(ctx.CanTraverseWater(0, FloorY + 1, 0));
    }

    /// <summary>A plain water column is still traversable; this is the control for the two above.</summary>
    [Fact]
    public void CanTraverseWater_StillAcceptsAPlainWaterColumn()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, FloorY);
        world.Fill(0, FloorY + 1, 0, 0, FloorY + 3, 0, FixtureWorld.Water);

        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY + 1, 0);

        Assert.True(ctx.CanTraverseWater(0, FloorY + 1, 0));
        Assert.True(ctx.CanTraverseWater(0, FloorY + 2, 0));
    }

    // Fall damage under the widening

    [Fact]
    public void WaterloggedStair_StillTakesFallDamage()
    {
        Assert.False(MoveHelper.AbsorbsFallDamage(State(FixtureWorld.WaterloggedStairs)));
        Assert.False(MoveHelper.AbsorbsFallDamage(State(FixtureWorld.WaterloggedFence)));
    }

    /// <summary>Plain water and pre-flattening flowing water both still break a fall.</summary>
    [Fact]
    public void PlainAndFlowingWater_StillAbsorbFallDamage()
    {
        Assert.True(MoveHelper.AbsorbsFallDamage(State(FixtureWorld.Water)));
        Assert.True(MoveHelper.AbsorbsFallDamage(State(FixtureWorld.FlowingWater)));
        Assert.False(MoveHelper.AbsorbsFallDamage(State(FixtureWorld.Lava)));
    }

    /// <summary>The planner-level form. A ten-block drop is far past <see cref="PathfinderOptions.MaxFallHeight"/> and well inside <see cref="PathfinderOptions.MaxFallHeightIntoWater"/>, so the landing block is the only thing that decides it: into water it is a plan, onto a waterlogged stair it is not.</summary>
    [Fact]
    public void LongDropOntoAWaterloggedStair_IsNotPlanned()
    {
        static PathResult Plan(int landingState)
        {
            var world = new FixtureWorld();
            world.Floor(-6, 6, -6, 6, FloorY);                       // the bottom
            world.Fill(0, FloorY + 1, 0, 0, FloorY + 10, 0, FixtureWorld.Stone);   // a pillar to stand on
            world.Set(1, FloorY + 1, 0, landingState);               // the landing cell, ten blocks down

            var start = new BlockPos(0, FloorY + 11, 0);
            var goal = new BlockPos(1, FloorY + 1, 0);
            return PathPlanner.FindPath(
                world.Capture(start, goal, margin: 6), PathfinderOptions.Default, start, new GoalBlock(goal));
        }

        Assert.Equal(PathStatus.Success, Plan(FixtureWorld.Water).Status);
        Assert.NotEqual(PathStatus.Success, Plan(FixtureWorld.WaterloggedStairs).Status);
    }

    // What the widening buys

    /// <summary>The gap the widening closes: on a pre-flattening dataset a flooded channel is <c>minecraft:flowing_water</c> for most of its length, and the narrow predicate made every one of those cells invisible to the swim graph, so the corridor read as an impassable wall.</summary>
    [Fact]
    public void FlowingWaterCorridor_IsSwimmable()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 14, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, 0, 8, FloorY + 3, 0, FixtureWorld.FlowingWater);

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(8, FloorY + 2, 0);

        PathResult result = PathPlanner.FindPath(
            world.Capture(start, goal, margin: 6), PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        bool swam = false;
        foreach (MoveType move in result.Moves)
            if (move == MoveType.Swim)
            {
                swam = true;
                break;
            }

        Assert.True(swam, "a flowing-water corridor should be swum, not walled off");
    }
}
