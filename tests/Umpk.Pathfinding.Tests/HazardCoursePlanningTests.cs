using System.Globalization;
using System.Text;
using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class HazardCoursePlanningTests
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    /// <summary>The harness's plan capture margin (see the class remarks).</summary>
    private const int CourseMargin = 24;

    private static readonly BlockPos CourseStart = new(1, BodyY, 3);
    private static readonly BlockPos CourseGoal = new(10, BodyY, 3);

    private static FixtureWorld LavaChannel()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 11, FloorY, 6, FixtureWorld.Stone);
        world.Fill(5, FloorY - 1, 0, 7, FloorY - 1, 5, FixtureWorld.Stone);
        world.Fill(5, FloorY, 0, 7, FloorY, 5, FixtureWorld.Lava);
        return world;
    }

    private static FixtureWorld MagmaStrip()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 11, FloorY, 6, FixtureWorld.Stone);
        world.Fill(5, FloorY, 0, 5, FloorY, 4, FixtureWorld.MagmaBlock);
        world.Fill(5, FloorY, 6, 5, FloorY, 6, FixtureWorld.MagmaBlock);
        return world;
    }

    private static FixtureWorld PowderSnowPit()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 11, FloorY, 6, FixtureWorld.Stone);
        world.Fill(5, FloorY - 3, 0, 7, FloorY - 1, 5, FixtureWorld.Stone);
        world.Fill(5, FloorY, 0, 7, FloorY, 5, FixtureWorld.PowderSnow);
        return world;
    }

    private static FixtureWorld MagmaDetour()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 4, FloorY, 20, FixtureWorld.Stone);
        world.Fill(8, FloorY, 0, 11, FloorY, 20, FixtureWorld.Stone);
        world.Fill(0, FloorY, 18, 11, FloorY, 20, FixtureWorld.Stone);
        world.Fill(0, FloorY, 0, 11, FloorY, 6, FixtureWorld.Stone);
        world.Fill(5, FloorY, 0, 7, FloorY, 6, FixtureWorld.MagmaBlock);
        return world;
    }

    private static PathResult PlanCourseRow(FixtureWorld world)
    {
        PlanningWorldView view = world.Capture(CourseStart, CourseGoal, CourseMargin);
        return PathPlanner.FindPath(view, PathfinderOptions.Default, CourseStart, new GoalBlock(CourseGoal));
    }

    /// <summary>Every horizontal cell the plan sweeps the body across, endpoints and interpolated cells alike, paired with the two feet levels of the leg it belongs to. A multi-block sprint jump is therefore measured across its whole span rather than only at its two nodes, which may both stand on stone.</summary>
    private static IEnumerable<(int X, int FeetY, int Z)> SweptCells(PathResult result)
    {
        for (int i = 1; i < result.Path.Count; i++)
        {
            PathNode from = result.Path[i - 1];
            PathNode to = result.Path[i];
            int steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Z - from.Z));
            for (int step = 0; step <= steps; step++)
            {
                int x = steps == 0 ? to.X : from.X + (int)Math.Round((double)(to.X - from.X) * step / steps, MidpointRounding.AwayFromZero);
                int z = steps == 0 ? to.Z : from.Z + (int)Math.Round((double)(to.Z - from.Z) * step / steps, MidpointRounding.AwayFromZero);
                yield return (x, from.Y, z);
                if (to.Y != from.Y)
                    yield return (x, to.Y, z);

            }
        }
    }

    /// <summary>Asserts no cell the plan carries the body across is a hazard, and no cell it is carried over rests on one.</summary>
    private static void AssertRouteClearsHazards(FixtureWorld world, PathResult result, string row)
    {
        PlanningWorldView view = world.Capture(CourseStart, CourseGoal, CourseMargin);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);
        var offences = new SortedSet<string>(StringComparer.Ordinal);

        foreach ((int x, int feetY, int z) in SweptCells(result))
        {
            Report(ctx, offences, "body", x, feetY, z);
            Report(ctx, offences, "support", x, feetY - 1, z);
        }

        if (offences.Count > 0)
            Assert.Fail($"{row}: the plan's route crosses hazard cells:\n{Join(offences)}{Render(result)}");

    }

    private static void AssertRouteAvoidsBoxes(PathResult result, string row, params (int X0, int Z0, int X1, int Z1)[] boxes)
    {
        var offences = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((int x, int feetY, int z) in SweptCells(result))
            foreach ((int x0, int z0, int x1, int z1) in boxes)
                if (x >= x0 && x <= x1 && z >= z0 && z <= z1)
                    offences.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  ({x},{feetY},{z}) is inside the forbidden box [{x0}, {z0}, {x1}, {z1}]"));

        if (offences.Count > 0)
            Assert.Fail($"{row}: the plan's route enters a trace-guard box:\n{Join(offences)}{Render(result)}");

    }

    private static void Report(CalculationContext ctx, SortedSet<string> offences, string role, int x, int y, int z)
    {
        BlockState state = ctx.GetBlock(x, y, z);
        if (!MoveHelper.IsHazard(ctx, state))
            return;

        offences.Add(string.Create(CultureInfo.InvariantCulture, $"  {role} cell ({x},{y},{z}) is {state.Block.Id}"));
    }

    private static string Join(SortedSet<string> offences)
    {
        var text = new StringBuilder();
        foreach (string offence in offences)
            text.Append(offence).Append('\n');

        return text.ToString();
    }

    private static string Render(PathResult result)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"plan: status={result.Status} nodes={result.Path.Count}\n");
        for (int i = 0; i < result.Path.Count; i++)
        {
            PathNode n = result.Path[i];
            string move = i == 0 ? "start" : result.Moves[i - 1].ToString();
            sb.Append(CultureInfo.InvariantCulture, $"  [{i}] ({n.X},{n.Y},{n.Z}) via {move}\n");
        }

        return sb.ToString();
    }

    [Fact]
    public void F1_LavaChannel_IsRoutedAroundNotOver()
    {
        FixtureWorld world = LavaChannel();
        PathResult result = PlanCourseRow(world);

        Assert.Equal(PathStatus.Success, result.Status);
        AssertRouteClearsHazards(world, result, "F1 lava");
        AssertRouteAvoidsBoxes(result, "F1 lava", (5, 0, 7, 5));
        Assert.Equal(CourseGoal.X, result.Path[^1].X);
    }

    [Fact]
    public void F4_MagmaStrip_IsRoutedAroundNotOver()
    {
        FixtureWorld world = MagmaStrip();
        PathResult result = PlanCourseRow(world);

        Assert.Equal(PathStatus.Success, result.Status);
        AssertRouteClearsHazards(world, result, "F4 magma");
        AssertRouteAvoidsBoxes(result, "F4 magma", (5, 0, 5, 4), (5, 6, 5, 6));
        Assert.Equal(CourseGoal.X, result.Path[^1].X);
    }

    [Fact]
    public void F7_PowderSnowPit_IsRoutedAroundNotOver()
    {
        FixtureWorld world = PowderSnowPit();
        PathResult result = PlanCourseRow(world);

        Assert.Equal(PathStatus.Success, result.Status);
        AssertRouteClearsHazards(world, result, "F7 powder");
        AssertRouteAvoidsBoxes(result, "F7 powder", (5, 0, 7, 5));
        Assert.Equal(CourseGoal.X, result.Path[^1].X);
    }

    [Fact]
    public void F10_MagmaAcrossTheDirectLine_IsNeverCrossed()
    {
        FixtureWorld world = MagmaDetour();
        PathResult result = PlanCourseRow(world);

        Assert.Equal(PathStatus.Success, result.Status);
        AssertRouteClearsHazards(world, result, "F10 hazarddetour");
        AssertRouteAvoidsBoxes(result, "F10 hazarddetour", (5, 0, 7, 6));
        Assert.Equal(CourseGoal.X, result.Path[^1].X);
    }

    /// <summary>A hazard floor is a floor, not a gap. This is the distinction the jump family lost: <see cref="MoveHelper.CanWalkOn"/> answers "may the plan stand here", and reading its refusal as "there is nothing here" turned "never stand on magma" into "always sprint-jump over magma".</summary>
    [Theory]
    [InlineData(FixtureWorld.MagmaBlock)]
    [InlineData(FixtureWorld.PowderSnow)]
    [InlineData(FixtureWorld.Lava)]
    public void AHazardFloorIsNotAnOpenGap(int stateId)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.False(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
        Assert.False(MoveHelper.IsOpenGap(ctx, 0, FloorY, 0));
        Assert.True(MoveHelper.IsHazardAt(ctx, 0, FloorY, 0));
    }

    /// <summary>Air is the open gap the jump family exists to cross; a stone floor is not one.</summary>
    [Fact]
    public void AirIsAnOpenGapAndStoneIsNot()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.Stone);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.True(MoveHelper.IsOpenGap(ctx, 3, FloorY, 0));
        Assert.False(MoveHelper.IsHazardAt(ctx, 3, FloorY, 0));
        Assert.False(MoveHelper.IsOpenGap(ctx, 0, FloorY, 0));
    }

    /// <summary>Powder snow's dataset shape is the empty one, so it blocks motion no more than a flower does; only the curated hazard set keeps a body out of it. Pinned because if that ever changed, F7 would start passing for the wrong reason.</summary>
    [Fact]
    public void PowderSnowIsRefusedByBothPassabilityChecks()
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, FixtureWorld.PowderSnow);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        BlockState state = ctx.GetBlock(0, FloorY, 0);
        Assert.False(state.BlocksMotion);
        Assert.False(state.IsSolid);
        Assert.False(MoveHelper.CanWalkThrough(ctx, 0, FloorY, 0));
        Assert.False(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
    }

    /// <summary>A pit whose hazard sits below the walking plane still has a genuine gap there and remains jumpable. Nearby hazards alone do not prohibit parkour.</summary>
    [Fact]
    public void ALavaPitBelowTheWalkingPlaneIsStillJumpable()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 3, FloorY, 0, FixtureWorld.Stone);
        world.Fill(7, FloorY, 0, 11, FloorY, 0, FixtureWorld.Stone);
        world.Fill(4, FloorY - 4, 0, 6, FloorY - 4, 0, FixtureWorld.Lava);

        var start = new BlockPos(0, BodyY, 0);
        var goal = new BlockPos(10, BodyY, 0);
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.True(MoveHelper.IsOpenGap(ctx, 5, FloorY, 0));

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(MoveType.Parkour, result.Moves);
    }
}
