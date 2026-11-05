using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class PhysicsCollisionQueryTests
{
    [Fact]
    public void SingleBlockQueryExcludesEntitiesBordersAndOtherBlocks()
    {
        var world = new BorderWorld();
        world.Blocks.Set(0, 64, 0, BlockKind.Stone);
        var query = new PhysicsCollisionQuery(world);
        var box = new Aabb(.2, 64.2, .2, .8, 64.8, .8);
        Assert.False(query.IntersectsBlock(new BlockPos(1, 64, 0), box));
        Assert.True(query.IntersectsBlock(new BlockPos(0, 64, 0), box));
        Assert.Equal(0, world.BorderCalls);
        Assert.Equal(0, world.EntityCalls);
    }

    [Fact]
    public void BorderQueryReceivesTheActualSourcePosition()
    {
        var world = new BorderWorld();
        var query = new PhysicsCollisionQuery(world);
        var source = new Vec3d(9, 65, 3);
        var box = new Aabb(.2, 64.2, .2, .8, 64.8, .8);
        Assert.False(query.NoCollision(source, box));
        Assert.Equal(source, world.LastSource);
        Assert.Equal(1, world.BorderCalls);
    }

    [Fact]
    public void BlockObstructionShortCircuitsTheBorderQuery()
    {
        var world = new BorderWorld();
        world.Blocks.Set(0, 64, 0, BlockKind.Stone);
        var query = new PhysicsCollisionQuery(world);
        Assert.False(query.NoCollision(new Vec3d(.5, 64, .5), new Aabb(.2, 64.2, .2, .8, 64.8, .8)));
        Assert.Equal(0, world.BorderCalls);
        Assert.Equal(0, world.EntityCalls);
    }

    [Fact]
    public void EmptyWorldAndChangedBlocksUseTheCurrentView()
    {
        var world = new FixtureWorld();
        var query = new PhysicsCollisionQuery(world);
        var box = new Aabb(.2, 64.2, .2, .8, 64.8, .8);
        var source = new Vec3d(.5, 64, .5);
        Assert.True(query.NoCollision(source, box));
        world.Set(0, 64, 0, BlockKind.Stone);
        Assert.False(query.NoCollision(source, box));
        world.Set(0, 64, 0, BlockKind.Air);
        Assert.True(query.NoCollision(source, box));
    }

    [Fact]
    public void ScaffoldUsesTheSourceHeightAndDescendingFlag()
    {
        var world = new FixtureWorld().Set(0, 64, 0, BlockKind.Scaffolding);
        var query = new PhysicsCollisionQuery(world);
        var box = new Aabb(.2, 64.9, .2, .8, 65.8, .8);
        Assert.False(query.NoCollision(new Vec3d(.5, 65, .5), box));
        Assert.True(query.NoCollision(new Vec3d(.5, 65, .5), box, descending: true));
        Assert.True(query.NoCollision(new Vec3d(.5, 64, .5), box));
    }

    [Fact]
    public void PowderSnowUsesTheSuppliedFallAndEquipmentFacts()
    {
        var world = new FixtureWorld().Set(0, 64, 0, BlockKind.PowderSnow);
        var query = new PhysicsCollisionQuery(world);
        var upper = new Aabb(.2, 64.95, .2, .8, 65.8, .8);
        var lower = new Aabb(.2, 64.2, .2, .8, 64.8, .8);
        var source = new Vec3d(.5, 65, .5);
        Assert.True(query.NoCollision(source, upper));
        Assert.False(query.NoCollision(source, upper, powderSnowWalkable: true));
        Assert.True(query.NoCollision(source, upper, descending: true, powderSnowWalkable: true));
        Assert.False(query.NoCollision(source, lower, fallDistance: 3));
    }

    private sealed class BorderWorld : IPhysicsWorldView
    {
        internal FixtureWorld Blocks { get; } = new();
        internal Vec3d? LastSource { get; private set; }
        internal int BorderCalls { get; private set; }
        internal int EntityCalls { get; private set; }

        public BlockState GetBlock(BlockPos pos) => Blocks.GetBlock(pos);
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => Blocks.GetCollisionShapes(state);
        public bool IsChunkLoaded(BlockPos pos) => true;
        public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into) => EntityCalls++;

        public bool CollidesWithBorder(in Aabb box, Vec3d sourcePosition)
        {
            BorderCalls++;
            LastSource = sourcePosition;
            return true;
        }
    }
}
