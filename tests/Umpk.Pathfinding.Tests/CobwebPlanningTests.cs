using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class CobwebPlanningTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    private static FixtureWorld F8World()
    {
        var world = new FixtureWorld();
        world.Fill(448, 99, 256, 459, 99, 262, FixtureWorld.Stone);
        world.Fill(453, 100, 256, 453, 101, 261, FixtureWorld.Cobweb);
        return world;
    }

    /// <summary>A web cell is still a corridor, not a wall. Charging it must not turn it into an obstacle: the whole point is that the plan may cross one when crossing is genuinely cheaper than going round.</summary>
    [Fact]
    public void ACobwebCell_IsStillWalkedThrough()
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.Cobweb);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.True(MoveHelper.CanWalkThrough(ctx, 0, 64, 0));
        Assert.False(MoveHelper.CanWalkOn(ctx, 0, 64, 0));
        Assert.True(MoveHelper.IsCobweb(ctx.GetBlock(0, 64, 0)));
    }

    /// <summary>The charge itself, at the two cells the body occupies and at the one it does not. A body 1.8 tall overlaps its feet cell and the cell above, so a web hung at head height holds it exactly as one at foot height does; a web two cells up holds nothing.</summary>
    [Theory]
    [InlineData(0, ActionCosts.MeasuredWebCostMultiplier)]
    [InlineData(1, ActionCosts.MeasuredWebCostMultiplier)]
    [InlineData(2, 1.0)]
    public void TheWebCharge_CoversBothBodyCellsAndNoOthers(int webOffset, double expected)
    {
        var world = new FixtureWorld();
        world.Fill(-2, 63, -2, 2, 63, 2, FixtureWorld.Stone);
        world.Set(0, 64 + webOffset, 0, FixtureWorld.Cobweb);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.Equal(expected, MoveHelper.WebPenalty(ctx, 0, 64, 0), 4);
    }

    /// <summary>A web and a slow floor compose, because the engine applies them through two different code paths: the block-speed factor scales velocity inside the friction loop, while cobweb contact multiplies the whole tick's movement and zeroes velocity.</summary>
    [Fact]
    public void AWebOverSoulSand_ChargesBoth()
    {
        var world = new FixtureWorld();
        world.Fill(-2, 63, -2, 2, 63, 2, FixtureWorld.SoulSand);
        world.Set(0, 64, 0, FixtureWorld.Cobweb);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.Equal(
            ActionCosts.MeasuredSlowFloorCostMultiplier * ActionCosts.MeasuredWebCostMultiplier,
            MoveHelper.FloorSpeedPenalty(ctx, 0, 64, 0),
            4);
    }

    /// <summary>A body in a web cannot jump, which is a stronger claim than "a jump out of a web is dear". Cobweb contact zeroes velocity on the tick the impulse would have been carried by, so no arc exists to price.</summary>
    [Fact]
    public void ABodyInAWeb_CannotTakeOffForAJump()
    {
        var world = new FixtureWorld();
        world.Fill(-2, 63, -2, 2, 63, 2, FixtureWorld.Stone);
        CalculationContext clear = FixtureContext.Around(world, 0, 64, 0);
        Assert.True(MoveHelper.CanTakeOffForAJump(clear, 0, 64, 0));

        world.Set(0, 64, 0, FixtureWorld.Cobweb);
        CalculationContext webbed = FixtureContext.Around(world, 0, 64, 0);
        Assert.False(MoveHelper.CanTakeOffForAJump(webbed, 0, 64, 0));
    }

    /// <summary>F8's plan is no longer a straight line through the web. The row's own note said the clear detour at dz=6 is what a hardened planner should pick, and with the crossing priced at its measured rate A* picks it: the plan's cells are all web-free.</summary>
    [Fact]
    public void F8_RoutesRoundTheWebRatherThanThroughIt()
    {
        FixtureWorld world = F8World();
        var start = new BlockPos(449, 100, 259);
        var goal = new BlockPos(458, 100, 259);
        PlanningWorldView view = world.Capture(start, goal, margin: 16);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);

        var ctx = new CalculationContext(view, PathfinderOptions.Default);
        foreach (PathNode node in result.Path)
            Assert.False(
                MoveHelper.IsCobweb(ctx.GetBlock(node.X, node.Y, node.Z))
                    || MoveHelper.IsCobweb(ctx.GetBlock(node.X, node.Y + 1, node.Z)),
                $"the plan still enters the web at ({node.X},{node.Y},{node.Z})");

    }

    [Fact]
    public void F8Sealed_CrossesTheWebAndSaysWhatItCosts()
    {
        var world = new FixtureWorld();
        world.Fill(448, 99, 256, 459, 99, 262, FixtureWorld.Stone);
        world.Fill(453, 100, 256, 453, 101, 262, FixtureWorld.Cobweb);

        var start = new BlockPos(449, 100, 259);
        var goal = new BlockPos(458, 100, 259);
        PlanningWorldView view = world.Capture(start, goal, margin: 16);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var exec = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(exec, segments, new Vec3d(449.5, 100, 259.5), 270f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 1200);

        Assert.True(
            state == PathExecutorState.Complete,
            $"the sealed F8 lane ended {state} at {Fmt(driver.State.Position)} after {driver.Trace.Count} ticks");
        Assert.True(
            driver.Trace.Count >= 70,
            $"the sealed lane took only {driver.Trace.Count} ticks; the same nine-block lane with the "
                + "web deleted takes 40 and with it 80, so a run under 70 means the crossing is not "
                + "being simulated at all");
    }

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");
}
