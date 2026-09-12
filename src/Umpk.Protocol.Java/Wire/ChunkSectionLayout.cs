namespace Umpk.Protocol.Java.Codecs;

/// <summary>How the packed long array of a paletted container is framed on the wire, and how the read side validates its length. The write side only distinguishes prefixed from fixed, which is why it takes the whole framing rather than a collapsed bool: an encoder that is deliberately less discriminating than its decoder should say so in its signature.</summary>
internal enum ChunkLongArrayFraming
{
    /// <summary>1.21.5+ (770/776): no length prefix. The long count derives from the storage bits and cell count; a single-value container has none.</summary>
    FixedSize,

    /// <summary>1.20.2-1.21.1 (764-767): a VarInt length prefix that must equal the storage-derived count against it. A single-value container writes a lone VarInt 0.</summary>
    PrefixedChecked,

    /// <summary>1.21.2-1.21.4 (768/769) and the 1.14-1.17.1 block container: a VarInt length prefix taken from the wire without a storage-derived cross-check. A single-value container still writes a lone VarInt 0.</summary>
    PrefixedRaw,
}

/// <summary>How one era frames a chunk section on the wire. Grouping framing, fluid count, biome presence, and minimum width keeps every section invariant attached to its era.</summary>
/// <param name="Framing">How the packed long array is framed and validated.</param>
/// <param name="HasFluidCount">775+ (26.1): the section carries a second short after <c>nonEmptyBlockCount</c>.</param>
/// <param name="HasBiomeContainer">False on 477-756, where biomes are chunk-level.</param>
/// <param name="MinSectionBytes">The smallest section the drain loop will attempt under this framing. A shorter remainder is a server-written pad the vanilla client ignores, and is kept verbatim.</param>
internal readonly record struct ChunkSectionLayout(
    ChunkLongArrayFraming Framing,
    bool HasFluidCount,
    bool HasBiomeContainer,
    int MinSectionBytes)
{
    /// <summary>477-756: the pre-1.18 block-only section, whose biomes are chunk-level. Only the 735-756 decode grid consults the guard, and 8 is the value it has always used.</summary>
    public static ChunkSectionLayout V1_14 { get; } =
        new(ChunkLongArrayFraming.PrefixedRaw, HasFluidCount: false, HasBiomeContainer: false, MinSectionBytes: 8);

    /// <summary>764-767: VarInt-prefixed arrays cross-checked against the storage-derived size.</summary>
    public static ChunkSectionLayout V1_20_2 { get; } =
        new(ChunkLongArrayFraming.PrefixedChecked, HasFluidCount: false, HasBiomeContainer: true, MinSectionBytes: 8);

    /// <summary>768/769: VarInt-prefixed arrays taken from the wire with no cross-check.</summary>
    public static ChunkSectionLayout V1_21_2 { get; } =
        new(ChunkLongArrayFraming.PrefixedRaw, HasFluidCount: false, HasBiomeContainer: true, MinSectionBytes: 8);

    /// <summary>770-774: fixed-size long arrays, no per-section fluid count.</summary>
    public static ChunkSectionLayout V1_21_5 { get; } =
        new(ChunkLongArrayFraming.FixedSize, HasFluidCount: false, HasBiomeContainer: true, MinSectionBytes: 6);

    /// <summary>775+ (26.1, and 26.2 reuses it): as 1.21.5 plus the per-section fluid count.</summary>
    public static ChunkSectionLayout V26_1 { get; } = V1_21_5 with { HasFluidCount = true };

    /// <summary>True when the packed long array carries a VarInt length prefix.</summary>
    public bool IsPrefixed => Framing != ChunkLongArrayFraming.FixedSize;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Framing},fluid={(HasFluidCount ? 1 : 0)},biome={(HasBiomeContainer ? 1 : 0)},min={MinSectionBytes}";
}
