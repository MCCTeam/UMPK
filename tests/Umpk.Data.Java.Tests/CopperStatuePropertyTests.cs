using Umpk;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class CopperStatuePropertyTests
{
    private static readonly string[] Names = ["copper_golem_statue", "exposed_copper_golem_statue", "weathered_copper_golem_statue", "oxidized_copper_golem_statue", "waxed_copper_golem_statue", "waxed_exposed_copper_golem_statue", "waxed_weathered_copper_golem_statue", "waxed_oxidized_copper_golem_statue"];
    private static readonly string[] Pose = ["standing", "sitting", "running", "star"];
    private static readonly string[] Facing = ["north", "south", "west", "east"];
    private static readonly string[] Waterlogged = ["true", "false"];

    [Theory]
    [InlineData(773, 27085)]
    [InlineData(774, 27085)]
    [InlineData(775, 27287)]
    [InlineData(776, 29760)]
    [InlineData(777, 33117)]
    public void NativeCopperStatueStatesRetainPoseFacingAndWaterlogging(int protocol, int firstNativeState)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        for (int index = 0; index < Names.Length; index++)
        {
            Assert.True(blocks.TryGet(Identifier.Minecraft(Names[index]), out RegistryEntry<BlockDefinition> entry));
            BlockDefinition block = entry.Value;
            int first = firstNativeState + index * 32;
            Assert.Equal((first, first + 31, first + 1), (block.MinStateId, block.MaxStateId, block.DefaultStateId));
            Assert.Equal(["copper_golem_pose", "facing", "waterlogged"], block.Properties.Select(property => property.Name));
            Assert.Equal(Pose, block.Properties[0].Values);
            Assert.Equal(Facing, block.Properties[1].Values);
            Assert.Equal(Waterlogged, block.Properties[2].Values);
            for (int state = first; state <= first + 31; state++)
            {
                int offset = state - first;
                Assert.True(block.TryGetPropertyValue(state, "copper_golem_pose", out string pose));
                Assert.True(block.TryGetPropertyValue(state, "facing", out string facing));
                Assert.True(block.TryGetPropertyValue(state, "waterlogged", out string waterlogged));
                Assert.Equal((Pose[offset / 8], Facing[offset / 2 % 4], Waterlogged[offset % 2]), (pose, facing, waterlogged));
                Assert.Equal(offset % 2 == 0, (block.FlagsForState(state) & BlockFlags.Waterlogged) != 0);
            }
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "copper_golem_pose", out string defaultPose));
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "facing", out string defaultFacing));
            Assert.True(block.TryGetPropertyValue(block.DefaultStateId, "waterlogged", out string defaultWaterlogged));
            Assert.Equal(("standing", "north", "false"), (defaultPose, defaultFacing, defaultWaterlogged));
        }
    }
}
