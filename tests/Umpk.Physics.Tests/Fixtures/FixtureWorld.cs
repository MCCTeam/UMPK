using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics.Tests.Fixtures;

/// <summary>An in-memory voxel <see cref="IPhysicsWorldView"/> over a sparse block map plus a shape table keyed by <see cref="BlockKind"/>. Blocks default to air. This is the "hand-built voxel fixtures" world the tests drive the engine against; it also supports an optional entity-collider feed for the entity-collider checklist item.</summary>
public sealed class FixtureWorld : IPhysicsWorldView
{
    private static readonly Aabb FullCube = new(0, 0, 0, 1, 1, 1);
    private static readonly Aabb SlabBox = new(0, 0, 0, 1, 0.5, 1);
    private static readonly Aabb[] Empty = [];
    private static readonly Aabb[] Cube = [FullCube];
    private static readonly Aabb[] Slab = [SlabBox];

    /// <summary>The un-offset pointed-dripstone tip shape, <c>column-shape construction</c>. Shape retrieval adds the position offset, so the table half is this box and the position half belongs to <see cref="BlockShapeOffset"/>.</summary>
    private static readonly Aabb[] DripstoneTipUp = [new(0.3125, 0.0, 0.3125, 0.6875, 0.6875, 0.6875)];

    /// <summary>The un-offset bamboo collision shape, <c>column-shape construction</c>.</summary>
    private static readonly Aabb[] BambooStalk = [new(0.40625, 0.0, 0.40625, 0.59375, 1.0, 0.59375)];

    /// <summary>The stable scaffolding shape: four full-height 2/16 corner posts and the 14/16-to-16/16 top plate, spelled as the seven boxes the dataset stores.</summary>
    private static readonly Aabb[] ScaffoldingStable =
    [
        new(0.0, 0.0, 0.0, 0.125, 1.0, 0.125),
        new(0.0, 0.0, 0.875, 0.125, 1.0, 1.0),
        new(0.875, 0.0, 0.0, 1.0, 1.0, 0.125),
        new(0.875, 0.0, 0.875, 1.0, 1.0, 1.0),
        new(0.0, 0.875, 0.125, 1.0, 1.0, 0.875),
        new(0.125, 0.875, 0.0, 0.875, 1.0, 0.125),
        new(0.125, 0.875, 0.875, 0.875, 1.0, 1.0),
    ];

    private readonly FixtureBlockData _data = new();
    private readonly Dictionary<BlockPos, BlockKind> _blocks = [];
    private readonly List<Aabb> _entityColliders = [];

    /// <summary>All positions are loaded by default; set to false to test unloaded-chunk fall drift.</summary>
    public bool AllChunksLoaded { get; set; } = true;

    /// <summary>Places one block.</summary>
    public FixtureWorld Set(int x, int y, int z, BlockKind kind)
    {
        _blocks[new BlockPos(x, y, z)] = kind;
        return this;
    }

    /// <summary>Fills an inclusive box with one block kind.</summary>
    public FixtureWorld Fill(int x1, int y1, int z1, int x2, int y2, int z2, BlockKind kind)
    {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
            for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
                    _blocks[new BlockPos(x, y, z)] = kind;

        return this;
    }

    /// <summary>A flat floor of one kind over an X/Z range at height <paramref name="y"/>.</summary>
    public FixtureWorld Floor(int x1, int x2, int z1, int z2, int y, BlockKind kind) =>
        Fill(x1, y, z1, x2, y, z2, kind);

    /// <summary>Adds a world-coordinate entity collider (boat/shulker style hard box).</summary>
    public FixtureWorld AddEntityCollider(Aabb box)
    {
        _entityColliders.Add(box);
        return this;
    }

    /// <summary>The block kind at a position (air when unset).</summary>
    public BlockKind KindAt(int x, int y, int z) =>
        _blocks.TryGetValue(new BlockPos(x, y, z), out BlockKind kind) ? kind : BlockKind.Air;

    public BlockState GetBlock(BlockPos pos)
    {
        BlockKind kind = _blocks.TryGetValue(pos, out BlockKind k) ? k : BlockKind.Air;
        return new BlockState(_data, (int)kind);
    }

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => (BlockKind)state.StateId switch
    {
        BlockKind.Air or BlockKind.Water or BlockKind.Lava or BlockKind.Ladder
            or BlockKind.FlowingWater or BlockKind.FlowingLava
            or BlockKind.WaterLevel1 or BlockKind.WaterLevel2 or BlockKind.WaterLevel3
            or BlockKind.WaterLevel4 or BlockKind.WaterLevel5 or BlockKind.WaterLevel7
            or BlockKind.FallingWater
            or BlockKind.LavaLevel1 or BlockKind.LavaLevel2
            or BlockKind.Kelp or BlockKind.Cobweb
            or BlockKind.BubbleColumnUp or BlockKind.BubbleColumnDown
            or BlockKind.PowderSnow => Empty,
        BlockKind.Slab => Slab,
        BlockKind.Scaffolding => ScaffoldingStable,
        BlockKind.PointedDripstone => DripstoneTipUp,
        BlockKind.Bamboo => BambooStalk,
        _ => Cube,
    };

    public bool IsChunkLoaded(BlockPos pos) => AllChunksLoaded;

    public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        foreach (Aabb box in _entityColliders)
            if (box.Intersects(region))
                into.Add(box);

    }
}
