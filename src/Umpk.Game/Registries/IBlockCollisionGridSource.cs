using Umpk.Game.Blocks;

namespace Umpk.Game.Registries;

/// <summary>Optional exact voxel grids for block collision queries. A null result preserves the source's bounding-box or contextual path.</summary>
public interface IBlockCollisionGridSource : IBlockShapeSource
{
    /// <summary>Gets the exact block-local grid for a context-independent state, or null when this source cannot supply that grid.</summary>
    BlockCollisionGrid? GetCollisionGrid(BlockState state);
}
