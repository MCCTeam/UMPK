using System.Buffers;
using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The pre-1.18 (1.14-1.15.2, protocols 477-578) level-chunk codec. Wire: int x, int z, bool fullChunk, VarInt availableSections (bitmask), named-root NBT heightmaps, VarInt buffer length + buffer, VarInt block-entity count + NBT block entities. The buffer holds one section per set bit of the bitmask (short non-empty count, then a block <see cref="ChunkPalettedContainerWire"/> - bits byte, indirect palette when bits &lt;= 8, VarInt-prefixed long array; no per-section biome container) followed, on a full chunk, by the chunk-level biome array (int[256] on 1.14, int[1024] on 1.15) which rides verbatim after the sections. Light is a separate packet from 1.14, so none travels here. Follows the two-path design: production encode replays <see cref="ClientboundLevelChunkPacket.RawBody"/>; the structural encoder rebuilds from the <see cref="Pre1_18ChunkWireBody"/> for the fidelity leg.</summary>
public static partial class ChunkCodecs
{
    internal static void EncodePre118Structure(ref PacketWriter w, Pre1_18ChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        w.WriteBool(body.FullChunk);
        w.WriteVarInt(body.AvailableSections);
        w.WriteNbt(body.Heightmaps, NbtWireFormat.JavaNamedRoot);

        // 1.15 biomes (separate field before the buffer); empty on 1.14.
        w.WriteBytes(body.SeparateBiomes);

        WriteSectionBuffer(ref w, body.Sections, body.BiomesAndPadding, ChunkSectionLayout.V1_14);
        w.WriteBytes(body.Tail);
    }
}
