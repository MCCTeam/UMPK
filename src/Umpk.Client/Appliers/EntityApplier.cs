using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies entity state: spawn/despawn, relative and absolute movement, head look, velocity, metadata, effects, attributes, passengers, equipment, item pickup, and entity status. Requires the Entities feature.</summary>
internal sealed class EntityApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        EntityStore store = context.State.Entities;
        switch (packet)
        {
            // add_entity is the SpawnObject packet on every version below 1.14, where its type id lives in an id space disjoint from add_mob's. From 1.14 the two merged and the space parameter is inert. Getting this wrong does not produce a miss, it produces a wrong name.
            //
            // The spawn's INITIAL VELOCITY rides the same frame and is applied here, through the same scale decode and the same self routing set_entity_motion uses, so the two cannot diverge. See SpawnVelocity for the wire form per era and for what vanilla does with the value.
            case ClientboundAddEntityPacket add:
                Vec3d addVelocity = SpawnVelocity(add.VelocityX, add.VelocityY, add.VelocityZ, add.ModernVelocityRaw);
                await SpawnAsync(store, context, add.EntityId, add.Uuid, add.TypeId, EntitySpawnSpace.Object,
                    add.X, add.Y, add.Z, add.YRot, add.XRot, add.YHeadRot, addVelocity).ConfigureAwait(false);
                TryApplySelfVelocity(context, add.EntityId, addVelocity);
                return true;

            case ClientboundAddMobPacket mob:
                Vec3d mobVelocity = SpawnVelocity(mob.VelocityX, mob.VelocityY, mob.VelocityZ, modernVelocityRaw: null);
                await SpawnAsync(store, context, mob.EntityId, mob.Uuid, mob.TypeId, EntitySpawnSpace.Living,
                    mob.X, mob.Y, mob.Z, mob.Yaw, mob.Pitch, mob.HeadPitch, mobVelocity).ConfigureAwait(false);
                TryApplySelfVelocity(context, mob.EntityId, mobVelocity);
                return true;

            // add_player and add_experience_orb carry no type id: the packet identity is the type, so the type is resolved by key rather than by a made-up id. None of these three frames carries a velocity on any protocol, so they spawn at rest and the velocity parameter is left at its default.
            case ClientboundAddPlayerPacket player:
                await SpawnKeyedAsync(store, context, player.EntityId, player.Uuid, PlayerType,
                    player.X, player.Y, player.Z, player.Yaw, player.Pitch, player.Yaw).ConfigureAwait(false);
                return true;

            case ClientboundAddExperienceOrbPacket orb:
                await SpawnKeyedAsync(store, context, orb.EntityId, Guid.Empty, ExperienceOrbType,
                    orb.X, orb.Y, orb.Z, 0, 0, 0).ConfigureAwait(false);
                return true;

            case ClientboundAddPaintingPacket painting:
                await SpawnKeyedAsync(store, context, painting.EntityId, Guid.Empty, PaintingType,
                    painting.Position.X, painting.Position.Y, painting.Position.Z, 0, 0, 0).ConfigureAwait(false);
                return true;

            case ClientboundRemoveEntitiesPacket remove:
                foreach (int id in remove.EntityIds)
                {
                    store.Remove(id);
                    await context.PublishAsync(new EntityRemoved(id)).ConfigureAwait(false);
                }

                return true;

            case ClientboundMoveEntityPosPacket move:
                if (store.TryGet(move.EntityId, out Entity? posEntity) && posEntity is not null)
                {
                    // 26.3 may carry stepped deltas: apply every step cumulatively, not just the mirrored first one.
                    (double dx, double dy, double dz) = SumSteps(move.Steps, move.DeltaX, move.DeltaY, move.DeltaZ);
                    posEntity.Position = posEntity.Position.Add(dx / 4096.0, dy / 4096.0, dz / 4096.0);
                    posEntity.OnGround = move.OnGround;
                    await context.PublishAsync(new EntityMoved(move.EntityId)).ConfigureAwait(false);
                }

                return true;

            case ClientboundMoveEntityRotPacket rot:
                if (store.TryGet(rot.EntityId, out Entity? rotEntity) && rotEntity is not null)
                {
                    rotEntity.Yaw = rot.Yaw;
                    rotEntity.Pitch = rot.Pitch;
                    rotEntity.OnGround = rot.OnGround;
                }

                return true;

            case ClientboundMoveEntityPosRotPacket posRot:
                if (store.TryGet(posRot.EntityId, out Entity? prEntity) && prEntity is not null)
                {
                    (double dx, double dy, double dz) = SumSteps(posRot.Steps, posRot.DeltaX, posRot.DeltaY, posRot.DeltaZ);
                    prEntity.Position = prEntity.Position.Add(dx / 4096.0, dy / 4096.0, dz / 4096.0);
                    prEntity.Yaw = posRot.Yaw;
                    prEntity.Pitch = posRot.Pitch;
                    prEntity.OnGround = posRot.OnGround;
                    await context.PublishAsync(new EntityMoved(posRot.EntityId)).ConfigureAwait(false);
                }

                return true;

            case ClientboundTeleportEntityPacket teleport:
                if (store.TryGet(teleport.EntityId, out Entity? tpEntity) && tpEntity is not null)
                {
                    tpEntity.Position = new Vec3d(teleport.X, teleport.Y, teleport.Z);
                    tpEntity.Yaw = teleport.Yaw;
                    tpEntity.Pitch = teleport.Pitch;
                    tpEntity.OnGround = teleport.OnGround;
                    await context.PublishAsync(new EntityMoved(teleport.EntityId)).ConfigureAwait(false);
                }

                return true;

            case ClientboundEntityPositionSyncPacket sync:
                if (store.TryGet(sync.EntityId, out Entity? syncEntity) && syncEntity is not null)
                {
                    syncEntity.Position = sync.Values.Position;
                    syncEntity.Velocity = sync.Values.DeltaMovement;
                    syncEntity.Yaw = sync.Values.YRot;
                    syncEntity.Pitch = sync.Values.XRot;
                    syncEntity.OnGround = sync.OnGround;
                    await context.PublishAsync(new EntityMoved(sync.EntityId)).ConfigureAwait(false);
                }

                return true;

            case ClientboundRotateHeadPacket head:
                if (store.TryGet(head.EntityId, out Entity? headEntity) && headEntity is not null)
                    headEntity.HeadYaw = head.HeadYaw;

                return true;

            // Every server-applied velocity arrives here: knockback from a mob or a player, an explosion, a piston, a fishing rod, or a bounce. The vanilla client includes the local player in its entity lookup, so the player's own id resolves and the update sets its delta movement. Self is not in UMPK's entity store, so the store lookup alone dropped every knockback aimed at the client.
            case ClientboundSetEntityMotionPacket motion:
                Vec3d motionVelocity = ReadVelocity(motion);
                if (!TryApplySelfVelocity(context, motion.EntityId, motionVelocity)
                    && store.TryGet(motion.EntityId, out Entity? motionEntity) && motionEntity is not null)
                    motionEntity.Velocity = motionVelocity;

                return true;

            // The decoded fields are written into the tracked entity's tier-1 (index-keyed) metadata store before the event is published, so a subscriber that reads Metadata sees this frame's values. Publishing the event alone left the store permanently empty on EVERY protocol: health, custom name, name visibility, pose, age and item-entity contents were decoded off the wire and then dropped on the floor, which is indistinguishable from the packet never arriving. Storing them is only half of it: the typed Entity properties are the surface a consumer actually reads, so the semantic keys are projected onto them here (tier 2).
            case ClientboundSetEntityDataPacket metadata:
                if (store.TryGet(metadata.EntityId, out Entity? metadataEntity) && metadataEntity is not null)
                {
                    foreach (EntityDataEntry entry in metadata.Metadata.Entries)
                        metadataEntity.Metadata.Set(entry.Index, entry.Value);

                    ProjectSemanticMetadata(metadataEntity);
                    await context.PublishAsync(new EntityMetadataChanged(metadata.EntityId)).ConfigureAwait(false);
                }

                return true;

            case ClientboundUpdateMobEffectPacket effect:
                await ApplyEffectAsync(store, context, effect).ConfigureAwait(false);
                return true;

            case ClientboundUpdateAttributesPacket attributes:
                ApplyAttributes(store, context, attributes);
                return true;

            case ClientboundRemoveMobEffectPacket removeEffect:
                if (removeEffect.EntityId == context.State.Self.EntityId)
                {
                    context.State.Self.RemoveEffect(removeEffect.EffectId);
                    context.PhysicsConditionsDirty();
                    await context.PublishAsync(new EntityEffectRemoved(removeEffect.EntityId, removeEffect.EffectId)).ConfigureAwait(false);
                }
                else if (store.TryGet(removeEffect.EntityId, out Entity? reEntity) && reEntity is not null)
                {
                    reEntity.RemoveEffect(removeEffect.EffectId);
                    await context.PublishAsync(new EntityEffectRemoved(removeEffect.EntityId, removeEffect.EffectId)).ConfigureAwait(false);
                }

                return true;

            // The decoded slot is written into the tracked entity BEFORE the event is published, for the same reason set_entity_data does it: Entity.Equipment is the surface a consumer reads, and publishing an event alone would leave that dictionary permanently empty. Nothing applied this packet on any protocol before, so even the eras that already decoded it (47, and 735 upward) threw the result away. The modern 1.16+ codec cannot name the slot yet and degrades to a raw tail, so it is passed over here rather than recorded under a made-up slot.
            case ClientboundSetEquipmentPacket equipment when equipment.LegacyItem is not null:
                if (store.TryGet(equipment.EntityId, out Entity? equipEntity) && equipEntity is not null)
                {
                    equipEntity.SetEquipment(
                        equipment.LegacySlot,
                        equipment.LegacyItem.IsEmpty ? null : equipment.LegacyItem);
                    await context.PublishAsync(
                        new EntityEquipmentChanged(equipment.EntityId, equipment.LegacySlot)).ConfigureAwait(false);
                }

                return true;

            case ClientboundSetPassengersPacket passengers:
                ApplyPassengers(store, passengers);
                await context.PublishAsync(new EntityPassengersChanged(passengers.VehicleId)).ConfigureAwait(false);
                return true;

            case ClientboundTakeItemEntityPacket pickup:
                store.Remove(pickup.ItemEntityId);
                await context.PublishAsync(new ItemPickedUp(pickup.ItemEntityId, pickup.CollectorEntityId, pickup.Amount ?? 0)).ConfigureAwait(false);
                return true;

            case ClientboundEntityEventPacket entityEvent:
                await context.PublishAsync(new EntityStatusChanged(entityEvent.EntityId, entityEvent.EventId)).ConfigureAwait(false);
                return true;

            case ClientboundDamageEventPacket damage:
                // damage is transient (no entity-state field); surface it as an event.
                await context.PublishAsync(new EntityDamaged(
                    damage.EntityId, damage.SourceTypeId, damage.SourceCauseId, damage.SourceDirectId, damage.SourcePosition)).ConfigureAwait(false);
                return true;

            case ClientboundHurtAnimationPacket hurt:
                await context.PublishAsync(new EntityHurt(hurt.EntityId, hurt.Yaw)).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The velocity a <c>set_entity_motion</c> frame carries, in blocks per tick.</summary>
    /// <remarks>Two wire forms, and the era decides which one holds the value. Through protocol 772 the packet is three shorts of <c>value * 8000</c>, clamped SERVER-side to +/-3.9 before they are written, which is why the client applies no clamp of its own. From 1.21.9 (protocol 773, so 1.21.9/1.21.10/1.21.11/26.1/26.2) the three shorts are replaced by the low-precision quantized block and the bound codec parks it in <c>ModernVelocityRaw</c> with the shorts left at literal zero, so reading the shorts on those protocols yields zero for every entity.</remarks>
    private static Vec3d ReadVelocity(ClientboundSetEntityMotionPacket motion)
        => SpawnVelocity(motion.VelocityX, motion.VelocityY, motion.VelocityZ, motion.ModernVelocityRaw);

    /// <summary>Sums a 26.3+ stepped relative move into raw wire deltas. An empty step list falls back to the legacy single-delta fields, so pre-26.3 packets and zero-step frames take the same path.</summary>
    private static (double Dx, double Dy, double Dz) SumSteps(
        IReadOnlyList<EntityMoveStep> steps, short deltaX, short deltaY, short deltaZ)
    {
        if (steps.Count == 0)
            return (deltaX, deltaY, deltaZ);

        long dx = 0, dy = 0, dz = 0;
        foreach (EntityMoveStep step in steps)
        {
            dx += step.DeltaX;
            dy += step.DeltaY;
            dz += step.DeltaZ;
        }

        return (dx, dy, dz);
    }

    /// <summary>The velocity a spawn frame (<c>add_entity</c> / <c>add_mob</c>) or a <c>set_entity_motion</c> frame carries, in blocks per tick. One decode for both, so the two paths cannot drift apart.</summary>
    /// <remarks>
    /// <para>Wire form. Through protocol 772 the value is three shorts of <c>value * 8000</c>, clamped server-side to +/-3.9 before encoding. Spawn and motion packets use identical arithmetic, so the client applies no clamp of its own. From 1.21.9 (protocol 773) the three shorts are replaced by the low-precision quantized block, and the bound codec parks it in <c>ModernVelocityRaw</c> with the shorts left at literal zero.</para>
    /// <para>One era omits the field: on 1.8 the three shorts are written ONLY when <c>data &gt; 0</c>, so the codec leaves them at zero otherwise and this returns the zero vector, which is what the vanilla client does too. <c>add_player</c>, <c>add_experience_orb</c> and <c>add_painting</c> carry no velocity at all.</para>
    /// <para>From 1.21.5 every spawned type applies the wire velocity directly. Earlier clients applied it in selected entity types. UMPK applies it uniformly because its entities do not re-derive motion; <see cref="Entity.Velocity"/> is a tracked report of what the server last said for every type.</para>
    /// </remarks>
    private static Vec3d SpawnVelocity(short velocityX, short velocityY, short velocityZ, byte[]? modernVelocityRaw)
        => modernVelocityRaw is { } raw
            ? LowPrecisionVelocity.Decode(raw)
            : new Vec3d(velocityX / 8000.0, velocityY / 8000.0, velocityZ / 8000.0);

    /// <summary>Routes a server-applied velocity to <see cref="Umpk.Client.State.SelfState"/> when it names the local player, returning whether it did. Shared by the spawn and <c>set_entity_motion</c> arms.</summary>
    /// <remarks>Self is not in the entity store, so a store lookup alone drops every velocity aimed at the client. Writing self state is also only half of it: the physics engine steps from its OWN fields and syncs the result back into self state every tick, so a velocity left in the field alone is overwritten before it can move anything, which is why <see cref="ApplierContext.PhysicsVelocityDirty"/> is pushed here. A vanilla server never spawns the local player through <c>add_entity</c> (its own tracker does not track a player to itself), so the spawn caller is defensive; it costs one comparison and keeps the two arms structurally identical.</remarks>
    private static bool TryApplySelfVelocity(ApplierContext context, int entityId, Vec3d velocity)
    {
        if (entityId != context.State.Self.EntityId)
            return false;

        context.State.Self.Velocity = velocity;
        context.PhysicsVelocityDirty();
        return true;
    }

    private static readonly Identifier PlayerType = Identifier.Minecraft("player");
    private static readonly Identifier ExperienceOrbType = Identifier.Minecraft("experience_orb");
    private static readonly Identifier PaintingType = Identifier.Minecraft("painting");

    private static ValueTask SpawnAsync(
        EntityStore store, ApplierContext context, int entityId, Guid uuid, int typeId, EntitySpawnSpace space,
        double x, double y, double z, float yaw, float pitch, float headYaw, Vec3d velocity)
    {
        RegistryEntry<EntityTypeDefinition> type =
            context.EntityTypes.Resolve(context.State.Registries, typeId < 0 ? 0 : typeId, space);

        // From 1.20.2 add_player is gone and players arrive here, on the generic spawn path, carrying the player entity type. That is the only signal available, so the profile hook keys on it.
        return AddAsync(store, context, entityId, uuid, type, x, y, z, yaw, pitch, headYaw, velocity,
            isPlayer: type.Id == PlayerType);
    }

    private static ValueTask SpawnKeyedAsync(
        EntityStore store, ApplierContext context, int entityId, Guid uuid, Identifier typeKey,
        double x, double y, double z, float yaw, float pitch, float headYaw) =>
        AddAsync(store, context, entityId, uuid,
            context.EntityTypes.ResolveByKey(context.State.Registries, typeKey),
            x, y, z, yaw, pitch, headYaw, Vec3d.Zero,
            // The packet identity IS the type here, so a player spawn stays a player spawn even when the era's registry has no player key and the type degrades to the unknown placeholder.
            isPlayer: typeKey == PlayerType);

    private static async ValueTask AddAsync(
        EntityStore store, ApplierContext context, int entityId, Guid uuid,
        RegistryEntry<EntityTypeDefinition> type,
        double x, double y, double z, float yaw, float pitch, float headYaw, Vec3d velocity,
        bool isPlayer)
    {
        // The key source is what makes the typed metadata properties readable at all: Entity builds its metadata store around it, and an entity constructed without one answers false to every tier-2 lookup no matter how complete the raw store is.
        //
        // The spawn velocity is set HERE, in the initializer, rather than after store.Add: EntitySpawned is published below and a subscriber that reads the new entity's Velocity has to see this frame's value, not a zero that a later set_entity_motion happens to correct.
        var entity = new Entity(entityId, uuid, type, context.MetadataKeys)
        {
            Position = new Vec3d(x, y, z),
            Yaw = yaw,
            Pitch = pitch,
            HeadYaw = headYaw,
            Velocity = velocity,
        };

        // The spawn packet carries the uuid but never the name, so the profile comes from the tab list. A spawn that beats its own tab-list entry onto the wire leaves this null; UiApplier backfills it when the entry lands, so the ordering does not decide whether a name is ever resolvable.
        if (isPlayer && context.State.TabList.TryGet(uuid, out TabListEntry? tabEntry))
            entity.PlayerProfile = tabEntry.Profile;

        store.Add(entity);
        await context.PublishAsync(new EntitySpawned(entityId, entity)).ConfigureAwait(false);
    }

    /// <summary>Projects the semantic metadata keys onto the typed <see cref="Entity"/> properties (tier 2). The raw store keeps every index; this is what a consumer that asks for the custom name actually reads.</summary>
    /// <remarks>The custom name is a plain STRING on protocols 47-340 and an <c>Optional&lt;Component&gt;</c> from 393, so the projection is chosen from the stored value's own kind rather than from the protocol: the wire already told us. Vanilla's pre-1.13 form writes an EMPTY string for "no custom name", which is not the same thing as a name of "", so an empty legacy string clears the property.</remarks>
    private static void ProjectSemanticMetadata(Entity entity)
    {
        if (entity.Metadata.TryResolve(EntityMetadataKeys.CustomName, out MetadataValue rawName))
            entity.CustomName = rawName.Kind switch
            {
                MetadataValueKind.OptionalComponent => rawName.AsOptionalComponent(),
                MetadataValueKind.Component => rawName.AsComponent(),
                MetadataValueKind.String => rawName.AsString() is { Length: > 0 } legacy
                    ? Umpk.Text.Component.Text(legacy)
                    : null,
                _ => entity.CustomName,
            };

        if (entity.Metadata.TryGet(EntityMetadataKeys.Pose, out EntityPose pose))
            entity.Pose = pose;

        if (entity.Metadata.TryGet(EntityMetadataKeys.CarriedItem, out IMetadataSlot? carried))
            entity.CarriedItem = carried is { IsEmpty: false } ? carried : null;

    }

    private static async ValueTask ApplyEffectAsync(EntityStore store, ApplierContext context, ClientboundUpdateMobEffectPacket effect)
    {
        bool isSelf = effect.EntityId == context.State.Self.EntityId;

        // The local player's own effects are the ones the server actually sends update_mob_effect for (vanilla on effect added behavior sends it to the player; a standalone mob's effect is only reflected in its metadata particle colour). Self is not in the shared entity store, so track it in SelfState. Other entities apply into the entity store when they are tracked.
        if (isSelf)
        {
            // Stamped with the session tick so the remaining duration can be DERIVED later. The wire duration is kept verbatim: vanilla sends this packet on add and on refresh and nothing in between, so without a stamp a 600-tick potion still reports 600 nine seconds later. A refresh re-stamps because it re-enters this arm with the new duration.
            context.State.Self.ApplyEffect(
                new State.ActiveEffect(effect.EffectId, effect.Amplifier, effect.Duration, effect.Flags)
                {
                    AppliedAtTick = context.State.SessionTick,
                });
            context.PhysicsConditionsDirty();
        }
        else if (store.TryGet(effect.EntityId, out Entity? entity) && entity is not null)
        {
            RegistryEntryOrNull(context, effect.EffectId, out var effectEntry);
            if (effectEntry is { } entry)
                entity.AddOrRefreshEffect(new EffectInstance(entry, effect.Amplifier, effect.Duration, (EffectFlags)effect.Flags));

        }
        else
        {
            // An effect for an entity we are not tracking; nothing to apply, and no event to surface.
            return;
        }

        await context.PublishAsync(new EntityEffectApplied(effect.EntityId, effect.EffectId, effect.Amplifier, effect.Duration)).ConfigureAwait(false);
    }

    /// <summary>Applies an <c>update_attributes</c> frame to the local player's own map or to a tracked entity's.</summary>
    /// <remarks>
    /// <para>The self/other split mirrors <see cref="ApplyEffectAsync"/>'s exactly, and for the identical reason: the local player is not in the shared <c>EntityStore</c>, so its attributes live on <c>SelfState</c>. The self arm raises <c>PhysicsConditionsDirty</c> because <c>PhysicsConditions.BaseMovementSpeedAttribute</c> is captured from this map; the entity arm does not, because nothing in physics reads another entity's attributes.</para>
    /// <para>Each frame replaces the named attribute's base value and complete modifier set. Merging would retain modifiers that a later frame omits, such as <c>minecraft:enchantment.soul_speed</c> after the player leaves soul sand.</para>
    /// <para>Because the frame carries <c>base</c>, the seeded player defaults are a PRE-FIRST-PACKET value rather than a floor: the first frame naming an attribute overwrites its base outright.</para>
    /// <para>Attributes not already declared by the entity are retained as server-stated data. Physics only reads the attributes it recognizes.</para>
    /// </remarks>
    private static void ApplyAttributes(
        EntityStore store, ApplierContext context, ClientboundUpdateAttributesPacket packet)
    {
        Game.Registries.RegistryAccess? registries = context.State.Registries;
        if (registries is null)
            return;

        bool isSelf = packet.EntityId == context.State.Self.EntityId;
        Entity? entity = null;
        if (!isSelf && (!store.TryGet(packet.EntityId, out entity) || entity is null))
        {
            // An attribute frame for an entity we are not tracking; nothing to apply.
            return;
        }

        bool appliedAny = false;
        foreach (AttributeSnapshot snapshot in packet.Attributes)
        {
            if (!TryResolveAttribute(registries, snapshot, out RegistryEntry<Game.Registries.AttributeDefinition> attribute))
                continue;

            Identifier canonical = AttributeIds.Canonical(attribute.Id);
            AttributeInstance instance = isSelf
                ? context.State.Self.Attributes.GetOrCreate(canonical, attribute)
                : entity!.Attributes.GetOrCreate(canonical, attribute);

            instance.BaseValue = snapshot.BaseValue;
            instance.ClearModifiers();
            foreach (AttributeModifierEntry modifier in snapshot.Modifiers)
                instance.AddOrReplaceModifier(new AttributeModifier(
                    AttributeModifierIds.Resolve(modifier.LegacyUuid, modifier.ModernId),
                    modifier.Amount,
                    (AttributeModifierOperation)modifier.Operation));

            if (isSelf)
                context.State.Self.Attributes.MarkServerStated(canonical);

            appliedAny = true;
        }

        if (isSelf && appliedAny)
            context.PhysicsConditionsDirty();

    }

    /// <summary>Resolves the attribute a wire snapshot names, across the three eras of the packet's id field.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// 47-578 send a camelCase, unnamespaced key (<c>"generic.movementSpeed"</c>), and no protocol in that band carries a <c>minecraft:attribute</c> registry to resolve it against. Unresolvable by design; the snapshot is skipped and <c>SelfAttributes.Known</c> is false for those sessions.
    /// </description></item>
    /// <item><description>
    /// 735-765 send a <c>ResourceLocation</c> string, which is a direct registry lookup by the RAW name the registry is keyed by.
    /// </description></item>
    /// <item><description>
    /// 766+ send the holder VarInt only (1.20.5 moved attributes into a registry), resolved by network id. This is the era that is unreadable without a populated <c>minecraft:attribute</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static bool TryResolveAttribute(
        Game.Registries.RegistryAccess registries,
        AttributeSnapshot snapshot,
        out RegistryEntry<Game.Registries.AttributeDefinition> attribute)
    {
        if (snapshot.LegacyKey is { Length: > 0 } key)
            return Identifier.TryParse(key, out Identifier id)
                ? registries.Attributes.TryGet(id, out attribute)
                : Fail(out attribute);

        return registries.Attributes.TryGet(snapshot.ModernId, out attribute);

        static bool Fail(out RegistryEntry<Game.Registries.AttributeDefinition> attribute)
        {
            attribute = default;
            return false;
        }
    }

    private static void RegistryEntryOrNull(ApplierContext context, int effectId, out RegistryEntry<Game.Registries.MobEffectDefinition>? entry)
    {
        entry = null;
        Game.Registries.RegistryAccess? registries = context.State.Registries;
        if (registries is not null && registries.MobEffects.TryGet(effectId, out RegistryEntry<Game.Registries.MobEffectDefinition> resolved))
            entry = resolved;

    }

    private static void ApplyPassengers(EntityStore store, ClientboundSetPassengersPacket packet)
    {
        if (!store.TryGet(packet.VehicleId, out Entity? vehicle) || vehicle is null)
            return;

        var passengers = new List<Entity>();
        foreach (int id in packet.Passengers)
            if (store.TryGet(id, out Entity? passenger) && passenger is not null)
                passengers.Add(passenger);

        store.SetPassengers(vehicle, passengers);
    }
}
