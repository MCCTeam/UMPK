using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class FootprintSupportPlanningTests
{
    private const int FloorY = 64;

    private static readonly PathfinderOptions Refused =
        PathfinderOptions.Default with { AllowPartialHeightSupport = false };

    /// <summary>The representative sweep: one state per outcome the footprint query can produce, each with the height it produces and the reason that height is what it is.</summary>
    [Theory]
    [InlineData(FixtureWorld.Stone, true, "full cube: Solid, decided before the footprint arm is reached")]
    [InlineData(FixtureWorld.BottomSlab, true, "0.5, a flat full-footprint top")]
    [InlineData(FixtureWorld.TopSlab, true, "1.0, a flat full-footprint top")]
    [InlineData(FixtureWorld.SnowLayer, true, "0.375, a flat full-footprint top")]
    [InlineData(FixtureWorld.Carpet, true, "0.0625, the thinnest floor in the game and still a floor")]
    [InlineData(FixtureWorld.Honey, true, "0.9375: inset 1/16 a side, so no full cover, but a 0.6 body fits")]
    [InlineData(FixtureWorld.LilyPad, true, "0.09375: the same inset, and the body stays dry on it")]
    [InlineData(FixtureWorld.StairsBottom, true, "1.0: the raised octet overlaps a centred footprint")]
    [InlineData(FixtureWorld.Cauldron, true, "0.25: the walls reach 1.0 at the edges, the body sits inside")]
    [InlineData(FixtureWorld.Campfire, false, "0.4375 of real floor, but a curated hazard: that gate is first")]
    [InlineData(FixtureWorld.SnowLayerOne, false, "0.0: the EMPTY shape, a pass-through and not a floor")]
    [InlineData(FixtureWorld.Fence, false, "1.5: the post protrudes past its own cell")]
    [InlineData(FixtureWorld.ClosedFenceGate, false, "1.5: a closed gate protrudes into the head cell")]
    [InlineData(FixtureWorld.Air, false, "air, refused before the footprint arm")]
    [InlineData(FixtureWorld.Water, false, "a fluid, refused before the footprint arm")]
    [InlineData(FixtureWorld.Ladder, false, "climbable, refused before the footprint arm")]
    [InlineData(FixtureWorld.MagmaBlock, false, "a curated hazard, refused before the footprint arm")]
    public void CanWalkOn_ByDefault(int stateId, bool standable, string why)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(standable, MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void ACampfire_IsRealFloorRefusedByTheHazardGate()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.Campfire);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);
        Umpk.Game.Blocks.BlockState state = ctx.GetBlock(0, FloorY, 0);

        Assert.Equal(
            0.4375,
            Umpk.Game.Blocks.BlockSupport.FootprintSupportHeight(
                world.Shapes.GetCollisionShapes(state), 0.5, 0.5, 0.3));
        Assert.True(MoveHelper.IsHazard(ctx, state));
        Assert.False(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
    }

    /// <summary>A pass-through is passable. <c>snow[layers=1]</c> carries no collision box at all, so it is not a floor AND it is not a wall; refusing it as a support must not turn it into an obstacle.</summary>
    [Fact]
    public void SnowLayerOne_IsWalkedThroughRatherThanStoodOn()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.SnowLayerOne);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.False(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
        Assert.True(MoveHelper.CanWalkThrough(ctx, 0, FloorY, 0));
    }

    /// <summary>The support arm is still a body test: a partial block is never routed THROUGH.</summary>
    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.Honey)]
    [InlineData(FixtureWorld.LilyPad)]
    [InlineData(FixtureWorld.StairsBottom)]
    public void CanWalkThrough_IsUnchangedForAPartialBlock(int stateId)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);

        Assert.False(MoveHelper.CanWalkThrough(FixtureContext.Around(world, 0, FloorY, 0), 0, FloorY, 0));
        Assert.False(
            MoveHelper.CanWalkThrough(FixtureContext.Around(world, 0, FloorY, 0, Refused), 0, FloorY, 0));
    }

    /// <summary>With the option off the graph narrows to full unit cubes, which is the bisection aid the flag is for. Nothing partial stands, however tall.</summary>
    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.TopSlab)]
    [InlineData(FixtureWorld.SnowLayer)]
    [InlineData(FixtureWorld.Carpet)]
    [InlineData(FixtureWorld.Honey)]
    [InlineData(FixtureWorld.LilyPad)]
    [InlineData(FixtureWorld.StairsBottom)]
    [InlineData(FixtureWorld.Cauldron)]
    public void CanWalkOn_RefusesEveryPartialSupport_WhenTheOptionIsOff(int stateId)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0, Refused);

        Assert.False(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
    }

    /// <summary>A stone floor is a stone floor either way; the flag must not touch it.</summary>
    [Fact]
    public void CanWalkOn_IsUnchangedForAFullBlock_WhenTheOptionIsOff()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.Stone);

        Assert.True(MoveHelper.CanWalkOn(FixtureContext.Around(world, 0, FloorY, 0), 0, FloorY, 0));
        Assert.True(MoveHelper.CanWalkOn(FixtureContext.Around(world, 0, FloorY, 0, Refused), 0, FloorY, 0));
    }

    /// <summary>The planner half, M9's shape: a three-cell bridge of one support family between two stone banks, with parkour off so the bridge is the only route. Honey, lily pads and bottom stairs are the rows that stayed <c>Failed</c> under the full-cover test with the option already on.</summary>
    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.Carpet)]
    [InlineData(FixtureWorld.Honey)]
    [InlineData(FixtureWorld.LilyPad)]
    [InlineData(FixtureWorld.StairsBottom)]
    [InlineData(FixtureWorld.Cauldron)]
    public void Planner_CrossesABridgeOfEverySupportFamily(int stateId)
    {
        PathResult result = BridgePlan(stateId, PathfinderOptions.Default);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(9, result.Path[^1].X);
    }

    /// <summary>The same bridge with the flag off: the banks are two islands.</summary>
    [Theory]
    [InlineData(FixtureWorld.BottomSlab)]
    [InlineData(FixtureWorld.Honey)]
    [InlineData(FixtureWorld.StairsBottom)]
    public void Planner_FindsNoRouteAcrossThatBridge_WhenTheOptionIsOff(int stateId)
        => Assert.NotEqual(PathStatus.Success, BridgePlan(stateId, Refused).Status);

    private static PathResult BridgePlan(int stateId, PathfinderOptions options)
    {
        var world = new FixtureWorld();
        world.Floor(-2, 2, 0, 0, FloorY);
        world.Fill(3, FloorY, 0, 5, FloorY, 0, stateId);
        world.Floor(6, 10, 0, 0, FloorY);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(9, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        return PathPlanner.FindPath(
            view,
            options with { AllowParkour = false, AllowParkourAscend = false },
            start,
            new GoalBlock(goal));
    }
}
