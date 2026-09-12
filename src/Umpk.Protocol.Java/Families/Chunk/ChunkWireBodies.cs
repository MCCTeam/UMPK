using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The structural, byte-faithful decode of a level-chunk frame. This is the model a structural re-encode writes from, deliberately distinct from <see cref="Umpk.Game.World.ChunkColumn"/> (the gameplay store, which normalises palettes and does not retain heightmaps/light). Concrete shapes are <see cref="ModernChunkWireBody"/> (770/776) and <see cref="LegacyChunkWireBody"/> (1.8).</summary>
public abstract record ChunkWireBody
{
    /// <summary>Re-serialises this body, ignoring the packet's retained bytes. Abstract rather than a type switch with a <c>default: throw</c>, so a new era body without an encoder is a compile error instead of an exception the first time a frame of that era decodes.</summary>
    /// <param name="writer">The writer to serialise into.</param>
    public abstract void EncodeInto(ref PacketWriter writer);
}

/// <summary>A modern (protocol 770/776) chunk-with-light frame in wire form. The section buffer is decoded fully into <see cref="Sections"/> plus any under-sized <see cref="SectionPadding"/> remainder; the heightmaps are structural. Block entities and the light-update block are preserved verbatim in <see cref="Tail"/> (the only not-yet-modelled span; it is not the whole body, and it excludes the structurally decoded section buffer where the palettes and block states live).</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="HasFluidCount">Whether sections carry the 26.1+ per-section fluid count.</param>
/// <param name="Heightmaps">The heightmaps, in wire order.</param>
/// <param name="Sections">The decoded sections, in wire order.</param>
/// <param name="SectionPadding">Any trailing under-sized bytes in the section buffer (a server-written pad the vanilla client ignores).</param>
/// <param name="Tail">The block-entity list plus the light-update block, preserved verbatim.</param>
public sealed record ModernChunkWireBody(
    int X,
    int Z,
    bool HasFluidCount,
    IReadOnlyList<ChunkHeightmapWire> Heightmaps,
    IReadOnlyList<ChunkSectionWire> Sections,
    byte[] SectionPadding,
    byte[] Tail) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeModernStructure(ref writer, this);
}

/// <summary>A 1.8 flat-ushort chunk frame in wire form. The block-state portion is decoded structurally into <see cref="SectionBlockStates"/> (one 4096-entry YZX array per present section, ascending section index); the trailing light and biome bytes are preserved verbatim in <see cref="Remainder"/>.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="GroundUp">The ground-up (full-chunk) flag.</param>
/// <param name="PrimaryMask">The present-section bitmask.</param>
/// <param name="SectionBlockStates">Per present section (ascending), 4096 raw block-state ids in YZX order.</param>
/// <param name="Remainder">The light and biome bytes following the block-state portion, preserved verbatim.</param>
public sealed record LegacyChunkWireBody(
    int X,
    int Z,
    bool GroundUp,
    ushort PrimaryMask,
    IReadOnlyList<int[]> SectionBlockStates,
    byte[] Remainder) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeLegacyStructure(ref writer, this);
}

/// <summary>A 1.8 <c>minecraft:map_chunk_bulk</c> frame in wire form (protocol 47 only): a packet-level sky-light flag, then several full columns whose per-chunk payload is the same shape <see cref="LegacyChunkWireBody"/> models for <c>minecraft:level_chunk</c>. The bulk frame carries no per-chunk length prefix, so each blob's size is derived from its own bitmask and the shared sky-light flag; see <see cref="ChunkCodecs.LegacyChunkBlobLength"/>. Every bulk column is ground-up, so each entry's <see cref="LegacyChunkWireBody.GroundUp"/> is true.</summary>
/// <param name="SkyLight">Whether the frame's columns carry sky light (the one packet-level flag).</param>
/// <param name="Chunks">The decoded columns, in wire order; their x/z/mask are the frame's metadata table.</param>
public sealed record MapChunkBulkWireBody(
    bool SkyLight,
    IReadOnlyList<LegacyChunkWireBody> Chunks) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeMapChunkBulkStructure(ref writer, this);
}

