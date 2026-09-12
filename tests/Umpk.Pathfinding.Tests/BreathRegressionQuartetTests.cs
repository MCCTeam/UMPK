using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class BreathRegressionQuartetTests
{
    private const int FloorY = 64;

    /// <summary>The bore length E3 and E4 share, in blocks.</summary>
    private const int BoreLength = 80;

    private readonly ITestOutputHelper _output;

    public BreathRegressionQuartetTests(ITestOutputHelper output) => _output = output;

    /// <summary>E3: eighty blocks of flooded bore under a solid lid with no air anywhere inside it. About 816 real ticks against a 300-tick lung, and no way to bank any of it.</summary>
    [Fact]
    public void E3_SealedBore_IsRefused()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = SealedBore();

        PathResult result = Plan(world, start, goal);

        _output.WriteLine($"E3 sealed bore: {result.Status}, {result.NodesExplored} nodes");
        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>E18: a forty-five block one-way descent into a flooded shaft with no refill below the mouth.</summary>
    [Fact]
    public void E18_OneWayDescent_IsRefused()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = OneWayDescent(depth: 45);

        PathResult result = Plan(world, start, goal);

        _output.WriteLine($"E18 one-way descent: {result.Status}, {result.NodesExplored} nodes");
        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>E4: E3's bore with three sealed two-high air bells over it, the middle one at the end of a two-block dead-end spur so that reaching it is a routing decision and not something the straight line collects for free. Every leg between bells fits inside one lung; the whole bore does not.</summary>
    [Fact]
    public void E4_BoreWithSealedBells_Plans()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = BoreWithBells();

        PathResult result = Plan(world, start, goal);

        _output.WriteLine(
            $"E4 bells: {result.Status}, {result.NodesExplored} nodes, {result.Path.Count} path nodes, "
            + $"cost {result.Cost:F1}");
        Assert.Equal(PathStatus.Success, result.Status);

        // And it really is the spur bell that carries it: the off-line detour is IN the route.
        Assert.Contains(result.Path, node => node.Z != 0);
    }

    /// <summary>E5: a lidded trench three deep whose lid carries breathable holes at path positions 18, 36 and 54 plus the entry mouth. Each hole is collared, so it can be breathed at and not climbed out of and the lid cannot be walked to it; the submerged stretches are 18/18/18/16 blocks.</summary>
    [Fact]
    public void E5_TrenchWithSurfaceHoles_Plans()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = TrenchWithSurfaceHoles();

        PathResult result = Plan(world, start, goal);

        _output.WriteLine(
            $"E5 surface holes: {result.Status}, {result.NodesExplored} nodes, {result.Path.Count} path "
            + $"nodes, cost {result.Cost:F1}");
        Assert.Equal(PathStatus.Success, result.Status);
    }

    /// <summary>The route the search returns for E4 and E5 has to survive the validator too, on the same lung. The two are meant to agree by construction (the search prices a submerged block at 10.204, at least what the validator charges), so a disagreement here is the search approving something the executor's own runtime assertion would refuse.</summary>
    [Theory]
    [InlineData("E4")]
    [InlineData("E5")]
    public void PlannedRoute_AlsoPassesTheValidator(string row)
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = row == "E4"
            ? BoreWithBells()
            : TrenchWithSurfaceHoles();

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        BreathValidation validation = BreathValidator.Validate(
            segments, view, PhysicsProfile.ForProtocol(772), allowSprint: true, (int)BreathModel.FullLungTicks);

        _output.WriteLine(
            $"{row}: peak deficit {validation.PeakDeficitTicks:F1} of a {validation.BudgetTicks:F0}-tick budget");
        Assert.True(
            validation.IsSurvivable,
            $"{row} planned but the validator refuses it: peak {validation.PeakDeficitTicks:F1} ticks, "
            + $"budget {validation.BudgetTicks:F0}, first violation at segment "
            + $"{validation.FirstViolationSegment} of {segments.Count}");
    }

    private static PathResult Plan(FixtureWorld world, BlockPos start, BlockPos goal)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        return PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
    }

    /// <summary>The shared casing: a two-cell-tall lane through solid stone, dry at both mouths and flooded for <see cref="BoreLength"/> blocks in between under a lid.</summary>
    private static FixtureWorld Bore()
    {
        var world = new FixtureWorld();
        world.Fill(-6, FloorY - 1, -4, BoreLength + 5, FloorY + 6, 4, FixtureWorld.Stone);
        world.Fill(-5, FloorY, 0, BoreLength + 4, FloorY + 1, 0, FixtureWorld.Air);
        world.Fill(0, FloorY, 0, BoreLength - 1, FloorY + 1, 0, FixtureWorld.Water);
        return world;
    }

    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) SealedBore()
        => (Bore(), new BlockPos(-3, FloorY, 0), new BlockPos(BoreLength + 2, FloorY, 0));

    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) BoreWithBells()
    {
        FixtureWorld world = Bore();

        // Two bells directly over the lane: air at the two cells above the swimmer's head cell, sealed by the casing above them.
        foreach (int x in new[] { 20, 60 })
            world.Fill(x, FloorY + 2, 0, x, FloorY + 3, 0, FixtureWorld.Air);

        // The middle bell is at the end of a two-block dead-end spur, so reaching it is a detour the search has to choose rather than something the straight line passes under.
        world.Fill(42, FloorY, 1, 42, FloorY + 1, 2, FixtureWorld.Water);
        world.Fill(42, FloorY + 2, 2, 42, FloorY + 3, 2, FixtureWorld.Air);

        return (world, new BlockPos(-3, FloorY, 0), new BlockPos(BoreLength + 2, FloorY, 0));
    }

    /// <summary>Course row E5: a trench three cells deep under a lid, with collared breathable holes at path positions 18, 36 and 54 plus the bare entry mouth at 0, and the goal on the far trench bed.</summary>
    /// <remarks>The collar is what makes the row about breathing rather than about walking: a three-high stone ring standing on the lid round each hole, with an air chimney up its middle. A swimmer can put its head in the hole; the cells it would climb out onto are stone and the collar top is two above the lid, so the hole is not a door and the lid is not a road to it.</remarks>
    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) TrenchWithSurfaceHoles()
    {
        const int length = 71;
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 2, -4, length + 3, FloorY + 8, 4, FixtureWorld.Stone);

        // The trench: bed at FloorY - 1, water three deep, lid at FloorY + 3.
        world.Fill(0, FloorY, 0, length, FloorY + 2, 0, FixtureWorld.Water);

        // The entry mouth, bare: it is the way in, and it leads nowhere now the others are collared.
        world.Set(0, FloorY + 3, 0, FixtureWorld.Air);

        foreach (int x in new[] { 18, 36, 54 })
        {
            world.Set(x, FloorY + 3, 0, FixtureWorld.Air);
            world.Fill(x, FloorY + 4, 0, x, FloorY + 6, 0, FixtureWorld.Air);
        }

        // Start floating at the mouth with the head in air, finish on the far trench bed.
        return (world, new BlockPos(0, FloorY + 2, 0), new BlockPos(length, FloorY, 0));
    }

    /// <summary>Course row E18: a sealed vertical water shaft with a dry rim, deep enough that the descent alone spends more than one lung and there is no air below the mouth.</summary>
    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) OneWayDescent(int depth)
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY - depth - 2, -2, 2, FloorY + 2, 2, FixtureWorld.Stone);
        world.Fill(0, FloorY - depth + 1, 0, 0, FloorY, 0, FixtureWorld.Water);
        world.Fill(-2, FloorY + 1, -2, 2, FloorY + 2, 2, FixtureWorld.Air);
        return (world, new BlockPos(1, FloorY + 1, 0), new BlockPos(0, FloorY - depth + 1, 0));
    }
}
