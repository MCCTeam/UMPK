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

    public void InvalidatePartialColumn(EntityMembershipState membership, ChunkColumn column)
    {
        if (membership.HasColumn(new BlockPos(column.Position.X << 4, 0, column.Position.Z << 4)))
            return;
        // Until the original partial-payload listener is accepted, its affected shared sections cannot retain earlier known cells. Other sections are independent.
        for (int index = 0; index < column.SectionCount; index++)
            if (column.GetSection(index) is not null)
            {
                int section = (column.MinY >> 4) + index;
                if (section is < 0 or >= 16)
                    continue;
                for (int key = section << 12, end = key + 4096; key < end; key++)
                    _states.Remove(key);
            }
    }

    private static int Key(BlockPos cell) => (cell.Y << 8) | ((cell.Z & 15) << 4) | (cell.X & 15);

    private static bool InBounds(BlockPos cell) => cell.Y is >= 0 and < 256
        && cell.X is >= -30_000_000 and < 30_000_000 && cell.Z is >= -30_000_000 and < 30_000_000;
}
