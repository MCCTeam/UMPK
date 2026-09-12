using Umpk.Game.Blocks;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.World;

/// <summary><see cref="World.FindNearest(BlockPos, int, System.Func{BlockState, bool})"/>, <see cref="World.FindNearest(BlockPos, int, System.Collections.Generic.IReadOnlyCollection{Identifier})"/>, and <see cref="World.Find"/>: a cubic radius scan with squared-distance nearest (Y included), a deterministic tie order, and unloaded columns that never answer a match (an unloaded column reads as air, which would otherwise make an air-matching predicate "find" ground that was never actually seen).</summary>
public class WorldSearchTests
{
    private static readonly BlockPos Origin = new(0, 60, 0);

    [Fact]
    public void FindNearest_ReturnsTheClosestMatch()
    {
        var world = WorldTestData.NewWorld();
        var near = Origin.Offset(2, 0, 0);
        var far = Origin.Offset(8, 0, 0);
        world.SetBlockStateId(near, WorldTestData.StoneState);
        world.SetBlockStateId(far, WorldTestData.StoneState);

        BlockPos? result = world.FindNearest(Origin, 10, state => state.StateId == WorldTestData.StoneState);

        Assert.Equal(near, result);
    }

    /// <summary>A candidate reachable only vertically must out-rank one that is closer purely on X/Z: proves Y contributes to the distance metric instead of only the horizontal plane.</summary>
    [Fact]
    public void FindNearest_SquaredDistanceIncludesY()
    {
        var world = WorldTestData.NewWorld();
        var farAboveButAlignedOnXz = Origin.Offset(0, 5, 0); // sqr = 25
        var nearOnXz = Origin.Offset(3, 0, 0); // sqr = 9
        world.SetBlockStateId(farAboveButAlignedOnXz, WorldTestData.StoneState);
        world.SetBlockStateId(nearOnXz, WorldTestData.StoneState);

        BlockPos? result = world.FindNearest(Origin, 10, state => state.StateId == WorldTestData.StoneState);

        Assert.Equal(nearOnXz, result);
    }

    [Fact]
    public void FindNearest_ReturnsNull_WhenNothingMatches()
    {
        var world = WorldTestData.NewWorld();
        world.SetBlockStateId(Origin.Offset(2, 0, 0), WorldTestData.StoneState);

        BlockPos? result = world.FindNearest(Origin, 10, state => state.StateId == WorldTestData.WaterState);

        Assert.Null(result);
    }

    /// <summary>The id-set overload finds the nearest match of ANY listed id in a single pass.</summary>
    [Fact]
    public void FindNearest_IdSet_FindsTheNearestOfAnyListedId()
    {
        var world = WorldTestData.NewWorld();
        var farRedBed = Origin.Offset(9, 0, 0);
        var nearBlueBed = Origin.Offset(4, 0, 0);
        world.SetBlockStateId(farRedBed, WorldTestData.RedBedState);
        world.SetBlockStateId(nearBlueBed, WorldTestData.BlueBedState);

        BlockPos? result = world.FindNearest(
            Origin, 15, [Identifier.Minecraft("red_bed"), Identifier.Minecraft("blue_bed")]);

        Assert.Equal(nearBlueBed, result);
    }

    /// <summary>An unloaded column reads as air (state id 0) but must not be reported as a match: the scan has to skip it rather than let an air-matching predicate "find" ground nobody ever loaded.</summary>
    [Fact]
    public void FindNearest_SkipsUnloadedColumns()
    {
        var world = WorldTestData.NewWorld();
        // Nothing is loaded anywhere; every position in range would read as air if columns were not skipped.

        BlockPos? result = world.FindNearest(Origin, 5, state => state.IsAir);

        Assert.Null(result);
    }

    [Fact]
    public void Find_OrdersResultsAscendingByDistance()
    {
        var world = WorldTestData.NewWorld();
        var far = Origin.Offset(9, 0, 0);
        var near = Origin.Offset(1, 0, 0);
        var middle = Origin.Offset(5, 0, 0);
        // Set in a scrambled order so a passing test cannot be an artifact of insertion order.
        world.SetBlockStateId(far, WorldTestData.StoneState);
        world.SetBlockStateId(near, WorldTestData.StoneState);
        world.SetBlockStateId(middle, WorldTestData.StoneState);

        IReadOnlyList<BlockPos> results = world.Find(Origin, 10, state => state.StateId == WorldTestData.StoneState);

        Assert.Equal([near, middle, far], results);
    }

    [Fact]
    public void Find_HonoursMaxResults()
    {
        var world = WorldTestData.NewWorld();
        for (int i = 1; i <= 5; i++)
            world.SetBlockStateId(Origin.Offset(i, 0, 0), WorldTestData.StoneState);

        IReadOnlyList<BlockPos> results = world.Find(
            Origin, 10, state => state.StateId == WorldTestData.StoneState, maxResults: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(Origin.Offset(1, 0, 0), results[0]);
        Assert.Equal(Origin.Offset(2, 0, 0), results[1]);
    }

    /// <summary>Two equidistant candidates: the scan order (Y outer, Z middle, X inner, all ascending) is the pinned tiebreak, so the result is deterministic rather than an artifact of hash/insertion order.</summary>
    [Fact]
    public void FindNearest_BreaksATieDeterministicallyByScanOrder()
    {
        var world = WorldTestData.NewWorld();
        var higherZ = Origin.Offset(3, 0, 4); // sqr = 25, found at dz = 4
        var lowerZ = Origin.Offset(4, 0, 3); // sqr = 25, found at dz = 3 (scanned first)
        world.SetBlockStateId(higherZ, WorldTestData.StoneState);
        world.SetBlockStateId(lowerZ, WorldTestData.StoneState);

        BlockPos? result = world.FindNearest(Origin, 5, state => state.StateId == WorldTestData.StoneState);

        Assert.Equal(lowerZ, result);
    }
}