/// <summary>The structural decode of a 764-767 chunk frame: NBT-compound heightmaps, modern sections (no fluid count), and the verbatim block-entity/light tail. See <see cref="ChunkCodecs.V1_20_2"/> for the framing.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="Heightmaps">The heightmaps network-NBT compound, order-preserved.</param>
/// <param name="Sections">The decoded sections, in wire order.</param>
/// <param name="SectionPadding">Any trailing under-sized bytes in the section buffer.</param>
/// <param name="Tail">The block-entity list plus the light-update block, preserved verbatim.</param>
/// <param name="HeightmapRootFormat">The network-NBT root framing of the heightmap compound: <see cref="NbtWireFormat.JavaNamedRoot"/> on 1.18-1.20.1 (protocols 757-763, which still send a named root) and <see cref="NbtWireFormat.JavaUnnamedRoot"/> from 1.20.2 (764+, the network-NBT root-name removal). Carried so the structural re-encode reproduces the exact heightmap bytes for both eras.</param>
public sealed record Protocol764To767ChunkWireBody(
    int X,
    int Z,
    NbtTag Heightmaps,
    IReadOnlyList<ChunkSectionWire> Sections,
    byte[] SectionPadding,
    byte[] Tail,
    NbtWireFormat HeightmapRootFormat = NbtWireFormat.JavaUnnamedRoot) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeStructure(ref writer, this);
}

/// <summary>A pre-1.21.5 modern chunk-with-light frame in wire form (protocols 768/769, 1.21.2-1.21.4). Identical to <see cref="ModernChunkWireBody"/> except the heightmaps are the one network-NBT compound (string serialization key -> TAG_Long_Array) that 1.21.5 replaced with a VarInt-keyed packed map (network NBT versus HEIGHTMAPS_STREAM_CODEC); the 1.21.2 and 1.21.4 trees are byte-identical. The compound is held as a parsed <see cref="NbtTag"/> (order-preserving) so the structural re-encode reproduces the exact bytes while remaining inspectable.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="Heightmaps">The heightmaps network-NBT compound (unnamed root).</param>
/// <param name="Sections">The decoded sections, in wire order (no fluid counts on these protocols).</param>
/// <param name="SectionPadding">Any trailing under-sized bytes in the section buffer (a server-written pad the vanilla client ignores).</param>
/// <param name="Tail">The block-entity list plus the light-update block, preserved verbatim.</param>
public sealed record NbtHeightmapChunkWireBody(
    int X,
    int Z,
    NbtTag Heightmaps,
    IReadOnlyList<ChunkSectionWire> Sections,
    byte[] SectionPadding,
    byte[] Tail) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeNbtHeightmapStructure(ref writer, this);
}

/// <summary>The pre-1.18 (1.14-1.15.2, protocols 477-578) level-chunk body. Unlike the full-column bodies, this carries a <c>fullChunk</c> flag and a VarInt section bitmask, the heightmaps are a NAMED-root network-NBT compound (pre-1.20.2 framing), sections carry only a block container (biomes are a chunk-level array, not per-section), and the biomes plus any trailing pad ride verbatim after the sections in the buffer.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="FullChunk">Whether this is a full (ground-up) chunk that also carries biomes.</param>
/// <param name="AvailableSections">The section bitmask (bit i set = section i present).</param>
/// <param name="Heightmaps">The heightmaps network-NBT compound (named root).</param>
/// <param name="Sections">The decoded sections (block container only), in wire order.</param>
/// <param name="BiomesAndPadding">The chunk-level biome array (when full) plus any trailing buffer pad, verbatim.</param>
/// <param name="Tail">The block-entity NBT list, preserved verbatim.</param>
/// <param name="SeparateBiomes">The 1.15 chunk-level biome array (1024 ints = 4096 bytes on a full chunk), written between the heightmaps and the section buffer. Empty on 1.14, where biomes ride inside the buffer after the sections (captured in <paramref name="BiomesAndPadding"/> instead).</param>
public sealed record Pre1_18ChunkWireBody(
    int X,
    int Z,
    bool FullChunk,
    int AvailableSections,
    NbtTag Heightmaps,
    IReadOnlyList<ChunkSectionWire> Sections,
    byte[] BiomesAndPadding,
    byte[] Tail,
    byte[] SeparateBiomes) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodePre118Structure(ref writer, this);
}

