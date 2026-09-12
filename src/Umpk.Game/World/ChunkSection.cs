using System.Threading;

namespace Umpk.Game.World;

/// <summary>
/// The 16x16x16 storage unit: a paletted block-state container, a 4x4x4 paletted biome container, and optional sky/block light arrays. Local coordinates are 0-15 on each axis; the linear order is YZX (<c>(y*16 + z)*16 + x</c>), matching the wire layout. Biomes index at 4x4x4 resolution with the same YZX order.
///
/// <para>Get/set take and return raw state ids (int). The mutation contract is inherited from <see cref="PalettedContainer"/>: writes are session-loop-only in-place atomic long updates; off-loop readers get per-read atomicity; consistent multi-cell views come from <see cref="Capture"/>.</para>
/// </summary>
public sealed class ChunkSection
{
    /// <summary>The edge length of a section in blocks.</summary>
    public const int Size = 16;

    /// <summary>The number of block cells in a section (16^3).</summary>
    public const int BlockCells = Size * Size * Size;

    /// <summary>The edge length of the biome grid (4x4x4 at one biome per 4 blocks).</summary>
    public const int BiomeSize = 4;

    /// <summary>The number of biome cells in a section (4^3).</summary>
    public const int BiomeCells = BiomeSize * BiomeSize * BiomeSize;

    // Vanilla block-state palette selection: linear palettes floor at 4 bits, hashmap widths up to 8, then the global (direct) palette at ceillog2(state count) bits.
    private const int BlockMinIndirectBits = 4;
    private const int BlockMaxIndirectBits = 8;

    // Vanilla biome palette selection: 1-3 bit linear palettes, then the global palette.
    private const int BiomeMinIndirectBits = 1;
    private const int BiomeMaxIndirectBits = 3;

    // Biomes have a small id space; 16 bits covers any realistic biome registry.
    private const int BiomeDirectBits = 16;

    private readonly PalettedContainer _blocks;
    private readonly PalettedContainer _biomes;

    // Light arrays are swapped by reference when the server sends new light; readers take one volatile read.
    private NibbleArray? _skyLight;
    private NibbleArray? _blockLight;

    private ChunkSection(PalettedContainer blocks, PalettedContainer biomes)
    {
        _blocks = blocks;
        _biomes = biomes;
    }

    /// <summary>Creates an empty section filled with a single block-state id and biome id. <paramref name="blockStateCount"/> is the global block-state count for the session's version; when positive, a mutation-promoted direct block store uses <c>ceillog2(count)</c> bits like the global palette (<c>globalPaletteBitsInMemory</c>). Pass 0 when unknown; the width is then derived from the ids actually stored and re-widened on demand.</summary>
    public static ChunkSection Filled(int blockStateId, int biomeId = 0, int blockStateCount = 0) =>
        new(
            PalettedContainer.SingleValue(BlockCells, DirectBitsForStateCount(blockStateCount), BlockMinIndirectBits, BlockMaxIndirectBits, blockStateId),
            PalettedContainer.SingleValue(BiomeCells, BiomeDirectBits, BiomeMinIndirectBits, BiomeMaxIndirectBits, biomeId));

    // ceillog2(count), floored at the max indirect width + 1 so a promoted store is always wider than any indirect palette it replaces; 0 (unknown) defers to derive-on-demand. Long shifts keep the loop overflow-safe for degenerate counts near int.MaxValue (capped at 31 bits).
    private static int DirectBitsForStateCount(int blockStateCount)
    {
        if (blockStateCount <= 0)
            return 0;

        int bits = 1;
        while (bits < 31 && 1L << bits < blockStateCount)
            bits++;

        return Math.Max(bits, BlockMaxIndirectBits + 1);
    }

    /// <summary>The linear cell index for local block coordinates (YZX order).</summary>
    public static int BlockIndex(int x, int y, int z) => ((y * Size) + z) * Size + x;

    /// <summary>The linear cell index for local biome coordinates (YZX order, 0-3 each).</summary>
    public static int BiomeIndex(int x, int y, int z) => ((y * BiomeSize) + z) * BiomeSize + x;

    /// <summary>Reads the raw block-state id at local coordinates from 0 to 15.</summary>
    public int GetBlockStateId(int x, int y, int z) => _blocks.Get(BlockIndex(x, y, z));

    /// <summary>Writes the raw block-state id at local coordinates from 0 to 15. Session-loop only.</summary>
    public void SetBlockStateId(int x, int y, int z, int stateId) => _blocks.Set(BlockIndex(x, y, z), stateId);

    /// <summary>Reads the raw biome id at local biome coordinates from 0 to 3.</summary>
    public int GetBiomeId(int x, int y, int z) => _biomes.Get(BiomeIndex(x, y, z));

    /// <summary>Writes the raw biome id at local biome coordinates from 0 to 3. Session-loop only.</summary>
    public void SetBiomeId(int x, int y, int z, int biomeId) => _biomes.Set(BiomeIndex(x, y, z), biomeId);

