using System.Buffers;
using System.Text.Json;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BedFacingTableTests
{
    [Fact]
    public void BedTablesPreserveTheSeparateEntityGeometry()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(6);
        for (int index = 0; index < 6; index++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteVarInt(416);
        writer.WriteByte(3);
        var geometry = (JavaEntityGeometry)JavaGameData.EntityGeometryData(404);
        JavaBlockInteractions source = JavaBlockInteractions.Create(buffer.WrittenSpan, geometry);
        Identifier villager = Identifier.Minecraft("villager");
        Assert.True(geometry.TryGetRule(villager, out EntityGeometryRule expectedRule));
        Assert.True(source.TryGetRule(villager, out EntityGeometryRule rule));
        Assert.Equal(expectedRule, rule);
        Assert.True(geometry.TryGetDimensions(villager, false, EntityPose.Standing, out EntityGeometryDimensions expected));
        Assert.True(source.TryGetDimensions(villager, false, EntityPose.Standing, out EntityGeometryDimensions actual));
        Assert.Equal(expected, actual);
        Assert.True(source.TryGetBedFacing(416, out Direction facing));
        Assert.Equal(Direction.South, facing);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("duplicate")]
    [InlineData("unordered")]
    [InlineData("down")]
    [InlineData("up")]
    [InlineData("unknown")]
    [InlineData("count")]
    [InlineData("trailing")]
    public void InvalidPackedBedStatesAreRejected(string defect)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(6);
        for (int index = 0; index < 6; index++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(defect is "duplicate" or "unordered" or "count" ? 2 : 1);
        writer.WriteVarInt(defect == "negative" ? -1 : 16);
        writer.WriteByte(defect switch { "down" => 0, "up" => 1, "unknown" => 6, _ => (byte)2 });
        if (defect is "duplicate" or "unordered")
        {
            writer.WriteVarInt(defect == "duplicate" ? 16 : 15);
            writer.WriteByte(2);
        }
        if (defect == "trailing")
            writer.WriteByte(0);
        Assert.Throws<ProtocolViolationException>(() => JavaBlockInteractions.Create(buffer.WrittenSpan));
    }

    [Fact]
    public void EmptyAndExistingProvidersKeepTheirUnknownDefault()
    {
        IBlockInteractionSource[] sources = [JavaBlockInteractions.Create([0]), JavaBlockInteractions.Create([6, 0, 0, 0, 0, 0, 0, 0]), new ExistingProvider()];
        foreach (IBlockInteractionSource source in sources)
        {
            Assert.False(source.HasData);
            Assert.False(source.TryGetBedFacing(416, out _));
        }
    }

    [Theory]
    [InlineData(477)]
    [InlineData(777)]
    public void UnmeasuredVersionsRemainUnknown(int protocol) => Assert.False(JavaGameData.BlockInteractionData(protocol).TryGetBedFacing(416, out _));

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
    public void EveryRawBedStateRetainsItsNativeFacing(int protocol)
    {
        string path = Path.Combine(FixturePaths.RepoRoot(), "data", "java", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "bed-facings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement states = document.RootElement.GetProperty("states");
        Assert.Equal(protocol >= 393 ? 256 : 16, states.GetArrayLength());
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        foreach (JsonElement state in states.EnumerateArray())
        {
            Assert.True(source.TryGetBedFacing(state[0].GetInt32(), out Direction facing));
            Assert.Equal((Direction)state[1].GetInt32(), facing);
        }
        Assert.False(source.TryGetBedFacing(0, out _));
        Assert.False(source.TryGetBedFacing(-1, out _));
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
