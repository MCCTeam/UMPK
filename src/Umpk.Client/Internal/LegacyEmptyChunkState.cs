using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Retains observed protocol-47 writes to the original provider's one shared EmptyChunk. Missing entries remain unknown.</summary>
internal sealed class LegacyEmptyChunkState
{
    private readonly Dictionary<int, int> _states = [];

    public int Read(BlockPos cell) => InBounds(cell) ? _states.GetValueOrDefault(Key(cell), -1) : -1;

    public void ObserveWrite(EntityMembershipState membership, BlockPos cell, int state)
    {
        if (!membership.HasColumn(cell) && InBounds(cell))
            _states[Key(cell)] = state;
    }

    public void ObservePartialColumn(EntityMembershipState membership, ChunkColumn column)
    {
        if (membership.HasColumn(new BlockPos(column.Position.X << 4, 0, column.Position.Z << 4)))
            return;
        // Original 1.8 payloads fill the provider's shared EmptyChunk sections. Copy values so later logical-world writes cannot mutate this context through shared decoded storage.
        for (int index = 0; index < column.SectionCount; index++)
            if (column.GetSection(index) is { } section)
            {
                int baseY = column.MinY + index * 16;
                if (baseY is < 0 or >= 256)
                    continue;
                for (int y = 0; y < 16; y++)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                            _states[((baseY + y) << 8) | (z << 4) | x] = section.GetBlockStateId(x, y, z);
            }
    }

    private static int Key(BlockPos cell) => (cell.Y << 8) | ((cell.Z & 15) << 4) | (cell.X & 15);

    private static bool InBounds(BlockPos cell) => cell.Y is >= 0 and < 256
        && cell.X is >= -30_000_000 and < 30_000_000 && cell.Z is >= -30_000_000 and < 30_000_000;
}
