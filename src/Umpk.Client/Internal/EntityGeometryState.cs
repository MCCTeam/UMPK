using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns native entity boxes on the session loop. Metadata refresh and position reapplication are separate operations.</summary>
internal sealed class EntityGeometryState
{
    private readonly ConditionalWeakTable<Entity, Geometry> _entities = new();
    private readonly ConditionalWeakTable<Entity, TrackedShulkerGeometry> _shulkers = new();
    private readonly ConditionalWeakTable<Entity, TrackedLegacyPlayerGeometry> _legacyPlayers = new();
    private World? _legacyWorld;
    private EntityMembershipState? _legacyMembership;
    private LegacyPlayerResizeClearance? _legacyClearance;
    private LegacyEmptyChunkState? _legacyEmptyChunk;
    private bool _hasLivingTickRules;
    private bool _hasShulkers;

    public void Register(Entity entity, IEntityGeometrySource source, int protocol = 0, bool isPlayer = false)
    {
        if (_entities.TryGetValue(entity, out _) || _shulkers.TryGetValue(entity, out _) || _legacyPlayers.TryGetValue(entity, out _))
            return;
        if (isPlayer && LegacyPlayerGeometryProfile.Supports(protocol))
        {
            _legacyPlayers.Add(entity, new TrackedLegacyPlayerGeometry(entity, protocol));
            _ = EmptyChunkFor(protocol);
            return;
        }
        if (TrackedShulkerGeometry.TryCreate(entity, protocol, out TrackedShulkerGeometry? shulker) && shulker is not null)
        {
            _shulkers.Add(entity, shulker);
            _hasShulkers = true;
            return;
        }
        if (!source.TryGetRule(entity.Type.Id, out EntityGeometryRule rule))
            return;
        var geometry = new Geometry(source, rule);
        _entities.Add(entity, geometry);
        _hasLivingTickRules |= rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick;
        ReapplyPosition(entity, geometry);
    }

    public bool TryGet(Entity entity, IEntityGeometrySource source, out Aabb box, out bool blocksBuilding, int protocol = 0)
    {
        Register(entity, source, protocol);
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
        {
            blocksBuilding = true;
            return legacy.TryGet(entity, out box);
        }
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
        {
            blocksBuilding = true;
            return shulker.TryGet(entity, out box);
        }
        if (!_entities.TryGetValue(entity, out Geometry? geometry))
        {
            box = default;
            blocksBuilding = false;
            return false;
        }
        ObserveDimensions(entity, geometry);
        if (geometry.Bounds.Position != entity.Position)
            ReapplyPosition(entity, geometry);
        box = geometry.Bounds.Box;
        blocksBuilding = geometry.Dimensions.BlocksBuilding;
        return geometry.Known;
    }

    public void ObserveMetadata(Entity entity, int index)
    {
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
        {
            legacy.ObserveMetadata(entity, index);
            return;
        }
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
        {
            shulker.ObserveMetadata(entity, index);
            return;
        }
        if (_entities.TryGetValue(entity, out Geometry? geometry) && (index == geometry.Rule.MetadataIndex || index == geometry.Rule.PoseMetadataIndex))
            ObserveDimensions(entity, geometry);
    }

    public void ObservePosition(Entity entity)
    {
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
        {
            legacy.ObservePosition(entity);
            return;
        }
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
        {
            shulker.ObservePosition(entity);
            return;
        }
        if (_entities.TryGetValue(entity, out Geometry? geometry))
            ReapplyPosition(entity, geometry);
    }

    public bool IgnoresVelocity(Entity entity) => _shulkers.TryGetValue(entity, out _);

