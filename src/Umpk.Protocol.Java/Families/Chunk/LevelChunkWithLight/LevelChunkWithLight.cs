using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Chunk with light (<c>minecraft:level_chunk_with_light</c> / 1.8 <c>minecraft:level_chunk</c>).</summary>
        public static readonly PacketType<ClientboundLevelChunkPacket> LevelChunk =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("level_chunk_with_light"));
    }
}

/// <summary>Chunk with light. The codec decodes the payload into <see cref="Column"/> using the <c>Umpk.Game</c> install APIs (modern paletted sections for 770/776; the 1.8 flat-ushort format into direct sections). The decoded column is carried on the packet for consumers; block-entity/light detail beyond sections is decoded in a later phase.</summary>
/// <remarks>
/// <para>The structural decode is deliberately lossy: heightmaps, block entities, the light-update block, and any trailing section-buffer padding are consumed but not modelled, so a purely structural re-encode cannot reproduce the frame byte-for-byte. To keep the chunk frame round-trippable (proxy-grade fidelity, the conformance contract), the decoder retains the exact wire body in <see cref="RawBody"/> and the encoder writes it back verbatim. This mirrors the raw-carrier pattern used for other lossy-but-must-round-trip payloads in this assembly (for example the 26.1+ velocity block on <c>ClientboundSetEntityMotionPacket</c>). A packet built in code (no <see cref="RawBody"/>) has no lossless wire model yet and cannot be encoded.</para>
/// </remarks>
public sealed record ClientboundLevelChunkPacket(int ChunkX, int ChunkZ, ChunkColumn Column, byte[]? RawBody = null) : IPacket
{
    /// <summary>Block-entity NBT carried with this chunk, when that protocol's chunk codec exposes it.</summary>
    public IReadOnlyList<ChunkBlockEntity> BlockEntities { get; init; } = [];

    /// <summary>Which structural body shape this frame carried. Together with <see cref="SectionLayout"/> it is everything <c>ChunkCodecs.DecodeStructure</c> needs to rebuild the model from <see cref="RawBody"/>, which is why the model itself is not retained here.</summary>
    public Umpk.Protocol.Java.Codecs.ChunkWireEra WireEra { get; init; }

    /// <summary>How this frame's sections were framed. The era alone cannot say, because 770-774 and 775+ share a body shape and differ only in the per-section fluid count.</summary>
    internal Umpk.Protocol.Java.Codecs.ChunkSectionLayout SectionLayout { get; init; }

    /// <summary>Whether this frame is a FULL (ground-up) column, i.e. a complete replacement, rather than an update carrying only the sections its bitmask names. True for every protocol that has no such flag on the wire (1.17 onward, where every chunk packet is full) and for a code-built packet.</summary>
    /// <remarks>A consumer must not install a non-full frame as a whole column: the frame physically does not carry the other sections. The server sends one after 64 distinct block changes accumulate in a chunk in one tick, in place of the per-block and multi-block forms, and only for a column already sent in full.</remarks>
    public bool FullChunk { get; init; } = true;

    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.LevelChunk;
}
