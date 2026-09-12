using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Structural chunk fidelity, hand-authored leg. The recorded-corpus leg that re-encodes every real chunk frame through <see cref="ChunkCodecs.EncodeStructural"/> and byte-compares against the recording lives in the conformance suite (it needs descriptor wire-id resolution to select only level_chunk frames); see <c>ChunkStructuralConformanceTests</c>. Here we pin the decoder against hand-authored frames with known palette entries and block states, and prove the structural encoder depends on the decoded palette, so the class of decode corruption a mutation probe demonstrated here is caught either by a content assertion or by the byte comparison.</summary>
public sealed class ChunkStructuralFidelityTests
{
    [Fact]
    public void ModernIndirectSection_DecodesExactPalette_AndStructurallyReEncodes()
    {
        // One section: 4-bit indirect palette [air=0, stone=10, dirt=20]. Cell (0,0,0)=stone, cell (1,0,0)=dirt, cell (5,5,5)=air. Single-value biome 0. A hand-authored heightmap and a small light tail exercise the structural heightmap and verbatim-tail paths.
        var packed = new long[256];
        SetCell(packed, ChunkSectionIndex(0, 0, 0), 1, 4); // stone
        SetCell(packed, ChunkSectionIndex(1, 0, 0), 2, 4); // dirt

        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(64);          // non-empty block count (arbitrary)
        sw.WriteByte(4);            // block bits = 4 (indirect)
        sw.WriteVarInt(3);          // palette length
        sw.WriteVarInt(0);          // air
        sw.WriteVarInt(10);         // stone
        sw.WriteVarInt(20);         // dirt
        foreach (long value in packed)
            sw.WriteLong(value);

        sw.WriteByte(0);            // biome single-value
        sw.WriteVarInt(0);

        byte[] frame = BuildModernFrame(x: 7, z: -4, heightmap: [0x0102030405060708L, unchecked((long)0xF0E0D0C0B0A09080L)], section.WrittenSpan, tail: [0x00, 0x0A, 0x0B]);

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        // Structural content assertions on the decoded wire model.
        var wire = Assert.IsType<ModernChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.Equal(7, wire.X);
        Assert.Equal(-4, wire.Z);
        Assert.Single(wire.Sections);
        ChunkPalettedContainerWire blocks = wire.Sections[0].Blocks;
        Assert.True(blocks.IsIndirect);
        Assert.Equal(new[] { 0, 10, 20 }, blocks.Palette);
        Assert.Equal(64, wire.Sections[0].NonEmptyBlockCount);
        Assert.Null(wire.Sections[0].FluidCount); // 770 has no per-section fluid count
        Assert.Single(wire.Heightmaps);
        Assert.Equal(0, wire.Heightmaps[0].TypeId);
        Assert.Equal(2, wire.Heightmaps[0].Data.Length);

        // Decoded block states through the gameplay column.
        Assert.Equal(10, packet.Column.GetSection(0)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(20, packet.Column.GetSection(0)!.GetBlockStateId(1, 0, 0));
        Assert.Equal(0, packet.Column.GetSection(0)!.GetBlockStateId(5, 5, 5));

        // Structural re-encode is byte-identical (RawBody bypassed).
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    [Fact]
    public void ModernSingleValueSection_DecodesAndStructurallyReEncodes()
    {
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(4096);        // non-empty block count
        sw.WriteByte(0);            // block bits = 0 (single value)
        sw.WriteVarInt(10);         // stone everywhere
        sw.WriteByte(0);            // biome single value
        sw.WriteVarInt(0);

        byte[] frame = BuildModernFrame(x: 0, z: 0, heightmap: [], section.WrittenSpan, tail: [0x00]);

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        var wire = Assert.IsType<ModernChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.True(wire.Sections[0].Blocks.IsSingleValue);
        Assert.Equal(10, wire.Sections[0].Blocks.SingleValue);
        Assert.Equal(10, packet.Column.GetSection(0)!.GetBlockStateId(9, 9, 9));
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    [Fact]
    public void Modern776Section_ReadsFluidCount_AndStructurallyReEncodes()
    {
        // 776 sections carry a second (fluid count) short after the non-empty block count.
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(4096);        // non-empty block count
        sw.WriteShort(17);          // fluid count (26.1+)
        sw.WriteByte(0);            // block single value
        sw.WriteVarInt(1);
        sw.WriteByte(0);            // biome single value
        sw.WriteVarInt(0);

        byte[] frame = BuildModernFrame(x: 1, z: 2, heightmap: [], section.WrittenSpan, tail: [0x00]);

        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V26_1.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        var wire = Assert.IsType<ModernChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.True(wire.HasFluidCount);
        Assert.Equal((short)17, wire.Sections[0].FluidCount);
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    [Fact]
    public void LegacySection_DecodesBlockStates_AndStructurallyReEncodes()
    {
        // 1.8 flat-ushort: one present section (bit 0) with cell (0,0,0)=stone(1), cell(2,0,0)=dirt(3), then a short light/biome remainder.
        var data = new ArrayBufferWriter<byte>();
        var dw = new PacketWriter(data);
        for (int i = 0; i < 4096; i++)
        {
            ushort v = i == ChunkSectionIndex(0, 0, 0) ? (ushort)1 : i == ChunkSectionIndex(2, 0, 0) ? (ushort)3 : (ushort)0;
            dw.WriteByte((byte)(v & 0xFF));
            dw.WriteByte((byte)(v >> 8));
        }

        dw.WriteBytes([0xAA, 0xBB, 0xCC]); // light/biome remainder

        var frame = new ArrayBufferWriter<byte>();
        var fw = new PacketWriter(frame);
        fw.WriteInt(3);
        fw.WriteInt(9);
        fw.WriteBool(true);      // groundUp
        fw.WriteUShort(0x0001);  // primary mask: section 0 present
        fw.WriteVarInt(data.WrittenCount);
        fw.WriteBytes(data.WrittenSpan);

        byte[] frameBytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(frameBytes);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_8.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        var wire = Assert.IsType<LegacyChunkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.Single(wire.SectionBlockStates);
        Assert.Equal(1, wire.SectionBlockStates[0][ChunkSectionIndex(0, 0, 0)]);
        Assert.Equal(3, wire.SectionBlockStates[0][ChunkSectionIndex(2, 0, 0)]);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, wire.Remainder);
        Assert.Equal(1, packet.Column.GetSection(0)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(frameBytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    /// <summary>A 1.8 column whose primary mask spans multiple sections must materialize every present section.</summary>
    [Fact]
    public void LegacyColumn_MultipleSections_AllMaterialize()
    {
        // Sections 0 and 4 present (mask 0x0011): terrain shape of a normal generated world.
        var data = new ArrayBufferWriter<byte>();
        var dw = new PacketWriter(data);
        for (int s = 0; s < 2; s++)
            for (int i = 0; i < 4096; i++)
            {
                ushort v = i == ChunkSectionIndex(0, 0, 0) ? (ushort)(16 + s) : (ushort)0;
                dw.WriteByte((byte)(v & 0xFF));
                dw.WriteByte((byte)(v >> 8));
            }

        var frame = new ArrayBufferWriter<byte>();
        var fw = new PacketWriter(frame);
        fw.WriteInt(-2);
        fw.WriteInt(7);
        fw.WriteBool(true);      // groundUp
        fw.WriteUShort(0x0011);  // primary mask: sections 0 and 4
        fw.WriteVarInt(data.WrittenCount);
        fw.WriteBytes(data.WrittenSpan);

        byte[] frameBytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(frameBytes);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_8.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);

        Assert.Equal(16, packet.Column.GetSection(0)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(17, packet.Column.GetSection(4)!.GetBlockStateId(0, 0, 0));
        Assert.Equal(frameBytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
    }

    [Fact]
    public void StructuralEncoder_DependsOnDecodedPalette()
    {
        var section = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(section);
        sw.WriteShort(64);
        sw.WriteByte(4);
        sw.WriteVarInt(2);
        sw.WriteVarInt(0);
        sw.WriteVarInt(10);
        for (int i = 0; i < 256; i++)
            sw.WriteLong(0);

        sw.WriteByte(0);
        sw.WriteVarInt(0);

        byte[] frame = BuildModernFrame(x: 0, z: 0, heightmap: [], section.WrittenSpan, tail: [0x00]);
        var reader = new PacketReader(frame);
        ClientboundLevelChunkPacket packet = ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);
        var wire = (ModernChunkWireBody)ChunkCodecs.DecodeStructure(packet);

        // A faithful re-encode matches; a palette perturbation (the class of bug a mutation probe demonstrated here) must change the bytes, proving the byte comparison has teeth.
        Assert.Equal(frame, ChunkCodecs.EncodeStructural(wire));

        ChunkPalettedContainerWire blocks = wire.Sections[0].Blocks;
        var corruptedBlocks = blocks with { Palette = [blocks.Palette![0] ^ 0x7FFF, blocks.Palette[1]] };
        var corruptedSection = wire.Sections[0] with { Blocks = corruptedBlocks };
        var corrupted = wire with { Sections = new[] { corruptedSection } };
        Assert.NotEqual(frame, ChunkCodecs.EncodeStructural(corrupted));
    }

    // Section cell index in YZX order (matches ChunkSection.BlockIndex).
    private static int ChunkSectionIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

    // Packs a value into the fixed-width LSB-first bit array used on the wire.
    private static void SetCell(long[] data, int cellIndex, int value, int bits)
    {
        int perLong = 64 / bits;
        int longIndex = cellIndex / perLong;
        int offset = (cellIndex % perLong) * bits;
        data[longIndex] |= (long)value << offset;
    }

    private static byte[] BuildModernFrame(int x, int z, long[] heightmap, ReadOnlySpan<byte> section, byte[] tail)
    {
        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(x);
        w.WriteInt(z);
        if (heightmap.Length == 0)
            w.WriteVarInt(0);

        else
        {
            w.WriteVarInt(1);          // one heightmap
            w.WriteVarInt(0);          // heightmap type id
            w.WriteVarInt(heightmap.Length);
            foreach (long value in heightmap)
                w.WriteLong(value);

        }

        w.WriteVarInt(section.Length);
        w.WriteBytes(section);
        w.WriteBytes(tail);            // block entities + light, captured verbatim by decode
        return frame.WrittenSpan.ToArray();
    }
}
