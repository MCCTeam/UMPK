using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns native entity boxes on the session loop. Metadata refresh and position reapplication are separate operations.</summary>
internal sealed partial class EntityGeometryState
{
    private readonly ConditionalWeakTable<Entity, Geometry> _entities = new();
    private readonly ConditionalWeakTable<Entity, TrackedShulkerGeometry> _shulkers = new();
    private readonly ConditionalWeakTable<Entity, TrackedLegacyPlayerGeometry> _legacyPlayers = new();
    private readonly ConditionalWeakTable<Entity, TrackedFallingBlockGeometry> _fallingBlocks = new();
    private readonly ConditionalWeakTable<Entity, TrackedLegacyAmbientGeometry> _legacyAmbient = new();
    private readonly Dictionary<Entity, LegacyPlacementEnvelope> _placementEnvelopes = [];
    internal Func<bool>? PlacementEnvelopeWorldContext { get; set; }
    private World? _legacyWorld;
    private EntityMembershipState? _legacyMembership;
    private LegacyPlayerResizeClearance? _legacyClearance;
    private LegacyEmptyChunkState? _legacyEmptyChunk;
    private bool _hasLivingTickRules;
    private bool _hasShulkers;
    private bool _hasFixedCrystals;
    internal Func<Entity, Aabb, bool>? FixedPositionContext { get; set; }

    internal static bool IsFixedCrystal(Entity entity, int protocol)
        => protocol is >= 769 and <= 777 && entity.Type.Id.IsMinecraft && entity.Type.Id.Path == "end_crystal";


    internal void RegisterPlacementEnvelope(Entity entity, int protocol, EntityStore entities)
    {
        LegacyPlacementEnvelope? envelope = LegacyWolfPlacementEnvelope.Create(entity, protocol)
            ?? LegacyRabbitPlacementEnvelope.Create(entity, protocol)
            ?? LegacyHorsePlacementEnvelope.Create(entity, protocol);
        if (envelope is null)
            return;
        _placementEnvelopes.Add(entity, envelope);
        RefreshPlacementEnvelopes(entities);
    }

    internal void InvalidatePlacementEnvelope(Entity entity)
    {
        if (_placementEnvelopes.TryGetValue(entity, out LegacyPlacementEnvelope? envelope))
            envelope.Invalidate();
    }

    internal void RefreshPlacementEnvelopes(EntityStore? entities)
    {
        if (_placementEnvelopes.Count == 0)
            return;
        bool worldKnown = entities is not null && PlacementEnvelopeWorldContext?.Invoke() is true;
        foreach (var pair in _placementEnvelopes)
            if (!worldKnown || !pair.Value.TryGet(pair.Key, out Aabb envelope)
                || !ExpansionPushIsIrrelevant(pair.Key, entities!, this, pair.Value.Protocol, envelope, envelope))
                pair.Value.Invalidate();
    }

    internal bool IsProvenPlacementNonCandidate(Entity entity, Aabb query)
        => _placementEnvelopes.TryGetValue(entity, out LegacyPlacementEnvelope? envelope)
            && envelope.TryGet(entity, out Aabb bounds) && !bounds.Intersects(query);

    public void Register(Entity entity, IEntityGeometrySource source, int protocol = 0, bool isPlayer = false)
    {
        if (_entities.TryGetValue(entity, out _) || _shulkers.TryGetValue(entity, out _) || _legacyPlayers.TryGetValue(entity, out _) || _fallingBlocks.TryGetValue(entity, out _) || _legacyAmbient.TryGetValue(entity, out _))
            return;
        if (LegacyAmbientMobProfile.TryGet(entity, protocol, out LegacyAmbientMobProfile ambient))
        {
            _legacyAmbient.Add(entity, new TrackedLegacyAmbientGeometry(entity, ambient));
            return;
        }
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
        bool fixedCrystal = IsFixedCrystal(entity, protocol);
        if (fixedCrystal)
            source = FixedCrystalSource.Instance;
        if (!source.TryGetRule(entity.Type.Id, out EntityGeometryRule rule))
            return;
        var geometry = new Geometry(source, rule, rule.ScaleAttribute is not null && LivingScaleProfile.Supports(protocol) ? LivingScaleProfile.ForProtocol(protocol) : null, isPlayer) { FixedCrystal = fixedCrystal };
        _entities.Add(entity, geometry);
        _hasLivingTickRules |= rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick;
        ReapplyPosition(entity, geometry);
        _hasFixedCrystals |= fixedCrystal;
        if (fixedCrystal)
            _ = PositionKnown(entity, geometry);
    }

