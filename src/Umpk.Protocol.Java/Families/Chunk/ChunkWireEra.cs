namespace Umpk.Protocol.Java.Codecs;

/// <summary>Which structural body shape a decoded chunk frame carried. It is what makes a decoded packet self-describing enough to rebuild its model from the retained bytes, so the model itself does not have to be kept alive on the packet. One member per band whose structural decode takes a different path; the two bands that share the 764 body shape and differ only in the heightmap NBT root name are two members rather than one member plus a hardcoded format, because fusing them reads the named compound as unnamed, drives the section-buffer length off, and yields an all-air structural column.</summary>
public enum ChunkWireEra
{
    /// <summary>47: the flat ushort chunk, with light and biomes riding verbatim behind it.</summary>
    Legacy1_8,

    /// <summary>107-404: header plus a verbatim section buffer that carries its light inline.</summary>
    Pre1_13,

    /// <summary>477-572: chunk-level biomes ride after the sections, inside the buffer.</summary>
    TrailingBiomes,

    /// <summary>573-734: the same body with the biome array written before the buffer.</summary>
    LeadingBiomes,

    /// <summary>735/736: VarInt section mask, full-chunk flag and forget-old-data flag.</summary>
    Bitmask1_16,

    /// <summary>751-754: the 1.16.2 section-mask reshape, no forget-old-data.</summary>
    Bitmask1_16_2,

    /// <summary>755/756: the mask becomes a BitSet long array and the full-chunk flag is gone.</summary>
    Bitmask1_17,

    /// <summary>757-763: the 1.18 full column, whose heightmap NBT still has a NAMED root.</summary>
    NamedHeightmap757,

    /// <summary>764-767: the same body over an unnamed heightmap root.</summary>
    Prefixed764,

    /// <summary>768/769: NBT heightmaps with raw VarInt-prefixed section long arrays.</summary>
    NbtHeightmap,

    /// <summary>770+: the VarInt-keyed packed heightmap map and fixed-size long arrays.</summary>
    Modern,
}
