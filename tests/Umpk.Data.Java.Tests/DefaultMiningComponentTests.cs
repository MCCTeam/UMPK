using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class DefaultMiningComponentTests
{
    [Theory]
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
    public void DefaultDiamondPickaxeKeepsNativeOrderedTagRules(int protocol)
    {
        IBlockMiningSource source = JavaGameData.BlockMiningData(protocol);

        Assert.True(source.TryGetDefaultTool(Identifier.Minecraft("diamond_pickaxe"), out ToolComponent? tool));
        Assert.NotNull(tool);
        Assert.Equal(1, tool.DefaultMiningSpeed);
        Assert.Equal(1, tool.DamagePerBlock);
        Assert.True(tool.CanDestroyBlocksInCreative);
        Assert.Equal(2, tool.Rules.Count);
        Assert.Equal("minecraft:incorrect_for_diamond_tool", Assert.IsType<NbtString>(tool.Rules[0].GetList("blocks")![0]).Value);
        Assert.False(tool.Rules[0].GetBool("correct_for_drops"));
        Assert.Equal("minecraft:mineable/pickaxe", Assert.IsType<NbtString>(tool.Rules[1].GetList("blocks")![0]).Value);
        Assert.Equal(8, tool.Rules[1].GetFloat("speed"));
        Assert.True(tool.Rules[1].GetBool("correct_for_drops"));
        ItemStack stack = new(JavaGameData.Registries(protocol).Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        Assert.Equal(tool, stack.Components.Get(DataComponents.Tool));
        Assert.Empty(stack.Components.Patch);
        ItemStack stripped = stack.WithComponents(stack.Components.Without(DataComponents.Tool));
        Assert.False(stripped.Components.Has(DataComponents.Tool));
        Assert.True(Assert.Single(stripped.Components.Patch).IsRemoval);
        Assert.True(source.TryGetDefaultTool(Identifier.Minecraft("stone"), out ToolComponent? absent));
        Assert.Null(absent);
        Assert.False(source.TryGetDefaultTool(Identifier.Minecraft("missing"), out _));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(765)]
    public void PreComponentVersionsDoNotInventToolComponents(int protocol)
        => Assert.False(JavaGameData.BlockMiningData(protocol).TryGetDefaultTool(Identifier.Minecraft("diamond_pickaxe"), out _));
}