    public bool TryGet(Entity entity, IEntityGeometrySource source, out Aabb box, out bool blocksBuilding, int protocol = 0)
    {
        Register(entity, source, protocol);
        if (_legacyAmbient.TryGetValue(entity, out TrackedLegacyAmbientGeometry? ambient))
        {
            box = ambient.Bounds;
            blocksBuilding = true;
            return ambient.Known;
        }
        if (_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling))
        {
            if (falling.ObservedPosition != entity.Position)
                falling.ObservePosition(entity);
            blocksBuilding = true;
            return falling.TryGet(out box);
        }
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
        return geometry.Known && PositionKnown(entity, geometry);
    }

    /// <summary>Reads cached native geometry values; success does not establish the actor's current world-query position.</summary>
    internal bool TryGetCachedGeometry(Entity entity, IEntityGeometrySource source, out Aabb box, out bool blocksBuilding, int protocol = 0)
    {
        bool current = TryGet(entity, source, out box, out blocksBuilding, protocol);
        return current || _entities.TryGetValue(entity, out Geometry? geometry) && geometry.Known;
    }

    public void ObserveMetadata(Entity entity, int index)
    {
        if (_placementEnvelopes.TryGetValue(entity, out LegacyPlacementEnvelope? envelope))
            _ = envelope.TryGet(entity, out _);
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
            ObserveDimensions(entity, geometry, refreshScale: true, forceRefresh: geometry.ScaleProfile is not null);
    }

    public void ObservePosition(Entity entity, bool independent = false)
    {
        if (_placementEnvelopes.TryGetValue(entity, out LegacyPlacementEnvelope? envelope))
            envelope.ObserveEndpoint(entity.Position, independent);
        if (_legacyAmbient.TryGetValue(entity, out TrackedLegacyAmbientGeometry? ambient))
        {
            ambient.ObservePosition(entity);
            return;
        }
        if (_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling))
        {
            falling.ObservePosition(entity);
            return;
        }
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
        {
            ReapplyPosition(entity, geometry);
            if (geometry.FixedCrystal && independent)
                geometry.PositionKnown = FixedPositionContext?.Invoke(entity, geometry.Bounds.Box) is true;
            _ = PositionKnown(entity, geometry);
        }
    }

    /// <summary>Returns the native float dimensions without reconstructing them from rounded box endpoints.</summary>
    public bool TryGetDimensions(Entity entity, out EntityGeometryDimensions dimensions)
    {
        if (_legacyAmbient.TryGetValue(entity, out TrackedLegacyAmbientGeometry? ambient))
        {
            dimensions = ambient.Profile.Dimensions;
            return true;
        }
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
        {
            dimensions = new(legacy.Geometry.Width, legacy.Geometry.Height, true, true);
            return legacy.TryGet(out _);
        }
        dimensions = default;
        if (!_entities.TryGetValue(entity, out Geometry? geometry))
            return false;
        ObserveDimensions(entity, geometry);
        dimensions = geometry.Dimensions;
        return geometry.Known && (!geometry.CowMotionActive || PositionKnown(entity, geometry));
    }

    /// <summary>A missing displacement decision makes the position unproven until the owning profile observes a supported recovery event.</summary>
    public void InvalidatePosition(Entity entity)
    {
        InvalidatePlacementEnvelope(entity);
        if (_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy))
            legacy.InvalidatePosition();
        if (_legacyAmbient.TryGetValue(entity, out TrackedLegacyAmbientGeometry? ambient))
            ambient.Known = false;
        if (_entities.TryGetValue(entity, out Geometry? geometry))
            geometry.PositionKnown = false;
    }

    public bool IgnoresVelocity(Entity entity) => _shulkers.TryGetValue(entity, out _);

    public bool IsPositionKnown(Entity entity)
        => (!_entities.TryGetValue(entity, out Geometry? geometry) || PositionKnown(entity, geometry))
            && (!_legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy) || legacy.IsPositionKnown)
            && (!_legacyAmbient.TryGetValue(entity, out TrackedLegacyAmbientGeometry? ambient) || ambient.Known)
            && (!_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling) || falling.Known);

    public bool TryGetLegacyAmbient(Entity entity, out TrackedLegacyAmbientGeometry? geometry) => _legacyAmbient.TryGetValue(entity, out geometry);

    public void BeginCowMotion(Entity entity)
    {
        if (_entities.TryGetValue(entity, out Geometry? geometry))
        {
            geometry.CowMotionActive = true;
            _ = PositionKnown(entity, geometry);
        }
    }

    private bool PositionKnown(Entity entity, Geometry geometry)
    {
        if (geometry.FixedCrystal && FixedPositionContext?.Invoke(entity, geometry.Bounds.Box) is not true)
            geometry.PositionKnown = false;
        if (geometry.CowMotionActive && !CowMotionProfile.IsKnownState(entity, geometry.Rule))
            geometry.PositionKnown = false;
        return geometry.PositionKnown;
    }

    /// <summary>Records uncertainty at the event or tick that can move an otherwise stationary actor. Removing a displacement source cannot restore its earlier position.</summary>
    internal void RefreshFixedPositions(EntityStore? entities)
    {
        RefreshPlacementEnvelopes(entities);
        if (!_hasFixedCrystals || entities is null)
            return;
        foreach (Entity entity in entities.All)
            if (_entities.TryGetValue(entity, out Geometry? geometry) && geometry.FixedCrystal)
                _ = PositionKnown(entity, geometry);
    }

    internal bool FixedContextIsKnown(Entity entity, Aabb bounds, EntityStore? entities, World? world, EntityMembershipState membership, int protocol, bool pistonsClear)
    {
        if (!pistonsClear || entities is null || world is null || entity.Vehicle is not null || entity.Passengers.Count != 0
            || !double.IsFinite(bounds.MinX) || !double.IsFinite(bounds.MaxX) || !double.IsFinite(bounds.MinY) || !double.IsFinite(bounds.MaxY)
            || !double.IsFinite(bounds.MinZ) || !double.IsFinite(bounds.MaxZ) || bounds.MinX <= int.MinValue || bounds.MaxX >= int.MaxValue
            || bounds.MinZ <= int.MinValue || bounds.MaxZ >= int.MaxValue || bounds.MinY < world.Dimension.MinY || bounds.MaxY >= world.Dimension.MinY + world.Dimension.Height
            || !ExpansionPushIsIrrelevant(entity, entities, this, protocol, bounds, bounds))
            return false;
        // The fixed actor has no travel phase. Non-air block callbacks and untracked block entities remain outside the proven empty-body context.
        for (int x = (int)Math.Floor(bounds.MinX); x <= (int)Math.Floor(bounds.MaxX); x++)
            for (int y = (int)Math.Floor(bounds.MinY); y <= (int)Math.Floor(bounds.MaxY); y++)
                for (int z = (int)Math.Floor(bounds.MinZ); z <= (int)Math.Floor(bounds.MaxZ); z++)
                {
                    var cell = new BlockPos(x, y, z);
                    if (membership.HasColumn(cell) && (world.GetColumn(cell) is null || !world.GetBlock(cell).IsValid || !world.GetBlock(cell).IsAir))
                        return false;
                }
        foreach (var pair in world.BlockEntityNbt)
            if (!world.GetBlock(pair.Key).IsValid || world.GetBlock(pair.Key).Block.Id == Identifier.Minecraft("moving_piston")
                || pair.Value.GetString("id") is "minecraft:piston" or "piston")
                return false;
        return true;
    }

    internal static bool ExpansionPushIsIrrelevant(Entity source, EntityStore entities, EntityGeometryState geometry, int protocol, Aabb before, Aabb after)
    {
        foreach (Entity peer in entities.All)
        {
            if (ReferenceEquals(source, peer) || peer.Type.Id != Identifier.Minecraft("shulker"))
                continue;
            if (peer.Vehicle is not null && IsFixedCrystal(source, protocol))
                return false;
            if (!geometry.TryGet(peer, Umpk.Data.Java.JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol))
                return false;
            // The existing all-face envelope includes every remaining half-body lid extension without simulating a push.
            Aabb swept = bounds.Inflate(Math.Max(bounds.XSize, bounds.YSize));
            if (swept.Intersects(before) || swept.Intersects(after))
                return false;
        }
        return true;
    }

    public void RegisterFallingBlock(Entity entity, int protocol = 107) => _fallingBlocks.Add(entity, new TrackedFallingBlockGeometry(entity, protocol));

    public bool TryGetFallingBlock(Entity entity, out Aabb box)
    {
        box = default;
        if (!_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling))
            return false;
        if (falling.ObservedPosition != entity.Position)
            falling.ObservePosition(entity);
        return falling.TryGet(out box);
    }

    public void MoveFallingBlock(Entity entity, Vec3d displacement)
    {
        if (_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling))
            falling.Move(entity, displacement);
    }

    public void InvalidateFallingBlock(Entity entity)
    {
        if (_fallingBlocks.TryGetValue(entity, out TrackedFallingBlockGeometry? falling))
            falling.Invalidate();
    }

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
        => EmptyChunkFor(protocol)?.ObservePartialColumn(membership, column);

    /// <summary>Reads the owned player's effective block context, retaining unknown cells separately from native air.</summary>
    public int ReadLegacyBlockState(Entity entity, World world, BlockPos cell, EntityMembershipState membership)
        => _legacyPlayers.TryGetValue(entity, out TrackedLegacyPlayerGeometry? legacy)
            ? legacy.ReadState(world, cell, membership, _legacyEmptyChunk) : -1;

    internal LegacyEmptyChunkState? EmptyChunkFor(int protocol)
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

    public void ObserveAttributes(Entity entity, bool scaleDirty = false)
    {
        if (_shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker))
        {
            shulker.ObserveAttributes(entity);
            return;
        }
        if (_entities.TryGetValue(entity, out Geometry? geometry))
        {
            if (geometry.ScaleProfile is not null)
                geometry.ScaleDirty |= scaleDirty;
            else
                ObserveDimensions(entity, geometry);
        }
    }

    /// <summary>Applies scale at the end of this eligible native living tick, after inherited movement. Bounds reads and explicit position reapplication keep cached dimensions.</summary>
    public void TickLivingScale(Entity entity, bool runsNormally, bool columnAvailable)
    {
        if (!_entities.TryGetValue(entity, out Geometry? geometry) || geometry.ScaleProfile is not { } profile
            || geometry.Rule.ScaleAttribute is not { } attribute || !geometry.IsPlayer && (!runsNormally || !columnAvailable))
            return;
        float scale = (float)entity.Attributes.GetValueOrDefault(attribute, 1.0);
        if (!float.IsFinite(scale) || scale <= 0F)
        {
            geometry.Known = false;
            return;
        }
        if (profile.NeedsRefresh(geometry.AppliedScale, scale, geometry.ScaleDirty))
        {
            geometry.AppliedScale = scale;
            ObserveDimensions(entity, geometry, refreshScale: true, forceRefresh: true);
        }
        geometry.ScaleDirty = false;
    }

    public void TickLivingDimensions(EntityStore? entities, World? world)
        => TickLivingDimensions(entities, world, includeShulkers: true);

    public void TickLivingDimensions(EntityStore? entities, World? world, bool includeShulkers)
    {
        if ((!_hasLivingTickRules && !_hasShulkers) || entities is null || world is null)
            return;
        foreach (Entity entity in entities.All)
        {
            if (includeShulkers && _shulkers.TryGetValue(entity, out TrackedShulkerGeometry? shulker) && HasLoadedArea(world, entity.Position, 0))
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
        _placementEnvelopes.Remove(entity);
        _entities.Remove(entity);
        _shulkers.Remove(entity);
        _legacyPlayers.Remove(entity);
        _fallingBlocks.Remove(entity);
        _legacyAmbient.Remove(entity);
    }
    public void Clear()
    {
        _placementEnvelopes.Clear();
        _entities.Clear();
        _shulkers.Clear();
        _legacyPlayers.Clear();
        _fallingBlocks.Clear();
        _legacyAmbient.Clear();
        _legacyWorld = null;
        _legacyMembership = null;
        _legacyClearance = null;
        _legacyEmptyChunk = null;
        _hasLivingTickRules = false;
        _hasShulkers = false;
        _hasFixedCrystals = false;
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
        if (geometry.ScaleProfile is not null)
        {
            if (!float.IsFinite(geometry.DimensionsScale) || geometry.DimensionsScale <= 0F)
                return false;
            dimensions = LivingScaleProfile.Apply(dimensions, geometry.DimensionsScale);
            return true;
        }
        return dimensions.Fixed || geometry.Rule.ScaleAttribute is not { } scale || entity.Attributes.GetValueOrDefault(scale, 1.0) == 1.0;
    }

    private static void ObserveDimensions(Entity entity, Geometry geometry, bool livingTick = false, bool refreshScale = false, bool forceRefresh = false)
    {
        if (geometry.Rule.RefreshTiming == EntityGeometryRefreshTiming.LivingTick && !livingTick)
            return;
        if (geometry.ScaleProfile is not null)
        {
            if (!refreshScale)
                return;
            geometry.DimensionsScale = (float)entity.Attributes.GetValueOrDefault(geometry.Rule.ScaleAttribute!.Value, 1.0);
        }
        if (!Resolve(entity, geometry, out bool selector, out EntityPose pose, out EntityGeometryDimensions dimensions))
        {
            geometry.Known = false;
            return;
        }
        if (!geometry.Known)
        {
            if (geometry.ScaleProfile is not null && !geometry.HasDimensions)
            {
                ReapplyPosition(entity, geometry);
                return;
            }
            if ((livingTick || geometry.ScaleProfile is not null) && geometry.HasDimensions)
                geometry.Known = true;
            else
            {
                if (geometry.Rule.Refresh == EntityDimensionsRefresh.Recenter)
                    ReapplyPosition(entity, geometry);
                return;
            }
        }
        if (!forceRefresh && geometry.Selector == selector && geometry.Pose == pose && geometry.Dimensions == dimensions)
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
        if (geometry.ScaleProfile is not null && geometry.HasDimensions)
        {
            geometry.Bounds = VanillaEntityBounds.Create(entity.Position, geometry.Dimensions.Width, geometry.Dimensions.Height);
            return;
        }
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

    private sealed class FixedCrystalSource : IEntityGeometrySource
    {
        internal static FixedCrystalSource Instance { get; } = new();
        public bool TryGetRule(Identifier entityType, out EntityGeometryRule rule)
        {
            rule = new(-1, -1, EntityDimensionsRefresh.Recenter, null);
            return entityType.IsMinecraft && entityType.Path == "end_crystal";
        }
        public bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions)
        {
            dimensions = new(2F, 2F, true, true);
            return entityType.IsMinecraft && entityType.Path == "end_crystal";
        }
    }

    private sealed class Geometry(IEntityGeometrySource source, EntityGeometryRule rule, LivingScaleProfile? scaleProfile, bool isPlayer)
    {
        public IEntityGeometrySource Source { get; } = source;
        public EntityGeometryRule Rule { get; } = rule;
        public LivingScaleProfile? ScaleProfile { get; } = scaleProfile;
        public bool IsPlayer { get; } = isPlayer;
        public float AppliedScale { get; set; } = 1F;
        public float DimensionsScale { get; set; } = 1F;
        public bool ScaleDirty { get; set; }
        public bool Known { get; set; }
        public bool PositionKnown { get; set; } = true;
        public bool HasDimensions { get; set; }
        public bool CowMotionActive { get; set; }
        public bool FixedCrystal { get; init; }
        public bool Selector { get; set; }
        public EntityPose Pose { get; set; }
        public EntityGeometryDimensions Dimensions { get; set; }
        public VanillaEntityBounds Bounds { get; set; }
    }
}
