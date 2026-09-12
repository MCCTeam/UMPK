using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.World;

public class RaycastTests
{
    private static Umpk.Game.World.World WorldWithBlock(BlockPos pos, int state = WorldTestData.StoneState)
    {
        var world = WorldTestData.NewWorld();
        world.SetBlockStateId(pos, state);
        return world;
    }

    [Fact]
    public void AxisHit_AlongPositiveX_HitsWestFace()
    {
        var world = WorldWithBlock(new BlockPos(5, 64, 0));
        // Ray from x=0 straight along +X through the center of the block at x=5.
        var start = new Vec3d(0.5, 64.5, 0.5);
        var end = new Vec3d(10.5, 64.5, 0.5);

        BlockHitResult hit = Raycast.CastBlock(world, start, end);
        Assert.True(hit.Hit);
        Assert.Equal(new BlockPos(5, 64, 0), hit.BlockPos);
        Assert.Equal(Direction.West, hit.Face);
        Assert.Equal(5.0, hit.Point.X, 3);
    }

    [Fact]
    public void AxisHit_AlongNegativeY_HitsTopFace()
    {
        var world = WorldWithBlock(new BlockPos(0, 60, 0));
        var start = new Vec3d(0.5, 70.0, 0.5);
        var end = new Vec3d(0.5, 55.0, 0.5);

        BlockHitResult hit = Raycast.CastBlock(world, start, end);
        Assert.True(hit.Hit);
        Assert.Equal(new BlockPos(0, 60, 0), hit.BlockPos);
        Assert.Equal(Direction.Up, hit.Face);
        Assert.Equal(61.0, hit.Point.Y, 3);
    }

    [Fact]
    public void NegativeCoordinates_Hit()
    {
        var world = WorldWithBlock(new BlockPos(-8, -20, -8));
        var start = new Vec3d(-15.5, -19.5, -7.5);
        var end = new Vec3d(-3.5, -19.5, -7.5);

        BlockHitResult hit = Raycast.CastBlock(world, start, end);
        Assert.True(hit.Hit);
        Assert.Equal(new BlockPos(-8, -20, -8), hit.BlockPos);
        Assert.Equal(Direction.West, hit.Face);
    }

    [Fact]
    public void Miss_WhenNoBlockInPath()
    {
        var world = WorldWithBlock(new BlockPos(5, 64, 0));
        var start = new Vec3d(0.5, 64.5, 5.5);
        var end = new Vec3d(10.5, 64.5, 5.5); // parallel row, never enters the block column
        Assert.False(Raycast.CastBlock(world, start, end).Hit);
    }

    [Fact]
    public void Fluids_SkippedUnlessIncluded()
    {
        var world = WorldWithBlock(new BlockPos(3, 64, 0), WorldTestData.WaterState);
        var start = new Vec3d(0.5, 64.5, 0.5);
        var end = new Vec3d(6.5, 64.5, 0.5);

        Assert.False(Raycast.CastBlock(world, start, end, includeFluids: false).Hit);
        Assert.True(Raycast.CastBlock(world, start, end, includeFluids: true).Hit);
    }

    [Fact]
    public void OriginInsideBlock_HitsImmediately()
    {
        var world = WorldWithBlock(new BlockPos(2, 64, 2));
        var start = new Vec3d(2.5, 64.5, 2.5);
        var end = new Vec3d(2.5, 64.5, 10.5);
        BlockHitResult hit = Raycast.CastBlock(world, start, end);
        Assert.True(hit.Hit);
        Assert.Equal(new BlockPos(2, 64, 2), hit.BlockPos);
    }

    [Fact]
    public void EntityRaycast_HitsClosestCandidate()
    {
        var near = Aabb.OfSize(3.0, 64.0, 0.5, 0.6, 1.8);   // centered near x=3
        var far = Aabb.OfSize(6.0, 64.0, 0.5, 0.6, 1.8);    // centered near x=6
        ReadOnlySpan<Aabb> candidates = [far, near];        // order reversed to prove distance ordering

        var start = new Vec3d(0.0, 64.9, 0.5);
        var end = new Vec3d(10.0, 64.9, 0.5);

        EntityHitResult hit = Raycast.CastEntities(start, end, candidates);
        Assert.True(hit.Hit);
        Assert.Equal(1, hit.CandidateIndex); // the "near" box, at span index 1
        Assert.True(hit.Distance < 3.0);
    }

    [Fact]
    public void EntityRaycast_MissesWhenNoOverlap()
    {
        var box = Aabb.OfSize(3.0, 64.0, 5.0, 0.6, 1.8);
        ReadOnlySpan<Aabb> candidates = [box];
        var start = new Vec3d(0.0, 64.9, 0.5);
        var end = new Vec3d(10.0, 64.9, 0.5);
        Assert.False(Raycast.CastEntities(start, end, candidates).Hit);
    }

    [Fact]
    public void BlockRaycast_HonorsShapeSource_PartialBoxMiss()
    {
        var world = WorldWithBlock(new BlockPos(5, 64, 0));
        // Shape source that gives the block a small box in its upper half only; a ray through the lower half must miss even though the DDA enters the voxel.
        var shapes = new HalfBlockShapeSource(world.BlockData, upperHalf: true);

        var lowStart = new Vec3d(0.5, 64.2, 0.5);
        var lowEnd = new Vec3d(10.5, 64.2, 0.5);
        Assert.False(Raycast.CastBlock(world, lowStart, lowEnd, shapes: shapes).Hit);

        var highStart = new Vec3d(0.5, 64.8, 0.5);
        var highEnd = new Vec3d(10.5, 64.8, 0.5);
        Assert.True(Raycast.CastBlock(world, highStart, highEnd, shapes: shapes).Hit);
    }
}
