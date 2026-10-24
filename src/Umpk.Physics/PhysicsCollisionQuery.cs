using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>Reuses the physics engine's collision rules for a caller-owned source body and proposed box.</summary>
/// <remarks>Own one query object per serialized caller. Its scratch buffer is reused; concurrent or reentrant calls require separate instances.</remarks>
public sealed class PhysicsCollisionQuery
{
    private readonly IPhysicsWorldView _world;
    private readonly ColliderBuffer _scratch = new();

    /// <summary>Creates a query over a live or captured world view.</summary>
    public PhysicsCollisionQuery(IPhysicsWorldView world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>Tests whether the proposed box is clear using the source's current position, descent, fall and equipment facts. The proposed box may be offset from the source.</summary>
    public bool NoCollision(Vec3d sourcePosition, in Aabb box, bool descending = false, double fallDistance = 0, bool powderSnowWalkable = false)
    {
        var body = new BodyCollisionContext(descending, fallDistance, powderSnowWalkable, sourcePosition);
        return CollisionResolver.NoCollision(_world, box, sourcePosition.Y, body, _scratch);
    }

    /// <summary>Tests one block's state-only collision shape against the proposed box. The caller owns block iteration and any entity, border or entity-context checks.</summary>
    public bool IntersectsBlock(BlockPos position, in Aabb box)
    {
        var block = _world.GetBlock(position);
        if (_world.CollisionQueryEra != BlockCollisionQueryEra.BoundingBoxes && _world.GetCollisionGrid(block) is { } grid)
            return VoxelCollisionQuery.Intersects(grid, position, box, _world.CollisionQueryEra, grid.OffsetAt(position));

        Vec3d offset = BlockShapeOffset.For(block, position);
        foreach (Aabb shape in _world.GetCollisionShapes(block))
            if (shape.Move(offset).Move(position.X, position.Y, position.Z).Intersects(box))
                return true;
        return false;
    }
}