    public bool IsPositionKnown(Entity entity)
        => !_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy) || legacy.IsPositionKnown;

    public void TickLegacyPlayer(Entity entity, World? world, IBlockShapeSource shapes, int protocol, EntityMembershipState? membership = null)
    {
        if (!_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
            return;
        if (!ReferenceEquals(world, _legacyWorld) || !ReferenceEquals(membership, _legacyMembership))
        {
            _legacyWorld = world;
            _legacyMembership = membership;
            _legacyClearance = world is not null && LegacyPlayerGeometryProfile.ForProtocol(protocol).HasSizePhase
                ? new LegacyPlayerResizeClearance(world, shapes, protocol, membership)
                : null;
        }
        legacy.Tick(_legacyClearance);
    }

    public bool BeginLegacySleep(Entity entity, World? world, IBlockInteractionSource source, BlockPos bed, EntityMembershipState? membership = null)
        => _legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy) && legacy.BeginSleep(entity, world, source, bed, membership, _legacyEmptyChunk);

    public void WakeLegacyPlayer(Entity entity, World? world, IBlockInteractionSource source, EntityMembershipState? membership = null)
    {
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
            legacy.Wake(entity, world, source, membership, _legacyEmptyChunk);
    }

    public void ObserveLegacyBlockUpdate(int protocol, EntityMembershipState membership, BlockPos cell, int state)
        => EmptyChunkFor(protocol)?.ObserveWrite(membership, cell, state);

    public void ObserveLegacyPartialColumn(int protocol, EntityMembershipState membership, ChunkColumn column)
        => EmptyChunkFor(protocol)?.InvalidatePartialColumn(membership, column);

    private LegacyEmptyChunkState? EmptyChunkFor(int protocol)
        => LegacyPlayerGeometryProfile.Supports(protocol) && LegacyPlayerGeometryProfile.ForProtocol(protocol).RetainsEmptyChunkWrites
            ? _legacyEmptyChunk ??= new LegacyEmptyChunkState() : null;

    public bool ObserveLegacyDeath(Entity entity)
    {
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
        {
            legacy.HandleDeath();
            return true;
        }
        return false;
    }

    public void ObserveMount(Entity entity)
    {
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
            shulker.ObserveMount(entity);
    }

    public void ObserveAttributes(Entity entity)
    {
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
        {
            shulker.ObserveAttributes(entity);
            return;
        }
        if (_entities.TryGetValue(entity, out Geometry? geometry))
            ObserveDimensions(entity, geometry);
    }

    public void TickLivingDimensions(EntityStore? entities, World? world)
    {
        if ((!_hasLivingTickRules && !_hasShulkers) || entities is null || world is null)
            return;
        foreach (Entity entity in entities.All)
        {
            if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker) && HasLoadedArea(world, entity.Position, 0))
                shulker.Tick(entity);
            if (_entities.TryGetValue(entity, out Geometry? geometry)
                && geometry.Rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick
                && HasLoadedArea(world, entity.Position, geometry.Rule.RequiredLoadedRadius))
                ObserveDimensions(entity, geometry, livingTick: true);
        }
    }

    private static bool HasLoadedArea(World world, Vec3d point, int radius)
    {
        if (radius is < 0 or > 32 || !double.IsFinite(point.X) || !double.IsFinite(point.Z))
            return false;
        int x = (int)Math.Floor(point.X), z = (int)Math.Floor(point.Z);
        for (int columnX = (x - radius) >> 4; columnX <= (x + radius) >> 4; columnX++)
            for (int columnZ = (z - radius) >> 4; columnZ <= (z + radius) >> 4; columnZ++)
                if (world.GetColumn(new ChunkPos(columnX, columnZ)) is null)
                    return false;
        return true;
    }

    public void Remove(Entity entity)
    {
        _entities.Remove(entity);
        _shulkers.Remove(entity);
        _legacyPlayers.Remove(entity);
    }
    public void Clear()
    {
        _entities.Clear();
        _shulkers.Clear();
        _legacyPlayers.Clear();
        _legacyWorld = null;
        _legacyMembership = null;
        _legacyClearance = null;
        _legacyEmptyChunk = null;
        _hasLivingTickRules = false;
        _hasShulkers = false;
    }

    private static bool Resolve(Entity entity, Geometry geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions)
    {
        selector = false;
        pose = entity.Pose;
        dimensions = default;
        if ((geometry.Rule.MetadataIndex < 0) != (geometry.Rule.SelectorKind == EntityGeometrySelectorKind.None))
            return false;
        if (entity.Metadata.TryGet(geometry.Rule.MetadataIndex, out MetadataValue raw))
        {
            switch (geometry.Rule.SelectorKind)
            {
                case EntityGeometrySelectorKind.Boolean when raw.Kind == MetadataValueKind.Boolean:
                    selector = raw.AsBoolean();
                    break;
                case EntityGeometrySelectorKind.NegativeByte when raw.Kind == MetadataValueKind.Byte:
                    selector = raw.AsByte() < 0;
                    break;
                default:
                    return false;
            }
        }
        if (!geometry.Source.TryGetDimensions(entity.Type.Id, selector, pose, out dimensions))
            return false;
        // Attribute-scale transitions need their own native sweep. Keep that state unknown rather than using default dimensions for a scaled entity.
        return dimensions.Fixed || geometry.Rule.ScaleAttribute is not { } scale || entity.Attributes.GetValueOrDefault(scale, 1.0) == 1.0;
    }

    private static void ObserveDimensions(Entity entity, Geometry geometry, bool livingTick = false)
    {
        if (geometry.Rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick && !livingTick)
            return;
        if (!Resolve(entity, geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions))
        {
            geometry.Known = false;
            return;
        }
        if (!geometry.Known)
        {
            if (livingTick && geometry.HasDimensions)
                geometry.Known = true;
            else
            {
                if (geometry.Rule.Refresh == EntityDimensionsRefresh.Recenter)
                    ReapplyPosition(entity, geometry);
                return;
            }
        }
        if (geometry.Selector == selector && geometry.Pose == pose && geometry.Dimensions == dimensions)
            return;
        VanillaEntityBounds previous = geometry.Bounds;
        geometry.Bounds = geometry.Rule.Refresh == EntityDimensionsRefresh.RetainMinimum
            ? new VanillaEntityBounds(entity.Position, dimensions.Width, dimensions.Height, new Aabb(previous.Box.MinX, previous.Box.MinY, previous.Box.MinZ, previous.Box.MinX + dimensions.Width, previous.Box.MinY + dimensions.Height, previous.Box.MinZ + dimensions.Width))
            : previous.Refresh(dimensions.Width, dimensions.Height, geometry.Rule.Refresh == EntityDimensionsRefresh.Recenter);
        geometry.Selector = selector;
        geometry.Pose = pose;
        geometry.Dimensions = dimensions;
    }

    private static void ReapplyPosition(Entity entity, Geometry geometry)
    {
        if (geometry.Rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick)
        {
            if (!geometry.HasDimensions)
            {
                // Native construction starts with the watcher default. A later metadata update waits for its living phase, including when a position is reapplied first.
                geometry.Known = geometry.Source.TryGetDimensions(entity.Type.Id, false, entity.Pose, out EntityGeometryDimensions initial);
                if (!geometry.Known)
                    return;
                geometry.Selector = false;
                geometry.Pose = entity.Pose;
                geometry.Dimensions = initial;
                geometry.HasDimensions = true;
            }
            geometry.Bounds = VanillaEntityBounds.Create(entity.Position, geometry.Dimensions.Width, geometry.Dimensions.Height);
            return;
        }
        geometry.Known = Resolve(entity, geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions);
        geometry.HasDimensions = geometry.Known;
        geometry.Selector = selector;
        geometry.Pose = pose;
        geometry.Dimensions = dimensions;
        geometry.Bounds = geometry.Known ? VanillaEntityBounds.Create(entity.Position, dimensions.Width, dimensions.Height) : default;
    }

    private sealed class Geometry(IEntityGeometrySource source, EntityGeometryRule rule)
    {
        public IEntityGeometrySource Source { get; } = source;
        public EntityGeometryRule Rule { get; } = rule;
        public bool Known { get; set; }
        public bool HasDimensions { get; set; }
        public bool Selector { get; set; }
        public EntityPose Pose { get; set; }
        public EntityGeometryDimensions Dimensions { get; set; }
        public VanillaEntityBounds Bounds { get; set; }
    }
}
