using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

internal sealed partial class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, BoatMotion> _boats = new();

    public void RegisterBoat(Entity entity, int protocol, EntityGeometryState geometry, EntityMembershipState membership, JukeboxPredictionState current)
    {
        if (!BoatMotionProfile.Supports(entity, protocol))
            return;
        var position = new VanillaEntityInterpolation(BoatMotionProfile.Interpolation(protocol), entity.Position, entity.Yaw, entity.Pitch, BoatMotionProfile.Width, BoatMotionProfile.Height, 3);
        position.SetCurrentRotation(entity.Yaw, entity.Pitch);
        _boats.Add(entity, new BoatMotion(protocol, position, geometry, membership, current));
        geometry.BoatPositionContext = IsBoatPositionKnown;
    }

    public bool IsTrackedBoat(Entity entity) => _boats.TryGetValue(entity, out _);

    private bool? IsBoatPositionKnown(Entity entity)
    {
        if (!_boats.TryGetValue(entity, out BoatMotion? boat))
            return null;
        return BoatMotionProfile.Supports(entity, boat.Protocol) && BoatMotionProfile.IsKnownState(entity)
            && entity.Position == boat.Position.Position && entity.Yaw == boat.Position.Yaw && entity.Pitch == boat.Position.Pitch;
    }

    private bool TryObserveBoatInterpolation(Entity entity, Vec3d? target, float? yaw, float? pitch)
    {
        if (!_boats.TryGetValue(entity, out BoatMotion? boat))
            return false;
        if (!boat.Geometry.IsPositionKnown(entity))
            return true;
        // The measured 770 listener supplies current values for omitted components.
        boat.Position.SetTarget(target ?? boat.Position.Position, yaw ?? boat.Position.Yaw, pitch ?? boat.Position.Pitch);
        return true;
    }

    private bool TryObserveBoatSync(Entity entity, PositionMoveRotation values)
    {
        if (!_boats.TryGetValue(entity, out BoatMotion? boat))
            return false;
        if (!boat.Geometry.IsPositionKnown(entity))
            return true;
        if (!boat.Membership.CanTick(entity) || boat.Position.Position.Subtract(values.Position).LengthSqr() > 4096.0)
        {
            // Original listener snap keeps an older pending interpolation target.
            boat.Position.SetPosition(values.Position);
            boat.Position.SetCurrentRotation(values.YRot, values.XRot);
            PushPosition(entity, boat.Position);
            boat.Geometry.ObservePosition(entity);
        }
        else
            boat.Position.SetTarget(values.Position, values.YRot, values.XRot);
        return true;
    }

    private bool InvalidateBoatContext(Entity entity)
    {
        if (!_boats.TryGetValue(entity, out BoatMotion? boat))
            return false;
        boat.Geometry.InvalidatePosition(entity);
        return true;
    }

    private void TickBoat(Entity entity, BoatMotion boat, List<int>? moved, World? world, IBlockShapeSource? shapes, EntityStore? entities, SelfState? self, Aabb? localBody)
    {
        EntityGeometryState geometry = boat.Geometry;
        if (!geometry.IsPositionKnown(entity) || !boat.Membership.CanTick(entity))
            return;
        if (world is null || shapes is null || entities is null || self is null || !ReferenceEquals(shapes, JavaGameData.BlockShapes(boat.Protocol))
            || world.BlockData is not RegistryBlockDataSource || !ReferenceEquals(world.BlockData.Blocks, JavaGameData.Registries(boat.Protocol).Blocks) || !_localPushPositionKnown
            || _localPushPoseUnsupported || _localPushBedUnsupported || IsLocalMountUnresolved || self.CameraEntityId is not null)
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        VanillaEntityInterpolation position = boat.Position;
        Aabb before = position.Bounds;
        Vec3d previous = entity.Position;
        bool KnownContext(Aabb box) => BoatTerrainIsNeutral(world, boat.Membership, boat.Current, boat.Protocol, box)
            && BoatPeersAreIrrelevant(entity, entities, geometry, boat.Membership, boat.Protocol, box, self, localBody);
        if (!KnownContext(before))
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        try
        {
            ModernPlayerCollision collision = boat.Collision ??= new ModernPlayerCollision(entity, entities, boat.Membership, this, geometry, JavaGameData.EntityGeometryData(boat.Protocol), boat.Protocol, world, shapes);
            position.Tick(box => KnownContext(box)
                ? collision.NoCollision(box) : throw new UnknownEntityCollisionContextException());
        }
        catch (UnknownEntityCollisionContextException)
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        // Native block callbacks inspect the movement path; both endpoints alone do not prove those callbacks irrelevant.
        if (!KnownContext(BoatSweep(before, position.Bounds)))
        {
            geometry.InvalidatePosition(entity);
            return;
        }
        PushPosition(entity, position);
        // Original AbstractBoat clears remote uncontrolled velocity after interpolation. It does not use living travel or decay.
        entity.Velocity = Vec3d.Zero;
        geometry.ObservePosition(entity);
        if (previous != entity.Position)
            moved?.Add(entity.Id);
    }

    private static Aabb BoatSweep(Aabb first, Aabb last) => new(Math.Min(first.MinX, last.MinX), Math.Min(first.MinY, last.MinY), Math.Min(first.MinZ, last.MinZ),
        Math.Max(first.MaxX, last.MaxX), Math.Max(first.MaxY, last.MaxY), Math.Max(first.MaxZ, last.MaxZ));

    private static bool BoatTerrainIsNeutral(World world, EntityMembershipState membership, JukeboxPredictionState current, int protocol, Aabb bounds)
    {
        // This enclosure covers original fluid/status, ground-friction and swept block-effect reads. Extra cells are deliberately conservative.
        Aabb box = bounds.Inflate(2);
        var canonical = JavaGameData.Registries(protocol).Blocks;
        RegistryEntry<BlockDefinition> air = canonical[Identifier.Minecraft("air")], stone = canonical[Identifier.Minecraft("stone")];
        if (!double.IsFinite(box.MinX) || !double.IsFinite(box.MaxX) || !double.IsFinite(box.MinY) || !double.IsFinite(box.MaxY) || !double.IsFinite(box.MinZ) || !double.IsFinite(box.MaxZ)
            || box.MinX <= int.MinValue || box.MaxX >= int.MaxValue || box.MinZ <= int.MinValue || box.MaxZ >= int.MaxValue
            || box.MinY < world.Dimension.MinY || box.MaxY >= world.Dimension.MaxY)
            return false;
        int minX = (int)Math.Floor(box.MinX), maxX = (int)Math.Floor(box.MaxX);
        int minY = (int)Math.Floor(box.MinY), maxY = (int)Math.Floor(box.MaxY);
        int minZ = (int)Math.Floor(box.MinZ), maxZ = (int)Math.Floor(box.MaxZ);
        // This is a knowledge budget, not a native movement limit. A larger unowned path stays unknown.
        if ((double)(maxX - (long)minX + 1) * (maxY - (long)minY + 1) * (maxZ - (long)minZ + 1) > 4096)
            return false;
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var cell = new BlockPos(x, y, z);
                    if (!membership.HasColumn(cell) || world.GetColumn(cell) is null)
                        return false;
                    var state = world.GetBlock(cell);
                    // This collision adapter reads received terrain. A different effective local state leaves its result unowned.
                    if (current.GetBlock(world, cell).StateId != state.StateId)
                        return false;
                    if (!state.IsValid || state.Block != air && state.Block != stone || state.StateId != state.Block.Value.DefaultStateId)
                        return false;
                }
        foreach (var pair in world.BlockEntityNbt)
            if (!world.GetBlock(pair.Key).IsValid || world.GetBlock(pair.Key).Block.Id == Identifier.Minecraft("moving_piston") || pair.Value.GetString("id") is "minecraft:piston" or "piston")
                return false;
        return true;
    }

    private static bool BoatPeersAreIrrelevant(Entity source, EntityStore entities, EntityGeometryState geometry, EntityMembershipState membership, int protocol, Aabb box, SelfState self, Aabb? localBody)
    {
        Aabb query = box.Inflate(.25);
        if (!CowLocalPushIsIrrelevant(self, localBody, query, JavaGameData.EntityGeometryData(protocol))
            || !EntityGeometryState.ExpansionPushIsIrrelevant(source, entities, geometry, protocol, box, box))
            return false;
        foreach (Entity peer in entities.All)
        {
            if (ReferenceEquals(source, peer) || peer.Id == self.EntityId || membership.IsWorldQueryMember(peer) is false)
                continue;
            if (membership.IsWorldQueryMember(peer) is not true
                || !geometry.TryGet(peer, JavaGameData.EntityGeometryData(protocol), out Aabb peerBounds, out _, protocol)
                || query.Intersects(peerBounds))
                return false;
        }
        return true;
    }

    private sealed class BoatMotion(int protocol, VanillaEntityInterpolation position, EntityGeometryState geometry, EntityMembershipState membership, JukeboxPredictionState current)
    {
        public int Protocol { get; } = protocol;
        public VanillaEntityInterpolation Position { get; } = position;
        public EntityGeometryState Geometry { get; } = geometry;
        public EntityMembershipState Membership { get; } = membership;
        public JukeboxPredictionState Current { get; } = current;
        public ModernPlayerCollision? Collision { get; set; }
    }
}
