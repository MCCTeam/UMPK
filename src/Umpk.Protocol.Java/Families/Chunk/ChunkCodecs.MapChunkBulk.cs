using Umpk.Game.World;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The 1.8-only bulk chunk codec (<c>minecraft:map_chunk_bulk</c>, protocol 47, clientbound play <c>0x26</c>). A 1.8 server delivers most terrain through this packet, so decoding it is required for world state.</summary>
/// <remarks>
/// <para>The body begins with a sky-light flag and chunk count, followed by every chunk's X, Z, and primary bitmask. A second loop carries the raw chunk blobs back to back.</para>
/// <para>The critical difference from <c>minecraft:level_chunk</c>: the blob length is NOT on the wire. Vanilla sizes each buffer from the populated-section count, sky-light flag, and biome presence: <c>sections*2*16*16*16 + sections*16*16*16/2 + (skyLight ? sections*16*16*16/2 : 0) + (hasBiomes ? 256 : 0)</c>. See <see cref="LegacyChunkBlobLength"/>. A decoder that instead looked for a length prefix would consume the first chunk plausibly and produce garbage for every chunk after it. A bulk chunk always carries its 256-byte biome array and is therefore a ground-up full column.</para>
/// <para>There is no per-chunk compression. 1.8 moved compression to the connection level; the 1.7 per-chunk deflate is gone. The committed protocol-47 captures exercise this layout byte-exactly.</para>
/// <para>The per-chunk blob is the same shape <c>minecraft:level_chunk</c> carries, so this codec routes it through the same <c>DecodeLegacyChunkBlob</c> / <c>BuildLegacyColumn</c> primitives the 1.8 single-chunk codec uses rather than growing a second implementation that could drift from it.</para>
/// </remarks>
public static partial class ChunkCodecs
{
    // One 16x16x16 section's flat little-endian ushort block states.
    private const int LegacySectionBlockBytes = SectionCellCount * 2;

    // One section's nibble-packed light array (block light, and again for sky light when present).
    private const int LegacySectionLightBytes = SectionCellCount / 2;

    // The trailing per-column biome array present on every ground-up chunk.
    private const int LegacyBiomeBytes = 256;

    /// <summary>The byte length of one 1.8 chunk blob, which the bulk packet computes rather than reading.</summary>
    /// <param name="sectionCount">The number of sections present, i.e. the bitmask's population count.</param>
    /// <param name="skyLight">Whether the dimension sends sky light (the packet-level flag).</param>
    /// <param name="hasBiomes">Whether the column carries its trailing biome array (always true in bulk form).</param>
    /// <returns>The exact blob length in bytes.</returns>
    public static int LegacyChunkBlobLength(int sectionCount, bool skyLight, bool hasBiomes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sectionCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sectionCount, 16);
        int blocks = sectionCount * LegacySectionBlockBytes;
        int blockLight = sectionCount * LegacySectionLightBytes;
        int skyLightBytes = skyLight ? sectionCount * LegacySectionLightBytes : 0;
        int biomes = hasBiomes ? LegacyBiomeBytes : 0;
        return blocks + blockLight + skyLightBytes + biomes;
    }

    internal static void EncodeMapChunkBulkStructure(ref PacketWriter w, MapChunkBulkWireBody body)
    {
        w.WriteBool(body.SkyLight);
        w.WriteVarInt(body.Chunks.Count);
        foreach (LegacyChunkWireBody chunk in body.Chunks)
        {
            w.WriteInt(chunk.X);
            w.WriteInt(chunk.Z);
            w.WriteUShort(chunk.PrimaryMask);
        }

        foreach (LegacyChunkWireBody chunk in body.Chunks)
            WriteLegacyChunkBlob(ref w, chunk);

    }
}
