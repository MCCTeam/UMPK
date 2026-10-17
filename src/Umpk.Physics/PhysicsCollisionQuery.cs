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
}
