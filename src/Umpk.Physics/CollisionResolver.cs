using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>AABB collision detection against the block world plus optional entity colliders. It preserves Y-first axis ordering and the game's 1e-5 blocked-axis epsilon), restructured to take an <see cref="IPhysicsWorldView"/> seam and reusable <see cref="ColliderBuffer"/>s so the steady-state tick allocates nothing.</summary>
internal static class CollisionResolver
{
    /// <summary>Resolves <paramref name="movement"/> with full collision detection including step-up. <paramref name="colliders"/> and <paramref name="stepColliders"/> are scratch buffers owned by the caller.</summary>
    /// <remarks><paramref name="body"/> carries the entity half of <c>collision-context construction</c> that any shape in the game actually reads - the sneak key, the fall distance and whether the body may walk on powder snow. Together with the box's own <c>MinY</c> it is all of it; see <see cref="BodyCollisionContext"/>, <see cref="ScaffoldingCollision"/> and <see cref="PowderSnowCollision"/>.</remarks>
    public static Vec3d Collide(
        IPhysicsWorldView world,
        in Aabb entityBox,
        Vec3d movement,
        bool onGround,
        in BodyCollisionContext body,
        float maxUpStep,
        ColliderBuffer colliders,
        ColliderBuffer stepColliders)
    {
        if (movement.LengthSqr() == 0.0)
            return movement;

        double entityBottom = entityBox.MinY;
        CollectColliders(world, entityBox.ExpandTowards(movement), entityBottom, in body, colliders);
        Vec3d resolved = CollideWithShapes(movement, entityBox, colliders);

        bool blockedX = Math.Abs(movement.X - resolved.X) > PhysicsConstants.BlockedAxisEpsilon;
        bool blockedZ = Math.Abs(movement.Z - resolved.Z) > PhysicsConstants.BlockedAxisEpsilon;
        bool blockedY = movement.Y != resolved.Y;
        bool hitGroundDuringMove = blockedY && movement.Y < 0.0;

        if (maxUpStep > 0.0f && (hitGroundDuringMove || onGround) && (blockedX || blockedZ))
        {
            Aabb stepBase = hitGroundDuringMove ? entityBox.Move(0, resolved.Y, 0) : entityBox;
            Aabb expanded = stepBase.ExpandTowards(movement.X, maxUpStep, movement.Z)
                .ExpandTowards(0, hitGroundDuringMove ? 0 : -PhysicsConstants.BlockedAxisEpsilon, 0);

            CollectColliders(world, expanded, entityBottom, in body, stepColliders);

            Span<float> heights = stackalloc float[StepHeightCap];
            int heightCount = CollectCandidateStepHeights(stepBase, stepColliders, maxUpStep, (float)resolved.Y, heights);

            for (int i = 0; i < heightCount; i++)
            {
                var stepMovement = new Vec3d(movement.X, heights[i], movement.Z);
                Vec3d stepResolved = CollideWithShapes(stepMovement, stepBase, stepColliders);

                if (stepResolved.HorizontalDistanceSqr() > resolved.HorizontalDistanceSqr())
                {
                    double yOffset = entityBox.MinY - stepBase.MinY;
                    return stepResolved.Subtract(0, yOffset, 0);
                }
            }
        }

        return resolved;
    }

    private const int StepHeightCap = 16;

    private static Vec3d CollideWithShapes(Vec3d movement, in Aabb entityBox, ColliderBuffer colliders)
    {
        if (colliders.Count == 0)
            return movement;

        Vec3d accumulated = Vec3d.Zero;
        (int a0, int a1, int a2) = GetAxisStepOrder(movement);
        accumulated = ResolveAxis(a0, movement, entityBox, colliders, accumulated);
        accumulated = ResolveAxis(a1, movement, entityBox, colliders, accumulated);
        accumulated = ResolveAxis(a2, movement, entityBox, colliders, accumulated);
        return accumulated;
    }

    private static Vec3d ResolveAxis(int axis, Vec3d movement, in Aabb entityBox, ColliderBuffer colliders, Vec3d accumulated)
    {
        double dist = movement.Get(axis);
        if (dist == 0.0)
            return accumulated;

        double resolved = CollideAxis(axis, entityBox.Move(accumulated), colliders, dist);
        return accumulated.With(axis, resolved);
    }

    /// <summary>Resolves Y first, then the larger horizontal axis, then the smaller.</summary>
    private static (int, int, int) GetAxisStepOrder(Vec3d movement) =>
        Math.Abs(movement.X) < Math.Abs(movement.Z)
            ? (1, 2, 0)   // Y Z X
            : (1, 0, 2);  // Y X Z

