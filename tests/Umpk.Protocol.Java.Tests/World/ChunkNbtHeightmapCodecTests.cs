using System.Buffers;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Pre-1.21.5 chunk era (protocols 768/769, 1.21.2-1.21.4). Heightmaps use one network-NBT compound rather than the later packed map, and long arrays retain their VarInt length prefix. Hand-authored frames pin the decoder; the recorded-corpus leg lives in the conformance suite.</summary>
public sealed class ChunkNbtHeightmapCodecTests
{
    [Fact]
    public void PreChunk_IndirectSection_DecodesAndStructurallyReEncodes()
    {
        // One section: 4-bit indirect palette [air=0, stone=10, dirt=20] with the pre-1.21.5 VarInt long-array prefix; single-value biome (whose empty storage serialises as a lone VarInt 0).
        var packed = new long[256];
        SetCell(packed, Index(0, 0, 0), 1, 4); // stone
        SetCell(packed, Index(2, 0, 0), 2, 4); // dirt

        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(64);           // non-empty block count
        sw.WriteByte(4);             // block bits = 4 (indirect)
        sw.WriteVarInt(3);           // palette length
        sw.WriteVarInt(0);
        sw.WriteVarInt(10);
        sw.WriteVarInt(20);
        sw.WriteVarInt(packed.Length); // pre-1.21.5: VarInt long-array prefix
        foreach (long value in packed)
            sw.WriteLong(value);

        sw.WriteByte(0);             // biome single-value
        sw.WriteVarInt(7);           // biome id
        sw.WriteVarInt(0);           // pre-1.21.5: empty storage still writes VarInt 0

        byte[] frame = BuildPreFrame(x: 3, z: -9, heightmapLongs: [0x0102030405060708L], section.WrittenSpan, tail: [0x00, 0x0A, 0x0B]);

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_21_2.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        var wire = Assert.IsType<NbtHeightmapChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.Equal(3, wire.X);
        Assert.Equal(-9, wire.Z);
        ChunkSectionWire s = Assert.Single(wire.Sections);
        Assert.Equal(64, s.NonEmptyBlockCount);
        Assert.Null(s.FluidCount);
        Assert.True(s.Blocks.IsIndirect);
        Assert.Equal(new[] { 0, 10, 20 }, s.Blocks.Palette);
        Assert.Equal(256, s.Blocks.Data.Length);
        Assert.True(s.Biomes.IsSingleValue);
        Assert.Equal(7, s.Biomes.SingleValue);

        var heightmaps = Assert.IsType<NbtCompound>(wire.Heightmaps);
        var motionBlocking = Assert.IsType<NbtLongArray>(heightmaps["MOTION_BLOCKING"]);
        Assert.Equal([0x0102030405060708L], motionBlocking.Value);

        // Decoded block states through the gameplay column.
        Assert.Equal(10, packet.Column.GetSection(0)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(20, packet.Column.GetSection(0)!.GetBlockStateId(2, 0, 0));
        Assert.Equal(0, packet.Column.GetSection(0)!.GetBlockStateId(5, 5, 5));

        // Structural re-encode is byte-identical (RawBody bypassed), and the production relay encode (RawBody) is byte-identical too.
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        ChunkCodecs.V1_21_2.Encode(ref writer, packet, PacketCodecContext.Registryless);
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void PreChunk_SingleValueSection_RoundTrips()
    {
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(0);
        sw.WriteByte(0);             // block single-value
        sw.WriteVarInt(0);           // air
        sw.WriteVarInt(0);           // empty storage prefix
        sw.WriteByte(0);             // biome single-value
        sw.WriteVarInt(1);
        sw.WriteVarInt(0);

        byte[] frame = BuildPreFrame(x: 0, z: 0, heightmapLongs: [], section.WrittenSpan, tail: [0x00]);

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_21_2.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        var wire = Assert.IsType<NbtHeightmapChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        ChunkSectionWire s = Assert.Single(wire.Sections);
        Assert.True(s.Blocks.IsSingleValue);
        Assert.True(s.Biomes.IsSingleValue);
        Assert.Equal(1, s.Biomes.SingleValue);
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    // Packs a value into the fixed-width LSB-first bit array used on the wire.
    private static void SetCell(long[] data, int cellIndex, int value, int bits)
    {
        int perLong = 64 / bits;
        int longIndex = cellIndex / perLong;
        int offset = (cellIndex % perLong) * bits;
        data[longIndex] |= (long)value << offset;
    }

    private static int Index(int x, int y, int z) => ((y * 16) + z) * 16 + x;

    private static byte[] BuildPreFrame(int x, int z, long[] heightmapLongs, ReadOnlySpan<byte> section, byte[] tail)
    {
        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(x);
        w.WriteInt(z);

        // Pre-1.21.5 heightmaps: one network-NBT compound (unnamed root).
        var compound = new NbtCompound();
        if (heightmapLongs.Length > 0)
            compound.Put("MOTION_BLOCKING", new NbtLongArray(heightmapLongs));

        w.WriteNbt(compound, NbtWireFormat.JavaUnnamedRoot);

        w.WriteVarInt(section.Length);
        w.WriteBytes(section);
        w.WriteBytes(tail);
        return frame.WrittenSpan.ToArray();
    }
}
