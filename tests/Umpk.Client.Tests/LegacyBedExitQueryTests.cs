using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyBedExitQueryTests
{
    public static IEnumerable<object[]> Protocols() => new[] { 47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404 }.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(Protocols))]
    public void SupportAndMaterialReadsShortCircuitWithoutPromotingUnknown(int protocol)
    {
        Assert.True(JavaGameData.Registries(protocol).Blocks.TryGet(Identifier.Minecraft("stone"), out var stone));
        int stoneId = stone.Value.DefaultStateId;
        var candidate = new BlockPos(1, 64, 1);
        BlockPos[] cells = [new(1, 63, 1), candidate, new(1, 65, 1)];
        foreach ((int[] states, bool known, bool expected, int readCount) in new (int[], bool, bool, int)[]
        {
            ([stoneId, 0, 0], true, true, 3),
            ([0, int.MaxValue, int.MaxValue], true, false, 1),
            ([stoneId, stoneId, int.MaxValue], true, false, 2),
            ([stoneId, 0, stoneId], true, false, 3),
            ([int.MaxValue, 0, 0], false, false, 1),
            ([stoneId, int.MaxValue, 0], false, false, 2),
            ([stoneId, 0, int.MaxValue], false, false, 3),
        })
        {
            List<BlockPos> reads = [];
            var query = new LegacyBedExitQuery(protocol, JavaGameData.BlockInteractionData(protocol), cell =>
            {
                reads.Add(cell);
                return states[Array.IndexOf(cells, cell)];
            });
            Assert.Equal(known, query.TryCanStand(candidate, out bool actual));
            Assert.Equal(expected, actual);
            Assert.Equal(cells.Take(readCount), reads);
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public void DirectReadsUseTheNativeBoundsIndependentlyOfResizeQueries(int protocol)
    {
        Assert.True(JavaGameData.Registries(protocol).Blocks.TryGet(Identifier.Minecraft("stone"), out var stone));
        int stoneId = stone.Value.DefaultStateId;
        foreach (int x in new[] { -30_000_001, -30_000_000, 29_999_999, 30_000_000 })
            foreach (int y in new[] { 0, 1, 254, 255, 256 })
            {
                List<BlockPos> reads = [];
                var query = new LegacyBedExitQuery(protocol, JavaGameData.BlockInteractionData(protocol), cell =>
                {
                    reads.Add(cell);
                    return cell.Y == y - 1 ? stoneId : 0;
                });
                bool bounded = protocol < 110 && x is < -30_000_000 or >= 30_000_000;
                Assert.True(query.TryCanStand(new BlockPos(x, y, 0), out bool actual));
                Assert.Equal(!bounded && y > 0 && y <= 256, actual);
                Assert.All(reads, cell => Assert.InRange(cell.Y, 0, 255));
                if (bounded || y == 0)
                    Assert.Empty(reads);
            }
    }
}
