using Umpk.Game.Blocks;

namespace Umpk.Game.Registries;

/// <summary>Optional exact voxel grids for block collision queries. A null result preserves the source's bounding-box or contextual path.</summary>
public interface IBlockCollisionGridSource : IBlockShapeSource
{
    /// <summary>Gets the exact base grid and any measured position-offset model, or null when this source cannot supply them. Apply <see cref="BlockCollisionGrid.OffsetAt"/> before the block's world position.</summary>
    BlockCollisionGrid? GetCollisionGrid(BlockState state);
}
