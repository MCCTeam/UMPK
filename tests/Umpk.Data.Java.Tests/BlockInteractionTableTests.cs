using System.Buffers;
using System.Text.Json;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BlockInteractionTableTests
{
    [Theory]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void LegacyItemDropsHaveTheNativeNonblockingContract(int protocol)
    {
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        Assert.True(source.IsAlwaysNonObstructingEntity(Identifier.Minecraft("item")));
        Assert.False(source.IsAlwaysNonObstructingEntity(Identifier.Minecraft("villager")));
        Assert.False(source.TryGetFixedEntityDimensions(Identifier.Minecraft("item"), out _, out _));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("nonblocking")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("nan")]
    public void InvalidPackedFixedDimensionsAreRejected(string defect)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(3);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteString("minecraft:fixed");
        writer.WriteByte(defect == "nonblocking" ? (byte)1 : (byte)0);
        writer.WriteVarInt(1);
        writer.WriteString(defect == "unknown" ? "minecraft:missing" : "minecraft:fixed");
        writer.WriteFloat(defect == "zero" ? 0 : defect == "nan" ? float.NaN : 2);
        writer.WriteFloat(defect == "negative" ? -1 : 2);

        Assert.Throws<ProtocolViolationException>(() => JavaBlockInteractions.Create(buffer.WrittenSpan));
    }

    [Theory]
    [InlineData(498)]
    [InlineData(754)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public void FixedEntityDimensionsComeFromTheNativeMeasurement(int protocol)
    {
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        Assert.True(source.TryGetFixedEntityDimensions(Identifier.Minecraft("end_crystal"), out float width, out float height));
        Assert.Equal(2.0F, width);
        Assert.Equal(2.0F, height);
        Assert.False(source.IsAlwaysNonObstructingEntity(Identifier.Minecraft("end_crystal")));
        Assert.False(source.TryGetFixedEntityDimensions(Identifier.Minecraft("cow"), out _, out _));
        Assert.False(source.TryGetFixedEntityDimensions(Identifier.Minecraft("armor_stand"), out _, out _));
        Assert.False(source.TryGetFixedEntityDimensions(Identifier.Minecraft("missing"), out _, out _));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(480)]
    [InlineData(485)]
    [InlineData(490)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(754)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public void NativeInheritanceContractsDistinguishPlacementFromUnknownUse(int protocol)
    {
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(protocol);

        Assert.True(data.HasData);
        Assert.True(data.TryGetBlock(Identifier.Minecraft("stone"), out BlockInteractionInfo stone));
        Assert.True(stone.PassiveUse);
        Assert.True(stone.NeverReplaceable);
        Assert.True(data.TryGetBlock(Identifier.Minecraft("chest"), out BlockInteractionInfo chest));
        Assert.False(chest.PassiveUse);
        Assert.True(data.TryGetItem(Identifier.Minecraft("stone"), out ItemUseOnInfo placement));
        Assert.Equal(ItemUseOnKind.SimpleCubePlacement, placement.Kind);
        Assert.Equal(Identifier.Minecraft("stone"), placement.PlacedBlock);
        Assert.True(data.TryGetItem(Identifier.Minecraft("stone_slab"), out ItemUseOnInfo slab));
        Assert.Equal(ItemUseOnKind.Unknown, slab.Kind);
        Assert.True(data.TryGetItem(Identifier.Minecraft("stick"), out ItemUseOnInfo stick));
        Assert.Equal(ItemUseOnKind.Pass, stick.Kind);
        Assert.Null(stick.PlacedBlock);
        Assert.False(data.TryGetBlock(Identifier.Minecraft("missing"), out _));
        Assert.False(data.TryGetItem(Identifier.Minecraft("missing"), out _));
    }

    [Fact]
    public void MissingMeasurementRemainsUnknown()
    {
        IBlockInteractionSource data = JavaBlockInteractions.Create([0]);

        Assert.False(data.HasData);
        Assert.False(data.TryGetBlock(Identifier.Minecraft("stone"), out _));
        Assert.False(data.TryGetItem(Identifier.Minecraft("stone"), out _));
        Assert.False(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("item")));
    }

    [Theory]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(754)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public void NativeEntityFlagsDoNotPromoteDynamicObstructionToNonblocking(int protocol)
    {
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(protocol);

        Assert.True(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("item")));
        Assert.True(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("experience_orb")));
        Assert.True(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("arrow")));
        Assert.False(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("armor_stand")));
        Assert.False(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("falling_block")));
        Assert.False(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("zombie")));
        Assert.False(data.IsAlwaysNonObstructingEntity(Identifier.Minecraft("missing")));
    }

    [Fact]
    public void EveryGeneratedEntityObstructionRuleMatchesItsNativeDataset()
    {
        string data = Path.Combine(FixturePaths.RepoRoot(), "data", "java");
        foreach (string file in Directory.EnumerateFiles(data, "entity-obstruction.json", SearchOption.AllDirectories))
        {
            int protocol = int.Parse(Directory.GetParent(file)!.Name, System.Globalization.CultureInfo.InvariantCulture);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
            foreach (JsonProperty entity in document.RootElement.GetProperty("entities").EnumerateObject())
                Assert.Equal(entity.Value.GetInt32() == 1, source.IsAlwaysNonObstructingEntity(Identifier.Parse(entity.Name)));
            if (document.RootElement.TryGetProperty("fixed_boxes", out JsonElement boxes))
                foreach (JsonProperty box in boxes.EnumerateObject())
                {
                    Assert.True(source.TryGetFixedEntityDimensions(Identifier.Parse(box.Name), out float width, out float height));
                    Assert.Equal(box.Value[0].GetSingle(), width);
                    Assert.Equal(box.Value[1].GetSingle(), height);
                }
        }
    }

    [Fact]
    public void EveryGeneratedInteractionContractMatchesItsNativeDataset()
    {
        string data = Path.Combine(FixturePaths.RepoRoot(), "data", "java");
        foreach (string file in Directory.EnumerateFiles(data, "block-interactions.json", SearchOption.AllDirectories))
        {
            int protocol = int.Parse(Directory.GetParent(file)!.Name, System.Globalization.CultureInfo.InvariantCulture);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
            foreach (JsonProperty block in document.RootElement.GetProperty("blocks").EnumerateObject())
            {
                Assert.True(source.TryGetBlock(Identifier.Parse(block.Name), out BlockInteractionInfo actual));
                int flags = block.Value.GetInt32();
                Assert.Equal(new BlockInteractionInfo((flags & 1) != 0, (flags & 2) != 0), actual);
            }
            foreach (JsonProperty item in document.RootElement.GetProperty("items").EnumerateObject())
            {
                Assert.True(source.TryGetItem(Identifier.Parse(item.Name), out ItemUseOnInfo actual));
                ItemUseOnKind kind = item.Value[0].GetString() switch { "pass" => ItemUseOnKind.Pass, "simple_cube" => ItemUseOnKind.SimpleCubePlacement, _ => ItemUseOnKind.Unknown };
                string block = item.Value[1].GetString()!;
                Assert.Equal(new ItemUseOnInfo(kind, block.Length == 0 ? null : Identifier.Parse(block)), actual);
            }
        }
    }
}
