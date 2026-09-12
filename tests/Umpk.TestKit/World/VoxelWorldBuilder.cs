using Umpk.Geometry;

namespace Umpk.TestKit.World;

/// <summary>A fluent builder for a <see cref="VoxelWorld"/>. Place individual blocks, fill flat floors, and fill cuboid regions; then <see cref="Build"/>. The declared loaded bounds default to a generous box around whatever was placed, so <see cref="VoxelWorld.IsChunkLoaded"/> answers true where blocks exist and false far away.</summary>
public sealed class VoxelWorldBuilder
{
    private readonly Dictionary<BlockPos, int> _blocks = [];
    private Aabb? _loadedBounds;

    /// <summary>Sets a single block.</summary>
    public VoxelWorldBuilder Set(int x, int y, int z, VoxelBlock block)
    {
        if (block == VoxelBlock.Air)
            _blocks.Remove(new BlockPos(x, y, z));

        else
            _blocks[new BlockPos(x, y, z)] = (int)block;

        return this;
    }

    /// <summary>Fills a flat floor slab at <paramref name="y"/> over the given XZ rectangle (inclusive).</summary>
    public VoxelWorldBuilder Floor(int minX, int minZ, int maxX, int maxZ, int y, VoxelBlock block = VoxelBlock.Stone)
    {
        for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
                Set(x, y, z, block);

        return this;
    }

    /// <summary>Fills a cuboid region (inclusive bounds).</summary>
    public VoxelWorldBuilder Fill(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, VoxelBlock block)
    {
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                    Set(x, y, z, block);

        return this;
    }

    /// <summary>Declares the loaded-chunk bounds explicitly (for off-world fall tests).</summary>
    public VoxelWorldBuilder LoadedBounds(int minX, int minZ, int maxX, int maxZ)
    {
        _loadedBounds = new Aabb(minX, 0, minZ, maxX, 0, maxZ);
        return this;
    }

    /// <summary>Materializes the world.</summary>
    public VoxelWorld Build()
    {
        Aabb bounds = _loadedBounds ?? DefaultBounds();
        return new VoxelWorld(new Dictionary<BlockPos, int>(_blocks), bounds);
    }

    private Aabb DefaultBounds()
    {
        if (_blocks.Count == 0)
            return new Aabb(-64, 0, -64, 64, 0, 64);

        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (BlockPos p in _blocks.Keys)
        {
            minX = Math.Min(minX, p.X);
            minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X);
            maxZ = Math.Max(maxZ, p.Z);
        }

        // Pad so blocks near the placed region still count as inside a loaded chunk.
        return new Aabb(minX - 16, 0, minZ - 16, maxX + 16, 0, maxZ + 16);
    }
}
