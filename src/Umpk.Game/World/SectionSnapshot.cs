namespace Umpk.Game.World;

/// <summary>An immutable copy of one section's block and biome cells, produced by <see cref="ChunkSection.Capture"/>. The pathfinder captures the sections in its region of interest into these and reads them off-loop with no coordination (region-capture consistency).</summary>
public sealed class SectionSnapshot
{
    private readonly PalettedSnapshot _blocks;
    private readonly PalettedSnapshot _biomes;

    internal SectionSnapshot(PalettedSnapshot blocks, PalettedSnapshot biomes)
    {
        _blocks = blocks;
        _biomes = biomes;
    }

    /// <summary>Whether any cell MAY hold a block-state id the predicate accepts, decided from the palette rather than by reading cells (see <see cref="PalettedSnapshot.MayContainValue"/>: false means certainly absent, true means possibly present).</summary>
    internal bool MayContainBlockState(Func<int, bool> predicate) => _blocks.MayContainValue(predicate);

    /// <summary>Reads the raw block-state id at local coordinates.</summary>
    public int GetBlockStateId(int x, int y, int z) => _blocks.Get(ChunkSection.BlockIndex(x, y, z));

    /// <summary>Reads the raw biome id at local biome coordinates.</summary>
    public int GetBiomeId(int x, int y, int z) => _biomes.Get(ChunkSection.BiomeIndex(x, y, z));
}
