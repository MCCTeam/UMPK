using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The entire world dependency of the physics engine. The engine reads blocks, their collision shapes, and (optionally) nearby hard entity colliders through this seam and nothing else. <c>Umpk.Client</c> implements it over the live <c>World</c>; the pathfinder implements it over planning snapshots; tests implement it over hand-built voxel fixtures.</summary>
public interface IPhysicsWorldView
{
    /// <summary>The block state at an integer position. Out-of-world positions return an air-like state.</summary>
    BlockState GetBlock(BlockPos pos);

    /// <summary>The collision AABBs for a block state, in local (0..1) block coordinates.</summary>
    ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state);

    /// <summary>Whether the chunk containing the given position is loaded (vanilla off-world fall behavior).</summary>
    bool IsChunkLoaded(BlockPos pos);

    /// <summary>Appends the hard hitboxes of nearby entities (boats, shulkers) that overlap <paramref name="region"/> into <paramref name="into"/>. The default implementation adds nothing; hosts with entity tracking override it. Colliders are in world coordinates.</summary>
    void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        // Default: no entity colliders. Hosts with an entity tracker override this.
    }
}
