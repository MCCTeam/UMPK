using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Instance-owned native motion controllers. Observations and ticks run on the session loop.</summary>
internal sealed class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, RemotePlayerMotion> _remotePlayers = new();
    private readonly ConditionalWeakTable<Entity, object> _immovable = new();
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

    public bool IsSpectator(Entity entity, TabList players)
        => _remotePlayers.TryGetValue(entity, out RemotePlayerMotion? player)
            && ResolvePlayerInfo(entity, player, players)?.GameMode == GameMode.Spectator;

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
        {
            if (!_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion))
                continue;
            if (players is not null)
                ResolvePlayerInfo(entity, motion, players);
            if (motion.Position is { } position)
            {
                Vec3d before = entity.Position;
                PullPosition(entity, position);
                bool reappliesPosition = position.HasActiveInterpolation;
                position.Tick(static _ => throw new InvalidOperationException("Legacy interpolation must not request world collisions."));
                PushPosition(entity, position);
                if (reappliesPosition)
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
    }

    public void Remove(Entity entity)
    {
        _remotePlayers.Remove(entity);
        _immovable.Remove(entity);
    }

    public void Clear()
    {
        _remotePlayers.Clear();
        _immovable.Clear();
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

    private readonly record struct RemotePlayerProfile(int VelocitySteps, bool AppliesSpawnVelocity, bool UsesPendingComponents, bool SuppressesNearTeleportPosition, bool CancelsNearTeleport, bool CachesPlayerInfo)
    {
        // Actual RemotePlayer traces cover all 75 releases; generic spawn starts carrying players in 1.20.2, before Entity.recreateFromPacket applies their velocity in 1.21.5.
        public static RemotePlayerProfile ForProtocol(int protocol)
            => new(protocol >= 762 ? 3 : 0, protocol >= 770, protocol is >= 764 and <= 769 or >= 773, protocol < 751, protocol is >= 107 and <= 498, protocol >= 761);
    }
}
