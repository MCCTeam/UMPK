using System.Buffers;
using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Pre-1.21.5 modern chunk codec (protocols 768/769, 1.21.2-1.21.4). Two wire differences from <see cref="ChunkCodecs.V1_21_5"/>: (a) the heightmap block is one network-NBT compound from string keys to long arrays instead of the 1.21.5+ VarInt-keyed packed map; (b) every paletted container's packed long array carries a VarInt length prefix, including single-value containers, whose empty array serialises as a lone VarInt 0. The palette strategies (blocks: single / linear 1-4 stored at 4 bits / hashmap 5-8 / global; biomes: single / linear 1-3 / global) are unchanged from 1.21.5. The 1.21.2 wire is byte-identical to 1.21.4's, so one codec covers both protocols.</summary>
public static partial class ChunkCodecs
{
    // This era uses an NBT heightmap compound and a VarInt length prefix on every packed long array;
    // single-value containers write a lone VarInt 0.
    internal static void EncodeNbtHeightmapStructure(ref PacketWriter w, NbtHeightmapChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        w.WriteNbt(body.Heightmaps, NbtWireFormat.JavaUnnamedRoot);

        WriteSectionBuffer(ref w, body.Sections, body.SectionPadding, ChunkSectionLayout.V1_21_2);
        w.WriteBytes(body.Tail);
    }
}
