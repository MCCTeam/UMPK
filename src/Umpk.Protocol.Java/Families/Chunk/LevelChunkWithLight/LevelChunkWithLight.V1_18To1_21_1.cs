using System.Buffers;
using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The 1.20.2-1.21.1 (protocols 764-767) level-chunk-with-light codec. Two wire deltas against the 1.21.5 member: heightmaps are ONE network-NBT compound (name to long-array) instead of the VarInt-count typed map, and every paletted container's long array carries a VarInt length prefix; a single-value container still writes a zero-length prefix. Sections are otherwise the modern shape: nonEmpty short, block container, biome container, no fluid count (26.1+).</summary>
/// <remarks>The layout is identical in 1.20.4, 1.20.6, and 1.21.1. The shared two-path design: the production encode replays <see cref="ClientboundLevelChunkPacket.RawBody"/> verbatim, and the structural encoder re-serialises from the structural model for fidelity tests.</remarks>
public static partial class ChunkCodecs
{
    internal static void EncodeStructure(ref PacketWriter w, Protocol764To767ChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        w.WriteNbt(body.Heightmaps, body.HeightmapRootFormat);

        // Prefixed: the VarInt long-array length that the 1.21.5 form dropped (writeLongArray).
        WriteSectionBuffer(ref w, body.Sections, body.SectionPadding, ChunkSectionLayout.V1_20_2);
        w.WriteBytes(body.Tail);
    }
}
