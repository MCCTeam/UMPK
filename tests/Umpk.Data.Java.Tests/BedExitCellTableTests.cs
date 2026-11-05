using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BedExitCellTableTests
{
    [Fact]
    public void CellTablesRetainBedFacingAndSeparateGeometry()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(7);
        for (int index = 0; index < 6; index++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteVarInt(416);
        writer.WriteByte(3);
        writer.WriteVarInt(4);
        for (int flags = 0; flags < 4; flags++)
        {
            writer.WriteVarInt(flags * 2);
            writer.WriteVarInt(flags * 2);
            writer.WriteByte((byte)flags);
        }
        var geometry = (JavaEntityGeometry)JavaGameData.EntityGeometryData(404);
        JavaBlockInteractions source = JavaBlockInteractions.Create(buffer.WrittenSpan, geometry);
        Assert.True(source.HasData);
        Assert.True(source.TryGetBedFacing(416, out Direction facing));
        Assert.Equal(Direction.South, facing);
        Identifier villager = Identifier.Minecraft("villager");
        Assert.True(geometry.TryGetDimensions(villager, false, EntityPose.Standing, out EntityGeometryDimensions expected));
        Assert.True(source.TryGetDimensions(villager, false, EntityPose.Standing, out EntityGeometryDimensions actual));
        Assert.Equal(expected, actual);
        for (int flags = 0; flags < 4; flags++)
        {
            Assert.True(source.TryGetBedExitCell(flags * 2, out BedExitCellInfo info));
            Assert.Equal(new BedExitCellInfo((flags & 1) != 0, (flags & 2) != 0), info);
            Assert.False(source.TryGetBedExitCell(flags * 2 + 1, out _));
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("negative")]
    [InlineData("reversed")]
    [InlineData("overlap")]
    [InlineData("unordered")]
    [InlineData("uncompacted")]
    [InlineData("flags")]
    [InlineData("count")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    public void MalformedPackedCellRangesAreRejected(string defect)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(7);
        for (int index = 0; index < 7; index++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(defect == "empty" ? 0 : defect is "overlap" or "unordered" or "uncompacted" or "count" ? 2 : 1);
        if (defect != "empty")
        {
            writer.WriteVarInt(defect == "negative" ? -1 : 10);
            writer.WriteVarInt(defect == "reversed" ? 9 : 11);
            if (defect != "truncated")
                writer.WriteByte(defect == "flags" ? (byte)4 : (byte)2);
        }
        if (defect is "overlap" or "unordered" or "uncompacted")
        {
            writer.WriteVarInt(defect == "overlap" ? 11 : defect == "unordered" ? 8 : 12);
            writer.WriteVarInt(15);
            writer.WriteByte(2);
        }
        if (defect == "trailing")
            writer.WriteByte(0);
        Assert.Throws<ProtocolViolationException>(() => JavaBlockInteractions.Create(buffer.WrittenSpan));
    }

    [Fact]
    public void OlderTablesAndExistingProvidersRetainUnknownCells()
    {
        IBlockInteractionSource[] sources = [JavaBlockInteractions.Create([0]), JavaBlockInteractions.Create([6, 0, 0, 0, 0, 0, 0, 0]), new ExistingProvider()];
        foreach (IBlockInteractionSource source in sources)
        {
            Assert.False(source.HasData);
            Assert.False(source.TryGetBedExitCell(0, out _));
        }
    }

    [Theory]
    [InlineData(477)]
    [InlineData(777)]
    public void UnmeasuredVersionsRetainUnknownCells(int protocol) => Assert.False(JavaGameData.BlockInteractionData(protocol).TryGetBedExitCell(0, out _));

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void OriginalSupportAndMaterialFactsPreserveRejectedMetadata(int protocol)
    {
        string path = Path.Combine(FixturePaths.RepoRoot(), "data", "java", protocol.ToString(CultureInfo.InvariantCulture), "bed-exit-cells.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        int known = 0;
        foreach (JsonElement range in document.RootElement.GetProperty("ranges").EnumerateArray())
            for (int state = range[0].GetInt32(); state <= range[1].GetInt32(); state++)
            {
                Assert.True(source.TryGetBedExitCell(state, out BedExitCellInfo info));
                int flags = range[2].GetInt32();
                Assert.Equal(new BedExitCellInfo((flags & 1) != 0, (flags & 2) != 0), info);
                known++;
            }
        Assert.Equal(document.RootElement.GetProperty("state_count").GetInt32(), known);
        foreach (JsonElement state in document.RootElement.GetProperty("rejected_states").EnumerateArray())
            Assert.False(source.TryGetBedExitCell(state.GetInt32(), out _));
        Assert.False(source.TryGetBedExitCell(-1, out _));
        Assert.False(source.TryGetBedExitCell(int.MaxValue, out _));
    }

    private sealed class ExistingProvider : IBlockInteractionSource
    {
        public bool HasData => false;
        public bool TryGetBlock(Identifier block, out BlockInteractionInfo info)
        {
            info = default;
            return false;
        }
        public bool TryGetItem(Identifier item, out ItemUseOnInfo info)
        {
            info = default;
            return false;
        }
    }
}
