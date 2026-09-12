using Umpk.Geometry;
using Xunit;

namespace Umpk.Tests.Geometry;

public class BlockPosTests
{
    [Theory]
    [InlineData(0.5, 64.2, 0.5, 0, 64, 0)]
    [InlineData(-0.1, 2.7, -3.0, -1, 2, -3)]
    [InlineData(-1.0, -0.5, 15.999, -1, -1, 15)]
    public void Containing_UsesFloorSemantics(double x, double y, double z, int bx, int by, int bz)
    {
        Assert.Equal(new BlockPos(bx, by, bz), BlockPos.Containing(x, y, z));
        Assert.Equal(new BlockPos(bx, by, bz), BlockPos.Containing(new Vec3d(x, y, z)));
    }

    [Fact]
    public void Offset_ByComponentsAndDirection()
    {
        var pos = new BlockPos(1, 2, 3);
        Assert.Equal(new BlockPos(2, 4, 6), pos.Offset(1, 2, 3));
        Assert.Equal(new BlockPos(1, 3, 3), pos.Offset(Direction.Up));
        Assert.Equal(new BlockPos(1, 2, 1), pos.Offset(Direction.North, 2));
        Assert.Equal(new BlockPos(3, 2, 3), pos.Offset(Direction.East, 2));
        Assert.Equal(new BlockPos(1, 5, 3), pos.Above(3));
        Assert.Equal(new BlockPos(1, 1, 3), pos.Below());
    }

    [Fact]
    public void CenterPoints_AreKnown()
    {
        var pos = new BlockPos(-2, 5, 7);
        Assert.Equal(new Vec3d(-1.5, 5.5, 7.5), pos.Center);
        Assert.Equal(new Vec3d(-1.5, 5, 7.5), pos.BottomCenter);
        Assert.Equal(new Vec3d(-2, 5, 7), pos.MinCorner);
    }
}

public class ChunkPosTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(15, 15, 0, 0)]
    [InlineData(16, 16, 1, 1)]
    [InlineData(-1, -16, -1, -1)]
    [InlineData(-17, 31, -2, 1)]
    public void Containing_UsesArithmeticShift(int blockX, int blockZ, int chunkX, int chunkZ)
    {
        Assert.Equal(new ChunkPos(chunkX, chunkZ), ChunkPos.Containing(new BlockPos(blockX, 64, blockZ)));
    }

    [Fact]
    public void MinBlockCorner_IsChunkTimesSixteen()
    {
        Assert.Equal(-32, new ChunkPos(-2, 3).MinBlockX);
        Assert.Equal(48, new ChunkPos(-2, 3).MinBlockZ);
    }
}

public class DirectionTests
{
    [Fact]
    public void Ordinals_MatchVanillaDirectionOrder()
    {
        // Wire codecs depend on this exact order.
        Assert.Equal(0, (int)Direction.Down);
        Assert.Equal(1, (int)Direction.Up);
        Assert.Equal(2, (int)Direction.North);
        Assert.Equal(3, (int)Direction.South);
        Assert.Equal(4, (int)Direction.West);
        Assert.Equal(5, (int)Direction.East);
    }

    [Theory]
    [InlineData(Direction.Down, Direction.Up)]
    [InlineData(Direction.North, Direction.South)]
    [InlineData(Direction.West, Direction.East)]
    public void Opposite_PairsMatch(Direction a, Direction b)
    {
        Assert.Equal(b, a.Opposite());
        Assert.Equal(a, b.Opposite());
    }

    [Theory]
    [InlineData(Direction.Down, 0, -1, 0)]
    [InlineData(Direction.Up, 0, 1, 0)]
    [InlineData(Direction.North, 0, 0, -1)]
    [InlineData(Direction.South, 0, 0, 1)]
    [InlineData(Direction.West, -1, 0, 0)]
    [InlineData(Direction.East, 1, 0, 0)]
    public void StepVectors_AreKnown(Direction direction, int sx, int sy, int sz)
    {
        Assert.Equal(sx, direction.StepX());
        Assert.Equal(sy, direction.StepY());
        Assert.Equal(sz, direction.StepZ());
    }

    [Theory]
    [InlineData(Direction.Down, Axis.Y)]
    [InlineData(Direction.Up, Axis.Y)]
    [InlineData(Direction.North, Axis.Z)]
    [InlineData(Direction.South, Axis.Z)]
    [InlineData(Direction.West, Axis.X)]
    [InlineData(Direction.East, Axis.X)]
    public void Axis_MapsCorrectly(Direction direction, Axis axis)
    {
        Assert.Equal(axis, direction.GetAxis());
    }
}
