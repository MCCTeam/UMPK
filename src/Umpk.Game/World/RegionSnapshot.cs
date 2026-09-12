using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>A point-in-time copy of a rectangular region of the world (region capture): the block-state ids (and optionally biome ids) in the captured box, plus its own reader. The pathfinder holds its region of interest as one of these and reads it with no coordination, because the section copies it owns never mutate. Reads are a table lookup and a masked shift.</summary>
/// <remarks>
/// <para>The copy is made section by section, through <see cref="ChunkSection.Capture"/>, into a table indexed by the section's <c>(cx, cz, sy)</c> offset inside the box. A section capture copies the packed <c>long[]</c> and the palette wholesale, so an air or stone section that is a single value copies nothing at all, and the cost is proportional to the terrain in the box rather than to the box.</para>
/// <para><b>Two modes, and they differ in exactly one property.</b> <see cref="World.CopyRegion"/> materialises every section in the box up front; the result never touches the world again and is safe to hand to any number of threads. <see cref="World.ViewRegion"/> materialises nothing and captures a section on the first read inside it, which is what the planner wants: an A* search that succeeds reads 0.2 to 0.6% of the cells in its box, so capturing the box up front copies about two hundred times more terrain than the search will ever ask for. A demand-faulted region keeps a live <see cref="World"/> reference and writes its own table as it goes, so it is single-threaded: ONE owner at a time, which for a plan means the search on the thread pool and then the executor on the session loop, ordered by the completion of the search's task. It is not a shared cache.</para>
/// <para>Both modes read the live world off the session loop, and that is legal for the same reason in both: a cell read is atomic. <c>BitStorage.Get</c> is a <c>Volatile.Read</c> of ONE long and <c>BitStorage.Set</c> is an <c>Interlocked.Exchange</c> of one long, and <c>PalettedContainer</c> keeps its whole encoding behind a single volatile <c>_state</c> reference, so a widening promotion swaps the <c>(palette, storage)</c> pair as a unit and a reader holding the old one sees a frozen, self-consistent encoding. A torn cell value is impossible. What demand faulting gives up is cross-cell SIMULTANEITY: two cells in different sections may be copied at different moments, so the region can hold half of a structure that changed while the search ran. The design's answer to terrain moving under a plan is the executor's deviation replan and the block-update replan, and those answer a half-copied structure exactly as they answer a stale one.</para>
/// </remarks>
public sealed class RegionSnapshot
{
    /// <summary>What a slot holds when the world has no section there: an all-air, all-default section, which reads exactly as the absent section did (0 everywhere). Shared because it is immutable, and used rather than a null so a materialised-but-empty slot is not mistaken for an unread one.</summary>
    private static readonly SectionSnapshot AbsentSection = new(
        PalettedSnapshot.Single(ChunkSection.BlockCells, 0),
        PalettedSnapshot.Single(ChunkSection.BiomeCells, 0));

    private readonly SectionSnapshot?[] _sections;
    private readonly World? _world;
    private readonly IBlockDataSource _blockData;
    private readonly bool _hasBiomes;
    private readonly int _worldMinY;
    private readonly int _sectionXMin;
    private readonly int _sectionZMin;
    private readonly int _sectionYMin;
    private readonly int _sectionXCount;
    private readonly int _sectionZCount;

    internal RegionSnapshot(World world, BlockPos min, int sizeX, int sizeY, int sizeZ, bool includeBiomes, bool demandFaulted)
    {
        Min = min;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _blockData = world.BlockData;
        _hasBiomes = includeBiomes;
        _worldMinY = world.Dimension.MinY;

        _sectionXMin = min.X >> 4;
        _sectionZMin = min.Z >> 4;
        _sectionYMin = (min.Y - _worldMinY) >> 4;
        _sectionXCount = ((min.X + sizeX - 1) >> 4) - _sectionXMin + 1;
        _sectionZCount = ((min.Z + sizeZ - 1) >> 4) - _sectionZMin + 1;
        int sectionYCount = ((min.Y + sizeY - 1 - _worldMinY) >> 4) - _sectionYMin + 1;

        _sections = new SectionSnapshot?[_sectionXCount * _sectionZCount * sectionYCount];

        if (demandFaulted)
        {
            _world = world;
            return;
        }

        for (int sy = 0; sy < sectionYCount; sy++)
            for (int cz = 0; cz < _sectionZCount; cz++)
                for (int cx = 0; cx < _sectionXCount; cx++)
                    _sections[(((sy * _sectionZCount) + cz) * _sectionXCount) + cx] =
                        Materialize(world, cx + _sectionXMin, cz + _sectionZMin, sy + _sectionYMin);

    }

    /// <summary>The inclusive minimum corner of the captured box.</summary>
    public BlockPos Min { get; }

    /// <summary>The box extent along X in blocks.</summary>
    public int SizeX { get; }

    /// <summary>The box extent along Y in blocks.</summary>
    public int SizeY { get; }

    /// <summary>The box extent along Z in blocks.</summary>
    public int SizeZ { get; }

