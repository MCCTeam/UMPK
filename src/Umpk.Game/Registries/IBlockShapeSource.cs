using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Game.Registries;

/// <summary>The per-version block-shape table seam. Implemented in <c>Umpk.Data.Java</c> over deduplicated AABB pools; consumed by <c>Umpk.Physics</c> through <c>IPhysicsWorldView.GetCollisionShapes</c>. Shapes are returned as spans over a shared backing pool, so a state's shape list costs no allocation. Collision and outline (visual/raycast) shapes are separate because they diverge for several blocks (fences, walls).</summary>
/// <remarks>The generated implementation currently answers outline queries with the collision shapes: the datasets carry no separate outline table, so the two are identical there even for the blocks where vanilla separates them. An implementation with real outline data is free to differ.</remarks>
public interface IBlockShapeSource
{
    /// <summary>The collision AABBs for a block state (empty for non-colliding states).</summary>
    ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state);

    /// <summary>The collision AABBs for a raw state id; equivalent to the <see cref="BlockState"/> overload.</summary>
    ReadOnlySpan<Aabb> GetCollisionShapes(int stateId);

    /// <summary>The outline (visual/raycast) AABBs for a block state.</summary>
    ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state);

    /// <summary>The outline AABBs for a raw state id.</summary>
    ReadOnlySpan<Aabb> GetOutlineShapes(int stateId);
}
