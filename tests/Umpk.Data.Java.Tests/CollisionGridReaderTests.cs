using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class CollisionGridReaderTests
{
    [Fact]
    public void NativeEmptyShapeRetainsZeroCoordinatePlanes()
    {
        var table = new JavaBlockCollisionGrids(new byte[] { 1, 1, 0, 0, 0, 0, 0, 0, 1, 0 });
        var grid = Assert.IsType<Umpk.Game.Blocks.BlockCollisionGrid>(table.Get(0));
        Assert.Empty(grid.XCoordinates.ToArray());
        Assert.Empty(grid.YCoordinates.ToArray());
        Assert.Empty(grid.ZCoordinates.ToArray());
        Assert.Empty(grid.OccupiedCells.ToArray());
        Assert.Null(table.Get(1));
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0 })]
    [InlineData(new byte[] { 0, 1 })]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 1, 1 })]
    public void MalformedGeneratedGridsAreRefused(byte[] bytes)
    {
        Assert.Throws<ProtocolViolationException>(() => new JavaBlockCollisionGrids(bytes));
    }
}
