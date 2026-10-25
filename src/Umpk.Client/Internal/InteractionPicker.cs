using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

internal enum InteractionPickKind
{
    Unknown,
    Miss,
    Block,
    Entity,
}

internal readonly record struct InteractionPickResult(InteractionPickKind Kind, BlockHitResult? BlockHit = null, int? EntityId = null)
{
    public static InteractionPickResult Unknown => new(InteractionPickKind.Unknown);
    public static InteractionPickResult Miss => new(InteractionPickKind.Miss);
}

internal readonly record struct InteractionPickProfile(bool AttributeReach, bool LegacyBoxClip, float PickRadius, bool ItemAttackRange, bool ModernNonparticipants)
{
    // Original 1.8.9 bfk/pk, 1.9 bng/rr, 1.13.2 cty/cea and mapped GameRenderer/ProjectileUtil: radius changes at 107, box clipping at 393, attribute reach at 766, and LocalPlayer's item-range override at 774.
    private bool LegacyExperienceOrbAlias { get; init; }

    public static InteractionPickProfile ForProtocol(int protocol) => new(protocol >= 766, protocol < 393, protocol == 47 ? .1F : 0, protocol >= 774, protocol is >= 769 and <= 777)
    {
        LegacyExperienceOrbAlias = protocol is >= 107 and <= 340,
    };

    public bool IsNativeNonparticipant(Identifier type)
        // Original inherited Entity.isPickable is false for these modern classes. The dragon root stays unknown because native queries also include its pickable parts.
        => type.IsMinecraft && (type.Path is "item" or "experience_orb"
            || LegacyExperienceOrbAlias && type.Path == "xp_orb"
            || ModernNonparticipants && type.Path is "area_effect_cloud" or "block_display" or "evoker_fangs" or "eye_of_ender" or "item_display" or "lightning_bolt" or "marker" or "ominous_item_spawner" or "text_display");

    public (double Block, double Entity) Ranges(ClientSessionServices services, double requestedBlockRange)
    {
        bool creative = services.Self.GameMode == GameMode.Creative;
        double block = AttributeReach ? services.Self.Attributes.Value(Identifier.Minecraft("block_interaction_range"), 4.5) : creative ? 5 : 4.5;
        double entity = AttributeReach ? services.Self.Attributes.Value(Identifier.Minecraft("entity_interaction_range"), 3) : creative ? 6 : 3;
        return (Math.Min(requestedBlockRange, block), entity);
    }
}

