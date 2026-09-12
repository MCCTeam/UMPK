using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>A level-chunk paletted container in its exact wire form: the bits-per-entry byte followed by the single value, or a palette plus a fixed-size long array (indirect), or a bare fixed-size long array (direct). Captured field-by-field so a structural re-encode reproduces the container bytes exactly.</summary>
/// <param name="BitsPerEntry">The wire bits-per-entry byte. Zero means a single-value (ZeroBitStorage) container.</param>
/// <param name="SingleValue">The single palette value when <see cref="BitsPerEntry"/> is zero; otherwise unused.</param>
/// <param name="Palette">The palette entries for an indirect container; <see langword="null"/> for single-value and direct.</param>
/// <param name="Data">The packed long array (empty for single-value).</param>
public sealed record ChunkPalettedContainerWire(byte BitsPerEntry, int SingleValue, int[]? Palette, long[] Data)
{
    /// <summary>True when the container is a single-value (zero-bit) container.</summary>
    public bool IsSingleValue => BitsPerEntry == 0;

    /// <summary>True when the container carries a palette plus packed indices.</summary>
    public bool IsIndirect => BitsPerEntry != 0 && Palette is not null;

    /// <summary>True when the container packs raw ids at the direct width (no palette).</summary>
    public bool IsDirect => BitsPerEntry != 0 && Palette is null;
}

/// <summary>One decoded chunk section in wire form: the non-empty block count, the optional per-section fluid count (26.1+ only), and the block and biome paletted containers, all captured verbatim so the section re-encodes byte-for-byte.</summary>
/// <param name="NonEmptyBlockCount">The section's non-empty block count short.</param>
/// <param name="FluidCount">The per-section fluid count short (26.1+; <see langword="null"/> for 1.21.5 and 1.8).</param>
/// <param name="Blocks">The block-state paletted container.</param>
/// <param name="Biomes">The biome paletted container.</param>
public sealed record ChunkSectionWire(short NonEmptyBlockCount, short? FluidCount, ChunkPalettedContainerWire Blocks, ChunkPalettedContainerWire Biomes);

/// <summary>A single chunk heightmap in wire form: its type id and packed long array.</summary>
/// <param name="TypeId">The heightmap type registry id.</param>
/// <param name="Data">The packed long array.</param>
public sealed record ChunkHeightmapWire(int TypeId, long[] Data);
