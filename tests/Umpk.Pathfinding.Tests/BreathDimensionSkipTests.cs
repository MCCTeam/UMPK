using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class BreathDimensionSkipTests
{
    private const int FloorY = 64;

    private static readonly PathfinderOptions BreathOff = PathfinderOptions.Default with { BreathAware = false };

    private readonly record struct PlanTrace(PathStatus Status, double Cost, int Nodes, int PathLength, long BlockReads);

    private static (PlanTrace Search, long ProbeReads) Plan(
        Func<FixtureWorld> build, BlockPos start, BlockPos goal, PathfinderOptions options, int margin = 16)
    {
        FixtureWorld world = build();
        PlanningWorldView view = world.Capture(start, goal, margin);

        long beforeProbe = world.Data.FlagReads;
        _ = view.MayContainWater;
        long probeReads = world.Data.FlagReads - beforeProbe;

        long before = world.Data.FlagReads;
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        return (
            new PlanTrace(
                result.Status, result.Cost, result.Diagnostics.NodesExplored, result.Path.Count, world.Data.FlagReads - before),
            probeReads);
    }

    private static FixtureWorld DryRunway()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 46, -6, 6, FloorY);
        return world;
    }

    private static FixtureWorld DryWallToRound()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 46, -20, 20, FloorY);
        world.Fill(20, FloorY + 1, -18, 20, FloorY + 4, 18, FixtureWorld.Stone);
        return world;
    }

    private static FixtureWorld FloodedChannel()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 46, -6, 6, FloorY);
        world.Fill(8, FloorY, -3, 30, FloorY, 3, FixtureWorld.Air);
        world.Fill(8, FloorY - 4, -3, 30, FloorY - 4, 3, FixtureWorld.Stone);
        world.Fill(8, FloorY - 3, -3, 30, FloorY + 1, 3, FixtureWorld.Water);
        return world;
    }

    [Fact]
    public void DryPlan_ReadsExactlyAsManyBlocksWithTheBreathDimensionOnAsOff()
    {
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(40, FloorY + 1, 0);
        (PlanTrace on, long probe) = Plan(DryRunway, start, goal, PathfinderOptions.Default);
        (PlanTrace off, _) = Plan(DryRunway, start, goal, BreathOff);

        Assert.Equal(off, on);
        Assert.True(probe < on.BlockReads / 10, $"the water probe read {probe} against the search's {on.BlockReads}");
    }

    /// <summary>The same on a shape with real branching, where the arm ran on tens of thousands of neighbours.</summary>
    [Fact]
    public void DryPlanAroundAWall_ReadsExactlyAsManyBlocksWithTheBreathDimensionOnAsOff()
    {
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(40, FloorY + 1, 0);
        (PlanTrace on, long probe) = Plan(DryWallToRound, start, goal, PathfinderOptions.Default, margin: 24);
        (PlanTrace off, _) = Plan(DryWallToRound, start, goal, BreathOff, margin: 24);

        Assert.Equal(off, on);
        Assert.True(on.Nodes > 500, $"the wall shape only explored {on.Nodes} nodes, which does not exercise the arm");

        // The probe scales with sections, not with the search: a shape that explores 140x the nodes of the flat runway pays about the same fixed probe for a box of similar section count.
        Assert.True(probe < 2000, $"the water probe read {probe} blocks, which is not a fixed cost");
    }

    /// <summary>And the arm still fires where it must: put water in the box and the two plans diverge, because the dimension is doing its job. Without this the test above could be satisfied by deleting the feature.</summary>
    [Fact]
    public void AFloodedPlan_StillPaysForTheBreathDimension()
    {
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(40, FloorY + 1, 0);
        (PlanTrace on, _) = Plan(FloodedChannel, start, goal, PathfinderOptions.Default, margin: 12);
        (PlanTrace off, _) = Plan(FloodedChannel, start, goal, BreathOff, margin: 12);

        Assert.NotEqual(off.BlockReads, on.BlockReads);
        Assert.True(
            on.BlockReads > off.BlockReads,
            $"the breath-aware plan read {on.BlockReads} blocks against the breath-off plan's {off.BlockReads}");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(FixtureWorld.Water, true)]
    [InlineData(FixtureWorld.FlowingWater, true)]
    [InlineData(FixtureWorld.WaterloggedStairs, true)]
    [InlineData(FixtureWorld.Lava, false)]
    [InlineData(FixtureWorld.Stone, false)]
    public void MayContainWater_AnswersFromTheSectionPalettes(int stateId, bool expected)
    {
        var world = new FixtureWorld();
        world.Floor(-6, 6, -6, 6, FloorY);
        if (stateId >= 0)
            world.Set(3, FloorY + 1, 3, stateId);

        PlanningWorldView captured = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(4, FloorY + 1, 4), margin: 8);
        PlanningWorldView faulted = world.CaptureOnDemand(new BlockPos(0, FloorY + 1, 0), new BlockPos(4, FloorY + 1, 4), margin: 8);

        Assert.Equal(expected, captured.MayContainWater);
        Assert.Equal(expected, faulted.MayContainWater);
    }

    /// <summary>The probe does not materialise anything: asking a demand-faulted view whether it may hold water leaves its copied-section count where it was, so the skip cannot pay for itself with the very copying it exists to avoid.</summary>
    [Fact]
    public void MayContainWater_CopiesNoSections()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 46, -6, 6, FloorY);
        PlanningWorldView view = world.CaptureOnDemand(new BlockPos(0, FloorY + 1, 0), new BlockPos(40, FloorY + 1, 0), margin: 24);

        Assert.False(view.MayContainWater);
        Assert.Equal(0, view.Region.SectionsCaptured);
    }
}
