using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class SubmergedUpstreamBreathTests
{
    private const int FloorY = 64;

    /// <summary>A two-cell-tall corridor cut through stone, dry at both ends and flooded for <paramref name="length"/> blocks in the middle under a solid lid. With <paramref name="upstream"/> the flooded stretch carries a real vanilla level gradient whose source is at the FAR end, so a crossing walks against it; otherwise the water is all source blocks and the flow is exactly zero.</summary>
    private static FixtureWorld SealedBore(int length, bool upstream)
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 1, -2, length + 3, FloorY + 4, 2, FixtureWorld.Stone);
        world.Fill(-3, FloorY + 1, 0, length + 2, FloorY + 2, 0, FixtureWorld.Air);
        world.Fill(0, FloorY + 1, 0, length - 1, FloorY + 2, 0, FixtureWorld.Water);
        if (upstream)
            world.FlowingRun(length - 1, FloorY + 1, 0, length, -1, 0, layers: 2);

        return world;
    }

    private static (BreathValidation Validation, PathResult Result, PlanningWorldView View) Run(
        int length, bool upstream)
    {
        FixtureWorld world = SealedBore(length, upstream);
        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(length + 1, FloorY + 1, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        PathResult result = PathPlanner.FindPath(
            view,
            PathfinderOptions.Default with { BreathAware = false },
            start,
            new GoalBlock(goal));
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        BreathValidation validation = BreathValidator.Validate(
            segments, view, PhysicsProfile.ForProtocol(772), allowSprint: true, (int)BreathModel.FullLungTicks);
        return (validation, result, view);
    }

    /// <summary>THE ROW. The same twenty-block sealed bore is survivable in still water and is not survivable walked against a current, and only the flow-aware validator can tell the two apart.</summary>
    [Fact]
    public void ASubmergedLegAgainstACurrent_IsRefusedWhereTheSameLegInStillWaterIsNot()
    {
        (BreathValidation still, PathResult stillPlan, _) = Run(20, upstream: false);
        (BreathValidation flowing, PathResult flowingPlan, _) = Run(20, upstream: true);

        Assert.Equal(PathStatus.Success, stillPlan.Status);
        Assert.Equal(PathStatus.Success, flowingPlan.Status);

        Assert.True(
            still.IsSurvivable,
            $"the still-water control was refused at a peak deficit of {still.PeakDeficitTicks:F1}, so this "
            + "row is not a discriminator");
        Assert.False(
            flowing.IsSurvivable,
            $"the upstream crossing was approved at a peak deficit of {flowing.PeakDeficitTicks:F1} against "
            + $"the still-water control's {still.PeakDeficitTicks:F1}");
    }

    /// <summary>The still-water rate understates a dead-upstream crossing by roughly the clamped multiplier.</summary>
    [Fact]
    public void TheStillWaterRateUnderStatesAnUpstreamCrossingSeveralFold()
    {
        (BreathValidation still, _, _) = Run(20, upstream: false);
        (BreathValidation flowing, _, _) = Run(20, upstream: true);

        double ratio = flowing.PeakDeficitTicks / still.PeakDeficitTicks;
        Assert.InRange(ratio, 2.0, ActionCosts.WadeMaxCurrentCostMultiplier + 0.01);

        // The upstream crossing must exceed a 300-tick lung; still-water pricing is only 189.5 ticks.
        Assert.True(
            flowing.PeakDeficitTicks > BreathModel.FullLungTicks,
            $"the upstream crossing is priced at {flowing.PeakDeficitTicks:F1} against a lung of "
            + $"{BreathModel.FullLungTicks:F0}, so it would still be approved");
    }

    /// <summary>The polarity guard. A current that HELPS is never credited, so the validator can only become more careful and never less. Downstream must price exactly as still water does.</summary>
    [Fact]
    public void ADownstreamCrossingIsNeverCreditedAgainstTheLung()
    {
        (BreathValidation still, _, _) = Run(20, upstream: false);

        // The same bore with the source at the NEAR end, so the crossing runs WITH the current.
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 1, -2, 23, FloorY + 4, 2, FixtureWorld.Stone);
        world.Fill(-3, FloorY + 1, 0, 22, FloorY + 2, 0, FixtureWorld.Air);
        world.Fill(0, FloorY + 1, 0, 19, FloorY + 2, 0, FixtureWorld.Water);
        world.FlowingRun(0, FloorY + 1, 0, 20, 1, 0, layers: 2);

        var start = new BlockPos(-2, FloorY + 1, 0);
        var goal = new BlockPos(21, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default with { BreathAware = false }, start, new GoalBlock(goal));
        BreathValidation downstream = BreathValidator.Validate(
            PathSegmentBuilder.FromPath(result.Path),
            view,
            PhysicsProfile.ForProtocol(772),
            allowSprint: true,
            (int)BreathModel.FullLungTicks);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(
            downstream.PeakDeficitTicks >= still.PeakDeficitTicks - 0.001,
            $"a downstream crossing was CREDITED against the lung: {downstream.PeakDeficitTicks:F4} against "
            + $"still water's {still.PeakDeficitTicks:F4}");
    }
}
