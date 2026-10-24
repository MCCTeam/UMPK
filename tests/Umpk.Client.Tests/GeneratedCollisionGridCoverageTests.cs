using System.Globalization;
using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class GeneratedCollisionGridCoverageTests
{
    [Fact]
    public void GeneratedDefaultGridsRetainEveryNativeCoordinateAndCell()
    {
        string root = Path.Combine(FixturePaths.RepoRoot(), "data", "java");
        int protocols = 0, checkedStates = 0, nativeStates = 0;
        foreach (string path in Directory.EnumerateFiles(root, "block-collision-grids.json", SearchOption.AllDirectories))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            string offsetsPath = Path.Combine(Path.GetDirectoryName(path)!, "block-collision-offsets.json");
            using JsonDocument? offsets = File.Exists(offsetsPath) ? JsonDocument.Parse(File.ReadAllBytes(offsetsPath)) : null;
            JsonElement table = document.RootElement;
            nativeStates += table.GetProperty("state_count").GetInt32();
            int protocol = int.Parse(Path.GetFileName(Path.GetDirectoryName(path))!, CultureInfo.InvariantCulture);
            Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
            var data = new RegistryBlockDataSource(blocks, isLegacy: false);
            var source = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(protocol));
            foreach (JsonProperty reference in table.GetProperty("states").EnumerateObject())
            {
                var state = new BlockState(data, int.Parse(reference.Name, CultureInfo.InvariantCulture));
                BlockCollisionGrid? grid = source.GetCollisionGrid(state);
                if (grid is null)
                {
                    Assert.True(state.IsAir || state.IsFluid || state.Block.Id.Path is "scaffolding" or "powder_snow" or "bamboo" or "pointed_dripstone", $"Unexplained grid gap at {protocol}/{reference.Name}");
                    continue;
                }
                bool hasOffset = offsets is not null && offsets.RootElement.GetProperty("states").TryGetProperty(reference.Name, out _);
                JsonElement native = hasOffset
                    ? offsets!.RootElement.GetProperty("groups")[offsets.RootElement.GetProperty("states").GetProperty(reference.Name).GetInt32()]
                    : table.GetProperty("groups")[reference.Value.GetInt32()];
                double[][] axes = [.. native.GetProperty("planes").EnumerateArray().Select(axis => axis.EnumerateArray().Select(value => value.GetDouble()).ToArray())];
                Assert.Equal(axes[0], grid.XCoordinates.ToArray());
                Assert.Equal(axes[1], grid.YCoordinates.ToArray());
                Assert.Equal(axes[2], grid.ZCoordinates.ToArray());
                Assert.Equal(native.GetProperty("cells").EnumerateArray().Select(value => value.GetInt32()), grid.OccupiedCells.ToArray());
                Assert.Equal(native.GetProperty("canonical_full_block").GetBoolean(), grid.CanonicalFullBlock);
                Assert.Equal(hasOffset ? native.GetProperty("max_horizontal_offset").GetDouble() : 0, grid.MaxHorizontalOffset);
                Assert.Equal(native.GetProperty("boxes").EnumerateArray().Select(box => new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble())), grid.Boxes.ToArray());
                checkedStates++;
            }
            protocols++;
        }
        Assert.Equal(39, protocols);
        Assert.Equal(797_628, nativeStates);
        Assert.True(checkedStates > 770_000);
    }

    [Fact]
    public void DefaultAndUnknownStatesDoNotResolveARegistryEntry()
    {
        var source = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(777));
        var data = new RegistryBlockDataSource(JavaGameData.Registries(777).Blocks, isLegacy: false);
        Assert.Null(source.GetCollisionGrid(default));
        Assert.Null(source.GetCollisionGrid(new BlockState(data, -1)));
        Assert.Null(source.GetCollisionGrid(new BlockState(data, int.MaxValue)));
    }

    [Theory]
    [InlineData("scaffolding")]
    [InlineData("powder_snow")]
    [InlineData("bamboo")]
    [InlineData("pointed_dripstone")]
    public void SourceSeparatesPositionModelsFromUnmeasuredEntityContext(string path)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(777).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        Assert.True(blocks.TryGetValue(Identifier.Minecraft(path), out BlockDefinition? block));
        var source = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(777));
        BlockCollisionGrid? grid = source.GetCollisionGrid(new BlockState(data, block.DefaultStateId));
        if (path is "bamboo" or "pointed_dripstone")
            Assert.True(grid is { MaxHorizontalOffset: > 0 });
        else
            Assert.Null(grid);
    }

}
