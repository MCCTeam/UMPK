using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Instance-owned native motion controllers. Observations and ticks run on the session loop.</summary>
internal sealed partial class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, RemotePlayerMotion> _remotePlayers = new();
    private readonly ConditionalWeakTable<Entity, object> _immovable = new();
    private readonly ConditionalWeakTable<Entity, FallingBlockMotion> _fallingBlocks = new();
    private static readonly object ImmovableMarker = new();

    public void RegisterRemotePlayer(Entity entity, int protocol)
    {
        RemotePlayerProfile profile = RemotePlayerProfile.ForProtocol(protocol);
        if (!profile.AppliesSpawnVelocity)
            entity.Velocity = Vec3d.Zero;
        var velocity = new VanillaEntityVelocity(profile.VelocitySteps);
        velocity.SetCurrent(entity.Velocity);
        VanillaEntityInterpolation position = VanillaEntityInterpolation.CreateRemotePlayer(protocol, entity.Position, entity.Yaw, entity.Pitch, 0.6F, 1.8F);
        _remotePlayers.Add(entity, new RemotePlayerMotion(protocol, profile, position, velocity));
    }

    public void RegisterImmovable(Entity entity)
    {
        _immovable.Add(entity, ImmovableMarker);
        entity.Velocity = Vec3d.Zero;
    }

    public bool RegisterFallingBlock(Entity entity, int protocol, int? objectData, EntityGeometryState geometry)
    {
        if (!LegacyFallingBlockProfile.Supports(entity, protocol))
            return false;
        geometry.RegisterFallingBlock(entity, protocol);
        entity.Velocity = Vec3d.Zero;
        _fallingBlocks.Add(entity, new FallingBlockMotion(protocol, objectData));
        if (objectData is null)
            geometry.InvalidateFallingBlock(entity);
        return true;
    }

    public bool IsSpectator(Entity entity, TabList players)
        => _remotePlayers.TryGetValue(entity, out RemotePlayerMotion? player)
            && ResolvePlayerInfo(entity, player, players)?.GameMode == GameMode.Spectator;

    public bool IsTrackedPlayer(Entity entity) => _remotePlayers.TryGetValue(entity, out _);

    private bool _localMountUnresolved;
    private Entity? _localMountVehicle;

    /// <summary>The bounded interaction picker cannot reconstruct the local rider's native position or shared-root filtering.</summary>
    public bool IsLocalMountUnresolved => _localMountUnresolved;

    public void ObserveLocalPassengers(int selfId, int vehicleId, IReadOnlyList<int> passengerIds, Entity? vehicle = null)
    {
        if (passengerIds.Contains(selfId))
        {
            _localMountUnresolved = true;
            _localMountVehicle = vehicle;
        }
        else if (vehicle is not null && vehicle.Id == vehicleId && ReferenceEquals(vehicle, _localMountVehicle))
        {
            _localMountUnresolved = false;
            _localMountVehicle = null;
        }
    }

    public void ObserveLocalLegacyMount(int selfId, int sourceId, int destinationId, bool? legacyLeash)
    {
        if (sourceId != selfId || legacyLeash != false)
            return;
        _localMountUnresolved = destinationId != -1;
        _localMountVehicle = null;
    }

    public static bool UsesLegacyMountTraversal(int protocol) => RemotePlayerProfile.ForProtocol(protocol).LegacyMounts;

    /// <summary>Routes the measured legacy remote graph and the separate Pig/Horse leash state. Unsupported or contradictory edges do not weaken EntityStore invariants.</summary>
    public void ObserveLegacyLink(EntityStore entities, EntityGeometryState geometry, int protocol, int selfId, ClientboundSetEntityLinkPacket link)
    {
        if (!UsesLegacyMountTraversal(protocol) || link.SourceId == selfId || !entities.TryGet(link.SourceId, out Entity? source))
            return;
        entities.TryGet(link.DestId, out Entity? destination);
        if (link.LegacyLeash == true)
        {
            if (link.DestId != selfId && source.Type.Id.IsMinecraft && source.Type.Id.Path is "pig" or "horse")
                source.LeashHolder = destination;
            return;
        }
        if (link.LegacyLeash != false)
            return;
        bool unsupported = link.DestId == selfId || destination is not null && destination.Passengers.Count != 0
            && (destination.Passengers.Count != 1 || !ReferenceEquals(destination.Passengers[0], source));
        for (Entity? ancestor = destination; ancestor is not null && !unsupported; ancestor = ancestor.Vehicle)
            unsupported = ReferenceEquals(ancestor, source);
        if (unsupported)
        {
            InvalidateRelation(source);
            if (destination is not null)
                InvalidateRelation(destination);
            return;
        }
        if (_remotePlayers.TryGetValue(source, out RemotePlayerMotion? motion))
            motion.LegacyMountKnown = true;
        else if (destination is null && source.Vehicle is not null)
            geometry.InvalidatePosition(source);
        entities.SetVehicle(source, destination);
        return;

        void InvalidateRelation(Entity actor)
        {
            geometry.InvalidatePosition(actor);
            if (_remotePlayers.TryGetValue(actor, out RemotePlayerMotion? motion))
                motion.LegacyMountKnown = false;
        }
    }

    public bool IsRiderGeometrySupported(Entity entity)
        => entity.Vehicle is null || _remotePlayers.TryGetValue(entity, out RemotePlayerMotion? rider) && rider.Profile.LegacyMounts && rider.LegacyMountKnown
            && _remotePlayers.TryGetValue(entity.Vehicle, out RemotePlayerMotion? vehicle) && vehicle.Profile.LegacyMounts && vehicle.LegacyMountKnown
            && entity.Vehicle.Passengers.Count == 1 && ReferenceEquals(entity.Vehicle.Passengers[0], entity);

    /// <summary>The original player tick and size callback finish before its vehicle reapplies rider position; pending interpolation fields survive that setter.</summary>
    public void PositionLegacyRider(Entity entity, EntityGeometryState geometry, List<int> moved)
    {
        if (entity.Vehicle is not { } vehicle || !_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? rider) || !rider.Profile.LegacyMounts)
            return;
        if (!IsRiderGeometrySupported(entity) || !geometry.IsPositionKnown(vehicle) || !geometry.TryGetDimensions(vehicle, out EntityGeometryDimensions dimensions))
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        Vec3d before = entity.Position;
        // 1.8.9 pk.an widens the native float height before multiplying by double .75; wn.am is double -.35.
        entity.Position = new(vehicle.Position.X, vehicle.Position.Y + dimensions.Height * .75 - .35, vehicle.Position.Z);
        rider.Position?.SetPosition(entity.Position);
        geometry.ObservePosition(entity);
        if (before != entity.Position)
            moved.Add(entity.Id);
    }

    internal bool TryGetRemotePlayerInterpolation(Entity entity, out (Vec3d Target, int RemainingSteps) pending)
    {
        pending = default;
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        pending = position.LegacyPending;
        return true;
    }

    private static TabListEntry? ResolvePlayerInfo(Entity entity, RemotePlayerMotion player, TabList players)
    {
        if (player.Profile.CachesPlayerInfo && player.PlayerInfo is { } cached)
            return cached;
        players.TryGet(entity.Uuid, out TabListEntry? info);
        if (player.Profile.CachesPlayerInfo)
            player.PlayerInfo = info;
        return info;
    }

    /// <summary>Applies setDeltaMovement without replacing an existing lerpMotion target.</summary>
    public void SetCurrentVelocity(Entity entity, Vec3d value)
    {
        if (!_immovable.TryGetValue(entity, out _))
            entity.Velocity = value;
    }

    public void ObserveVelocity(Entity entity, Vec3d value)
    {
        if (_cows.TryGetValue(entity, out CowMotion? cow))
            BeginCowMotion(entity, cow);
        if (_immovable.TryGetValue(entity, out _))
            return;
        if (_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion))
        {
            VanillaEntityVelocity velocity = motion.Velocity;
            velocity.SetCurrent(entity.Velocity);
            velocity.Observe(value);
            entity.Velocity = velocity.Value;
        }
        else
            entity.Velocity = value;
    }

    public bool TryObserveInterpolation(Entity entity, Vec3d? target, float? yaw, float? pitch)
    {
        if (TryObserveLegacyAmbientInterpolation(entity, target, yaw, pitch))
            return true;
        if (TryObserveCowInterpolation(entity, target, yaw, pitch))
            return true;
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        bool pending = motion.Profile.UsesPendingComponents;
        position.SetTarget(target ?? (pending ? position.TargetPosition : position.Position),
            yaw ?? (pending ? position.TargetYaw : position.Yaw), pitch ?? (pending ? position.TargetPitch : position.Pitch));
        return true;
    }

    public bool TryObservePath(Entity entity, ReadOnlySpan<VanillaPositionStep> path, float? yaw, float? pitch)
    {
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Protocol != 777 || motion.Position is not { } position || path.IsEmpty)
            return false;
        PullPosition(entity, position);
        position.SetPath(path, yaw, pitch);
        return true;
    }

    public bool TryObserveLegacyTeleport(Entity entity, Vec3d target, float yaw, float pitch)
    {
        if (TryObserveLegacyAmbientTeleport(entity, target, yaw, pitch))
            return true;
        if (InvalidateCowContext(entity))
            return false;
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        bool near = motion.Profile.SuppressesNearTeleportPosition
            && !(Math.Abs(position.Position.X - target.X) >= 0.03125)
            && !(Math.Abs(position.Position.Y - target.Y) >= 0.015625)
            && !(Math.Abs(position.Position.Z - target.Z) >= 0.03125);
        if (near && motion.Profile.CancelsNearTeleport)
            position.Cancel();
        else
            position.SetTarget(near ? position.Position : target, yaw, pitch);
        return true;
    }

    public bool TryObserveTeleport(Entity entity, PositionMoveRotation change, int flags)
    {
        if (InvalidateCowContext(entity))
            return false;
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        motion.Velocity.SetCurrent(entity.Velocity);
        EntityTeleportProfile.ForProtocol(motion.Protocol).Apply(position, motion.Velocity, entity.Velocity, change, flags, interpolate: true);
        PushPosition(entity, position);
        entity.Velocity = motion.Velocity.Value;
        return true;
    }

    public bool TryObservePositionSync(Entity entity, PositionMoveRotation values, ReadOnlySpan<VanillaPositionStep> path = default)
    {
        if (TryObserveCowSync(entity, values))
            return true;
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        if (position.Position.Subtract(values.Position).LengthSqr() > 4096.0)
        {
            // Native moveTo changes current pose without canceling a pending interpolation target.
            position.SetPosition(values.Position);
            position.SetCurrentRotation(values.YRot, values.XRot);
            PushPosition(entity, position);
        }
        else if (!path.IsEmpty && motion.Protocol == 777)
            position.SetPath(path, values.YRot, values.XRot);
        else
            position.SetTarget(values.Position, values.YRot, values.XRot);
        return true;
    }

    public void Tick(EntityStore? entities, List<int>? moved = null, EntityGeometryState? geometry = null, TabList? players = null)
    {
        if (entities is null)
            return;
        foreach (Entity entity in entities.All)
            TickEntity(entity, moved, geometry, players);
    }

    public void TickEntity(Entity entity, List<int>? moved = null, EntityGeometryState? geometry = null, TabList? players = null, World? world = null, IBlockShapeSource? shapes = null, EntityMembershipState? membership = null, EntityStore? entities = null, float tickRate = 20F, SelfState? self = null, Aabb? localBody = null)
    {
        if (_legacyAmbient.TryGetValue(entity, out LegacyAmbientMotion? ambient))
        {
            TickLegacyAmbient(entity, ambient, moved, world, shapes, membership, entities, localBody);
            return;
        }
        if (_cows.TryGetValue(entity, out CowMotion? cow))
        {
            TickCow(entity, cow, moved, world, shapes, entities, self, localBody);
            return;
        }
        if (_fallingBlocks.TryGetValue(entity, out FallingBlockMotion? falling))
        {
            TickFallingBlock(entity, falling, moved, geometry, world, shapes, membership, entities);
            return;
        }
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion))
            return;
        if (players is not null)
            ResolvePlayerInfo(entity, motion, players);
        if (motion.Position is { } position)
        {
            Vec3d before = entity.Position;
            PullPosition(entity, position);
            bool reappliesPosition = position.HasActiveInterpolation;
            bool positionKnown = geometry?.IsPositionKnown(entity) != false;
            if (motion.Profile.UsesWorldCollision && reappliesPosition)
            {
                if (world is null || shapes is null || membership is null || entities is null || geometry is null || !positionKnown
                    || entity.Pose != EntityPose.Standing || entity.Vehicle is not null || entity.Passengers.Count != 0 || IsLocalMountUnresolved
                    || !geometry.TryGetDimensions(entity, out EntityGeometryDimensions dimensions))
                    positionKnown = false;
                else
                {
                    position.RefreshDimensions(dimensions.Width, dimensions.Height, recenter: true);
                    motion.Collision ??= new ModernPlayerCollision(entity, entities, membership, this, geometry, JavaGameData.EntityGeometryData(motion.Protocol), motion.Protocol, world, shapes);
                    try
                    {
                        position.Tick(motion.Collision.NoCollision, tickRate > 20F ? tickRate / 20F : 1F);
                    }
                    catch (UnknownEntityCollisionContextException)
                    {
                        positionKnown = false;
                    }
                }
                if (!positionKnown)
                    geometry?.InvalidatePosition(entity);
            }
            else
                position.Tick(static _ => throw new InvalidOperationException("Legacy interpolation must not request world collisions."));
            if (positionKnown)
                PushPosition(entity, position);
            else
            {
                // A completed arithmetic interpolation cannot prove a position whose starting value was unknown. Rotation remains independent of that uncertainty.
                entity.Yaw = position.Yaw;
                entity.Pitch = position.Pitch;
            }
            if (reappliesPosition && positionKnown)
                geometry?.ObservePosition(entity);
            if (before != entity.Position)
                moved?.Add(entity.Id);
        }
        // Native rideTick zeros current movement before invoking the player's tick; the pending lerpMotion target survives.
        VanillaEntityVelocity velocity = motion.Velocity;
        velocity.SetCurrent(entity.Vehicle is null ? entity.Velocity : Vec3d.Zero);
        velocity.Tick();
        entity.Velocity = velocity.Value;
    }

    private void TickFallingBlock(Entity entity, FallingBlockMotion motion, List<int>? moved, EntityGeometryState? geometry, World? world, IBlockShapeSource? shapes, EntityMembershipState? membership, EntityStore? entities)
    {
        if (geometry is null)
            return;
        if (world is null || shapes is null || membership is null || entities is null || entity.Vehicle is not null || entity.Passengers.Count != 0
            || !geometry.TryGetFallingBlock(entity, out Aabb box) || motion.ObjectData is not { } data || !world.BlockData.Blocks.TryGet(data & 4095, out var block) || block.NetworkId == 0)
        {
            geometry.InvalidateFallingBlock(entity);
            return;
        }
        motion.Collision ??= new LegacyFallingBlockCollision(world, new WorldPhysicsView(world, shapes, WorldBorderContainmentEra.Legacy,
            entityContext: new EntityCollisionContext(entity, entities, membership, this, motion.Protocol)), motion.Protocol);
        if (!motion.HasTicked)
        {
            motion.HasTicked = true;
            var cell = new BlockPos((int)Math.Floor(entity.Position.X), (int)Math.Floor(entity.Position.Y), (int)Math.Floor(entity.Position.Z));
            var original = motion.Collision.ReadBlock(cell);
            if (!original.IsValid)
            {
                geometry.InvalidateFallingBlock(entity);
                return;
            }
            if (original.Block.NetworkId == block.NetworkId)
                world.SetBlockStateId(cell, 0);
        }
        Vec3d requested = entity.Velocity.Subtract(0, .04F, 0);
        if (!motion.Collision.TryCollide(box, requested, out Vec3d displacement))
        {
            geometry.InvalidateFallingBlock(entity);
            return;
        }
        Vec3d before = entity.Position;
        geometry.MoveFallingBlock(entity, displacement);
        entity.OnGround = requested.Y != displacement.Y && requested.Y < 0;
        entity.Velocity = new Vec3d(requested.X == displacement.X ? requested.X : 0, requested.Y == displacement.Y ? requested.Y : 0, requested.Z == displacement.Z ? requested.Z : 0).Scale(.98F);
        if (before != entity.Position)
            moved?.Add(entity.Id);
    }

    public void Remove(Entity entity)
    {
        _remotePlayers.Remove(entity);
        _cows.Remove(entity);
        _immovable.Remove(entity);
        _fallingBlocks.Remove(entity);
        _legacyAmbient.Remove(entity);
    }

    public void Clear()
    {
        _remotePlayers.Clear();
        _cows.Clear();
        _immovable.Clear();
        _fallingBlocks.Clear();
        _legacyAmbient.Clear();
        ResetLocalMount();
    }

    public void ResetLocalMount()
    {
        _localMountUnresolved = false;
        _localMountVehicle = null;
        _localPushPositionKnown = false;
        _localPushPoseUnsupported = false;
        _localPushBedUnsupported = false;
    }

    private static void PullPosition(Entity entity, VanillaEntityInterpolation position)
    {
        position.SetPosition(entity.Position);
        position.SetCurrentRotation(entity.Yaw, entity.Pitch);
    }

    private static void PushPosition(Entity entity, VanillaEntityInterpolation position)
    {
        entity.Position = position.Position;
        entity.Yaw = position.Yaw;
        entity.Pitch = position.Pitch;
    }

    private sealed record RemotePlayerMotion(int Protocol, RemotePlayerProfile Profile, VanillaEntityInterpolation? Position, VanillaEntityVelocity Velocity)
    {
        public bool LegacyMountKnown { get; set; } = true;
        public TabListEntry? PlayerInfo { get; set; }
        public ModernPlayerCollision? Collision { get; set; }
    }

    private sealed class FallingBlockMotion(int protocol, int? objectData)
    {
        public int Protocol { get; } = protocol;
        public int? ObjectData { get; } = objectData;
        public bool HasTicked { get; set; }
        public LegacyFallingBlockCollision? Collision { get; set; }
    }

    private readonly record struct RemotePlayerProfile(int VelocitySteps, bool AppliesSpawnVelocity, bool UsesPendingComponents, bool SuppressesNearTeleportPosition, bool CancelsNearTeleport, bool CachesPlayerInfo, bool UsesWorldCollision, bool LegacyMounts)
    {
        // Actual RemotePlayer traces cover all 75 releases; generic spawn starts carrying players in 1.20.2, before Entity.recreateFromPacket applies their velocity in 1.21.5.
        public static RemotePlayerProfile ForProtocol(int protocol)
            => new(protocol >= 762 ? 3 : 0, protocol >= 770, protocol is >= 764 and <= 769 or >= 773, protocol < 751, protocol is >= 107 and <= 498, protocol >= 761, ModernHardCollisionProfile.Supports(protocol), protocol == 47);
    }
}
