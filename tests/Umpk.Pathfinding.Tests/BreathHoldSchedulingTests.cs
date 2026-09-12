using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class BreathHoldSchedulingTests
{
    private const int FloorY = 64;

    /// <summary>The bore length E3 and E4 share, in blocks.</summary>
    private const int BoreLength = 80;

    private readonly ITestOutputHelper _output;

    public BreathHoldSchedulingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheSearchBanksABreathingPause_AndThePlanMustCarryIt()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = BoreWithOneCellBells();
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);

        // The route genuinely DEPENDS on breathing, so the assertion below cannot pass vacuously: the same bore with its bells filled in is refused outright, because eighty blocks of submerged lane is more than one lung buys and there is nowhere to refill it.
        (FixtureWorld sealedWorld, BlockPos sealedStart, BlockPos sealedGoal) = SealedBore();
        PathResult withoutBells = PathPlanner.FindPath(
            sealedWorld.Capture(sealedStart, sealedGoal, margin: 8),
            PathfinderOptions.Default,
            sealedStart,
            new GoalBlock(sealedGoal));
        Assert.NotEqual(PathStatus.Success, withoutBells.Status);

        foreach ((PathSegment segment, int index) in segments.Select((s, i) => (s, i)))
            if (segment.BreathHoldTicks > 0.0)
                _output.WriteLine(
                    $"  HOLD seg {index} end {segment.End}: {segment.BreathHoldTicks:F1} ticks, "
                    + $"travel {segment.PlannedTickCost:F1}");

        double totalHold = segments.Sum(segment => segment.BreathHoldTicks);
        _output.WriteLine(
            $"segments={segments.Count} held={segments.Count(s => s.BreathHoldTicks > 0.0)} "
            + $"totalHold={totalHold:F1}");

        // The plan must say where to stop, or nothing can perform it.
        Assert.Contains(segments, segment => segment.BreathHoldTicks > 0.0);

        // And the pauses must be worth something: a hold of a fraction of a tick would satisfy the line above while leaving the bot to drown exactly as before. Four bells at roughly a half-lung each is well over a hundred ticks of standing still.
        Assert.True(totalHold > 100.0, $"the scheduled pauses must be real: {totalHold:F1} ticks");
    }

    [Fact]
    public void TheBreathingPauseIsNotChargedAsTravelTime()
    {
        (FixtureWorld world, BlockPos start, BlockPos goal) = BoreWithOneCellBells();
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);

        // The slowest thing a single move can be is a submerged block at the no-sprint terminal velocity, which is what the search itself prices a wet move at: SubmergedBlocksPerTick is 0.098 BLOCKS a tick, so its reciprocal, 10.204, is the ticks a block. Two of those is generous headroom for a diagonal, and still nowhere near a breathing stop.
        double ceiling = 2.0 / BreathValidator.SubmergedBlocksPerTick;

        PathSegment worst = segments.MaxBy(segment => segment.PlannedTickCost)!;

        _output.WriteLine(
            $"worst travel cost {worst.PlannedTickCost:F1} ticks over "
            + $"{(worst.End - worst.Start).Length():F2} blocks, "
            + $"hold {worst.BreathHoldTicks:F1}; ceiling {ceiling:F1}");

        Assert.True(
            worst.PlannedTickCost <= ceiling,
            $"a single move's travel budget must be a move's worth of ticks, not a breathing stop's: "
            + $"{worst.PlannedTickCost:F1} > {ceiling:F1}");
    }

    /// <summary>The E4 bore with its three bells reduced to ONE cell each: the head layer over the lane, with the swimmer's feet cell still water and the casing sealed straight above.</summary>
    /// <remarks>A one-cell bell affords no move with both endpoints out of the water, so breathing requires a four-tick station hold with the head in the air cell. This distinguishes the geometry from E4's two-cell bells, where a passing body can partly breathe while moving.</remarks>
    /// <summary>The same bore with no bells at all: the control that makes the row non-vacuous.</summary>
    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) SealedBore()
    {
        var world = new FixtureWorld();
        world.Fill(-6, FloorY - 1, -4, BoreLength + 5, FloorY + 6, 4, FixtureWorld.Stone);
        world.Fill(-5, FloorY, 0, BoreLength + 4, FloorY + 1, 0, FixtureWorld.Air);
        world.Fill(0, FloorY, 0, BoreLength - 1, FloorY + 1, 0, FixtureWorld.Water);
        return (world, new BlockPos(-3, FloorY, 0), new BlockPos(BoreLength + 2, FloorY, 0));
    }

    private static (FixtureWorld World, BlockPos Start, BlockPos Goal) BoreWithOneCellBells()
    {
        var world = new FixtureWorld();
        world.Fill(-6, FloorY - 1, -4, BoreLength + 5, FloorY + 6, 4, FixtureWorld.Stone);
        world.Fill(-5, FloorY, 0, BoreLength + 4, FloorY + 1, 0, FixtureWorld.Air);
        world.Fill(0, FloorY, 0, BoreLength - 1, FloorY + 1, 0, FixtureWorld.Water);

        // One air cell over the lane at each bell, and nothing above it.
        foreach (int x in new[] { 20, 42, 60 })
            world.Fill(x, FloorY + 2, 0, x, FloorY + 2, 0, FixtureWorld.Air);

        return (world, new BlockPos(-3, FloorY, 0), new BlockPos(BoreLength + 2, FloorY, 0));
    }
}
