using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Bedrock.IntegrationTests;

public sealed class BulkFillPacketMatcherTests
{
    [Fact]
    public void MatchesCapturedNineCellFill()
    {
        byte[] payload = Convert.FromHexString("00800140090C80014C90F299B80502FFFFFFFFFFFFFFFFFF01000C80014E90F299B80502FFFFFFFFFFFFFFFFFF01000C80015090F299B80502FFFFFFFFFFFFFFFFFF01000E80014C90F299B80502FFFFFFFFFFFFFFFFFF01000E80014E90F299B80502FFFFFFFFFFFFFFFFFF01000E80015090F299B80502FFFFFFFFFFFFFFFFFF01001080014C90F299B80502FFFFFFFFFFFFFFFFFF01001080014E90F299B80502FFFFFFFFFFFFFFFFFF01001080015090F299B80502FFFFFFFFFFFFFFFFFF010000");
        BedrockUpdateSubChunkBlocksPacket packet = BedrockUpdateSubChunkBlocksCodecs.Decode(payload);

        Assert.True(BulkFillPacketMatcher.TryMatchNineCellRegion(packet, out IReadOnlyList<(int X, int Y, int Z)> cells, out uint runtimeId));
        Assert.Equal(1460042000u, runtimeId);
        Assert.Equal(9, cells.Count);
        Assert.All(cells, static cell => Assert.Equal(64, cell.Y));
        Assert.Equal(cells, cells.OrderBy(static cell => (cell.X, cell.Y, cell.Z)).ToArray());
    }

    [Fact]
    public void RejectsSplitRuntimeIds()
    {
        List<BedrockSubChunkBlockChange> grid = BuildGrid();
        BedrockSubChunkBlockChange first = grid[0];
        grid[0] = new BedrockSubChunkBlockChange(first.X, first.Y, first.Z, 101u, first.Flags, first.SyncedEntityId, first.Transition);

        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(Packet(grid), out _, out _));
    }

    [Fact]
    public void RejectsNonCoplanarCells()
    {
        List<BedrockSubChunkBlockChange> grid = BuildGrid();
        BedrockSubChunkBlockChange first = grid[0];
        grid[0] = new BedrockSubChunkBlockChange(first.X, first.Y + 1, first.Z, first.BlockRuntimeId, first.Flags, first.SyncedEntityId, first.Transition);

        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(Packet(grid), out _, out _));
    }

    [Fact]
    public void RejectsGapsAndExtraLayerEntries()
    {
        List<BedrockSubChunkBlockChange> grid = BuildGrid();
        BedrockSubChunkBlockChange moved = grid[8];
        grid[8] = new BedrockSubChunkBlockChange(50, moved.Y, 50, moved.BlockRuntimeId, moved.Flags, moved.SyncedEntityId, moved.Transition);
        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(Packet(grid), out _, out _));

        var extra = new BedrockSubChunkBlockChange(5, 60, 7, 100u, 2u, -1L, BedrockSubChunkBlockTransition.None);
        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(new BedrockUpdateSubChunkBlocksPacket(0, 0, 0, BuildGrid(), [extra]), out _, out _));
    }

    [Fact]
    public void RejectsWrongCountsAndDuplicates()
    {
        List<BedrockSubChunkBlockChange> grid = BuildGrid();
        grid.RemoveAt(0);
        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(Packet(grid), out _, out _));

        grid = BuildGrid();
        grid[1] = grid[0];
        Assert.False(BulkFillPacketMatcher.TryMatchNineCellRegion(Packet(grid), out _, out _));
    }

    private static BedrockUpdateSubChunkBlocksPacket Packet(IReadOnlyList<BedrockSubChunkBlockChange> blocks) => new(0, 0, 0, blocks, []);

    private static List<BedrockSubChunkBlockChange> BuildGrid()
    {
        var grid = new List<BedrockSubChunkBlockChange>(9);
        for (int dx = 0; dx <= 2; dx++)
            for (int dz = 0; dz <= 2; dz++)
                grid.Add(new BedrockSubChunkBlockChange(5 + dx, 60, 7 + dz, 100u, 2u, -1L, BedrockSubChunkBlockTransition.None));
        return grid;
    }
}
