using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ModernSlabCollisionShapeTests
{
    [Theory]
    [InlineData("bottom")]
    [InlineData("top")]
    [InlineData("double")]
    public void SelectedSlabResultReadsCollisionAndNeverSubstitutesOutline(string type)
    {
        Aabb expected = type == "bottom" ? new(0, 0, 0, 1, .5, 1) : type == "top" ? new(0, .5, 0, 1, 1, 1) : new(0, 0, 0, 1, 1, 1);
        var shapes = new CollisionOnlyShapes(expected);
        var blocks = new RegistryBlockDataSource(JavaGameData.Registries(777).Blocks, isLegacy: false);

        Assert.True(VanillaSlabPlacement.TryGetCollisionShape(blocks, Identifier.Minecraft("oak_slab"), type, shapes, out Aabb actual));

        Assert.Equal(expected, actual);
        Assert.Equal(1, shapes.CollisionReads);
    }

    [Fact]
    public void MissingHalfCollisionCannotBecomeAFullCubePrediction()
    {
        var shapes = new CollisionOnlyShapes(new(0, 0, 0, 1, 1, 1));
        var blocks = new RegistryBlockDataSource(JavaGameData.Registries(777).Blocks, isLegacy: false);

        Assert.False(VanillaSlabPlacement.TryGetCollisionShape(blocks, Identifier.Minecraft("oak_slab"), "bottom", shapes, out _));
    }

    private sealed class CollisionOnlyShapes(Aabb shape) : IBlockShapeSource
    {
        private readonly Aabb[] _boxes = [shape];
        internal int CollisionReads { get; private set; }
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state)
        {
            CollisionReads++;
            return _boxes;
        }
        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => throw new InvalidOperationException("The selected collision state must retain its data source.");
        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => throw new InvalidOperationException("Slab placement must not read outline geometry.");
        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => throw new InvalidOperationException("Slab placement must not read outline geometry.");
    }
}
