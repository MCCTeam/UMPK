using Umpk.Protocol.Bedrock;

namespace Umpk.Bedrock.IntegrationTests;

internal static class BulkFillPacketMatcher
{
    internal static bool TryMatchNineCellRegion(BedrockUpdateSubChunkBlocksPacket packet, out IReadOnlyList<(int X, int Y, int Z)> cells, out uint runtimeId)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cells = [];
        runtimeId = 0;
        if (packet.Blocks.Count != 9 || packet.Extra.Count != 0)
            return false;

        int y = packet.Blocks[0].Y;
        uint runtime = packet.Blocks[0].BlockRuntimeId;
        var positions = new HashSet<(int X, int Y, int Z)>();
        int minX = packet.Blocks[0].X;
        int minZ = packet.Blocks[0].Z;
        foreach (BedrockSubChunkBlockChange change in packet.Blocks)
        {
            if (change.Y != y || change.BlockRuntimeId != runtime)
                return false;
            if (!positions.Add((change.X, change.Y, change.Z)))
                return false;
            minX = Math.Min(minX, change.X);
            minZ = Math.Min(minZ, change.Z);
        }

        for (int dx = 0; dx <= 2; dx++)
            for (int dz = 0; dz <= 2; dz++)
                if (!positions.Contains((minX + dx, y, minZ + dz)))
                    return false;

        var ordered = new List<(int X, int Y, int Z)>(positions);
        ordered.Sort();
        cells = ordered;
        runtimeId = runtime;
        return true;
    }
}
