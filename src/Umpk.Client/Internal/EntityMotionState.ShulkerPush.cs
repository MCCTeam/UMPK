using System.Runtime.CompilerServices;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

internal sealed partial class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, ShulkerPushPlayer> _shulkerPushPlayers = new();
    internal bool HasUnknownShulkerSelfDisplacement { get; private set; }

    internal void ObserveShulkerSelfCorrection(bool absolute)
    {
        if (absolute)
            HasUnknownShulkerSelfDisplacement = false;
    }

    internal void BeginShulkerPushPlayerTick(Entity entity, TabList players)
    {
        if (_remotePlayers.TryGetValue(entity, out RemotePlayerMotion? motion) && ShulkerPushProfile.ForProtocol(motion.Protocol).HasRules)
            _shulkerPushPlayers.GetValue(entity, static _ => new()).NoPhysics = IsSpectator(entity, players);
    }

    internal void ApplyShulkerPush(Entity source, ClientState state, IBlockShapeSource shapes, int protocol, Aabb region, Vec3d requested, List<int> moved, Aabb? localBody)
    {
        ShulkerPushProfile profile = ShulkerPushProfile.ForProtocol(protocol);
        if (!profile.HasRules || state.EntitiesOrNull is not { } entities || state.WorldOrNull is not { } world)
            return;
        // The bounded slice does not simulate local or rider movement. A relevant unowned self body must stop being offered as proven geometry.
        if (state.Self.GameMode != GameMode.Spectator && !(_localMountVehicle is { } localVehicle && SharedShulkerVehicle(source, localVehicle))
            && (IsLocalMountUnresolved || localBody is null || region.Intersects(localBody.Value)))
            HasUnknownShulkerSelfDisplacement = true;
        bool sourceOrderKnown = state.EntityMembership.IsShulkerTickOrderKnown(source);
        foreach (Entity target in entities.All)
        {
            if (ReferenceEquals(source, target) || target.Type.Id == Identifier.Minecraft("shulker") || SharedShulkerVehicle(source, target))
                continue;
            if (!sourceOrderKnown && _remotePlayers.TryGetValue(target, out RemotePlayerMotion? ordering)
                && ordering.Position?.HasActiveInterpolation == true && !IsKnownSpectator(target, state.TabList))
            {
                state.EntityGeometry.InvalidatePosition(target);
                continue;
            }
            bool? member = state.EntityMembership.IsWorldQueryMember(target);
            if (member == false)
                continue;
            if (!state.EntityGeometry.IsPositionKnown(target)
                || !state.EntityGeometry.TryGet(target, JavaGameData.EntityGeometryData(protocol), out Aabb box, out _, protocol) || !ShulkerPushProfile.Finite(box))
            {
                state.EntityGeometry.InvalidatePosition(target);
                continue;
            }
            if (!region.Intersects(box))
                continue;
            if (member is null || !profile.IncludesSection(region, target.Position))
            {
                if (member is null)
                    state.EntityGeometry.InvalidatePosition(target);
                continue;
            }
            // The native query evaluates the lazy spectator predicate after spatial membership; the loop checks noPhysics after that query.
            if (IsSpectator(target, state.TabList))
                continue;
            if (!sourceOrderKnown)
            {
                state.EntityGeometry.InvalidatePosition(target);
                continue;
            }
            if (_remotePlayers.TryGetValue(target, out RemotePlayerMotion? player)
                && (!_shulkerPushPlayers.TryGetValue(target, out ShulkerPushPlayer? ticked) || ticked.NoPhysics))
                continue;
            if (player is null || player.Position is not { } position || target.Pose != EntityPose.Standing || target.Vehicle is not null || target.Passengers.Count != 0 || IsLocalMountUnresolved || ReferenceEquals(target, _localMountVehicle) || !profile.HasSupportedPlayerLifecycle(target)
                || !state.EntityGeometry.TryGetDimensions(target, out EntityGeometryDimensions dimensions)
                || !profile.TryStepHeight(target, out float stepHeight) || !profile.HasUnitSpeedContext(world, state.EntityMembership, box, requested, stepHeight)
                || !profile.HardQueryContextMatches(state, protocol, box.ExpandTowards(requested).ExpandTowards(0, stepHeight, 0)))
            {
                state.EntityGeometry.InvalidatePosition(target);
                continue;
            }
            PullPosition(target, position);
            position.RefreshDimensions(dimensions.Width, dimensions.Height, recenter: true);
            player.Collision ??= new ModernPlayerCollision(target, entities, state.EntityMembership, this, state.EntityGeometry,
                JavaGameData.EntityGeometryData(protocol), protocol, world, shapes);
            try
            {
                if (!player.Collision.TryCollideWithoutStep(box, requested, stepHeight, out Vec3d actual))
                {
                    state.EntityGeometry.InvalidatePosition(target);
                    continue;
                }
                Vec3d before = target.Position;
                if (actual.LengthSqr() > 1.0E-7 || requested.LengthSqr() - actual.LengthSqr() < 1.0E-7)
                {
                    target.Position = before.Add(actual);
                    position.SetPosition(target.Position);
                    if (profile.RecordsPredictedMovement)
                        position.ApplyPredictedMovement(actual, player.Collision.NoCollision);
                    state.EntityGeometry.ObservePosition(target);
                }
                if (requested.Y != 0)
                    target.OnGround = requested.Y != actual.Y && requested.Y < 0;
                if (profile.ClipsRemoteHorizontalVelocity)
                    target.Velocity = new Vec3d(Math.Abs(requested.X - actual.X) < 1.0E-5F ? target.Velocity.X : 0,
                        target.Velocity.Y, Math.Abs(requested.Z - actual.Z) < 1.0E-5F ? target.Velocity.Z : 0);
                if (before != target.Position)
                    moved.Add(target.Id);
            }
            catch (UnknownEntityCollisionContextException)
            {
                state.EntityGeometry.InvalidatePosition(target);
            }
        }
    }

    internal void InvalidateUnownedShulkerPush(ClientState state)
    {
        if (state.Self.GameMode != GameMode.Spectator)
            HasUnknownShulkerSelfDisplacement = true;
        foreach (Entity target in state.Entities.All)
            if (target.Type.Id != Identifier.Minecraft("shulker"))
                state.EntityGeometry.InvalidatePosition(target);
    }

    private static bool SharedShulkerVehicle(Entity first, Entity second)
    {
        for (Entity? left = first; left is not null; left = left.Vehicle)
            for (Entity? right = second; right is not null; right = right.Vehicle)
                if (ReferenceEquals(left, right))
                    return true;
        return false;
    }

    private sealed class ShulkerPushPlayer
    {
        // Native RemotePlayer's constructor sets true. Player.tick assigns isSpectator before inherited interpolation or a later actor's push.
        internal bool NoPhysics { get; set; } = true;
    }
}

