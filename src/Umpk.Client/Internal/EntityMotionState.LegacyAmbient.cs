using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

internal sealed partial class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, LegacyAmbientMotion> _legacyAmbient = new();

    public void RegisterLegacyAmbient(Entity entity, EntityGeometryState geometry)
    {
        if (geometry.TryGetLegacyAmbient(entity, out TrackedLegacyAmbientGeometry? bounds) && bounds is not null)
            _legacyAmbient.Add(entity, new(bounds, new VanillaEntityInterpolation(VanillaInterpolationProfile.LegacyDoubleRotation,
                entity.Position, entity.Yaw, entity.Pitch, bounds.Profile.Width, bounds.Profile.Height, 3)));
    }

    private bool TryObserveLegacyAmbientInterpolation(Entity entity, Vec3d? target, float? yaw, float? pitch)
    {
        if (!_legacyAmbient.TryGetValue(entity, out LegacyAmbientMotion? motion))
            return false;
        motion.Position.SetTarget(target ?? entity.Position, yaw ?? entity.Yaw, pitch ?? entity.Pitch);
        return true;
    }

    private bool TryObserveLegacyAmbientTeleport(Entity entity, Vec3d target, float yaw, float pitch)
    {
        if (!_legacyAmbient.TryGetValue(entity, out LegacyAmbientMotion? motion))
            return false;
        bool near = !(Math.Abs(entity.Position.X - target.X) >= .03125)
            && !(Math.Abs(entity.Position.Y - target.Y) >= .015625)
            && !(Math.Abs(entity.Position.Z - target.Z) >= .03125);
        motion.Position.SetTarget(near ? entity.Position : target, yaw, pitch);
        if (near)
            motion.Position.Cancel();
        return true;
    }

    public bool ObserveLegacyAmbientStatus(Entity entity, int status)
    {
        if (status != 3 || !_legacyAmbient.TryGetValue(entity, out _))
            return false;
        // Native living status 3 sets health and calls onDeath. It does not use the remote-player death launch or size callback.
        entity.Metadata.Set(7, MetadataValue.Float(0));
        return true;
    }

    /// <summary>Reads retained controller fields separately from whether a complete actor position is proven.</summary>
    internal bool TryGetLegacyAmbientInterpolation(Entity entity, out LegacyAmbientInterpolationSnapshot snapshot)
    {
        snapshot = default;
        if (!_legacyAmbient.TryGetValue(entity, out LegacyAmbientMotion? motion))
            return false;
        (Vec3d target, int remainingSteps) = motion.Position.LegacyPending;
        snapshot = new(target, remainingSteps, motion.Geometry.Known);
        return true;
    }

    private void TickLegacyAmbient(Entity entity, LegacyAmbientMotion motion, List<int>? moved, World? world,
        IBlockShapeSource? shapes, EntityMembershipState? membership, EntityStore? entities, Aabb? localBody)
    {
        TrackedLegacyAmbientGeometry geometry = motion.Geometry;
        void Invalidate()
        {
            geometry.Known = false;
            membership?.InvalidateAmbientRegistration(entity);
        }
        // After unknown travel, another native interpolation count cannot be proved from the retained complete pose. Recovery requires an owning position/lifetime event.
        if (!geometry.Known)
        {
            Invalidate();
            return;
        }
        if (entity.Vehicle is not null || entity.Passengers.Count != 0)
        {
            Invalidate();
            return;
        }
        bool hanging = geometry.Profile.IsBat;
        if (hanging && entity.Metadata.TryGet(12, out MetadataValue flag))
        {
            if (flag.Kind != MetadataValueKind.Byte)
            {
                Invalidate();
                return;
            }
            hanging = (flag.AsByte() & 1) != 0;
        }
        Aabb box = geometry.Bounds;
        if (geometry.Profile.IsBat && !hanging && (world is null || membership is null || !LegacyAmbientCollision.IsDry(world, membership, box)))
        {
            // The inherited tick senses currents at entry, before living interpolation changes the cached box.
            Invalidate();
            return;
        }
        Vec3d before = entity.Position;
        Vec3d next = before;
        float yaw = entity.Yaw, pitch = entity.Pitch;
        bool grounded = entity.OnGround;
        Vec3d velocity = entity.Velocity;
        VanillaEntityInterpolation position = motion.Position;
        bool interpolating = position.HasActiveInterpolation;
        if (interpolating)
        {
            PullPosition(entity, position);
            position.Tick(static _ => throw new InvalidOperationException("Legacy living interpolation must not request collision clearance."));
            // This deterministic native phase precedes virtual travel. Retain its target/count even if that later phase makes the complete pose unknown.
            next = position.Position;
            yaw = position.Yaw;
            pitch = position.Pitch;
            box = position.Bounds;
        }
        else
            velocity = velocity.Scale(.98);
        velocity = new(Math.Abs(velocity.X) < .003 ? 0 : velocity.X, Math.Abs(velocity.Y) < .003 ? 0 : velocity.Y, Math.Abs(velocity.Z) < .003 ? 0 : velocity.Z);
        if (geometry.Profile.IsBat)
        {
            if (hanging)
            {
                velocity = Vec3d.Zero;
                next = new(next.X, Math.Floor(next.Y) + 1.0 - geometry.Profile.Height, next.Z);
            }
            else
                velocity = new(velocity.X, velocity.Y * .6F, velocity.Z);
        }
        else
        {
            // Squid.bg returns false, so water sensing does not add flow velocity. Its virtual travel still calls native movement on the client.
            if (world is null || shapes is null || membership is null || entities is null || IsLocalMountUnresolved || localBody is not { } body
                || !LegacyAmbientCollision.Finite(body) || body.XSize <= 0 || body.YSize <= 0 || body.ZSize <= 0
                || body.Intersects(box.ExpandTowards(velocity))
                || !LegacyAmbientCollision.TryMove(world, shapes, membership, entities, entity, box, velocity, out box, out velocity, out grounded))
            {
                Invalidate();
                return;
            }
            next = new((box.MinX + box.MaxX) / 2.0, box.MinY, (box.MinZ + box.MaxZ) / 2.0);
        }
        entity.Position = next;
        entity.Yaw = yaw;
        entity.Pitch = pitch;
        entity.Velocity = velocity;
        entity.OnGround = grounded;
        geometry.ObserveMovement(entity, box);
        if (before != entity.Position)
            moved?.Add(entity.Id);
    }

    private sealed record LegacyAmbientMotion(TrackedLegacyAmbientGeometry Geometry, VanillaEntityInterpolation Position);

    internal readonly record struct LegacyAmbientInterpolationSnapshot(Vec3d Target, int RemainingSteps, bool PositionKnown);

    /// <summary>The measured air, stone and liquid collision slice. Unclassified callbacks, hard peers, border transitions and useful step alternatives remain unknown.</summary>
    private static class LegacyAmbientCollision
    {
        public static bool IsDry(World world, EntityMembershipState membership, Aabb box)
        {
            Aabb sensed = box.Inflate(0, -.4F, 0).Inflate(-.001);
            if (!Finite(sensed))
                return false;
            for (int x = (int)Math.Floor(sensed.MinX); x < Math.Ceiling(sensed.MaxX); x++)
                for (int y = (int)Math.Floor(sensed.MinY); y < Math.Ceiling(sensed.MaxY); y++)
                    for (int z = (int)Math.Floor(sensed.MinZ); z < Math.Ceiling(sensed.MaxZ); z++)
                    {
                        var cell = new BlockPos(x, y, z);
                        if (!membership.HasColumn(cell) || y is < 0 or >= 256)
                            continue;
                        var block = world.GetBlock(cell);
                        if (world.GetColumn(cell) is null || !block.IsValid || block.IsFluid || block.IsWaterlogged)
                            return false;
                    }
            return true;
        }

        public static bool TryMove(World world, IBlockShapeSource shapes, EntityMembershipState membership, EntityStore entities, Entity source,
            Aabb original, Vec3d requested, out Aabb box, out Vec3d velocity, out bool grounded)
        {
            box = original;
            velocity = requested;
            grounded = false;
            Aabb region = original.ExpandTowards(requested).ExpandTowards(0, .6F, 0);
            if (!Finite(region) || region.XSize > 16 || region.YSize > 16 || region.ZSize > 16
                || region.MinX <= world.Border.MinX + 2 || region.MaxX >= world.Border.MaxX - 2
                || region.MinZ <= world.Border.MinZ + 2 || region.MaxZ >= world.Border.MaxZ - 2)
                return false;
            foreach (Entity peer in entities.All)
            {
                if (ReferenceEquals(peer, source) || membership.IsWorldQueryMember(peer) is false)
                    continue;
                // Original sm.ag and sm.j are null through these audited inheritance chains. Other peer contracts remain unowned.
                if (peer.Type.Id.IsMinecraft && peer.Type.Id.Path is "bat" or "squid" or "cow" or "player")
                    continue;
                return false;
            }
            List<Aabb> colliders = [];
            int minX = (int)Math.Floor(region.MinX) - 1, maxX = (int)Math.Ceiling(region.MaxX) + 1;
            int minY = (int)Math.Floor(region.MinY) - 1, maxY = (int)Math.Ceiling(region.MaxY) + 1;
            int minZ = (int)Math.Floor(region.MinZ) - 1, maxZ = (int)Math.Ceiling(region.MaxZ) + 1;
            for (int x = minX; x < maxX; x++)
                for (int z = minZ; z < maxZ; z++)
                {
                    int sides = (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                    if (sides == 2 || !membership.HasColumn(new BlockPos(x, 64, z)))
                        continue;
                    for (int y = minY; y < maxY; y++)
                    {
                        if (sides != 0 && (y == minY || y == maxY - 1) || y is < 0 or >= 256)
                            continue;
                        var cell = new BlockPos(x, y, z);
                        var block = world.GetBlock(cell);
                        if (world.GetColumn(cell) is null || !block.IsValid)
                            return false;
                        if (block.IsAir)
                            continue;
                        ReadOnlySpan<Aabb> bounds = shapes.GetCollisionShapes(block);
                        if (block.Block.NetworkId is 8 or 9 or 10 or 11)
                        {
                            if (!bounds.IsEmpty)
                                return false;
                            continue;
                        }
                        if (block.Block.NetworkId != 1 || bounds.Length != 1 || bounds[0] != Aabb.BlockAt(0, 0, 0))
                            return false;
                        Aabb collider = bounds[0].Move(x, y, z);
                        if (collider.Intersects(region))
                            colliders.Add(collider);
                    }
                }
            Vec3d actual = Clip(original, requested, colliders);
            if ((source.OnGround || requested.Y != actual.Y && requested.Y < 0) && (requested.X != actual.X || requested.Z != actual.Z))
            {
                // Native evaluates two step candidates. A potentially better candidate needs its own measured movement context.
                double upA = ClipAxis(1, original.ExpandTowards(requested.X, 0, requested.Z), .6F, colliders);
                double upB = ClipAxis(1, original, .6F, colliders);
                foreach (double up in new[] { upA, upB })
                {
                    Vec3d candidate = Clip(original.Move(0, up, 0), new(requested.X, 0, requested.Z), colliders);
                    if (candidate.X * candidate.X + candidate.Z * candidate.Z > actual.X * actual.X + actual.Z * actual.Z)
                        return false;
                }
            }
            box = original.Move(actual.X, actual.Y, actual.Z);
            grounded = requested.Y != actual.Y && requested.Y < 0;
            velocity = new(requested.X == actual.X ? requested.X : 0, requested.Y == actual.Y ? requested.Y : 0, requested.Z == actual.Z ? requested.Z : 0);
            return true;
        }

        private static Vec3d Clip(Aabb box, Vec3d requested, List<Aabb> colliders)
        {
            double y = ClipAxis(1, box, requested.Y, colliders);
            box = box.Move(0, y, 0);
            double x = ClipAxis(0, box, requested.X, colliders);
            box = box.Move(x, 0, 0);
            return new(x, y, ClipAxis(2, box, requested.Z, colliders));
        }

        private static double ClipAxis(int axis, Aabb moving, double distance, List<Aabb> colliders)
        {
            foreach (Aabb obstacle in colliders)
            {
                int first = (axis + 1) % 3, second = (axis + 2) % 3;
                if (Maximum(moving, first) <= Minimum(obstacle, first) || Minimum(moving, first) >= Maximum(obstacle, first)
                    || Maximum(moving, second) <= Minimum(obstacle, second) || Minimum(moving, second) >= Maximum(obstacle, second))
                    continue;
                if (distance > 0 && Maximum(moving, axis) <= Minimum(obstacle, axis))
                    distance = Math.Min(distance, Minimum(obstacle, axis) - Maximum(moving, axis));
                else if (distance < 0 && Minimum(moving, axis) >= Maximum(obstacle, axis))
                    distance = Math.Max(distance, Maximum(obstacle, axis) - Minimum(moving, axis));
            }
            return distance;
        }

        private static double Minimum(Aabb box, int axis) => axis == 0 ? box.MinX : axis == 1 ? box.MinY : box.MinZ;
        private static double Maximum(Aabb box, int axis) => axis == 0 ? box.MaxX : axis == 1 ? box.MaxY : box.MaxZ;
        public static bool Finite(Aabb box) => double.IsFinite(box.MinX) && double.IsFinite(box.MinY) && double.IsFinite(box.MinZ)
            && double.IsFinite(box.MaxX) && double.IsFinite(box.MaxY) && double.IsFinite(box.MaxZ)
            && box.MinX > int.MinValue + 2.0 && box.MinY > int.MinValue + 2.0 && box.MinZ > int.MinValue + 2.0
            && box.MaxX < int.MaxValue - 2.0 && box.MaxY < int.MaxValue - 2.0 && box.MaxZ < int.MaxValue - 2.0;
    }
}
