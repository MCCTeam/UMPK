using Umpk.Game.Blocks;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Blocks;

public sealed class BlockCollisionGridTests
{
    [Fact]
    public void NativeEmptyShapeRetainsZeroCoordinatePlanes()
    {
        var grid = new BlockCollisionGrid([], [], [], [], []);
        Assert.Empty(grid.XCoordinates.ToArray());
        Assert.Empty(grid.YCoordinates.ToArray());
        Assert.Empty(grid.ZCoordinates.ToArray());
        Assert.Empty(grid.OccupiedCells.ToArray());
        Assert.Empty(grid.Boxes.ToArray());
        Assert.False(grid.CanonicalFullBlock);
    }

    [Theory]
    [InlineData("partial-axis")]
    [InlineData("occupied-cell")]
    [InlineData("box")]
    [InlineData("canonical")]
    [InlineData("offset")]
    public void AbsentCoordinatePlanesCannotHideMalformedGeometry(string defect)
    {
        double[] x = defect == "partial-axis" ? [0] : [];
        int[] cells = defect == "occupied-cell" ? [0] : [];
        Aabb[] boxes = defect == "box" ? [Aabb.BlockAt(0, 0, 0)] : [];
        if (defect == "offset")
            Assert.Throws<ArgumentException>(() => BlockCollisionGrid.CreateWithHorizontalOffset(x, [], [], cells, boxes, .25));
        else
            Assert.Throws<ArgumentException>(() => new BlockCollisionGrid(x, [], [], cells, boxes, defect == "canonical"));
    }

    [Fact]
    public void NativeCoincidentWallPlanesRemainOccupied()
    {
        var grid = new BlockCollisionGrid([.5, .5], [0, 1.5], [.5, .5], [0], [new(.5, 0, .5, .5, 1.5, .5)]);
        Assert.Equal(new[] { .5, .5 }, grid.XCoordinates.ToArray());
        Assert.Equal(new[] { 0 }, grid.OccupiedCells.ToArray());
        Assert.False(grid.CanonicalFullBlock);
    }

    [Fact]
    public void CallerMutationsCannotChangeTheStoredGrid()
    {
        double[] x = [0, 1];
        int[] cells = [0];
        Aabb[] boxes = [Aabb.BlockAt(0, 0, 0)];
        var grid = new BlockCollisionGrid(x, [0, 1], [0, 1], cells, boxes, canonicalFullBlock: true);
        x[1] = 2;
        cells[0] = 5;
        boxes[0] = Aabb.Empty;
        Assert.Equal(1, grid.XCoordinates[1]);
        Assert.Equal(0, grid.OccupiedCells[0]);
        Assert.Equal(Aabb.BlockAt(0, 0, 0), grid.Boxes[0]);
    }

    [Theory]
    [InlineData("empty-axis")]
    [InlineData("backwards")]
    [InlineData("nonfinite")]
    [InlineData("duplicate-cell")]
    [InlineData("out-of-range")]
    [InlineData("negative-cell")]
    [InlineData("missing-box")]
    [InlineData("false-canonical")]
    public void InvalidNativeGridIsRejected(string defect)
    {
        double[] x = defect switch { "empty-axis" => [], "backwards" => [1, 0], "nonfinite" => [0, double.NaN], _ => [0, 1] };
        int[] cells = defect switch { "duplicate-cell" => [0, 0], "out-of-range" => [1], "negative-cell" => [-1], _ => [0] };
        Aabb[] boxes = defect == "missing-box" ? [] : [Aabb.BlockAt(0, 0, 0)];
        Assert.Throws<ArgumentException>(() => new BlockCollisionGrid(x, [0, defect == "false-canonical" ? .5 : 1], [0, 1], cells, boxes, canonicalFullBlock: defect == "false-canonical"));
    }
}