/// <summary>Session-owned ordinary interaction selection. Unsupported participants, rider context and item-range overrides remain explicit unknowns.</summary>
internal static class InteractionPicker
{
    public static InteractionPickResult Cast(ClientSessionServices services, double requestedBlockRange)
    {
        ClientState state = services.State;
        if (!state.HasWorld || !state.Features.Entities || state.EntityMotion.IsLocalMountUnresolved || state.EntityMotion.HasUnknownShulkerSelfDisplacement || state.LegacySelfGeometry is { IsKnown: false })
            return InteractionPickResult.Unknown;
        int protocol = services.Version.Version.Protocol;
        InteractionPickProfile profile = InteractionPickProfile.ForProtocol(protocol);
        if (profile.ItemAttackRange && !HasOrdinaryItemRange(state))
            return InteractionPickResult.Unknown;
        (double blockRange, double entityRange) = profile.Ranges(services, requestedBlockRange);
        if (!double.IsFinite(blockRange) || !double.IsFinite(entityRange) || blockRange < 0 || entityRange < 0)
            return InteractionPickResult.Unknown;
        double traceRange = profile.AttributeReach ? Math.Max(blockRange, entityRange) : services.Self.GameMode == GameMode.Creative ? 6 : blockRange;
        Vec3d eye = services.EyePosition;
        double yaw = services.Self.Yaw * Math.PI / 180, pitch = services.Self.Pitch * Math.PI / 180;
        var direction = new Vec3d(-Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch));
        BlockHitResult? block = services.ReadBlockHit(profile.AttributeReach ? traceRange : blockRange);
        double blockDistance = block is { } hit ? hit.Point.Subtract(eye).LengthSqr() : traceRange * traceRange;
        if (profile.AttributeReach && block is not null)
            traceRange = Math.Sqrt(blockDistance);
        Vec3d end = eye.Add(direction.Scale(traceRange));
        if (!HasLoadedCorridor(state.World, eye, block?.Point ?? end))
            return InteractionPickResult.Unknown;
        Aabb? body = services.ReadPhysicsState()?.BoundingBox;
        Aabb? query = body is { XSize: > 0, YSize: > 0 } box ? box.ExpandTowards(direction.Scale(traceRange)).Inflate(1) : null;
        IEntityGeometrySource geometry = JavaGameData.EntityGeometryData(protocol);
        double nearest = blockDistance;
        Entity? selected = null;
        Vec3d selectedPoint = default;
        bool insideSelected = false, unknown = false;
        foreach (Entity entity in state.Entities.All)
        {
            if (entity.Id == state.Self.EntityId || profile.IsNativeNonparticipant(entity.Type.Id))
                continue;
            bool? member = state.EntityMembership.IsWorldQueryMember(entity);
            if (member is false)
                continue;
            // The existing geometry owner has not established native rider placement. A packet position alone cannot bound that actor's query box.
            if (entity.Vehicle is not null || query is null || !TryBounds(entity, out Aabb bounds))
            {
                unknown = true;
                continue;
            }
            if (!query.Value.Intersects(bounds))
                continue;
            bool player = state.EntityMotion.IsTrackedPlayer(entity);
            bool cow = entity.Type.Id.IsMinecraft && entity.Type.Id.Path == "cow";
            bool crystal = EntityGeometryState.IsFixedCrystal(entity, protocol);
            if (!player && !cow && !crystal && !state.EntityMotion.IsTrackedOrdinaryLiving(entity))
            {
                unknown = true;
                continue;
            }
            if (player && state.EntityMotion.IsSpectator(entity, state.TabList))
                continue;
            Aabb target = bounds.Inflate(profile.PickRadius);
            bool inside = Contains(target, eye, profile.LegacyBoxClip);
            bool clipped = TryClip(target, eye, end, profile.LegacyBoxClip, out Vec3d point);
            if (!inside && !clipped)
                continue;
            if (member is null || !state.EntityGeometry.IsPositionKnown(entity) || cow && entity.Metadata.TryGet(EntityMetadataKeys.Health, out float health) && !(health > 0))
            {
                unknown = true;
                continue;
            }
            if (inside)
            {
                if (selected is not null)
                    unknown = true;
                selected = entity;
                selectedPoint = clipped ? point : eye;
                nearest = 0;
                insideSelected = true;
            }
            else
            {
                double distance = point.Subtract(eye).LengthSqr();
                if (insideSelected)
                    unknown = true; // Native query order can replace an inside-origin result; EntityStore order does not prove native section order.
                if (distance < nearest || nearest == 0)
                {
                    selected = entity;
                    selectedPoint = point;
                    nearest = distance;
                }
                else if (selected is not null && distance == nearest)
                    unknown = true;
            }
        }
        if (unknown)
            return InteractionPickResult.Unknown;
        if (selected is not null)
        {
            double distance = selectedPoint.Subtract(eye).LengthSqr();
            if (!profile.AttributeReach && services.Self.GameMode != GameMode.Creative && distance > entityRange * entityRange)
                return InteractionPickResult.Miss;
            if ((profile.AttributeReach ? distance : nearest) < blockDistance || block is null)
                return profile.AttributeReach && !(distance < entityRange * entityRange) ? InteractionPickResult.Miss : new(InteractionPickKind.Entity, EntityId: selected.Id);
        }
        if (block is null || profile.AttributeReach && !(blockDistance < blockRange * blockRange))
            return InteractionPickResult.Miss;
        return new(InteractionPickKind.Block, block);