/// <summary>The 1.13-1.13.2 (protocols 393-404) level-chunk body. 1.13 predates the 1.14 chunk changes: there are NO heightmaps in the packet (added at 1.14), the per-section layout carries light inline (block light + sky light bytes ride inside the section buffer, not a separate update_light packet), and the chunk-level biome array is an <c>int[256]</c> at the end of the buffer on a full chunk. Because the section boundaries cannot be found without dimension-dependent sky-light knowledge, the section buffer plus block-entity list is preserved verbatim after the header. The header is int x, int z, bool groundUp, and a VarInt bitmask; <see cref="Tail"/> carries the remaining bytes.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="FullChunk">The ground-up (full-chunk) flag.</param>
/// <param name="AvailableSections">The present-section VarInt bitmask.</param>
/// <param name="Tail">The section buffer (with inline light and the trailing biome array) plus the block-entity NBT list, preserved verbatim.</param>
internal sealed record Pre1_13ChunkWireBody(
    int X,
    int Z,
    bool FullChunk,
    int AvailableSections,
    byte[] Tail) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodePre1_13Structure(ref writer, this);
}

/// <summary>A pre-1.18 (protocols 735-756, MC 1.16-1.17.1) level-chunk frame in wire form. Unlike 1.18+, these versions send the section data as one opaque length-prefixed <c>buffer</c> (the paletted sections are serialised into it separately), so the packet-level structural decode captures the header, the present-section mask, and the (named-root) heightmaps NBT, and preserves everything after the heightmaps (biomes, the section buffer, and block entities) as a verbatim <see cref="Tail"/>. This is the honest structural shape of the wire: there are no inline palettes at this level to decode. Ground The mask is a VarInt on 1.16-1.16.5 (<see cref="MaskVarInt"/>) and a BitSet long array on 1.17+ (<see cref="MaskBitset"/>); <see cref="FullChunk"/>/<see cref="ForgetOldData"/> are present only on the versions that carry them.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Z">Chunk Z.</param>
/// <param name="FullChunk">The full-chunk flag (1.16-1.16.5); <see langword="null"/> on 1.17+.</param>
/// <param name="ForgetOldData">The forget-old-data flag (1.16/1.16.1 only); <see langword="null"/> otherwise.</param>
/// <param name="MaskVarInt">The present-section VarInt bitmask (1.16-1.16.5); <see langword="null"/> on 1.17+.</param>
/// <param name="MaskBitset">The present-section BitSet long array (1.17+); <see langword="null"/> on 1.16-1.16.5.</param>
/// <param name="Heightmaps">The heightmaps named-root NBT compound, order-preserved.</param>
/// <param name="Tail">Biomes, the section buffer, and block entities, preserved verbatim.</param>
internal sealed record BitmaskChunkWireBody(
    int X,
    int Z,
    bool? FullChunk,
    bool? ForgetOldData,
    int? MaskVarInt,
    long[]? MaskBitset,
    NbtTag Heightmaps,
    byte[] Tail) : ChunkWireBody
{
    /// <inheritdoc />
    public override void EncodeInto(ref PacketWriter writer) => ChunkCodecs.EncodeBitmaskStructure(ref writer, this);
}
