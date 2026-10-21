using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Client.Navigation;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Instance-owned native motion controllers. Observations and ticks run on the session loop.</summary>
internal sealed class EntityMotionState
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
        // Handler-era position controllers need the native world-collision query before they can join this owner.
        VanillaEntityInterpolation? position = protocol < 770
            ? VanillaEntityInterpolation.CreateRemotePlayer(protocol, entity.Position, entity.Yaw, entity.Pitch, 0.6F, 1.8F)
            : null;
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
        geometry.RegisterFallingBlock(entity);
        entity.Velocity = Vec3d.Zero;
        _fallingBlocks.Add(entity, new FallingBlockMotion(objectData));
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
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        bool pending = motion.Profile.UsesPendingComponents;
        position.SetTarget(target ?? (pending ? position.TargetPosition : position.Position),
            yaw ?? (pending ? position.TargetYaw : position.Yaw), pitch ?? (pending ? position.TargetPitch : position.Pitch));
        return true;
    }

    public bool TryObserveLegacyTeleport(Entity entity, Vec3d target, float yaw, float pitch)
    {
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
        if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) || motion.Position is not { } position)
            return false;
        PullPosition(entity, position);
        motion.Velocity.SetCurrent(entity.Velocity);
        EntityTeleportProfile.ForProtocol(motion.Protocol).Apply(position, motion.Velocity, entity.Velocity, change, flags, interpolate: true);
        PushPosition(entity, position);
        entity.Velocity = motion.Velocity.Value;
        return true;
    }

    public bool TryObservePositionSync(Entity entity, PositionMoveRotation values)
    {
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

    public void TickEntity(Entity entity, List<int>? moved = null, EntityGeometryState? geometry = null, TabList? players = null, World? world = null, IBlockShapeSource? shapes = null, EntityMembershipState? membership = null, EntityStore? entities = null)
    {
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
            entityContext: new EntityCollisionContext(entity, entities, membership, this, 107)));
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
        _immovable.Remove(entity);
        _fallingBlocks.Remove(entity);
    }

    public void Clear()
    {
        _remotePlayers.Clear();
        _immovable.Clear();
        _fallingBlocks.Clear();
        ResetLocalMount();
    }

    public void ResetLocalMount()
    {
        _localMountUnresolved = false;
        _localMountVehicle = null;
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
        public TabListEntry? PlayerInfo { get; set; }
    }

    private sealed class FallingBlockMotion(int? objectData)
    {
        public int? ObjectData { get; } = objectData;
        public bool HasTicked { get; set; }
        public LegacyFallingBlockCollision? Collision { get; set; }
    }

    private readonly record struct RemotePlayerProfile(int VelocitySteps, bool AppliesSpawnVelocity, bool UsesPendingComponents, bool SuppressesNearTeleportPosition, bool CancelsNearTeleport, bool CachesPlayerInfo)
    {
        // Actual RemotePlayer traces cover all 75 releases; generic spawn starts carrying players in 1.20.2, before Entity.recreateFromPacket applies their velocity in 1.21.5.
        public static RemotePlayerProfile ForProtocol(int protocol)
            => new(protocol >= 762 ? 3 : 0, protocol >= 770, protocol is >= 764 and <= 769 or >= 773, protocol < 751, protocol is >= 107 and <= 498, protocol >= 761);
    }
}
