using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class LevelCrossingElevationTests
{
    private const int FloorY = 64;
    private const int FeetY = FloorY + 1;

    /// <summary>The predicate itself, and its asymmetry. Rises past vanilla's 0.6 auto-step are refused; drops of any size are not, because a fall inside a cell is free and the body takes it.</summary>
    [Theory]
    // dest support,               source support,             walked, why
    [InlineData(FixtureWorld.Stone, FixtureWorld.Stone, true, "flat: two full blocks")]
    [InlineData(FixtureWorld.Stone, FixtureWorld.BottomSlab, true, "0.5 rise, inside the auto-step")]
    [InlineData(FixtureWorld.Stone, FixtureWorld.Carpet, false, "0.9375 rise, past the auto-step")]
    [InlineData(FixtureWorld.Stone, FixtureWorld.LilyPad, false, "0.90625 rise, past the auto-step")]
    [InlineData(FixtureWorld.Carpet, FixtureWorld.Stone, true, "0.9375 DROP, and a drop is free")]
    [InlineData(FixtureWorld.LilyPad, FixtureWorld.Stone, true, "0.90625 DROP, and a drop is free")]
    [InlineData(FixtureWorld.BottomSlab, FixtureWorld.Stone, true, "0.5 drop")]
    public void IsWalkedLevelCrossing_ComparesTheElevationsTheTwoEndsPresent(
        int destinationSupport, int sourceSupport, bool walked, string why)
    {
        Assert.NotNull(why);

        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, sourceSupport);
        world.Set(1, FloorY, 0, destinationSupport);
        CalculationContext ctx = FixtureContext.Around(world, 0, FeetY, 0);

        Assert.Equal(walked, MoveHelper.IsWalkedLevelCrossing(ctx, 0, FeetY, 0, 1, 0));
    }

    /// <summary>The arm the refusal routes to. A carpet lane abutting a full block at the same node Y is still planned, and planned as an <see cref="MoveType.Ascend"/> rather than walked for free - which is what puts the crossing on the far side of the takeoff gate for the first time.</summary>
    [Fact]
    public void AnOverTallLevelCrossing_IsPlannedAsAnAscend()
    {
        PathResult result = CarpetLaneToAStep(FixtureWorld.Stone);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(MoveType.Ascend, result.Moves);
    }

    [Fact]
    public void ALevelCrossingInsideTheAutoStep_IsStillWalked()
    {
        PathResult result = CarpetLaneToAStep(FixtureWorld.BottomSlab, laneSupport: FixtureWorld.BottomSlab);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.DoesNotContain(MoveType.Ascend, result.Moves);
    }

    /// <summary>A three-cell lane whose support is <paramref name="laneSupport"/>, then a bank whose support is <paramref name="bankSupport"/>. Both sit in the same cell layer, so every crossing is offered to the level arm; only the elevations differ.</summary>
    private static PathResult CarpetLaneToAStep(int bankSupport, int laneSupport = FixtureWorld.Carpet)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, -1, 4, FloorY, 1, laneSupport);
        world.Fill(5, FloorY, -1, 8, FloorY, 1, bankSupport);

        var start = new BlockPos(0, FeetY, 0);
        var goal = new BlockPos(7, FeetY, 0);
        return PathPlanner.FindPath(
            world.Capture(start, goal, margin: 8),
            PathfinderOptions.Default with { AllowParkour = false, AllowParkourAscend = false },
            start,
            new GoalBlock(goal));
    }
}
