using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves.Impl;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class BreathValidationTests
{
    private const int FloorY = 64;

    /// <summary>A two-cell-tall corridor cut through solid stone, dry at both ends and flooded for <paramref name="length"/> blocks in the middle under a solid lid: crossing it is a submerged bottom-walk with no air anywhere above it, and the only air is at the two mouths.</summary>
    private static FixtureWorld SealedBore(int length)
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 1, -2, length + 3, FloorY + 4, 2, FixtureWorld.Stone);
        world.Fill(-3, FloorY + 1, 0, length + 2, FloorY + 2, 0, FixtureWorld.Air);
        world.Fill(0, FloorY + 1, 0, length - 1, FloorY + 2, 0, FixtureWorld.Water);
        return world;
    }

    /// <summary>Planning with the air dimension off: what the validator was written to judge.</summary>
    private static readonly PathfinderOptions NotBreathAware = PathfinderOptions.Default with { BreathAware = false };

    private static BreathValidation ValidatePlan(
        FixtureWorld world,
        BlockPos start,
        BlockPos goal,
        PathfinderOptions options,
        out PathResult result,
        int airTicks = (int)BreathModel.FullLungTicks)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        return BreathValidator.Validate(segments, view, PhysicsProfile.ForProtocol(772), allowSprint: true, airTicks);
    }

    [Fact]
    public void SealedEightyBlockBore_IsRefused()
    {
        FixtureWorld world = SealedBore(80);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(81, FloorY + 1, 0);

        BreathValidation validation = ValidatePlan(world, start, goal, NotBreathAware, out PathResult result);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(
            validation.PeakDeficitTicks > 400.0,
            $"eighty submerged blocks are more than 400 real ticks, not {validation.PeakDeficitTicks:F1}");
        Assert.False(validation.IsSurvivable);

        // And with the dimension on, the search never offers it in the first place.
        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        PathResult breathAware = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.NotEqual(PathStatus.Success, breathAware.Status);
    }

    [Fact]
    public void TwentyBlockBore_IsAccepted()
    {
        FixtureWorld world = SealedBore(20);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(21, FloorY + 1, 0);

        BreathValidation validation = ValidatePlan(world, start, goal, PathfinderOptions.Default, out PathResult result);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(validation.IsSurvivable, $"peak deficit was {validation.PeakDeficitTicks:F1} ticks");
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(250, true)]
    [InlineData(164, true)]
    [InlineData(163, false)]
    [InlineData(150, false)]
    [InlineData(20, false)]
    public void TwentyBlockBore_IsJudgedAgainstTheLungThePlayerHolds(int airTicks, bool survivable)
    {
        FixtureWorld world = SealedBore(20);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(21, FloorY + 1, 0);

        BreathValidation validation = ValidatePlan(
            world, start, goal, NotBreathAware, out PathResult result, airTicks);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(airTicks - BreathModel.ReactionTicks, validation.BudgetTicks);
        Assert.True(
            survivable == validation.IsSurvivable,
            $"air {airTicks} gives a budget of {validation.BudgetTicks:F1} ticks and the crossing peaks "
            + $"at {validation.PeakDeficitTicks:F1}: expected survivable={survivable}, "
            + $"got {validation.IsSurvivable}");
    }

    [Fact]
    public void ShallowSurfaceSwim_IsUnaffected()
    {
        // Deep enough that no bottom-walk lane competes with the surface: the top water cell is at

        var world = new FixtureWorld();
        world.Fill(-2, FloorY - 12, -2, 22, FloorY, 2, FixtureWorld.Stone);
        world.Fill(1, FloorY - 9, 0, 20, FloorY, 0, FixtureWorld.Water);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(21, FloorY + 1, 0);

        BreathValidation validation = ValidatePlan(world, start, goal, PathfinderOptions.Default, out PathResult result);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(validation.IsSurvivable);
        Assert.Equal(0.0, validation.PeakDeficitTicks);
    }

    [Fact]
    public void FortyBlockOneWayDive_IsRefused()
    {
        FixtureWorld world = DeepShaft(40);
        var start = new BlockPos(1, FloorY + 1, 0);
        var goal = new BlockPos(0, FloorY - 39, 0);

        BreathValidation validation = ValidatePlan(world, start, goal, NotBreathAware, out PathResult result);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.False(validation.IsSurvivable);

        // submerged drop costs its real 40 ticks a block, swimming down at 9.09 is the better move.
        Assert.DoesNotContain(MoveType.Fall, result.Moves);

        // And with the dimension on, the search never offers it in the first place.
        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        PathResult breathAware = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.NotEqual(PathStatus.Success, breathAware.Status);
    }

    [Fact]
    public void FallIntoWater_IsCostedAtTheSinkRate()
    {
        FixtureWorld world = DeepShaft(40);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0, margin: 48);
        var move = new MoveFall();

        // Inside the column the drop never leaves the water: it is the passive sink, 40 ticks.
        var wet = default(MoveResult);
        move.Calculate(ctx, 0, FloorY, 0, ref wet);

        Assert.False(wet.IsImpossible);
        Assert.Equal(FloorY - 1, wet.DestY);
        Assert.Equal(ActionCosts.WaterSinkOneBlock, wet.Cost, 6);

        // From the dry rim one block above the surface the body really does fall that block, so the air fall table still applies and is untouched.
        var dry = default(MoveResult);
        move.Calculate(ctx, 0, FloorY + 1, 0, ref dry);

        Assert.False(dry.IsImpossible);
        Assert.Equal(FloorY, dry.DestY);
        Assert.Equal(ActionCosts.FallCost(1), dry.Cost, 6);
    }

    /// <summary>A search that exhausts its node budget returns the best node it reached, the navigator executes that partial route and reports success, so a partial is exactly as capable of drowning the player as a complete one and is validated the same way.</summary>
    /// <remarks>The search is deliberately NOT breath-aware here, and that is the shape the production comment names ("the ocean case that produces partials"). With the dimension ON, the search's own per-node refusal bounds every partial it can return to 300 ticks of deficit at 10.204 ticks a block - 29.4 blocks - and a 29-block wade is inside a lung at the measured rate, so a breath-aware partial can no longer be unsurvivable and the row would be asserting on a shape that cannot occur. Without the dimension the search walks the whole bore and the partial is 40 blocks in.</remarks>
    [Fact]
    public void PartialPath_IsAlsoValidated()
    {
        FixtureWorld world = SealedBore(80);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(81, FloorY + 1, 0);
        var options = NotBreathAware with { MaxNodes = 60 };

        BreathValidation validation = ValidatePlan(world, start, goal, options, out PathResult result);

        Assert.Equal(PathStatus.Partial, result.Status);
        Assert.False(validation.IsSurvivable);
    }

    /// <summary>A sealed vertical water shaft <paramref name="depth"/> blocks deep with a stone floor at the bottom, reachable only from the rim: a one-way dive.</summary>
    private static FixtureWorld DeepShaft(int depth)
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY - depth - 2, -2, 2, FloorY + 2, 2, FixtureWorld.Stone);
        world.Fill(0, FloorY - depth + 1, 0, 0, FloorY, 0, FixtureWorld.Water);
        world.Fill(-2, FloorY + 1, -2, 2, FloorY + 2, 2, FixtureWorld.Air);
        return world;
    }

    /// <summary>The route's own AIR STOPS, which are what a surfacing budget has to be sized against. A flat allowance cannot tell a route that legitimately breathes five times from a bot oscillating in place: to a counter that only counts, the fifth honest breath and the fifth failed one are the same event.</summary>
    /// <remarks>The shape is a bore broken by dry sills, so the number of stops is a property of the WORLD and is stated here as a literal rather than derived from the same walk the validator makes. A route crossing <paramref name="pools"/> flooded stretches separated by dry ground surfaces once per stretch, and the dry approach and the dry arrival are walks rather than breaths.</remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public void BreathingStops_CountTheRoutesOwnAirStops(int pools, int expectedStops)
    {
        var world = new FixtureWorld();
        int length = (pools * 6) + 6;
        world.Fill(-4, FloorY - 1, -2, length + 3, FloorY + 4, 2, FixtureWorld.Stone);
        world.Fill(-3, FloorY + 1, 0, length + 2, FloorY + 2, 0, FixtureWorld.Air);
        for (int i = 0; i < pools; i++)
        {
            // Four flooded cells, then two dry ones to stand and breathe in.
            world.Fill((i * 6) + 1, FloorY + 1, 0, (i * 6) + 4, FloorY + 2, 0, FixtureWorld.Water);
        }

        BreathValidation validation = ValidatePlan(
            world,
            new BlockPos(-2, FloorY + 1, 0),
            new BlockPos(length + 1, FloorY + 1, 0),
            PathfinderOptions.Default,
            out PathResult result);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(validation.IsSurvivable);
        Assert.Equal(expectedStops, validation.BreathingStops);
    }
}