internal readonly record struct ShulkerPushProfile(bool HasRules, bool RecordsPredictedMovement, bool ClipsRemoteHorizontalVelocity)
{
    internal static ShulkerPushProfile ForProtocol(int protocol) => new(protocol is >= 770 and <= 777, protocol == 777, protocol < 777);

    internal bool IncludesSection(Aabb query, Vec3d position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
            return false;
        return Math.Floor(position.X / 16) >= Math.Floor((query.MinX - 2) / 16) && Math.Floor(position.X / 16) <= Math.Floor((query.MaxX + 2) / 16)
            && Math.Floor(position.Y / 16) >= Math.Floor((query.MinY - 4) / 16) && Math.Floor(position.Y / 16) <= Math.Floor(query.MaxY / 16)
            && Math.Floor(position.Z / 16) >= Math.Floor((query.MinZ - 2) / 16) && Math.Floor(position.Z / 16) <= Math.Floor((query.MaxZ + 2) / 16);
    }

    internal static bool Finite(Aabb box) => double.IsFinite(box.MinX) && double.IsFinite(box.MaxX) && double.IsFinite(box.MinY)
        && double.IsFinite(box.MaxY) && double.IsFinite(box.MinZ) && double.IsFinite(box.MaxZ);

    internal bool HardQueryContextMatches(ClientState state, int protocol, Aabb query)
    {
        Aabb candidates = query.Inflate(1.0E-7);
        foreach (Entity peer in state.Entities.All)
        {
            if (peer.Type.Id != Identifier.Minecraft("shulker"))
                continue;
            if (!state.EntityGeometry.TryGet(peer, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol) || !Finite(bounds))
                return false;
            // The existing hard-collider adapter has no section-index filter. Do not let an indexed-out oversized or boundary peer become a false collider in this bounded caller.
            if (candidates.Intersects(bounds) && !IncludesSection(candidates, peer.Position))
                return false;
        }
        return true;
    }

    internal bool HasSupportedPlayerLifecycle(Entity target)
    {
        if (target.Metadata.TryGet(14, out MetadataValue sleeping)
            && (sleeping.Kind != MetadataValueKind.OptionalPosition || sleeping.AsOptionalPosition() is not null))
            return false;
        return !target.Metadata.TryResolve(EntityMetadataKeys.Health, out MetadataValue health)
            || health.Kind == MetadataValueKind.Float && health.AsFloat() > 0;
    }

    internal bool TryStepHeight(Entity target, out float height)
    {
        height = (float)target.Attributes.GetValueOrDefault(Identifier.Minecraft("step_height"), .6D);
        return float.IsFinite(height) && height is >= 0 and <= 10;
    }

    internal bool HasUnitSpeedContext(World world, EntityMembershipState membership, Aabb box, Vec3d requested, float stepHeight)
    {
        Aabb region = box.ExpandTowards(requested).ExpandTowards(0, stepHeight, 0);
        if (region.MinX < int.MinValue + 4D || region.MaxX > int.MaxValue - 4D || region.MinY < int.MinValue + 4D || region.MaxY > int.MaxValue - 4D
            || region.MinZ < int.MinValue + 4D || region.MaxZ > int.MaxValue - 4D || region.XSize > 16 || region.YSize > 32 || region.ZSize > 16
            || region.MinX <= world.Border.MinX + 2 || region.MaxX >= world.Border.MaxX - 2 || region.MinZ <= world.Border.MinZ + 2 || region.MaxZ >= world.Border.MaxZ - 2)
            return false;
        // Every possible cached/below-foot speed-factor read is one in this bounded context. Non-unit factors and border shapes require their own mover witness.
        for (int x = (int)Math.Floor(region.MinX) - 1; x <= (int)Math.Floor(region.MaxX) + 1; x++)
            for (int z = (int)Math.Floor(region.MinZ) - 1; z <= (int)Math.Floor(region.MaxZ) + 1; z++)
                for (int y = (int)Math.Floor(region.MinY) - 2; y <= (int)Math.Floor(region.MaxY) + 1; y++)
                {
                    var cell = new BlockPos(x, y, z);
                    if (!membership.HasColumn(cell))
                        continue;
                    var state = world.GetBlock(cell);
                    if (!state.IsValid || state.SpeedFactor != 1F)
                        return false;
                }
        return true;
    }
}
