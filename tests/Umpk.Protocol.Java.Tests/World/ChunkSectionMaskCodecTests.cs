using System.Buffers;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>The netty-modern (protocols 735-756, MC 1.16-1.17.1) level-chunk era codecs. Pre-1.18, section data is one opaque length-prefixed buffer, so the structural decode captures the header, the present-section mask, and the named-root heightmaps NBT, and preserves the rest verbatim; the structural re-encode must reproduce the frame byte-for-byte. These hand-authored frames pin the decoder (the 1.17 BitSet mask path especially, which only two protocols exercise); the recorded-corpus leg is in the conformance suite.</summary>
public sealed class ChunkSectionMaskCodecTests
{
    private static byte[] HeightmapsNbt()
    {
        var hm = new NbtCompound();
        hm.PutString("marker", "heightmaps");
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteNbt(hm, NbtWireFormat.JavaNamedRoot);
        return buffer.WrittenSpan.ToArray();
    }

    // Biomes + opaque section buffer + block entities, captured verbatim by the codec.
    private static readonly byte[] Tail = [0x01, 0x02, 0x03, 0x00, 0xFF, 0x10];

    [Fact]
    public void V1_16_VarIntMask_FullChunkFlags_StructurallyReEncodesByteIdentical()
    {
        byte[] nbt = HeightmapsNbt();
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteInt(4);
        w.WriteInt(-9);
        w.WriteBool(true);   // fullChunk
        w.WriteBool(false);  // forgetOldData (1.16/1.16.1 only)
        w.WriteVarInt(0b0000_0000_0000_1011); // present-section VarInt mask
        w.WriteBytes(nbt);
        w.WriteBytes(Tail);
        byte[] frame = buffer.WrittenSpan.ToArray();

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_16.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        Assert.Equal(4, packet.ChunkX);
        Assert.Equal(-9, packet.ChunkZ);
        Assert.NotNull(packet.RawBody);

        byte[] structural = ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet));
        Assert.Equal(frame, structural);
    }

    [Fact]
    public void V1_17_BitSetMask_StructurallyReEncodesByteIdentical()
    {
        // The 1.17-only vertical-strip path: the present-section mask is (a VarInt long-count followed by that many longs), NOT the 1.16 VarInt, and there is no fullChunk/forgetOldData flag. Captured raw so trailing-zero longs survive the round-trip.
        byte[] nbt = HeightmapsNbt();
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteInt(4);
        w.WriteInt(-9);
        w.WriteVarInt(2);             // BitSet long count
        w.WriteLong(0b1011L);         // low sections present
        w.WriteLong(0L);              // a trailing zero long that a canonical bitset form would trim
        w.WriteBytes(nbt);
        w.WriteBytes(Tail);
        byte[] frame = buffer.WrittenSpan.ToArray();

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_17.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        Assert.Equal(4, packet.ChunkX);
        Assert.NotNull(packet.RawBody);

        byte[] structural = ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet));
        // Byte-identical including the trailing zero long: proves the raw long-array capture, not a BitSet round-trip that would drop it.
        Assert.Equal(frame, structural);
    }
}
