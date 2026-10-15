using System.Text.Json;
using Umpk.Game.Registries;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BlockMiningTableTests
{
    [Fact]
    public void On1_10StructureVoidHasNoCollision()
        => Assert.True(JavaGameData.BlockShapes(210).GetCollisionShapes(217 << 4).IsEmpty);

    [Theory]
    [InlineData(210, "magma", 213)]
    [InlineData(210, "nether_wart_block", 214)]
    [InlineData(210, "red_nether_brick", 215)]
    [InlineData(210, "bone_block", 216)]
    [InlineData(210, "structure_void", 217)]
    [InlineData(315, "silver_shulker_box", 227)]
    [InlineData(316, "silver_shulker_box", 227)]
    [InlineData(335, "silver_shulker_box", 227)]
    [InlineData(338, "silver_shulker_box", 227)]
    [InlineData(340, "silver_shulker_box", 227)]
    [InlineData(335, "silver_glazed_terracotta", 243)]
    [InlineData(338, "silver_glazed_terracotta", 243)]
    [InlineData(340, "silver_glazed_terracotta", 243)]
    public void LegacyBlockNamesAndIdsMatchTheNativeArtifact(int protocol, string name, int id)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;

        Assert.True(blocks.TryGetValue(Identifier.Minecraft(name), out BlockDefinition? block));
        Assert.Equal(id << 4, block.DefaultStateId);
    }

    [Theory]
    [InlineData("totem_of_undying", 449)]
    [InlineData("iron_nugget", 452)]
    public void On1_11_1ItemNamesAndIdsMatchTheNativeArtifact(string name, int id)
    {
        Registry<ItemDefinition> items = JavaGameData.Registries(316).Items;

        Assert.True(items.TryGet(Identifier.Minecraft(name), out RegistryEntry<ItemDefinition> item));
        Assert.Equal(id << 16, item.NetworkId);
    }

    [Fact]
    public void EveryGeneratedMiningRowMatchesItsArtifactDerivedDataset()
    {
        string data = Path.Combine(FixturePaths.RepoRoot(), "data", "java");
        foreach (string file in Directory.EnumerateFiles(data, "block-mining.json", SearchOption.AllDirectories))
        {
            int protocol = int.Parse(Directory.GetParent(file)!.Name, System.Globalization.CultureInfo.InvariantCulture);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            IBlockMiningSource source = JavaGameData.BlockMiningData(protocol);
            foreach (JsonProperty block in document.RootElement.GetProperty("blocks").EnumerateObject())
            {
                Assert.True(source.TryGetBlock(Identifier.Parse(block.Name), out BlockMiningInfo actual), $"Missing block {block.Name} on {protocol}");
                Assert.Equal(new BlockMiningInfo(block.Value[0].GetSingle(), block.Value[1].GetBoolean()), actual);
            }
            foreach (JsonProperty tool in document.RootElement.GetProperty("tools").EnumerateObject())
                foreach (JsonProperty block in tool.Value.EnumerateObject())
                {
                    Assert.True(source.TryGetTool(Identifier.Parse(tool.Name), Identifier.Parse(block.Name), out ToolMiningInfo actual), $"Missing tool {tool.Name} against {block.Name} on {protocol}");
                    Assert.Equal(new ToolMiningInfo(block.Value[0].GetSingle(), block.Value[1].GetBoolean()), actual);
                }
        }
    }

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
    [InlineData(477)]
    [InlineData(480)]
    [InlineData(485)]
    [InlineData(490)]
    [InlineData(498)]
    [InlineData(573)]
    [InlineData(575)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(736)]
    [InlineData(751)]
    [InlineData(753)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(756)]
    [InlineData(757)]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public void StoneUsesMeasuredHardnessAndDefaultToolSpeeds(int protocol)
    {
        IBlockMiningSource source = JavaGameData.BlockMiningData(protocol);
        Identifier stone = Identifier.Minecraft("stone");

        Assert.True(source.HasData);
        Assert.True(source.TryGetBlock(stone, out BlockMiningInfo block));
        Assert.Equal(1.5F, block.Hardness);
        Assert.True(block.RequiresCorrectTool);
        Assert.True(source.TryGetTool(Identifier.Minecraft("wooden_pickaxe"), stone, out ToolMiningInfo wood));
        Assert.Equal(new ToolMiningInfo(2.0F, true), wood);
        Assert.True(source.TryGetTool(Identifier.Minecraft("diamond_pickaxe"), stone, out ToolMiningInfo diamond));
        Assert.Equal(new ToolMiningInfo(8.0F, true), diamond);
        Assert.True(source.TryGetTool(Identifier.Minecraft("stick"), stone, out ToolMiningInfo stick));
        Assert.Equal(new ToolMiningInfo(1.0F, false), stick);
    }

    [Fact]
    public void MissingMeasurementRefusesBothBlockAndToolQueries()
    {
        IBlockMiningSource source = JavaBlockMining.Create([0]);

        Assert.False(source.HasData);
        Assert.False(source.TryGetBlock(Identifier.Minecraft("stone"), out _));
        Assert.False(source.TryGetTool(Identifier.Minecraft("diamond_pickaxe"), Identifier.Minecraft("stone"), out _));
    }

    [Fact]
    public void UnknownNamesRefuseRatherThanReceivingDefaultToolRules()
    {
        IBlockMiningSource source = JavaGameData.BlockMiningData(777);

        Assert.False(source.TryGetBlock(Identifier.Minecraft("unknown"), out _));
        Assert.False(source.TryGetTool(Identifier.Minecraft("unknown"), Identifier.Minecraft("stone"), out _));
        Assert.False(source.TryGetTool(Identifier.Minecraft("stick"), Identifier.Minecraft("unknown"), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => JavaGameData.BlockMiningData(-1));
    }

    [Fact]
    public void UnbreakableAndInstantBlocksRetainTheirMeasuredHardness()
    {
        IBlockMiningSource source = JavaGameData.BlockMiningData(777);

        Assert.True(source.TryGetBlock(Identifier.Minecraft("bedrock"), out BlockMiningInfo bedrock));
        Assert.Equal(-1.0F, bedrock.Hardness);
        Assert.True(source.TryGetBlock(Identifier.Minecraft("torch"), out BlockMiningInfo torch));
        Assert.Equal(0.0F, torch.Hardness);
    }
}