    /// <summary>The sky-light array, or null when the section has no sky light.</summary>
    public NibbleArray? SkyLight => Volatile.Read(ref _skyLight);

    /// <summary>The block-light array, or null when the section has no block light.</summary>
    public NibbleArray? BlockLight => Volatile.Read(ref _blockLight);

    /// <summary>Installs (or clears) the sky-light array for this section. Session-loop only.</summary>
    public void SetSkyLight(NibbleArray? light) => Volatile.Write(ref _skyLight, light);

    /// <summary>Installs (or clears) the block-light array for this section. Session-loop only.</summary>
    public void SetBlockLight(NibbleArray? light) => Volatile.Write(ref _blockLight, light);

    /// <summary>Reads the light at local block coordinates; missing arrays report 0.</summary>
    public LightLevels GetLight(int x, int y, int z)
    {
        int index = BlockIndex(x, y, z);
        NibbleArray? sky = Volatile.Read(ref _skyLight);
        NibbleArray? block = Volatile.Read(ref _blockLight);
        return new LightLevels(sky is null ? (byte)0 : sky.Get(index), block is null ? (byte)0 : block.Get(index));
    }

    /// <summary>Installs a decoded block container into a new section: single-value shape. Used by the Java codec layer's chunk decoder for the single-valued palette. <paramref name="blockStateCount"/> as on <see cref="Filled"/>.</summary>
    public static ChunkSection FromSingleValueBlocks(int blockStateId, int blockStateCount = 0) =>
        new(
            PalettedContainer.SingleValue(BlockCells, DirectBitsForStateCount(blockStateCount), BlockMinIndirectBits, BlockMaxIndirectBits, blockStateId),
            PalettedContainer.SingleValue(BiomeCells, BiomeDirectBits, BiomeMinIndirectBits, BiomeMaxIndirectBits, 0));

    /// <summary>Installs a decoded indirect block container (palette + packed indices) into a new section. Used by the Java codec layer's chunk decoder. <paramref name="blockStateCount"/> as on <see cref="Filled"/>.</summary>
    public static ChunkSection FromIndirectBlocks(int[] palette, int bitsPerEntry, long[] packedData, int blockStateCount = 0) =>
        new(
            PalettedContainer.Indirect(BlockCells, DirectBitsForStateCount(blockStateCount), BlockMinIndirectBits, BlockMaxIndirectBits, palette, bitsPerEntry, packedData),
            PalettedContainer.SingleValue(BiomeCells, BiomeDirectBits, BiomeMinIndirectBits, BiomeMaxIndirectBits, 0));

    /// <summary>Installs a decoded direct block container (raw packed ids) into a new section. Used by the Java codec layer's chunk decoder and by the 1.8 flat-ushort path. The wire width doubles as the mutation direct width (vanilla writes the global palette's in-memory bit width); wider ids re-widen on demand.</summary>
    public static ChunkSection FromDirectBlocks(int directBits, long[] packedData) =>
        new(
            PalettedContainer.Direct(BlockCells, directBits, BlockMinIndirectBits, BlockMaxIndirectBits, packedData),
            PalettedContainer.SingleValue(BiomeCells, BiomeDirectBits, BiomeMinIndirectBits, BiomeMaxIndirectBits, 0));

    /// <summary>Installs a decoded single-value biome container. Session-loop only.</summary>
    public void SetSingleValueBiomes(int biomeId)
    {
        for (int i = 0; i < BiomeCells; i++)
            _biomes.Set(i, biomeId);

    }

    /// <summary>Installs decoded indirect biomes (palette + packed indices). Session-loop only.</summary>
    public void SetIndirectBiomes(int[] palette, int bitsPerEntry, long[] packedData)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(packedData);
        var decoded = PalettedContainer.Indirect(BiomeCells, BiomeDirectBits, BiomeMinIndirectBits, BiomeMaxIndirectBits, palette, bitsPerEntry, packedData);
        for (int i = 0; i < BiomeCells; i++)
            _biomes.Set(i, decoded.Get(i));

    }

    /// <summary>Copies this section's block and biome cells into an immutable snapshot (region capture).</summary>
    public SectionSnapshot Capture() => new(_blocks.Capture(), _biomes.Capture());

    /// <summary>Whether any cell MAY hold a block-state id the predicate accepts, decided from the palette rather than by reading cells, and without copying anything. False means certainly absent; true means possibly present (see <see cref="PalettedSnapshot.MayContainValue"/>).</summary>
    internal bool MayContainBlockState(Func<int, bool> predicate) => _blocks.MayContainValue(predicate);

    // Encoding introspection for tests (InternalsVisibleTo): current widths and direct-mode flags.
    internal int BlockPaletteBits => _blocks.BitsPerEntry;

    internal int BiomePaletteBits => _biomes.BitsPerEntry;

    internal bool BlocksAreDirect => _blocks.IsDirect;

    internal bool BiomesAreDirect => _biomes.IsDirect;
}
