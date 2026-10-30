using System.Globalization;
using System.Text.Json;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BedInteractionSourceTests
{
    [Fact]
    public void NarrowSourcePreservesAllOriginalBedFactsAndLeavesOtherContractsUnknown()
    {
        Func<int, IBlockInteractionSource> lookup = JavaGameData.BedInteractionData;
        int measured = 0;
        foreach (JavaVersion version in JavaVersions.All)
        {
            int protocol = version.Version.Protocol;
            if (protocol > 404)
                continue;
            IBlockInteractionSource source = lookup(protocol);
            Assert.Same(source, lookup(protocol));
            Assert.True(source.HasData);
            IBlockInteractionSource full = JavaGameData.BlockInteractionData(protocol);
            string root = Path.Combine(FixturePaths.RepoRoot(), "data", "java", protocol.ToString(CultureInfo.InvariantCulture));
            using JsonDocument beds = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "bed-facings.json")));
            foreach (JsonElement row in beds.RootElement.GetProperty("states").EnumerateArray())
            {
                int state = row[0].GetInt32();
                Assert.True(source.TryGetBedFacing(state, out Direction facing));
                Assert.Equal((Direction)row[1].GetInt32(), facing);
                Assert.True(full.TryGetBedFacing(state, out Direction original));
                Assert.Equal(original, facing);
            }
            using JsonDocument cells = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "bed-exit-cells.json")));
            foreach (JsonElement range in cells.RootElement.GetProperty("ranges").EnumerateArray())
                for (int state = range[0].GetInt32(); state <= range[1].GetInt32(); state++)
                {
                    int flags = range[2].GetInt32();
                    Assert.True(source.TryGetBedExitCell(state, out BedExitCellInfo info));
                    Assert.Equal(new BedExitCellInfo((flags & 1) != 0, (flags & 2) != 0), info);
                    Assert.True(full.TryGetBedExitCell(state, out BedExitCellInfo original));
                    Assert.Equal(original, info);
                }
            foreach (JsonElement state in cells.RootElement.GetProperty("rejected_states").EnumerateArray())
                Assert.False(source.TryGetBedExitCell(state.GetInt32(), out _));
            Assert.False(source.TryGetBedFacing(-1, out _));
            Assert.False(source.TryGetBedExitCell(-1, out _));
            Assert.False(source.TryGetBlock(Identifier.Minecraft("stone"), out _));
            Assert.False(source.TryGetItem(Identifier.Minecraft("stone"), out _));
            Assert.False(source.TryGetCubeReplacement(0, 0, out _));
            Assert.False(source.IsAlwaysNonObstructingEntity(Identifier.Minecraft("item")));
            Assert.False(source.TryGetFixedEntityDimensions(Identifier.Minecraft("player"), out _, out _));
            measured++;
        }
        Assert.Equal(14, measured);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(477)]
    [InlineData(777)]
    [InlineData(int.MaxValue)]
    public void MissingBedMeasurementsRemainUnknown(int protocol)
    {
        IBlockInteractionSource source = JavaGameData.BedInteractionData(protocol);
        Assert.False(source.HasData);
        Assert.False(source.TryGetBedFacing(416, out _));
        Assert.False(source.TryGetBedExitCell(0, out _));
    }

}
