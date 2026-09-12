using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Chunk framing with NBT heightmaps and length-prefixed palette containers.</summary>
public sealed class ChunkPaletteContainerCodecTests
{
    [Fact]
    public void PrefixedPaletteContainers_DecodeAndReencodeByteExactly()
    {
        byte[] frame = BuildSyntheticChunkFrame();
        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_20_2.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        Assert.Equal(4, packet.ChunkX);
        Assert.Equal(-2, packet.ChunkZ);

        var wire = Assert.IsType<Protocol764To767ChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        ChunkSectionWire section = Assert.Single(wire.Sections);
        Assert.Equal(100, section.NonEmptyBlockCount);
        Assert.Null(section.FluidCount);
        Assert.True(section.Blocks.IsSingleValue);
        Assert.Equal(9, section.Blocks.SingleValue);
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(wire));
        Assert.Equal(frame, CodecRoundTrip.Encode(ChunkCodecs.V1_20_2, packet));
    }

    private static byte[] BuildSyntheticChunkFrame()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteInt(4);
        writer.WriteInt(-2);

        var heightmaps = new NbtCompound();
        heightmaps.Put("MOTION_BLOCKING", new NbtLongArray([1L, 2L, 3L]));
        heightmaps.Put("WORLD_SURFACE", new NbtLongArray([4L]));
        writer.WriteNbt(heightmaps, NbtWireFormat.JavaUnnamedRoot);

        var section = new System.Buffers.ArrayBufferWriter<byte>();
        var sectionWriter = new PacketWriter(section);
        sectionWriter.WriteShort(100);
        sectionWriter.WriteByte(0);
        sectionWriter.WriteVarInt(9);
        sectionWriter.WriteVarInt(0);
        sectionWriter.WriteByte(0);
        sectionWriter.WriteVarInt(0);
        sectionWriter.WriteVarInt(0);

        writer.WriteVarInt(section.WrittenCount);
        writer.WriteBytes(section.WrittenSpan);
        writer.WriteVarInt(0);
        writer.WriteBytes([0x01, 0x02, 0x03]);
        return buffer.WrittenSpan.ToArray();
    }
}