    /// <summary>The number of block cells in the captured box (<see cref="SizeX"/> * <see cref="SizeY"/> * <see cref="SizeZ"/>), as a <see cref="long"/> because a large capture overflows an <see cref="int"/>. This is the size of the region the search was handed, and the planner reports it so a slow plan can be attributed to the region size rather than to the search.</summary>
    public long CellCount => (long)SizeX * SizeY * SizeZ;

    /// <summary>How many 16x16x16 sections this region has actually copied. The honest measure of what a capture cost, where <see cref="CellCount"/> is the measure of what it was asked for. On a demand-faulted region it grows as the reader touches new sections, so it is a running total, and it is read after the work rather than during it.</summary>
    public int SectionsCaptured { get; private set; }

    /// <summary>True when this region materialises its sections on first read rather than up front.</summary>
    public bool IsDemandFaulted => _world is not null;

    /// <summary>True when biome ids were captured.</summary>
    public bool HasBiomes => _hasBiomes;

    /// <summary>True when a world position falls inside the captured box.</summary>
    public bool Contains(BlockPos pos) =>
        pos.X >= Min.X && pos.X < Min.X + SizeX &&
        pos.Y >= Min.Y && pos.Y < Min.Y + SizeY &&
        pos.Z >= Min.Z && pos.Z < Min.Z + SizeZ;

    /// <summary>The raw block-state id at a world position; 0 (air) outside the captured box.</summary>
    public int GetBlockStateId(BlockPos pos)
    {
        if (!Contains(pos))
            return 0;

        return SectionAt(pos).GetBlockStateId(pos.X & 15, pos.Y & 15, pos.Z & 15);
    }

    /// <summary>The block state at a world position; the air/unknown state outside the box.</summary>
    public BlockState GetBlock(BlockPos pos) => _blockData.GetState(GetBlockStateId(pos));

    /// <summary>The raw biome id at a world position; 0 outside the box or when biomes were not captured.</summary>
    public int GetBiomeId(BlockPos pos)
    {
        if (!_hasBiomes || !Contains(pos))
            return 0;

        return SectionAt(pos).GetBiomeId((pos.X & 15) >> 2, (pos.Y & 15) >> 2, (pos.Z & 15) >> 2);
    }

    /// <summary>Whether any cell in the box MAY hold a block state the predicate accepts, decided from the sections' palettes rather than by reading cells, and without materialising anything.</summary>
    /// <remarks>
    /// <para>False means CERTAINLY ABSENT: no section overlapping the box can produce such a state, so no read of this region will ever return one. True means possibly present - a palette is a superset of the ids its cells hold, and a direct-width section has no palette to test and answers true unconditionally. So this answers "may I skip the work that would only matter if this state existed", and never "this state is here".</para>
    /// <para>The cost is one predicate call per distinct id per section, which for the boxes the planner builds is a few hundred calls against a box of a million cells. On a demand-faulted region the LIVE sections are tested where they have not been materialised yet, which neither copies them nor counts against <see cref="SectionsCaptured"/>; the answer is therefore about the world at the moment of the call, on the same footing as everything else a demand-faulted region reports.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
    public bool MayContainBlockState(Func<BlockState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        IBlockDataSource data = _blockData;
        bool ById(int stateId) => predicate(data.GetState(stateId));

        int sectionYCount = _sections.Length / (_sectionXCount * _sectionZCount);
        for (int sy = 0; sy < sectionYCount; sy++)
            for (int cz = 0; cz < _sectionZCount; cz++)
                for (int cx = 0; cx < _sectionXCount; cx++)
                {
                    SectionSnapshot? captured = _sections[(((sy * _sectionZCount) + cz) * _sectionXCount) + cx];
                    if (captured is not null)
                    {
                        if (captured.MayContainBlockState(ById))
                            return true;

                        continue;
                    }

                    ChunkSection? live = _world!
                        .GetColumn(new ChunkPos(cx + _sectionXMin, cz + _sectionZMin))?
                        .GetSection(sy + _sectionYMin);
                    if (live is not null && live.MayContainBlockState(ById))
                        return true;

                }

        return false;
    }

    private SectionSnapshot SectionAt(BlockPos pos)
    {
        int cx = (pos.X >> 4) - _sectionXMin;
        int cz = (pos.Z >> 4) - _sectionZMin;
        int sy = ((pos.Y - _worldMinY) >> 4) - _sectionYMin;
        int slot = (((sy * _sectionZCount) + cz) * _sectionXCount) + cx;

        SectionSnapshot? section = _sections[slot];
        if (section is null)
        {
            // Only reachable on a demand-faulted region: the eager constructor fills every slot.
            section = Materialize(_world!, cx + _sectionXMin, cz + _sectionZMin, sy + _sectionYMin);
            _sections[slot] = section;
        }

        return section;
    }

    private SectionSnapshot Materialize(World world, int chunkX, int chunkZ, int sectionIndex)
    {
        ChunkSection? section = world.GetColumn(new ChunkPos(chunkX, chunkZ))?.GetSection(sectionIndex);
        if (section is null)
            return AbsentSection;

        SectionsCaptured++;
        return section.Capture();
    }
}
