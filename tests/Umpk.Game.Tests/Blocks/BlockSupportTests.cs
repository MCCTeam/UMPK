using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Blocks;

/// <summary>
/// Pins <see cref="BlockSupport"/> against independent literal protocol-766 box tables. The boxes:
/// <list type="bullet">
/// <item>Full cube: shape 21, <c>[[0,0,0,1,1,1]]</c>.</item>
/// <item>Bottom/top oak_slab: <c>minecraft:oak_slab</c> refs <c>[22,22,23,23,21,21]</c> over
/// properties <c>(type: top, bottom, double; waterlogged)</c>; shape 22 is <c>[[0,0.5,0,1,1,1]]</c> (top), shape 23 is <c>[[0,0,0,1,0.5,1]]</c> (bottom).</item>
/// <item>Snow layers: <c>minecraft:snow</c> refs <c>[0,225,315,233,23,316,236,272]</c> over the
/// 8 <c>layers</c> values. Layer 1 has no collision (shape 0, empty) and layers=2..8 give shapes 225/315/233/23/316/236/272, heights 0.125 through 0.875 in eighths.</item>
/// <item>Dirt path: shape 235, <c>[[0,0,0,1,0.9375,1]]</c>.</item>
/// <item>Bottom-half oak_stairs (straight and outer-left): <c>minecraft:oak_stairs</c> refs a
/// 20-slot table (facing x half x shape, waterlogged folded out) whose <c>half=bottom, shape=straight</c> slot is index 29, <c>[[0,0,0,1,0.5,1],[0,0.5,0,1,1,0.5]]</c>, and whose <c>half=bottom, shape=outer_left</c> slot is index 32, <c>[[0,0,0,1,0.5,1],[0,0.5,0,0.5,1,0.5]]</c>. Both are a full 0.5 shelf plus a partial-footprint step, so neither has one height at which the union covers the whole 1x1 footprint. (A TOP-half straight stair, shape 24, is the mirror image and DOES cover the footprint flush at 1.0, matching vanilla's flat-topped upside-down stair; this suite intentionally never asserts that case as "no cover".)</item>
/// <item>Fence post: <c>[[0.375,0,0.375,0.625,1.5,0.625]]</c>, a thin post that both exceeds the cell
/// (1.5) and never reaches the full 1x1 footprint at any height.</item>
/// </list>
/// </summary>
public sealed class BlockSupportTests
{
    private static readonly Aabb[] FullCube = [new(0, 0, 0, 1, 1, 1)];
    private static readonly Aabb[] TopSlab = [new(0, 0.5, 0, 1, 1, 1)];
    private static readonly Aabb[] BottomSlab = [new(0, 0, 0, 1, 0.5, 1)];
    private static readonly Aabb[] DirtPath = [new(0, 0, 0, 1, 0.9375, 1)];
    private static readonly Aabb[] Fence = [new(0.375, 0, 0.375, 0.625, 1.5, 0.625)];

    // Shape 29: half=bottom, shape=straight.
    private static readonly Aabb[] BottomStraightStair =
    [
        new(0, 0, 0, 1, 0.5, 1),
        new(0, 0.5, 0, 1, 1, 0.5),
    ];

    // Shape 32: half=bottom, shape=outer_left.
    private static readonly Aabb[] BottomOuterLeftStair =
    [
        new(0, 0, 0, 1, 0.5, 1),
        new(0, 0.5, 0, 0.5, 1, 0.5),
    ];

    [Fact]
    public void FullCube_SupportsAtOne()
    {
        Assert.Equal(1.0, BlockSupport.FullCoverSupportHeight(FullCube));

        // Also exercises the BlockState/IBlockShapeSource convenience overload.
        var source = new FakeBlockDataSource();
        BlockState stone = source.GetState(1);
        Assert.Equal(1.0, BlockSupport.FullCoverSupportHeight(stone, new FixedShapeSource(FullCube)));
    }

    [Fact]
    public void BottomSlab_SupportsAtAHalf()
    {
        Assert.Equal(0.5, BlockSupport.FullCoverSupportHeight(BottomSlab));

        var source = new FakeBlockDataSource();
        BlockState stone = source.GetState(1);
        Assert.Equal(0.5, BlockSupport.FullCoverSupportHeight(stone, new FixedShapeSource(BottomSlab)));
    }

    [Fact]
    public void TopSlab_SupportsAtOne() => Assert.Equal(1.0, BlockSupport.FullCoverSupportHeight(TopSlab));

    [Theory]
    [InlineData(0.125)] // layers=2
    [InlineData(0.5)]   // layers=5
    [InlineData(0.875)] // layers=8
    public void SnowLayers_SupportAtTheirOwnHeight(double height)
    {
        Aabb[] shape = [new Aabb(0, 0, 0, 1, height, 1)];
        Assert.Equal(height, BlockSupport.FullCoverSupportHeight(shape));
    }

    [Fact]
    public void DirtPath_SupportsAtNineSixteenths() => Assert.Equal(0.9375, BlockSupport.FullCoverSupportHeight(DirtPath));

    [Theory]
    [MemberData(nameof(BottomHalfStairShapes))]
    public void Stairs_HaveNoFullCoverPlane(Aabb[] shape) => Assert.Null(BlockSupport.FullCoverSupportHeight(shape));

    public static TheoryData<Aabb[]> BottomHalfStairShapes() => new()
    {
        BottomStraightStair,
        BottomOuterLeftStair,
    };

    [Fact]
    public void Fence_ExceedsTheCellAndIsExcluded() => Assert.Null(BlockSupport.FullCoverSupportHeight(Fence));

    [Fact]
    public void EmptyShape_HasNoSupport()
    {
        Assert.Null(BlockSupport.FullCoverSupportHeight(ReadOnlySpan<Aabb>.Empty));
        Assert.Equal(0.0, BlockSupport.MaxSupportHeight(ReadOnlySpan<Aabb>.Empty));
    }

    [Fact]
    public void MaxSupportHeight_ReportsTheTopEvenWithoutFullCover()
    {
        Assert.Null(BlockSupport.FullCoverSupportHeight(BottomStraightStair));
        Assert.Equal(1.0, BlockSupport.MaxSupportHeight(BottomStraightStair));
    }

    [Fact]
    public void BottomSlab_IsWithinThePlayerStepHeight()
    {
        double? height = BlockSupport.FullCoverSupportHeight(BottomSlab);
        Assert.NotNull(height);
        Assert.True(height <= BlockSupport.PlayerStepHeight);
    }

    [Fact]
    public void FullBlock_IsNot()
    {
        double? height = BlockSupport.FullCoverSupportHeight(FullCube);
        Assert.NotNull(height);
        Assert.False(height <= BlockSupport.PlayerStepHeight);
    }

    /// <summary>A block-shape source that answers the same fixed shape for every state, for exercising the <see cref="BlockSupport.FullCoverSupportHeight(BlockState, IBlockShapeSource)"/> overload without needing a real per-version shape table.</summary>
    private sealed class FixedShapeSource(Aabb[] shape) : IBlockShapeSource
    {
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => shape;

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => shape;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => shape;

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => shape;
    }
}
