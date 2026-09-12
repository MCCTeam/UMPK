using System.Buffers;
using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The 1.16-1.17.1 (protocols 735-756) level-chunk codecs. These versions predate the 1.18 full-column format: the client receives a present-section mask plus one opaque length-prefixed section <c>buffer</c> (the paletted sections are serialised into it separately), the heightmaps as a named-root NBT compound, biomes as a fixed int array (1.16.0/1.16.1) or a VarInt array (1.16.2+), and a block-entity NBT list. The packet-level wire therefore has no inline palettes to decode; the structural model (<see cref="BitmaskChunkWireBody"/>) captures the header, the mask, and the heightmaps NBT, and preserves biomes + section buffer + block entities as a verbatim tail, which re-encodes byte-for-byte.</summary>
/// <remarks>Deltas: 1.16/1.16.1 carry a <c>forgetOldData</c> bool and read biomes via <c>ChunkBiomeContainer</c>; 1.16.2-1.16.5 drop <c>forgetOldData</c> and send biomes as a VarInt array; 1.17/1.17.1 replace the VarInt mask with a BitSet long array, drop <c>fullChunk</c>, and always send the biome VarInt array. The 1.18 form (full column, no mask) is <see cref="V1_20_2"/>. Follows the shared two-path design: the production encode replays <see cref="ClientboundLevelChunkPacket.RawBody"/> verbatim; the structural encoder re-serialises from the model for the fidelity tests.</remarks>
public static partial class ChunkCodecs
{
    internal static void EncodeBitmaskStructure(ref PacketWriter w, BitmaskChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        if (body.FullChunk is { } fullChunk)
            w.WriteBool(fullChunk);

        if (body.ForgetOldData is { } forgetOldData)
            w.WriteBool(forgetOldData);

        if (body.MaskBitset is { } bitset)
        {
            w.WriteVarInt(bitset.Length);
            foreach (long value in bitset)
                w.WriteLong(value);

        }
        else
            w.WriteVarInt(body.MaskVarInt ?? 0);

        w.WriteNbt(body.Heightmaps, NbtWireFormat.JavaNamedRoot);
        w.WriteBytes(body.Tail);
    }
}
