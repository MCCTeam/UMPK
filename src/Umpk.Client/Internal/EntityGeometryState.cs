using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns native entity boxes on the session loop. Metadata refresh and position reapplication are separate operations.</summary>
internal sealed class EntityGeometryState
{
    private readonly ConditionalWeakTable<Entity, Geometry> _entities = new();

    public void Register(Entity entity, IEntityGeometrySource source)
    {
        if (_entities.TryGetValue(entity, out _) || !source.TryGetRule(entity.Type.Id, out EntityGeometryRule rule))
            return;
        var geometry = new Geometry(source, rule);
        _entities.Add(entity, geometry);
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

    public void Remove(Entity entity) => _entities.Remove(entity);
    public void Clear() => _entities.Clear();

    private static bool Resolve(Entity entity, Geometry geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions)
    {
        selector = false;
        pose = entity.Pose;
        dimensions = default;
        if (entity.Metadata.TryGet(geometry.Rule.MetadataIndex, out MetadataValue raw))
        {
            if (raw.Kind != MetadataValueKind.Boolean)
                return false;
            selector = raw.AsBoolean();
        }
        if (!geometry.Source.TryGetDimensions(entity.Type.Id, selector, pose, out dimensions))
            return false;
        // Attribute-scale transitions need their own native sweep. Keep that state unknown rather than using default dimensions for a scaled entity.
        return dimensions.Fixed || geometry.Rule.ScaleAttribute is not { } scale || entity.Attributes.GetValueOrDefault(scale, 1.0) == 1.0;
    }

    private static void ObserveDimensions(Entity entity, Geometry geometry)
    {
        if (!Resolve(entity, geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions))
        {
            geometry.Known = false;
            return;
        }
        if (!geometry.Known)
        {
            if (geometry.Rule.Refresh == EntityDimensionsRefresh.Recenter)
                ReapplyPosition(entity, geometry);
            return;
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
        geometry.Known = Resolve(entity, geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions);
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
        public bool Selector { get; set; }
        public EntityPose Pose { get; set; }
        public EntityGeometryDimensions Dimensions { get; set; }
        public VanillaEntityBounds Bounds { get; set; }
    }
}
