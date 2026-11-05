using Umpk;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class CopperChestPropertyTests
{
    private static readonly string[] Names = ["copper_chest", "exposed_copper_chest", "weathered_copper_chest", "oxidized_copper_chest", "waxed_copper_chest", "waxed_exposed_copper_chest", "waxed_weathered_copper_chest", "waxed_oxidized_copper_chest"];
    private static readonly string[] Facing = ["north", "south", "west", "east"];
    private static readonly string[] Type = ["single", "left", "right"];
    private static readonly string[] Waterlogged = ["true", "false"];

    [Theory]
    [InlineData(773, 26893)]
    [InlineData(774, 26893)]
    [InlineData(775, 27095)]
    [InlineData(776, 29568)]
    [InlineData(777, 32925)]
    public void NativeCopperChestStatesRetainHorizontalFacingChestTypeAndWaterlogging(int protocol, int firstNativeState)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        for (int index = 0; index < Names.Length; index++)
        {
            Assert.True(blocks.TryGet(Identifier.Minecraft(Names[index]), out RegistryEntry<BlockDefinition> entry));
            BlockDefinition block = entry.Value;
            int first = firstNativeState + index * 24;
            Assert.Equal((first, first + 23, first + 1), (block.MinStateId, block.MaxStateId, block.DefaultStateId));
            Assert.Equal(["facing", "type", "waterlogged"], block.Properties.Select(property => property.Name));
            Assert.Equal(Facing, block.Properties[0].Values);
            Assert.Equal(Type, block.Properties[1].Values);
            Assert.Equal(Waterlogged, block.Properties[2].Values);
            for (int state = first; state <= first + 23; state++)
            {
                int offset = state - first;
                Assert.True(block.TryGetPropertyValue(state, "facing", out string facing));
                Assert.True(block.TryGetPropertyValue(state, "type", out string type));
                Assert.True(block.TryGetPropertyValue(state, "waterlogged", out string waterlogged));
                Assert.Equal((Facing[offset / 6], Type[offset / 2 % 3], Waterlogged[offset % 2]), (facing, type, waterlogged));
            }
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "facing", out string defaultFacing));
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "type", out string defaultType));
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "waterlogged", out string defaultWaterlogged));
            Assert.Equal(("north", "single", "false"), (defaultFacing, defaultType, defaultWaterlogged));
        }
    }
}
