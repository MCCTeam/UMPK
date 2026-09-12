using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Client.Snapshots;

/// <summary>One block state at a position, plus the fact a <see cref="BlockState"/> cannot carry: whether the column is loaded. <see cref="Umpk.Game.World.World.GetBlockStateId"/> returns 0 outside a loaded column, indistinguishable from real air, which is exactly how "the world keeps answering reads" reads like a working world read.</summary>
public sealed record BlockSnapshot(BlockPos Position, BlockState State, bool ChunkLoaded)
{
    /// <summary>True only for a real air state in a loaded column; an unloaded column is never air.</summary>
    public bool IsAir => ChunkLoaded && State.IsAir;
}
