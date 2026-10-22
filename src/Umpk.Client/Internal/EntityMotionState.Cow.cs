using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

internal sealed partial class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, CowMotion> _cows = new();
    private bool _localPushPositionKnown;
    private bool _localPushPoseUnsupported;
    private bool _localPushBedUnsupported;

    public void RegisterCow(Entity entity, int protocol, EntityGeometryState geometry, EntityMembershipState membership)
    {
        if (!CowMotionProfile.Supports(entity, protocol))
            return;
        CowMotionProfile profile = CowMotionProfile.ForProtocol(protocol);
        VanillaEntityInterpolation? position = geometry.TryGetDimensions(entity, out EntityGeometryDimensions dimensions)
            ? new VanillaEntityInterpolation(profile.Interpolation, entity.Position, entity.Yaw, entity.Pitch, dimensions.Width, dimensions.Height, 3)
            : null;
        var cow = new CowMotion(protocol, profile, position, geometry, membership);
        _cows.Add(entity, cow);
        if (entity.Velocity != Vec3d.Zero)
            BeginCowMotion(entity, cow);
    }

    public bool IsTrackedCowSource(Entity entity) => _cows.TryGetValue(entity, out CowMotion? cow) && cow.Protocol == 770;

    public void ObserveLocalPushMetadata(int protocol, EntityMetadataList metadata)
    {
        if (!CowMotionProfile.Supports(protocol))
            return;
        // Original Entity.DATA_POSE and LivingEntity.SLEEPING_POS_ID are 6 and 14 on both measured clients. Bed assignment can reapply position before the next tick.
        foreach (EntityDataEntry entry in metadata.Entries)
        {
            if (entry.Index == 6)
                _localPushPoseUnsupported = entry.Value.Kind != MetadataValueKind.Pose || entry.Value.AsPose() != EntityPose.Standing;
            else if (entry.Index == 14)
                _localPushBedUnsupported = entry.Value.Kind != MetadataValueKind.OptionalPosition || entry.Value.AsOptionalPosition() is not null;
        }
        if (_localPushPoseUnsupported || _localPushBedUnsupported)
            _localPushPositionKnown = false;
    }

    public void ObserveLocalPushPosition(int protocol, Vec3d position, bool absolute)
    {
        if (CowMotionProfile.Supports(protocol))
            _localPushPositionKnown = !_localPushPoseUnsupported && !_localPushBedUnsupported && (absolute || _localPushPositionKnown)
                && double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z);
    }

    private static void BeginCowMotion(Entity entity, CowMotion cow)
    {
        cow.HasObservedMotion = true;
        cow.Geometry.BeginCowMotion(entity);
        if (cow.Position is null)
            cow.Geometry.InvalidatePosition(entity);
    }

    private bool TryObserveCowInterpolation(Entity entity, Vec3d? target, float? yaw, float? pitch)
    {
        if (!_cows.TryGetValue(entity, out CowMotion? cow))
            return false;
        BeginCowMotion(entity, cow);
        if (cow.Position is { } position)
        {
            PullPosition(entity, position);
            bool pending = cow.Profile.UsesPendingComponents;
            position.SetTarget(target ?? (pending ? position.TargetPosition : position.Position),
                yaw ?? (pending ? position.TargetYaw : position.Yaw), pitch ?? (pending ? position.TargetPitch : position.Pitch));
        }
        return true;
    }

    private bool TryObserveCowSync(Entity entity, PositionMoveRotation values)
    {
        if (!_cows.TryGetValue(entity, out CowMotion? cow))
            return false;
        BeginCowMotion(entity, cow);
        if (cow.Position is { } position)
        {
            PullPosition(entity, position);
            // Native isTickingEntity consults membership, not the world freeze decision. A snap keeps an older pending target intact.
            if (!cow.Membership.CanTick(entity) || position.Position.Subtract(values.Position).LengthSqr() > 4096.0)
            {
                position.SetPosition(values.Position);
                position.SetCurrentRotation(values.YRot, values.XRot);
                PushPosition(entity, position);
            }
            else
                position.SetTarget(values.Position, values.YRot, values.XRot);
        }
        return true;
    }

    private bool InvalidateCowContext(Entity entity)
    {
        if (!_cows.TryGetValue(entity, out CowMotion? cow))
            return false;
        BeginCowMotion(entity, cow);
        cow.Geometry.InvalidatePosition(entity);
        return true;
    }

    public void ObserveCowStatus(Entity entity, int status)
    {
        if (status == 3)
            _ = InvalidateCowContext(entity);
    }

    private void TickCow(Entity entity, CowMotion cow, List<int>? moved, World? world, IBlockShapeSource? shapes, EntityStore? entities, SelfState? self, Aabb? localBody)
    {
        if (!cow.HasObservedMotion)
            return;
        EntityGeometryState geometry = cow.Geometry;
        EntityMembershipState membership = cow.Membership;
        if (!geometry.IsPositionKnown(entity))
            return;
        if (cow.Position is not { } position || world is null || shapes is null || entities is null || self is null
            || !geometry.TryGetDimensions(entity, out EntityGeometryDimensions dimensions)
            || !_localPushPositionKnown || _localPushPoseUnsupported || _localPushBedUnsupported || IsLocalMountUnresolved || self.CameraEntityId is not null)
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        PullPosition(entity, position);
        position.RefreshDimensions(dimensions.Width, dimensions.Height, recenter: true);
        Aabb beforeBounds = position.Bounds;
        if (!CowBodyIsDry(world, membership, beforeBounds))
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        bool active = position.HasActiveInterpolation;
        Vec3d before = entity.Position;
        try
        {
            if (cow.Protocol == 770)
            {
                cow.Collision ??= new ModernPlayerCollision(entity, entities, membership, this, geometry, JavaGameData.EntityGeometryData(cow.Protocol), cow.Protocol, world, shapes);
                position.Tick(cow.Collision.NoCollision);
            }
            else
                position.Tick(static _ => throw new InvalidOperationException("The measured inline Cow controller must not query collisions."));
        }
        catch (UnknownEntityCollisionContextException)
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        if (!CowBodyIsDry(world, membership, position.Bounds)
            || !CowLocalPushIsIrrelevant(self, localBody, position.Bounds, JavaGameData.EntityGeometryData(cow.Protocol))
            || !EntityGeometryState.ExpansionPushIsIrrelevant(entity, entities, geometry, cow.Protocol, beforeBounds, position.Bounds))
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        PushPosition(entity, position);
        entity.Velocity = CowMotionProfile.TickVelocity(entity.Velocity, active);
        if (active)
            geometry.ObservePosition(entity);
        if (before != entity.Position)
            moved?.Add(entity.Id);
    }

    private static bool CowLocalPushIsIrrelevant(SelfState self, Aabb? localBody, Aabb cow, IEntityGeometrySource geometry)
    {
        if (localBody is not { } bounds && !CowMotionProfile.TryLocalInfluenceBound(self, geometry, out bounds))
            return false;
        return double.IsFinite(bounds.MinX) && double.IsFinite(bounds.MaxX) && double.IsFinite(bounds.MinY) && double.IsFinite(bounds.MaxY)
            && double.IsFinite(bounds.MinZ) && double.IsFinite(bounds.MaxZ) && bounds.XSize > 0 && bounds.YSize > 0 && !bounds.Intersects(cow);
    }

    private static bool CowBodyIsDry(World world, EntityMembershipState membership, Aabb box)
    {
        if (!double.IsFinite(box.MinX) || !double.IsFinite(box.MaxX) || !double.IsFinite(box.MinY) || !double.IsFinite(box.MaxY) || !double.IsFinite(box.MinZ) || !double.IsFinite(box.MaxZ)
            || box.MinX <= int.MinValue || box.MaxX >= int.MaxValue || box.MinZ <= int.MinValue || box.MaxZ >= int.MaxValue
            || box.MinY < world.Dimension.MinY || box.MaxY > world.Dimension.MinY + world.Dimension.Height)
            return false;
        for (int x = (int)Math.Floor(box.MinX); x <= (int)Math.Floor(box.MaxX); x++)
            for (int y = (int)Math.Floor(box.MinY); y <= (int)Math.Floor(box.MaxY); y++)
                for (int z = (int)Math.Floor(box.MinZ); z <= (int)Math.Floor(box.MaxZ); z++)
                {
                    var cell = new BlockPos(x, y, z);
                    // Native absent client chunks answer air; a synthetic block write does not change that. A received column without stored terrain cannot prove a dry context.
                    if (!membership.HasColumn(cell))
                        continue;
                    if (world.GetColumn(cell) is null)
                        return false;
                    var state = world.GetBlock(cell);
                    if (!state.IsValid || state.IsFluid || state.IsWaterlogged || state.IsPowderSnow || state.Block.Id == Identifier.Minecraft("moving_piston"))
                        return false;
                }
        return true;
    }

    private sealed class CowMotion(int protocol, CowMotionProfile profile, VanillaEntityInterpolation? position, EntityGeometryState geometry, EntityMembershipState membership)
    {
        public int Protocol { get; } = protocol;
        public CowMotionProfile Profile { get; } = profile;
        public VanillaEntityInterpolation? Position { get; } = position;
        public EntityGeometryState Geometry { get; } = geometry;
        public EntityMembershipState Membership { get; } = membership;
        public bool HasObservedMotion { get; set; }
        public ModernPlayerCollision? Collision { get; set; }
    }
}
