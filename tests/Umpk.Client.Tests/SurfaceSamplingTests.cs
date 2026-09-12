using Umpk.Client.Snapshots;
using Umpk.Client.Tests.Support;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="SurfaceRegionSnapshot.Sample"/> and <see cref="ChunkStatusGrid.Around"/> are pure operations over <see cref="Umpk.Game.World.World"/> with no session attached.</summary>
public sealed class SurfaceSamplingTests
{
    private static readonly BlockPos Origin = new(0, 64, 0);

    [Fact]
    public void Sample_FindsTheFirstNonAirBlockScanningDown()
    {
        World world = SnapshotWorldFixture.NewWorld();
        world.SetBlockStateId(Origin.Offset(0, -20, 0), SnapshotWorldFixture.StoneState); // floor
        world.SetBlockStateId(Origin, SnapshotWorldFixture.StoneState); // the actual surface

        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(world, Origin.X, Origin.Z, 1, 1);

        SurfaceColumn column = snapshot.At(0, 0);
        Assert.True(column.Loaded);
        Assert.True(column.Found);
        Assert.Equal(Origin.Y, column.SurfaceY);
        Assert.Equal(SnapshotWorldFixture.StoneState, column.State.StateId);
    }

    /// <summary>Cave mode: a ceiling below the world's real top must clip the scan, not just cap the report.</summary>
    [Fact]
    public void Sample_HonoursTheCeiling_AndDoesNotReportTheSurfaceAboveIt()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var aboveCeiling = Origin.Offset(0, 10, 0); // y = 74
        var belowCeiling = Origin; // y = 64
        world.SetBlockStateId(aboveCeiling, SnapshotWorldFixture.StoneState);
        world.SetBlockStateId(belowCeiling, SnapshotWorldFixture.StoneState);

        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(
            world, Origin.X, Origin.Z, 1, 1, ceilingY: 70);

        SurfaceColumn column = snapshot.At(0, 0);
        Assert.True(column.Found);
        Assert.Equal(belowCeiling.Y, column.SurfaceY);
    }

    [Fact]
    public void Sample_UnloadedColumn_ReportsUnloaded()
    {
        World world = SnapshotWorldFixture.NewWorld();
        // Nothing loaded anywhere.

        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(world, 500, 500, 1, 1);

        SurfaceColumn column = snapshot.At(0, 0);
        Assert.Equal(SurfaceColumn.Unloaded, column);
        Assert.False(column.Loaded);
        Assert.False(column.Found);
    }

    /// <summary>A loaded column with nothing but air down to the floor is Loaded, but never Found.</summary>
    [Fact]
    public void Sample_LoadedVoidColumn_ReportsNotFound()
    {
        World world = SnapshotWorldFixture.NewWorld();
        world.LoadColumn(ChunkPos.Containing(Origin)); // loaded, empty: every section is air

        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(world, Origin.X, Origin.Z, 1, 1);

        SurfaceColumn column = snapshot.At(0, 0);
        Assert.True(column.Loaded);
        Assert.False(column.Found);
    }

    /// <summary>Row-major over Z then X: a single distinguishable block at one cell must land at the matching (column, row) and nowhere else, catching a transposed index.</summary>
    [Fact]
    public void Sample_IsRowMajorOverZThenX()
    {
        World world = SnapshotWorldFixture.NewWorld();
        // A 3-wide, 2-tall region. Put water only at column=2, row=1 (world x = OriginX+2, z = OriginZ+1).
        var waterPos = new BlockPos(Origin.X + 2, Origin.Y, Origin.Z + 1);
        world.SetBlockStateId(waterPos, SnapshotWorldFixture.WaterState);
        // Everything else in range gets a floor of stone so every column is Found (not a void column).
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 3; dx++)
            {
                var pos = new BlockPos(Origin.X + dx, Origin.Y, Origin.Z + dz);
                if (pos != waterPos)
                    world.SetBlockStateId(pos, SnapshotWorldFixture.StoneState);

            }

        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(world, Origin.X, Origin.Z, 3, 2);

        Assert.Equal(SnapshotWorldFixture.WaterState, snapshot.At(2, 1).State.StateId);
        Assert.Equal(SnapshotWorldFixture.StoneState, snapshot.At(0, 0).State.StateId);
        Assert.Equal(SnapshotWorldFixture.StoneState, snapshot.At(1, 1).State.StateId);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 100)]
    public void At_ThrowsOutsideTheGrid(int column, int row)
    {
        World world = SnapshotWorldFixture.NewWorld();
        SurfaceRegionSnapshot snapshot = SurfaceRegionSnapshot.Sample(world, Origin.X, Origin.Z, 3, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.At(column, row));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, -1)]
    public void Sample_RejectsNonPositiveDimensions(int width, int length)
    {
        World world = SnapshotWorldFixture.NewWorld();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SurfaceRegionSnapshot.Sample(world, Origin.X, Origin.Z, width, length));
    }

    [Fact]
    public void Around_CentresAnOddSizedGridOnThePlayerChunk()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var center = ChunkPos.Containing(Origin);

        ChunkStatusGrid grid = ChunkStatusGrid.Around(world, center, columns: 5, rows: 5);

        Assert.Equal(center, grid.ChunkAt(2, 2));
    }

    /// <summary>Sweeps every cell of a 17x17 grid: ChunkAt must reproduce exactly the position Around built from.</summary>
    [Fact]
    public void ChunkAt_AgreesWithTheBuildersCentring_AcrossTheWholeGrid()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var center = new ChunkPos(10, -6);
        const int size = 17;
        int half = (size - 1) / 2;

        ChunkStatusGrid grid = ChunkStatusGrid.Around(world, center, columns: size, rows: size);

        for (int row = 0; row < size; row++)
            for (int column = 0; column < size; column++)
            {
                var expected = new ChunkPos(center.X - half + column, center.Z - half + row);
                Assert.Equal(expected, grid.ChunkAt(row, column));
            }

    }

    /// <summary>A non-square grid catches a transposed X/Z half: Columns spans X, Rows spans Z, and swapping them would only show up when the two dimensions actually differ.</summary>
    [Fact]
    public void ChunkAt_IsCorrectForANonSquareGrid()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var center = new ChunkPos(0, 0);
        const int columns = 5; // X span
        const int rows = 9; // Z span
        int halfX = (columns - 1) / 2;
        int halfZ = (rows - 1) / 2;

        ChunkStatusGrid grid = ChunkStatusGrid.Around(world, center, columns, rows);

        // Row 0, column 0: the min corner. halfX != halfZ, so a transposed formula would fail this.
        Assert.Equal(new ChunkPos(center.X - halfX, center.Z - halfZ), grid.ChunkAt(0, 0));
        // The far corner.
        Assert.Equal(
            new ChunkPos(center.X - halfX + columns - 1, center.Z - halfZ + rows - 1),
            grid.ChunkAt(rows - 1, columns - 1));
    }

    [Fact]
    public void LoadedCount_MatchesTheLoadedFlags()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var center = ChunkPos.Containing(Origin);
        world.LoadColumn(center);
        world.LoadColumn(new ChunkPos(center.X + 1, center.Z));

        ChunkStatusGrid grid = ChunkStatusGrid.Around(world, center, columns: 3, rows: 1);

        Assert.Equal(2, grid.LoadedCount);
    }

    [Fact]
    public void Around_RejectsNonPositiveDimensions()
    {
        World world = SnapshotWorldFixture.NewWorld();
        var center = ChunkPos.Containing(Origin);

        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkStatusGrid.Around(world, center, columns: 0, rows: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkStatusGrid.Around(world, center, columns: 3, rows: 0));
    }
}