        bool TryBounds(Entity entity, out Aabb bounds)
            => state.EntityGeometry.TryGet(entity, geometry, out bounds, out _, protocol);
    }

    private static bool HasOrdinaryItemRange(ClientState state)
    {
        if (!state.Features.Inventory)
            return false;
        ItemStack main = state.Inventory.GetSlot(36 + state.Self.HeldSlot), off = state.Inventory.GetSlot(45);
        // 1.21.11+ LivingEntity.getActiveItem selects the use stack or main hand. Until use-hand ownership is tracked, either hand's range override makes this context unknown. Native Item.Properties.spear is the only vanilla default ATTACK_RANGE supplier.
        return Ordinary(main) && Ordinary(off);

        static bool Ordinary(ItemStack item) => item.IsEmpty || !item.Components.Has(DataComponents.PiercingWeapon)
            && !(item.Item.Id.IsMinecraft && item.Item.Id.Path is "wooden_spear" or "stone_spear" or "copper_spear" or "iron_spear" or "golden_spear" or "diamond_spear" or "netherite_spear")
            && !item.Components.Any(entry => entry.Type.Id == Identifier.Minecraft("attack_range"));
    }

    private static bool HasLoadedCorridor(World world, Vec3d start, Vec3d end)
    {
        int minX = (int)Math.Floor(Math.Min(start.X, end.X)) >> 4, maxX = (int)Math.Floor(Math.Max(start.X, end.X)) >> 4;
        int minZ = (int)Math.Floor(Math.Min(start.Z, end.Z)) >> 4, maxZ = (int)Math.Floor(Math.Max(start.Z, end.Z)) >> 4;
        for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
                if (world.GetColumn(new ChunkPos(x, z)) is null)
                    return false;
        return true;
    }

    private static bool Contains(Aabb box, Vec3d point, bool legacy) => point.X < box.MaxX && point.Y < box.MaxY && point.Z < box.MaxZ
        && (legacy ? point.X > box.MinX && point.Y > box.MinY && point.Z > box.MinZ : point.X >= box.MinX && point.Y >= box.MinY && point.Z >= box.MinZ);

    internal static bool TryClip(Aabb box, Vec3d start, Vec3d end, bool legacy, out Vec3d point)
    {
        Vec3d delta = end.Subtract(start);
        double best = legacy ? double.PositiveInfinity : 1;
        bool found = false;
        for (int axis = 0; axis < 3; axis++)
        {
            double component = Coordinate(delta, axis);
            if (legacy ? component * component < 1.0000000116860974E-7 : Math.Abs(component) <= 1E-7)
                continue;
            if (legacy)
            {
                Plane(axis, box.Min(axis));
                Plane(axis, box.Max(axis));
            }
            else
                Plane(axis, component > 0 ? box.Min(axis) : box.Max(axis));
        }
        point = found ? start.Add(delta.Scale(best)) : default;
        return found;

        void Plane(int axis, double plane)
        {
            double fraction = (plane - Coordinate(start, axis)) / Coordinate(delta, axis);
            if (fraction >= best || (legacy ? fraction < 0 || fraction > 1 : fraction <= 0))
                return;
            Vec3d candidate = start.Add(delta.Scale(fraction));
            double tolerance = legacy ? 0 : 1E-7;
            for (int other = 0; other < 3; other++)
            {
                if (other == axis)
                    continue;
                double coordinate = Coordinate(candidate, other);
                if (legacy ? coordinate < box.Min(other) || coordinate > box.Max(other)
                    : coordinate <= box.Min(other) - tolerance || coordinate >= box.Max(other) + tolerance)
                    return;
            }
            best = fraction;
            found = true;
        }

        static double Coordinate(Vec3d value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    }
}
