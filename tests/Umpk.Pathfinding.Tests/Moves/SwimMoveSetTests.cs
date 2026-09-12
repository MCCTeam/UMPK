using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class SwimMoveSetTests
{
    /// <summary>A walled tank with a 1x1 water column at (3, 61..topWater, 3). Everything else in the box is stone, so the column's head cell is solid unless a caller carves it.</summary>
    private static FixtureWorld Column(int topWater)
    {
        var world = new FixtureWorld();
        world.Fill(0, 58, 0, 8, 74, 8, FixtureWorld.Stone);
        world.Fill(3, 61, 3, 3, topWater, 3, FixtureWorld.Water);
        return world;
    }

    private static MoveResult Calculate(IMove move, CalculationContext ctx, int x, int y, int z)
    {
        var result = default(MoveResult);
        move.Calculate(ctx, x, y, z, ref result);
        return result;
    }

    // MoveSwimVertical

    /// <summary>The ascend arm accepted on <c>IsWater(dest)</c> alone, so it planned a node in a water cell whose head cell is stone. A 1.8-tall body cannot occupy it; <c>MoveSwim</c> refuses the same cell through <see cref="Umpk.Pathfinding.Moves.MoveHelper.CanTraverseWater"/>.</summary>
    [Fact]
    public void SwimUp_RefusesAOneBlockTallWaterPocket()
    {
        FixtureWorld world = Column(topWater: 66);   // head cell (3, 67, 3) stays stone
        CalculationContext ctx = FixtureContext.Around(world, 3, 65, 3);

        MoveResult result = Calculate(new MoveSwimVertical(up: true), ctx, 3, 65, 3);

        Assert.True(result.IsImpossible, "the top water cell has a stone head cell and cannot hold a body");
    }

    /// <summary>The same column with its head cell carved is still a legal ascend.</summary>
    [Fact]
    public void SwimUp_AcceptsACellWithHeadroom()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        CalculationContext ctx = FixtureContext.Around(world, 3, 65, 3);

        MoveResult result = Calculate(new MoveSwimVertical(up: true), ctx, 3, 65, 3);

        Assert.False(result.IsImpossible);
        Assert.Equal(66, result.DestY);
    }

    /// <summary>The surface-break arm planted the feet in the air cell above the water. Physics cannot hold that: the body leaves the fluid, gravity applies, and it falls straight back in, so an <c>Ascend</c> planned out of such a node can never open its <c>(OnGround || inFluid)</c> jump gate. The top water cell is the surfacing node; the cell above it is not a node at all.</summary>
    [Fact]
    public void SwimUp_DoesNotEmitAFeetInAirNode()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3);

        MoveResult result = Calculate(new MoveSwimVertical(up: true), ctx, 3, 66, 3);

        Assert.True(result.IsImpossible, "feet in air above water is not a state the engine can hold");
    }

    /// <summary>The planner-level form. A cell of open air over the middle of a pool is not somewhere a body can be: nothing holds it up and the fluid it just left is below it. The surface-break arm made that cell a node, so the planner answered "Success" for a goal it can never occupy. It is a refusal, not a route. The supported case, a bank cell one block over the same waterline, is <see cref="GoalOneAboveSurface_StillReachable"/>, and that one has to keep working.</summary>
    [Fact]
    public void SwimUp_RefusesAGoalFloatingOverOpenWater()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 16, -6, 6, 64);                                 // land, top face y=64
        world.Floor(1, 8, -2, 2, 59);                                   // pool bed
        world.Fill(1, 60, -2, 8, 64, 2, FixtureWorld.Water);            // pool, top water cell y=64
        world.Fill(1, 65, -2, 8, 70, 2, FixtureWorld.Air);              // open sky over the pool

        var start = new BlockPos(4, 61, 0);
        var goal = new BlockPos(4, 65, 0);                               // air, directly over the pool
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    // MoveSwimExit

    [Fact]
    public void SwimExit_RefusesWhenTheSwimmersOwnHeadCellIsBlocked()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 68, 3, 3, 70, 3, FixtureWorld.Air);   // y+2 open, y+1 (3,67,3) still stone
        world.Fill(4, 67, 3, 4, 70, 3, FixtureWorld.Air);   // the ledge at (4,66,3) with clear cells over it

        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3);

        MoveResult result = Calculate(new MoveSwimExit(1, 0), ctx, 3, 66, 3);

        Assert.True(result.IsImpossible, "a swimmer cannot rise through its own head cell");
    }

    /// <summary>With the cap carved away, the same climb-out is legal.</summary>
    [Fact]
    public void SwimExit_ClimbsOutOnceItsOwnHeadCellIsClear()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        world.Fill(4, 67, 3, 4, 70, 3, FixtureWorld.Air);

        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3);

        MoveResult result = Calculate(new MoveSwimExit(1, 0), ctx, 3, 66, 3);

        Assert.False(result.IsImpossible);
        Assert.Equal(4, result.DestX);
        Assert.Equal(67, result.DestY);
    }

    /// <summary>An exit under an overhang, or in a corner between pools: the cells over the ledge are water. The planner approved them because <c>CanWalkThrough</c> admits water when <c>AllowSwim</c> is set; the clearance test refuses them because any liquid in the probe is fatal to the hop, so the 0.3 lift never fires and the executor grinds into the bank until its stuck counter trips.</summary>
    [Fact]
    public void SwimExit_RefusesWhenTheLedgeCellsHoldWater()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        world.Fill(4, 68, 3, 4, 70, 3, FixtureWorld.Air);
        world.Set(4, 67, 3, FixtureWorld.Water);            // the destination cell is flooded

        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3);

        MoveResult result = Calculate(new MoveSwimExit(1, 0), ctx, 3, 66, 3);

        Assert.True(result.IsImpossible, "the hop probe refuses a box that contains liquid");
    }

    /// <summary>A pier whose deck is TWO blocks over the waterline. Measured against this engine on protocols 47, 340, 772 and 776, with Forward+Jump, with and without Sprint, at pitch 0 and pitch -90, and with a full-depth run-up: the body tops out at y=65.9147 (47/340) and y=65.9234 (772/776) over a surface plane of y=65.0, and never once reaches the y=66.0 its feet would need. The hop stops firing the moment the body stops touching the fluid, and the ballistic tail is ~0.92 blocks. So a two-up exit is refused, and the deck is reached the long way or not at all.</summary>
    [Fact]
    public void SwimExit_RefusesATwoUpPier()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        world.Set(4, 67, 3, FixtureWorld.Stone);            // the deck, one block over the waterline
        world.Fill(4, 68, 3, 4, 70, 3, FixtureWorld.Air);   // standable cell (4, 68, 3)

        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3);

        MoveResult result = Calculate(new MoveSwimExit(1, 0), ctx, 3, 66, 3);

        Assert.True(result.IsImpossible, "jumpOutOfFluid cannot lift a body two blocks over the waterline");
    }

    /// <summary>The exit's jump penalty must come from the caller's options, not from the constant.</summary>
    [Fact]
    public void SwimExit_ChargesTheOptionsJumpPenalty()
    {
        FixtureWorld world = Column(topWater: 66);
        world.Fill(3, 67, 3, 3, 70, 3, FixtureWorld.Air);
        world.Fill(4, 67, 3, 4, 70, 3, FixtureWorld.Air);

        var options = PathfinderOptions.Default with { JumpPenalty = 7.0 };
        CalculationContext ctx = FixtureContext.Around(world, 3, 66, 3, options);

        MoveResult result = Calculate(new MoveSwimExit(1, 0), ctx, 3, 66, 3);

        Assert.False(result.IsImpossible);
        Assert.Equal(ActionCosts.SwimOneBlock + 14.0, result.Cost, 9);
    }

    // Reachability guard for the surface-break removal

    [Fact]
    public void GoalOneAboveSurface_StillReachable()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 16, -6, 6, 64);                                 // land, top face y=64
        world.Floor(1, 8, -2, 2, 59);                                   // pool bed
        world.Fill(1, 60, -2, 8, 64, 2, FixtureWorld.Water);            // pool, top water cell y=64
        world.Fill(1, 65, -2, 8, 70, 2, FixtureWorld.Air);              // open sky over the pool

        var start = new BlockPos(4, 61, 0);                              // submerged, mid-pool
        var goal = new BlockPos(10, 65, 0);                              // the bank cell, one above the surface
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode end = result.Path[^1];
        Assert.Equal(goal.X, end.X);
        Assert.Equal(goal.Y, end.Y);
        Assert.Equal(goal.Z, end.Z);

        foreach (PathNode node in result.Path)
        {
            bool feetInAir = view.GetBlock(new BlockPos(node.X, node.Y, node.Z)).IsAir;
            bool waterBelow = view.GetBlock(new BlockPos(node.X, node.Y - 1, node.Z)).IsFluid;
            Assert.False(feetInAir && waterBelow, $"node ({node.X},{node.Y},{node.Z}) floats over water");
        }
    }

    // E11 replica (pathfinding course row "elevator")

    /// <summary>The course's E11 "elevator" as generated: a 15-tall 1x1 water column in a stone casing, with the exit corridor carved at y=115..116 in the cells BESIDE the column while the column itself stops at y=114. The column is capped one block under its own exit, so there is no route, and the planner must say so rather than plan into the cap.</summary>
    [Fact]
    public void E11Elevator_AsGenerated_IsUnreachable()
    {
        FixtureWorld world = BuildE11(carveColumnTop: false);
        var start = new BlockPos(15, 100, 10);
        var goal = new BlockPos(15, 115, 10);

        PathResult result = PathPlanner.FindPath(
            world.Capture(start, goal, margin: 12), PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The same column with its top carved through to the corridor is a plain swim-up and exit.</summary>
    [Fact]
    public void E11Elevator_WithTheColumnTopCarved_SwimsUpAndClimbsOut()
    {
        FixtureWorld world = BuildE11(carveColumnTop: true);
        var start = new BlockPos(15, 100, 10);
        var goal = new BlockPos(15, 115, 10);

        PathResult result = PathPlanner.FindPath(
            world.Capture(start, goal, margin: 12), PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);

        int swimMoves = 0;
        foreach (MoveType move in result.Moves)
            if (move == MoveType.Swim)
                swimMoves++;

        Assert.True(swimMoves >= 14, $"expected the 15-tall column to be swum, saw {swimMoves} swim moves");
    }

    private static FixtureWorld BuildE11(bool carveColumnTop)
    {
        var world = new FixtureWorld();
        world.Fill(8, 99, 8, 12, 117, 12, FixtureWorld.Stone);          // casing y=99..117
        world.Fill(10, 100, 10, 10, 114, 10, FixtureWorld.Water);       // the 15-tall column
        world.Set(10, 99, 10, FixtureWorld.Stone);                      // base block
        world.Fill(11, 100, 10, 12, 101, 10, FixtureWorld.Air);         // bottom side opening
        world.Fill(11, 115, 10, 12, 116, 10, FixtureWorld.Air);         // top side opening
        world.Fill(13, 99, 9, 17, 99, 11, FixtureWorld.Stone);          // bottom pad (feet y=100)
        world.Fill(13, 100, 9, 17, 102, 9, FixtureWorld.Stone);
        world.Fill(13, 100, 11, 17, 102, 11, FixtureWorld.Stone);
        world.Fill(17, 100, 9, 17, 102, 11, FixtureWorld.Stone);
        world.Fill(13, 114, 9, 17, 114, 11, FixtureWorld.Stone);        // top pad (feet y=115)
        if (carveColumnTop)
            world.Fill(10, 115, 10, 10, 116, 10, FixtureWorld.Air);

        return world;
    }
}