    private static double CollideAxis(int axis, Aabb entityBox, ColliderBuffer colliders, double movement)
    {
        for (int i = 0; i < colliders.Count; i++)
        {
            if (Math.Abs(movement) < PhysicsConstants.CollisionEpsilon)
                return 0.0;

            movement = entityBox.Collide(axis, colliders[i], movement);
        }

        return movement;
    }

    /// <summary>Collects all block and entity collision boxes overlapping <paramref name="searchBox"/> into <paramref name="result"/>.</summary>
    public static void CollectColliders(
        IPhysicsWorldView world, in Aabb searchBox, double entityBottom, in BodyCollisionContext body, ColliderBuffer result)
    {
        result.Clear();

        int minBX = (int)Math.Floor(searchBox.MinX - PhysicsConstants.CollisionEpsilon) - 1;
        int maxBX = (int)Math.Floor(searchBox.MaxX + PhysicsConstants.CollisionEpsilon) + 1;
        int minBY = (int)Math.Floor(searchBox.MinY - PhysicsConstants.CollisionEpsilon) - 1;
        int maxBY = (int)Math.Floor(searchBox.MaxY + PhysicsConstants.CollisionEpsilon) + 1;
        int minBZ = (int)Math.Floor(searchBox.MinZ - PhysicsConstants.CollisionEpsilon) - 1;
        int maxBZ = (int)Math.Floor(searchBox.MaxZ + PhysicsConstants.CollisionEpsilon) + 1;

        for (int bx = minBX; bx <= maxBX; bx++)
        {
            for (int bz = minBZ; bz <= maxBZ; bz++)
            {
                for (int by = minBY; by <= maxBY; by++)
                {
                    BlockState block = world.GetBlock(new BlockPos(bx, by, bz));

                    // The two context-dependent shapes in the game, chained. Each is a no-op for the other's block and for every other block, and each bails on a flag read before it ever touches a registry path; the order between them is therefore free.
                    ReadOnlySpan<Aabb> shapes = ScaffoldingCollision.ShapesFor(
                        block, world.GetCollisionShapes(block), by, entityBottom, body.Descending);
                    shapes = PowderSnowCollision.ShapesFor(block, shapes, by, entityBottom, in body);

                    // The third position-dependent shape rule, and the only one that is a TRANSLATION rather than a substitution, which is why it composes into the move below instead of replacing the span. `shape behavior` and `shape behavior` both end in `shape.move(state.getOffset(pos))`; the shape table cannot carry it, because the table is keyed on the STATE and the offset is a hash of the POSITION. Treating it as a constant can make the client enter space that the server considers occupied.
                    Vec3d shapeOffset = BlockShapeOffset.For(block, new BlockPos(bx, by, bz));

                    for (int s = 0; s < shapes.Length; s++)
                    {
                        Aabb worldShape = shapes[s].Move(bx + shapeOffset.X, by, bz + shapeOffset.Z);
                        if (worldShape.Intersects(searchBox))
                            result.Add(worldShape);

                    }
                }
            }
        }

        world.CollectEntityColliders(searchBox, result);
    }

    private static int CollectCandidateStepHeights(
        in Aabb stepBase,
        ColliderBuffer colliders,
        float maxUpStep,
        float currentY,
        Span<float> into)
    {
        int count = 0;
        for (int i = 0; i < colliders.Count; i++)
        {
            float h = (float)(colliders[i].MaxY - stepBase.MinY);
            if (h > currentY && h <= maxUpStep && !ContainsHeight(into, count, h))
                if (count < into.Length)
                    into[count++] = h;

        }

        // No fallback is allowed when nothing qualifies: an empty candidate set performs no step-up. Manufacturing maxUpStep here can lift a player into a full block at flush contact.
        if (count == 0)
            return 0;

        // Vanilla uses a FloatArraySet then unstableSort (ascending). Insertion-sort in place.
        for (int i = 1; i < count; i++)
        {
            float key = into[i];
            int j = i - 1;
            while (j >= 0 && into[j] > key)
            {
                into[j + 1] = into[j];
                j--;
            }

            into[j + 1] = key;
        }

        return count;
    }

    private static bool ContainsHeight(Span<float> buffer, int count, float value)
    {
        for (int i = 0; i < count; i++)
            if (buffer[i] == value)
                return true;

        return false;
    }

    /// <summary>True when the box has no collisions at all - block shapes AND any hard entity colliders, since it goes through <see cref="CollectColliders"/>, the same collection <see cref="Collide"/> uses. Used for pose-fit checks and for the collision half of <c>PlayerPhysics.IsFree</c>, whose contract depends on the entity colliders being included here.</summary>
    public static bool NoCollision(
        IPhysicsWorldView world, in Aabb box, double entityBottom, in BodyCollisionContext body, ColliderBuffer scratch)
    {
        CollectColliders(world, box, entityBottom, in body, scratch);
        return scratch.Count == 0;
    }
}
