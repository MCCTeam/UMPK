using System.Collections.Concurrent;
using System.Threading;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;

namespace Umpk.Game.World;

/// <summary>
/// The per-session world model: a map of loaded <see cref="ChunkColumn"/>s keyed by <see cref="ChunkPos"/>, the dimension state, the world border, and world time. All state is per-instance. Dimension changes swap the whole <see cref="World"/> instance rather than mutating this one.
///
/// <para>The column map is a <see cref="ConcurrentDictionary{TKey,TValue}"/> so column-level lookup is safe from any thread; per-block reads inherit the mutation contract from <see cref="ChunkSection"/> (per-read atomicity off-loop; consistency via <see cref="CopyRegion"/>). Writes happen on the session loop.</para>
/// </summary>
public sealed class World
{
    private readonly ConcurrentDictionary<ChunkPos, ChunkColumn> _columns = new();
    private readonly IBlockDataSource _blockData;
    private readonly Registry<BiomeDefinition> _biomes;
    private readonly ConcurrentDictionary<BlockPos, NbtCompound> _blockEntityNbt = new();

    private WorldBorderState _border = WorldBorderState.Default;
    private long _worldAge;
    private long _timeOfDay;

    /// <summary>Creates a world for a dimension over a block data source and biome registry.</summary>
    /// <param name="dimension">The dimension identity and vertical bounds.</param>
    /// <param name="blockData">The per-version block data source used to resolve <see cref="BlockState"/>s.</param>
    /// <param name="biomes">The biome registry used to resolve <see cref="GetBiome"/>.</param>
    public World(DimensionState dimension, IBlockDataSource blockData, Registry<BiomeDefinition> biomes)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blockData);
        ArgumentNullException.ThrowIfNull(biomes);
        Dimension = dimension;
        _blockData = blockData;
        _biomes = biomes;
    }

    /// <summary>The dimension identity and vertical bounds for this world.</summary>
    public DimensionState Dimension { get; }

    /// <summary>The block data source backing this world's <see cref="BlockState"/> resolution.</summary>
    public IBlockDataSource BlockData => _blockData;

    /// <summary>The current world border state.</summary>
    public WorldBorderState Border => Volatile.Read(ref _border);

    /// <summary>Ticks since world creation.</summary>
    public long WorldAge => Interlocked.Read(ref _worldAge);

    /// <summary>The daylight clock value.</summary>
    public long TimeOfDay => Interlocked.Read(ref _timeOfDay);

    /// <summary>The loaded columns.</summary>
    public IReadOnlyCollection<ChunkColumn> LoadedColumns => (IReadOnlyCollection<ChunkColumn>)_columns.Values;

    /// <summary>Latest raw block-entity payloads for loaded positions.</summary>
    public IReadOnlyCollection<KeyValuePair<BlockPos, NbtCompound>> BlockEntityNbt =>
        (IReadOnlyCollection<KeyValuePair<BlockPos, NbtCompound>>)_blockEntityNbt;

    /// <summary>Replaces the world border state. Session-loop only.</summary>
    public void SetBorder(WorldBorderState border)
    {
        ArgumentNullException.ThrowIfNull(border);
        Volatile.Write(ref _border, border);
    }

    /// <summary>Sets the world time fields. Session-loop only.</summary>
    public void SetTime(long worldAge, long timeOfDay)
    {
        Interlocked.Exchange(ref _worldAge, worldAge);
        Interlocked.Exchange(ref _timeOfDay, timeOfDay);
    }

    /// <summary>The loaded column at a chunk position, or null.</summary>
    public ChunkColumn? GetColumn(ChunkPos pos) =>
        _columns.TryGetValue(pos, out ChunkColumn? column) ? column : null;

    /// <summary>The loaded column containing a block, or null.</summary>
    public ChunkColumn? GetColumn(BlockPos pos) => GetColumn(ChunkPos.Containing(pos));

    /// <summary>Gets the latest raw block-entity payload for a loaded position, if one was received.</summary>
    public NbtCompound? GetBlockEntityNbt(BlockPos pos) =>
        _blockEntityNbt.TryGetValue(pos, out NbtCompound? nbt) ? nbt : null;

    /// <summary>Stores a raw block-entity payload. Session-loop only.</summary>
    public void SetBlockEntityNbt(BlockPos pos, NbtCompound nbt)
    {
        ArgumentNullException.ThrowIfNull(nbt);
        _blockEntityNbt[pos] = nbt;
    }

    /// <summary>Removes cached block-entity payloads belonging to an unloaded column. Session-loop only.</summary>
    public void RemoveBlockEntityNbt(ChunkPos chunk)
    {
        foreach (BlockPos position in _blockEntityNbt.Keys.Where(position => ChunkPos.Containing(position) == chunk))
            _blockEntityNbt.TryRemove(position, out _);

    }

    /// <summary>Loads (or replaces) a column at a chunk position, returning a fresh empty column ready for section installs. Session-loop only.</summary>
    public ChunkColumn LoadColumn(ChunkPos pos)
    {
        // Thread the version's global state count so mutation-promoted sections pick the vanilla global-palette width (ceillog2(state count)) instead of a wasteful fixed one.
        var column = new ChunkColumn(pos, Dimension, _blockData.StateCount);
        _columns[pos] = column;
        return column;
    }

    /// <summary>Installs an already-built column at its position, re-bound to this world's dimension. Session-loop only.</summary>
    /// <remarks>The re-bind is what keeps a world's columns from disagreeing with the world about where the ground is. A column decoded from a chunk frame cannot know its dimension: the frame carries none, and the codec layer has no session. It therefore arrives on a wire-relative placeholder and is given the real bounds at this install seam.</remarks>
    public void LoadColumn(ChunkColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        ChunkColumn bound = column.RebindTo(Dimension, _blockData.StateCount);
        _columns[bound.Position] = bound;
    }

    /// <summary>Installs only the sections an incoming column actually carries, leaving every other section of the loaded column untouched. Session-loop only. This is the install path for a chunk frame that is NOT ground-up.</summary>
    /// <remarks>
    /// <para>Every protocol from 1.8 to 1.16.5 carries a full-chunk (ground-up) flag because the server re-sends a chunk mid-session carrying ONLY the sections that changed: once 64 distinct block changes accumulate in one chunk in one tick, vanilla abandons the per-block and multi-block forms and broadcasts a chunk packet instead. That packet contains only the masked sections. Installing such a frame with <see cref="LoadColumn(ChunkColumn)"/> would erase every section it did not carry.</para>
    /// <para>An absent section is the wire's own record of what the frame omitted: the decoders only <c>SetSection</c> for the bits the packet's mask names, so a null slot here means "not carried", never "carried and empty". A carried section that is genuinely all air is a real, non-null section and does replace what was there.</para>
    /// <para>With no column loaded there is nothing to merge into and the column is installed whole. That case is not reachable from a vanilla server, which only sends a non-full frame for a column it has already sent in full, so this keeps the sections the frame does carry rather than inventing a buffering rule vanilla does not have.</para>
    /// <para>This keeps the existing column's block-entity store and heightmap. Pre-1.17 codecs do not populate those fields. A codec that starts populating them requires corresponding merge logic.</para>
    /// </remarks>
    public void MergeColumnSections(ChunkColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        ChunkColumn bound = column.RebindTo(Dimension, _blockData.StateCount);
        if (!_columns.TryGetValue(bound.Position, out ChunkColumn? existing))
        {
            _columns[bound.Position] = bound;
            return;
        }

        int shared = Math.Min(bound.SectionCount, existing.SectionCount);
        for (int i = 0; i < shared; i++)
            if (bound.GetSection(i) is { } section)
                existing.SetSection(i, section);

    }

    /// <summary>Unloads the column at a chunk position; returns true when one was removed.</summary>
    public bool UnloadColumn(ChunkPos pos) => _columns.TryRemove(pos, out _);

    /// <summary>The raw block-state id at a position; 0 (air) outside loaded chunks.</summary>
    public int GetBlockStateId(BlockPos pos)
    {
        ChunkColumn? column = GetColumn(pos);
        return column is null ? 0 : column.GetBlockStateId(pos.X, pos.Y, pos.Z);
    }

    /// <summary>The block state at a position; the unknown/air state outside loaded chunks.</summary>
    public BlockState GetBlock(BlockPos pos)
    {
        ChunkColumn? column = GetColumn(pos);
        int stateId = column is null ? 0 : column.GetBlockStateId(pos.X, pos.Y, pos.Z);
        return _blockData.GetState(stateId);
    }

    /// <summary>Writes the raw block-state id at a position, loading the column if needed. Session-loop only.</summary>
    public void SetBlockStateId(BlockPos pos, int stateId)
    {
        ChunkColumn column = GetColumn(pos) ?? LoadColumn(ChunkPos.Containing(pos));
        column.SetBlockStateId(pos.X, pos.Y, pos.Z, stateId);
    }

    /// <summary>Writes a block state at a position. Session-loop only.</summary>
    public void SetBlock(BlockPos pos, BlockState state) => SetBlockStateId(pos, state.StateId);

    /// <summary>The biome entry at a position; the default (unbound) entry outside loaded chunks or for unknown ids.</summary>
    public RegistryEntry<BiomeDefinition> GetBiome(BlockPos pos)
    {
        ChunkColumn? column = GetColumn(pos);
        ChunkSection? section = column?.GetSectionForY(pos.Y);
        if (section is null)
            return default;

        int biomeId = section.GetBiomeId((pos.X & 15) >> 2, (pos.Y & 15) >> 2, (pos.Z & 15) >> 2);
        return _biomes.TryGet(biomeId, out RegistryEntry<BiomeDefinition> entry) ? entry : default;
    }

    /// <summary>The light levels at a position; dark outside loaded chunks.</summary>
    public LightLevels GetLight(BlockPos pos)
    {
        ChunkColumn? column = GetColumn(pos);
        return column is null ? LightLevels.Dark : column.GetLight(pos.X, pos.Y, pos.Z);
    }

    /// <summary>The block entity at a position, or null.</summary>
    public BlockEntityData? GetBlockEntity(BlockPos pos) => GetColumn(pos)?.GetBlockEntity(pos);

    /// <summary>Adds or replaces a block entity. Session-loop only.</summary>
    public void SetBlockEntity(BlockEntityData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ChunkColumn column = GetColumn(data.Position) ?? LoadColumn(ChunkPos.Containing(data.Position));
        column.SetBlockEntity(data);
    }

    /// <summary>Removes a block entity; returns true when one was removed.</summary>
    public bool RemoveBlockEntity(BlockPos pos) => GetColumn(pos)?.RemoveBlockEntity(pos) ?? false;

    /// <summary>Captures a box of block-state ids (and optionally biome ids) into an immutable <see cref="RegionSnapshot"/> the pathfinder reads off-loop with cross-cell consistency. Both corners are inclusive. Positions outside loaded chunks capture as air / unbound biome.</summary>
    /// <param name="min">The inclusive minimum corner.</param>
    /// <param name="max">The inclusive maximum corner.</param>
    /// <param name="includeBiomes">When true, biome ids are captured alongside block ids.</param>
    public RegionSnapshot CopyRegion(BlockPos min, BlockPos max, bool includeBiomes = false)
        => BuildRegion(min, max, includeBiomes, demandFaulted: false);

    /// <summary>Bounds a box the same way <see cref="CopyRegion"/> does but copies nothing yet: each 16x16x16 section is captured on the first read inside it, and every later read of that section is a table lookup against the immutable copy. Both corners are inclusive; positions outside the box, and outside loaded chunks, read as air / unbound biome, exactly as a full capture does.</summary>
    /// <remarks>
    /// <para>For a consumer that reads a small, unpredictable part of a large box - an A* search reads 0.2 to 0.6% of the cells in its region on the shapes the pathfinding course records - this is the difference between copying the box and copying the route. The trade is stated on <see cref="RegionSnapshot"/>: the result is single-owner rather than freely shareable, and its sections are consistent individually rather than with each other.</para>
    /// <para>A consumer that reads most of the box, or that needs one moment in time across the whole box, wants <see cref="CopyRegion"/>.</para>
    /// </remarks>
    /// <param name="min">The inclusive minimum corner.</param>
    /// <param name="max">The inclusive maximum corner.</param>
    /// <param name="includeBiomes">When true, biome ids are readable alongside block ids.</param>
    public RegionSnapshot ViewRegion(BlockPos min, BlockPos max, bool includeBiomes = false)
        => BuildRegion(min, max, includeBiomes, demandFaulted: true);

    private RegionSnapshot BuildRegion(BlockPos min, BlockPos max, bool includeBiomes, bool demandFaulted)
    {
        int minX = Math.Min(min.X, max.X);
        int minY = Math.Min(min.Y, max.Y);
        int minZ = Math.Min(min.Z, max.Z);
        int maxX = Math.Max(min.X, max.X);
        int maxY = Math.Max(min.Y, max.Y);
        int maxZ = Math.Max(min.Z, max.Z);

        return new RegionSnapshot(
            this,
            new BlockPos(minX, minY, minZ),
            maxX - minX + 1,
            maxY - minY + 1,
            maxZ - minZ + 1,
            includeBiomes,
            demandFaulted);
    }

    /// <summary>The nearest block state matching <paramref name="match"/> within <paramref name="radius"/> blocks (a cube, not a sphere) of <paramref name="origin"/>, or null when none match. Distance is squared and includes Y. An unloaded column is skipped entirely rather than read as air: <see cref="GetBlockStateId"/> returns 0 (air) outside loaded chunks, indistinguishable from a real air block, so a predicate like "is air" must never be allowed to "find" ground nobody has actually loaded. Ties are broken by scan order (Y outermost, then Z, then X, all ascending), so the result is deterministic rather than an artifact of insertion order.</summary>
    public BlockPos? FindNearest(BlockPos origin, int radius, Func<BlockState, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        BlockPos? best = null;
        long bestSqr = long.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var pos = new BlockPos(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    ChunkColumn? column = GetColumn(pos);
                    if (column is null)
                        continue;

                    var state = new BlockState(_blockData, column.GetBlockStateId(pos.X, pos.Y, pos.Z));
                    if (!match(state))
                        continue;

                    long sqr = ((long)dx * dx) + ((long)dy * dy) + ((long)dz * dz);
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = pos;
                    }
                }

        return best;
    }

    /// <summary>The nearest block whose owning block id is any of <paramref name="ids"/>, in one scan (for example every bed color at once: 16 ids, one call). See <see cref="FindNearest(BlockPos, int, Func{BlockState, bool})"/> for the scan, boundary, and tie rules; this overload matches the block registry id, not the raw state id.</summary>
    public BlockPos? FindNearest(BlockPos origin, int radius, IReadOnlyCollection<Identifier> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var idSet = ids as IReadOnlySet<Identifier> ?? new HashSet<Identifier>(ids);
        return FindNearest(origin, radius, state => idSet.Contains(state.Block.Id));
    }

    /// <summary>Every block state matching <paramref name="match"/> within <paramref name="radius"/> blocks of <paramref name="origin"/>, nearest first, capped at <paramref name="maxResults"/>. Same scan, unloaded-column skip, and scan-order tiebreak as <see cref="FindNearest(BlockPos, int, Func{BlockState, bool})"/>.</summary>
    public IReadOnlyList<BlockPos> Find(BlockPos origin, int radius, Func<BlockState, bool> match, int maxResults = 64)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (maxResults <= 0)
            return [];

        var hits = new List<(BlockPos Pos, long DistanceSqr, int Order)>();
        int order = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var pos = new BlockPos(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    ChunkColumn? column = GetColumn(pos);
                    if (column is null)
                        continue;

                    var state = new BlockState(_blockData, column.GetBlockStateId(pos.X, pos.Y, pos.Z));
                    if (!match(state))
                        continue;

                    long sqr = ((long)dx * dx) + ((long)dy * dy) + ((long)dz * dz);
                    hits.Add((pos, sqr, order++));
                }

        hits.Sort((a, b) =>
        {
            int byDistance = a.DistanceSqr.CompareTo(b.DistanceSqr);
            return byDistance != 0 ? byDistance : a.Order.CompareTo(b.Order);
        });

        int count = Math.Min(maxResults, hits.Count);
        var results = new BlockPos[count];
        for (int i = 0; i < count; i++)
            results[i] = hits[i].Pos;

        return results;
    }
}
