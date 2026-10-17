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
    private bool _hasLivingTickRules;

    public void Register(Entity entity, IEntityGeometrySource source)
    {
        if (_entities.TryGetValue(entity, out _) || !source.TryGetRule(entity.Type.Id, out EntityGeometryRule rule))
            return;
        var geometry = new Geometry(source, rule);
        _entities.Add(entity, geometry);
        _hasLivingTickRules |= rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick;
        ReapplyPosition(entity, geometry);
    }

    public bool TryGet(Entity entity, IEntityGeometrySource source, out Aabb box, out bool blocksBuilding)
    {
        Register(entity, source);
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
        if (_entities.TryGetValue(entity, out Geometry? geometry) && (index == geometry.Rule.MetadataIndex || index == geometry.Rule.PoseMetadataIndex))
            ObserveDimensions(entity, geometry);
    }

    public void ObservePosition(Entity entity)
    {
        if (_entities.TryGetValue(entity, out Geometry? geometry))
            ReapplyPosition(entity, geometry);
    }

    public void ObserveAttributes(Entity entity)
    {
        if (_entities.TryGetValue(entity, out Geometry? geometry))
            ObserveDimensions(entity, geometry);
    }

    public void TickLivingDimensions(EntityStore? entities, World? world)
    {
        if (!_hasLivingTickRules || entities is null || world is null)
            return;
        foreach (Entity entity in entities.All)
            if (_entities.TryGetValue(entity, out Geometry? geometry)
                && geometry.Rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick
                && HasLoadedArea(world, entity.Position, geometry.Rule.RequiredLoadedRadius))
                ObserveDimensions(entity, geometry, livingTick: true);
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

    public void Remove(Entity entity) => _entities.Remove(entity);
    public void Clear()
    {
        _entities.Clear();
        _hasLivingTickRules = false;
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
