using System.Collections.Concurrent;
using System.Threading;
using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>
/// A 16-wide vertical stack of <see cref="ChunkSection"/>s at one <see cref="ChunkPos"/>. The section count and Y origin come from the owning world's dimension (<see cref="DimensionState.MinY"/>/<see cref="DimensionState.Height"/>, dynamic world height, 1.17+). It also owns this column's block-entity store and per-column heightmap.
///
/// <para>Sections are created lazily and swapped by reference on install (a decoded section replaces the slot atomically). Per-block writes go through <see cref="ChunkSection"/> under the mutation contract. The block-entity store is a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by <see cref="BlockPos"/> so lookups are safe from any thread.</para>
/// </summary>
public sealed class ChunkColumn
{
    private readonly ChunkSection?[] _sections;
    private readonly int _minSectionY;
    private readonly int _minY;
    private readonly ConcurrentDictionary<BlockPos, BlockEntityData> _blockEntities = new();

    // Heightmaps are optional per-column data (motion-blocking surface); stored as delivered.
    private int[]? _motionBlockingHeightmap;

    private readonly int _blockStateCount;

    /// <summary>Creates an empty column sized for the given dimension at a chunk position. <paramref name="blockStateCount"/> is the session's global block-state count, threaded into lazily created sections so mutation-promoted direct storage uses the vanilla global width (<c>ceillog2(count)</c>); pass 0 when unknown (the width derives from stored ids on demand).</summary>
    public ChunkColumn(ChunkPos position, DimensionState dimension, int blockStateCount = 0)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        Position = position;
        Dimension = dimension;
        _sections = new ChunkSection?[dimension.SectionCount];
        _minSectionY = dimension.MinSectionY;
        _minY = dimension.MinY;
        _blockStateCount = blockStateCount;
    }

    /// <summary>The chunk position of this column.</summary>
    public ChunkPos Position { get; }

    /// <summary>The dimension this column belongs to.</summary>
    public DimensionState Dimension { get; }

    /// <summary>The number of vertical sections in this column.</summary>
    public int SectionCount => _sections.Length;

    /// <summary>The lowest block Y this column stores.</summary>
    public int MinY => _minY;

    /// <summary>The exclusive upper block Y this column stores.</summary>
    public int MaxY => _minY + _sections.Length * ChunkSection.Size;

    /// <summary>The section at the given 0-based index from the bottom, or null when absent.</summary>
    public ChunkSection? GetSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _sections.Length)
            return null;

        return Volatile.Read(ref _sections[sectionIndex]);
    }

    /// <summary>The section containing world block Y, or null when out of range or absent.</summary>
    public ChunkSection? GetSectionForY(int blockY) => GetSection((blockY - _minY) >> 4);

    /// <summary>Installs (or clears) the section at a 0-based index, swapping the slot atomically. Session-loop only. The chunk decoder calls this entry point for each decoded section.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the column's section range.</exception>
    public void SetSection(int sectionIndex, ChunkSection? section)
    {
        if (sectionIndex < 0 || sectionIndex >= _sections.Length)
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));

        Volatile.Write(ref _sections[sectionIndex], section);
    }

    /// <summary>Ensures a section exists at the index and returns it, creating an air-filled one when absent. Session-loop only (used by set-block into an unloaded section).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the column's section range.</exception>
    public ChunkSection GetOrCreateSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _sections.Length)
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));

        ChunkSection? existing = Volatile.Read(ref _sections[sectionIndex]);
        if (existing is not null)
            return existing;

        var created = ChunkSection.Filled(0, biomeId: 0, blockStateCount: _blockStateCount);
        Volatile.Write(ref _sections[sectionIndex], created);
        return created;
    }

    /// <summary>Reads the raw block-state id at world coordinates within this column; 0 (air) when the section is absent.</summary>
    public int GetBlockStateId(int worldX, int worldY, int worldZ)
    {
        ChunkSection? section = GetSectionForY(worldY);
        if (section is null)
            return 0;

        return section.GetBlockStateId(worldX & 15, worldY & 15, worldZ & 15);
    }

    /// <summary>Writes the raw block-state id at world coordinates within this column, creating the section if needed. Session-loop only.</summary>
    public void SetBlockStateId(int worldX, int worldY, int worldZ, int stateId)
    {
        int sectionIndex = (worldY - _minY) >> 4;
        ChunkSection section = GetOrCreateSection(sectionIndex);
        section.SetBlockStateId(worldX & 15, worldY & 15, worldZ & 15, stateId);
    }

    /// <summary>Reads light at world coordinates; missing sections/arrays report 0.</summary>
    public LightLevels GetLight(int worldX, int worldY, int worldZ)
    {
        ChunkSection? section = GetSectionForY(worldY);
        return section is null ? LightLevels.Dark : section.GetLight(worldX & 15, worldY & 15, worldZ & 15);
    }

    /// <summary>The block-entity at a world position, or null.</summary>
    public BlockEntityData? GetBlockEntity(BlockPos pos) =>
        _blockEntities.TryGetValue(pos, out BlockEntityData? data) ? data : null;

    /// <summary>Adds or replaces the block-entity at its position. Session-loop only.</summary>
    public void SetBlockEntity(BlockEntityData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _blockEntities[data.Position] = data;
    }

    /// <summary>Removes the block-entity at a position; returns true when one was removed.</summary>
    public bool RemoveBlockEntity(BlockPos pos) => _blockEntities.TryRemove(pos, out _);

    /// <summary>The block-entities in this column.</summary>
    public IReadOnlyCollection<BlockEntityData> BlockEntities => (IReadOnlyCollection<BlockEntityData>)_blockEntities.Values;

    /// <summary>Returns this column re-bound to <paramref name="dimension"/>, or itself when it is already bound to that exact dimension. Called by <see cref="World.LoadColumn(ChunkColumn)"/> so every column a world holds shares the world's bounds; a column decoded from the wire carries only a placeholder.</summary>
    /// <remarks>
    /// <para>This is a change of ORIGIN, not a re-index. Sections ride the chunk wire bottom-up from the dimension's own floor, so the i-th section in the buffer is the i-th section above the floor whatever that floor is; slot i stays slot i and only <see cref="MinY"/> moves. Re-indexing by the old column's absolute Y would be wrong precisely because that absolute Y is the placeholder's guess, which is what put every nether and end column 64 blocks low.</para>
    /// <para>Sections past the dimension's own count are dropped because the client reads the section buffer into a fixed-size array and never drains the remainder. On 1.21.5+, the announced chunk size still counts a removed long-array length prefix, so the buffer it sends is zero-padded past the last real section.</para>
    /// </remarks>
    internal ChunkColumn RebindTo(DimensionState dimension, int blockStateCount)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        if (ReferenceEquals(dimension, Dimension))
            return this;

        var rebound = new ChunkColumn(Position, dimension, blockStateCount);
        int shared = Math.Min(_sections.Length, rebound._sections.Length);
        for (int i = 0; i < shared; i++)
            rebound._sections[i] = Volatile.Read(ref _sections[i]);

        foreach (BlockEntityData data in _blockEntities.Values)
            rebound._blockEntities[data.Position] = data;

        rebound._motionBlockingHeightmap = Volatile.Read(ref _motionBlockingHeightmap);
        return rebound;
    }

    /// <summary>The motion-blocking heightmap (256 surface Y values in ZX order), or null when not received.</summary>
    public IReadOnlyList<int>? MotionBlockingHeightmap => Volatile.Read(ref _motionBlockingHeightmap);

    /// <summary>Installs the motion-blocking heightmap. Session-loop only.</summary>
    /// <exception cref="ArgumentException">The array is not 256 entries.</exception>
    public void SetMotionBlockingHeightmap(int[]? heightmap)
    {
        if (heightmap is not null && heightmap.Length != 256)
            throw new ArgumentException("A column heightmap must have 256 entries (16x16).", nameof(heightmap));

        Volatile.Write(ref _motionBlockingHeightmap, heightmap);
    }
}
