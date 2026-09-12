using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>Decodes a hand-built modern chunk payload into sections via the <c>Umpk.Game</c> install APIs.</summary>
public class ChunkCodecTests
{
    [Fact]
    public void ModernChunk_SingleValueSections_DecodeIntoColumn()
    {
        // Build one chunk-with-light payload: x, z, empty heightmap map, a section buffer with two single-value sections (blocks=stone id 10, biomes=plains id 0), no block entities, empty light.
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        for (int i = 0; i < 2; i++)
        {
            sw.WriteShort(4096);        // non-empty block count
            sw.WriteByte(0);            // block bits per entry = 0 (single value)
            sw.WriteVarInt(10);         // block state id
            // No data longs: a single-value (ZeroBitStorage) container serializes zero longs and, per FriendlyByteBuf.writeFixedSizeLongArray, writes no length prefix either.
            sw.WriteByte(0);            // biome bits per entry = 0 (single value)
            sw.WriteVarInt(0);          // biome id
        }

        var payload = new ArrayBufferWriter<byte>();
        var pw = new PacketWriter(payload);
        pw.WriteInt(5);                 // chunk x
        pw.WriteInt(-3);                // chunk z
        pw.WriteVarInt(0);              // heightmap map: 0 entries
        pw.WriteVarInt(section.WrittenCount);
        pw.WriteBytes(section.WrittenSpan);
        pw.WriteVarInt(0);              // block entities: 0
        // Light data block: empty bitsets + empty arrays. The codec drains the remainder, so leave it empty (a real server sends more, but the codec is remainder-tolerant for the light block).

        var reader = new PacketReader(payload.WrittenSpan);
        ClientboundLevelChunkPacket decoded = ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);

        Assert.Equal(5, decoded.ChunkX);
        Assert.Equal(-3, decoded.ChunkZ);
        Assert.Equal(2, decoded.Column.SectionCount);
        Assert.Equal(10, decoded.Column.GetSection(0)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(10, decoded.Column.GetSection(1)!.GetBlockStateId(5, 5, 5));
    }

    [Fact]
    public void ModernChunk_IndirectPalette_Decodes()
    {
        // One section with a 4-bit indirect palette [air, stone(10)] and all-zero packed data -> air.
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(0);           // non-empty block count
        sw.WriteByte(4);            // block bits per entry = 4 (indirect)
        sw.WriteVarInt(2);          // palette length
        sw.WriteVarInt(0);          // palette[0] = air
        sw.WriteVarInt(10);         // palette[1] = stone
        // 4096 cells at 4 bits, 16 per long -> 256 longs, all zero (index 0 = air). Fixed-size long array on the wire: no VarInt length prefix (FriendlyByteBuf.writeFixedSizeLongArray).
        for (int i = 0; i < 256; i++)
        {
            sw.WriteLong(0);
        }

        sw.WriteByte(0);            // biome single value
        sw.WriteVarInt(0);

        var payload = new ArrayBufferWriter<byte>();
        var pw = new PacketWriter(payload);
        pw.WriteInt(0);
        pw.WriteInt(0);
        pw.WriteVarInt(0);
        pw.WriteVarInt(section.WrittenCount);
        pw.WriteBytes(section.WrittenSpan);
        pw.WriteVarInt(0);

        var reader = new PacketReader(payload.WrittenSpan);
        ClientboundLevelChunkPacket decoded = ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(1, decoded.Column.SectionCount);
        Assert.Equal(0, decoded.Column.GetSection(0)!.GetBlockStateId(1, 1, 1));
    }
}
