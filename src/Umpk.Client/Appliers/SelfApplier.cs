using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies local-player state: health/food, position sync + teleport confirm, experience, held slot, abilities, cooldowns, camera, and the game-event codes that touch self. Abilities/effect/gamemode changes trigger a physics-conditions repush.</summary>
internal sealed class SelfApplier : IApplier
{
    // Relative bit indices are frozen from the 1.9 five-value RelativeMovement set through the modern nine-value one, so the low five constants serve every era and the upper four exist only on 1.21.2+ frames.
    private const int RelativeX = 1 << 0;
    private const int RelativeY = 1 << 1;
    private const int RelativeZ = 1 << 2;
    private const int RelativeYaw = 1 << 3;
    private const int RelativePitch = 1 << 4;

    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        SelfState self = context.State.Self;
        switch (packet)
        {
            case ClientboundSetHealthPacket health:
                bool wasAlive = self.Health > 0;
                self.Health = health.Health;
                self.Food = health.Food;
                self.Saturation = health.Saturation;
                self.HealthObserved = true;
                self.NutritionObservationLifetime = context.State.PlayerLifetime;
                await context.PublishAsync(new HealthChanged(health.Health, health.Food, health.Saturation)).ConfigureAwait(false);
                if (wasAlive && health.Health <= 0)
                    await context.PublishAsync(new Died()).ConfigureAwait(false);

                return true;

            case ClientboundPlayerPositionPacket pos:
                await ApplyPositionAsync(pos, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundSetEntityMotionPacket motion when motion.EntityId == self.EntityId:
                self.Velocity = motion.ModernVelocityRaw is { } raw
                    ? LowPrecisionVelocity.Decode(raw)
                    : new Vec3d(motion.VelocityX / 8000D, motion.VelocityY / 8000D, motion.VelocityZ / 8000D);
                context.PhysicsVelocityDirty();
                return true;

            case ClientboundUseBedPacket bed when bed.PlayerId == self.EntityId && LegacyPlayerGeometryProfile.Supports(context.Version.Version.Protocol):
                Vec3d beforeSleep = self.Position;
                TrackedLegacySelfGeometry sleeping = context.State.GetLegacySelfGeometry(context.Version.Version.Protocol)!;
                sleeping.ObserveInput(self.Sneaking);
                sleeping.BeginSleep(JavaGameData.BedInteractionData(context.Version.Version.Protocol), bed.Position);
                context.PhysicsConditionsDirty();
                context.PhysicsVelocityDirty();
                if (self.Position != beforeSleep)
                    await context.PublishAsync(new EntityMoved(self.EntityId)).ConfigureAwait(false);
                return true;

            case ClientboundAnimatePacket { Action: 2 } wake when wake.EntityId == self.EntityId && LegacyPlayerGeometryProfile.Supports(context.Version.Version.Protocol):
                Vec3d beforeWake = self.Position;
                TrackedLegacySelfGeometry waking = context.State.GetLegacySelfGeometry(context.Version.Version.Protocol)!;
                waking.ObserveInput(self.Sneaking);
                waking.Wake(JavaGameData.BedInteractionData(context.Version.Version.Protocol));
                context.PhysicsConditionsDirty();
                if (self.Position != beforeWake)
                    await context.PublishAsync(new EntityMoved(self.EntityId)).ConfigureAwait(false);
                return true;

            case ClientboundEntityEventPacket { EventId: 3 } death when death.EntityId == self.EntityId && LegacyPlayerGeometryProfile.Supports(context.Version.Version.Protocol):
                context.State.GetLegacySelfGeometry(context.Version.Version.Protocol)!.HandleDeath();
                context.PhysicsConditionsDirty();
                context.PhysicsVelocityDirty();
                await context.PublishAsync(new EntityStatusChanged(death.EntityId, death.EventId)).ConfigureAwait(false);
                return true;

            case ClientboundSetExperiencePacket xp:
                self.ExperienceProgress = xp.ExperienceProgress;
                self.ExperienceLevel = xp.Level;
                self.TotalExperience = xp.TotalExperience;
                await context.PublishAsync(new ExperienceChanged(xp.ExperienceProgress, xp.Level, xp.TotalExperience)).ConfigureAwait(false);
                return true;

            case ClientboundSetHeldSlotPacket held:
                self.HeldSlot = held.Slot;
                await context.PublishAsync(new HeldSlotChanged(held.Slot)).ConfigureAwait(false);
                return true;

            case ClientboundPlayerAbilitiesPacket abilities:
                ApplyAbilities(abilities, self);
                self.AbilitiesObservationLifetime = context.State.PlayerLifetime;
                context.PhysicsConditionsDirty();
                await context.PublishAsync(new AbilitiesChanged(
                    self.Invulnerable, self.Flying, self.MayFly, self.InstantBuild, self.FlyingSpeed, self.WalkingSpeed))
                    .ConfigureAwait(false);
                return true;

            case ClientboundCooldownPacket cooldown:
                int? cooldownItem = cooldown.ItemId;
                if (cooldownItem is { } itemId)
                    self.SetItemCooldown(itemId, cooldown.Ticks);
                else
                {
                    if (context.State.Registries?.Items.TryGet(cooldown.CooldownGroup, out var item) is true)
                        cooldownItem = item.NetworkId;
                    self.SetGroupCooldown(cooldown.CooldownGroup, cooldown.Ticks, cooldownItem);
                }
                // The public event describes an item ID. Custom groups without an item projection remain in the exact group owner.
                if (cooldownItem is { } knownItem)
                    await context.PublishAsync(new ItemCooldownChanged(knownItem, cooldown.Ticks)).ConfigureAwait(false);
                return true;

            // Entity metadata addressed to the LOCAL player. Self is not a member of the shared EntityStore, so EntityApplier's own set_entity_data arm looked this id up, missed, and returned true anyway: every field the server told the client about ITSELF was decoded and then dropped. This applier runs BEFORE EntityApplier (see ApplierCatalog), and the guard is what keeps that ordering harmless - a frame for any other entity falls through to the default and reaches the entity store exactly as before.
            case ClientboundSetEntityDataPacket metadata when metadata.EntityId == self.EntityId:
                ApplySelfMetadata(metadata, context, self);
                return true;

            case ClientboundSetCameraPacket camera:
                self.CameraEntityId = camera.CameraId == self.EntityId ? null : camera.CameraId;
                return true;

            case ClientboundGameEventPacket gameEvent:
                await ApplyGameEventAsync(gameEvent, context).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Resolves a server teleport into an absolute self position, rotation and velocity, then re-seeds the physics engine there and confirms the teleport.</summary>
    /// <remarks>
    /// <para>Both eras resolve position and rotation the same way (a relative bit means "add to what we have"). They differ on VELOCITY, and vanilla sets it on both:</para>
    /// <list type="bullet">
    /// <item><description>
    /// 1.8-1.21.1: each relative axis keeps its current velocity, while each absolute axis resets it to zero. There is no velocity delta on the wire.
    /// </description></item>
    /// <item><description>
    /// 1.21.2+: the packet carries a <c>PositionMoveRotation</c> whose velocity rides the wire. The shared entity teleport profile resolves relative position, rotation and movement with native lookup-table trigonometry. From 1.21.5 it clamps pitch before rotating velocity; the final entity pitch setter separately applies remainder and clamp on every modern release.
    /// </description></item>
    /// </list>
    /// <para>Bit 8 only survives to this method because the modern codec carries the full-width bitset; a byte field would have dropped <c>ROTATE_DELTA</c> before any of this ran.</para>
    /// </remarks>
    private static async ValueTask ApplyPositionAsync(ClientboundPlayerPositionPacket pos, ApplierContext context, CancellationToken ct)
    {
        SelfState self = context.State.Self;
        int flags = pos.RelativeFlags;

        double x, y, z;
        float yaw, pitch;
        Vec3d velocity;
        if (pos.ModernValues is { } modern)
        {
            var source = new PositionMoveRotation(self.Position, self.Velocity, self.Yaw, self.Pitch);
            PositionMoveRotation destination = EntityTeleportProfile.ForProtocol(context.Version.Version.Protocol).Resolve(source, modern, flags);
            x = destination.Position.X;
            y = destination.Position.Y;
            z = destination.Position.Z;
            yaw = destination.YRot;
            pitch = Math.Clamp(destination.XRot % 360.0F, -90.0F, 90.0F);
            velocity = destination.DeltaMovement;
        }
        else
        {
            x = (flags & RelativeX) != 0 ? self.Position.X + pos.X : pos.X;
            y = (flags & RelativeY) != 0 ? self.Position.Y + pos.Y : pos.Y;
            z = (flags & RelativeZ) != 0 ? self.Position.Z + pos.Z : pos.Z;
            yaw = (flags & RelativeYaw) != 0 ? self.Yaw + pos.Yaw : pos.Yaw;
            pitch = (flags & RelativePitch) != 0 ? self.Pitch + pos.Pitch : pos.Pitch;
            velocity = new Vec3d(
                (flags & RelativeX) != 0 ? self.Velocity.X : 0.0,
                (flags & RelativeY) != 0 ? self.Velocity.Y : 0.0,
                (flags & RelativeZ) != 0 ? self.Velocity.Z : 0.0);
        }

        self.Position = new Vec3d(x, y, z);
        self.Velocity = velocity;
        self.Yaw = yaw;
        self.Pitch = pitch;
        self.HasSpawned = true;
        context.State.EntityMotion.ObserveLocalPushPosition(context.Version.Version.Protocol, self.Position, (flags & (RelativeX | RelativeY | RelativeZ)) == 0);
        context.State.EntityMotion.ObserveShulkerSelfCorrection((flags & (RelativeX | RelativeY | RelativeZ)) == 0);

        // Re-seed the physics engine at the absolute position resolved above (never at the raw packet fields, which may be relative offsets). The engine steps from its own position and syncs the result back into SelfState every tick, so without this the teleport survives less than one tick and the client reports the engine's stale position instead of the server's. The resolved velocity rides along, so a teleport that hands the client momentum keeps it.
        context.PhysicsPositionDirty();

        bool acceptCarriesDestination = pos.TeleportId.HasValue && AcceptCarriesDestination(context.Wire);
        if (pos.TeleportId is { } teleportId)
        {
            // The confirmation shape is era-bound: 26.3 echoes the resolved destination after the id, while older eras carry the id alone. The outbound codec capability selects the matching packet model without branching on a protocol number.
            if (context.Wire.CanSendPlay(EntityPackets.Serverbound.AcceptTeleportation))
            {
                var confirmation = acceptCarriesDestination
                    ? new ServerboundAcceptTeleportationPacket(teleportId, x, y, z, yaw, pitch)
                    : new ServerboundAcceptTeleportationPacket(teleportId);
                await context.Sink.SendAsync(
                    confirmation,
                    ct).ConfigureAwait(false);
            }

        }

        // 1.8 has no teleport id but still echoes the resolved position and rotation (cee.a(ii) in the vanilla client). Later id-only accepts also need the echo. Only a destination-carrying accept replaces it.
        if (!acceptCarriesDestination)
            await context.Sink.SendAsync(
                    new ServerboundMovePlayerPosRotPacket(x, y, z, yaw, pitch, OnGround: false, HorizontalCollision: false),
                    ct)
                .ConfigureAwait(false);

        if (EntityTeleportProfile.HasBlockPredictionTeleportBarrier(context.Version.Version.Protocol))
            context.Sequences.ObserveTeleport();
        if (BlockInteractionProfile.ForProtocol(context.Version.Version.Protocol).TeleportStopsDestroying)
            await context.StopBlockDestruction(ct).ConfigureAwait(false);

        await context.PublishAsync(new PositionCorrected(self.Position, yaw, pitch)).ConfigureAwait(false);
    }

    /// <summary>Whether the session's bound accept_teleportation codec carries the echoed destination (26.3+: VarInt id, three doubles, two floats). Reads the bound codec's wire shape off the outbound table, never a protocol number: a rewording of either shape token flips the echo the wrong way, and the 770-era applier test (both packets) plus the 777-era applier test (accept only) pin both sides loudly.</summary>
    private static bool AcceptCarriesDestination(WireIndex wire) =>
        wire.OutboundCodec(ProtocolPhase.Play, EntityPackets.Serverbound.AcceptTeleportation) is { } bound &&
        string.Equals(bound.Shape.Token, "varint,double,double,double,float,float", StringComparison.Ordinal);

    /// <summary>The entity type the local player is, for tier-2 metadata index resolution.</summary>
    private static readonly Identifier PlayerType = Identifier.Minecraft("player");

    /// <summary>Applies the fields of a self <c>set_entity_data</c> frame that <see cref="SelfState"/> tracks. Air and the measured legacy health field use semantic keys; legacy body flags go to the always-present self lifecycle owner.</summary>
    /// <remarks>
    /// <para>The index is resolved through the version's tier-2 key table rather than written as a literal. It is 1 on all 50 protocols because air is the second field and nothing has ever been inserted above it, but that is the TABLE's fact to state, not this applier's.</para>
    /// <para>Air accepts only <see cref="MetadataValueKind.VarInt"/>. On 1.8 the field is a 16-bit short rather than a VarInt, and the legacy metadata decoder already normalises both onto that one kind (<c>EntityMetadataCodec.ReadLegacy</c>: type 1 short and type 2 int both build <c>MetadataValue.VarInt</c>), so the check costs nothing on any era and refuses a value whose shape says the index does not mean what this table thinks it means.</para>
    /// <para>A frame carries only dirty metadata entries, so "this frame has no air in it" is the ordinary case and must leave the tracked value alone rather than reset it.</para>
    /// </remarks>
    private static void ApplySelfMetadata(ClientboundSetEntityDataPacket packet, ApplierContext context, SelfState self)
    {
        RegistryEntry<EntityTypeDefinition> playerType =
            context.EntityTypes.ResolveByKey(context.State.Registries, PlayerType);
        context.State.EntityMotion.ObserveLocalPushMetadata(context.Version.Version.Protocol, packet.Metadata);
        if (!context.MetadataKeys.TryResolveIndex(playerType, EntityMetadataKeys.AirSupply, out int airIndex))
            return;

        TrackedLegacySelfGeometry? legacy = context.State.GetLegacySelfGeometry(context.Version.Version.Protocol);
        bool hasHealth = context.MetadataKeys.TryResolveIndex(playerType, EntityMetadataKeys.Health, out int healthIndex);
        foreach (EntityDataEntry entry in packet.Metadata.Entries)
        {
            if (entry.Index == airIndex && entry.Value.Kind == MetadataValueKind.VarInt)
                self.AirSupply = entry.Value.AsVarInt();
            if (legacy is not null && hasHealth && entry.Index == healthIndex && entry.Value.Kind == MetadataValueKind.Float)
                self.Health = entry.Value.AsFloat();
        }
        if (legacy is not null)
        {
            legacy.ObserveMetadata(packet.Metadata);
            context.PhysicsConditionsDirty();
        }

    }

    private static void ApplyAbilities(ClientboundPlayerAbilitiesPacket abilities, SelfState self)
    {
        byte flags = abilities.Flags;
        self.Invulnerable = (flags & 0x01) != 0;
        self.Flying = (flags & 0x02) != 0;
        self.MayFly = (flags & 0x04) != 0;
        self.InstantBuild = (flags & 0x08) != 0;
        self.FlyingSpeed = abilities.FlyingSpeed;
        self.WalkingSpeed = abilities.WalkingSpeed;
    }

    private static async ValueTask ApplyGameEventAsync(ClientboundGameEventPacket packet, ApplierContext context)
    {
        // Reason 3 = change game mode.
        if (packet.Event == 3)
        {
            var mode = (Game.Players.GameMode)(int)packet.Param;
            context.State.Self.GameMode = mode;
            context.PhysicsConditionsDirty();
            await context.PublishAsync(new GameModeChanged(mode)).ConfigureAwait(false);
            return;
        }

        // Weather, vanilla-mapped (ClientPacketListener.handleGameEvent): 1 begins raining
        // (vanilla resets the level to 0.0 and lets level packets build it), 2 ends raining
        // (vanilla leaves 1.0 to fade), 7 carries the rain level, 8 the thunder level.
        Umpk.Game.World.World world;
        try
        {
            world = context.State.World;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        switch (packet.Event)
        {
            case 1:
                world.SetRain(0.0f, raining: true);
                await context.PublishAsync(new RainLevelChanged(0.0f, true)).ConfigureAwait(false);
                break;
            case 2:
                world.SetRain(1.0f, raining: false);
                await context.PublishAsync(new RainLevelChanged(1.0f, false)).ConfigureAwait(false);
                break;
            case 7:
                {
                    float level = Math.Clamp(packet.Param, 0.0f, 1.0f);
                    if (level != world.RainLevel)
                    {
                        world.SetRain(level, raining: level > 0.0f);
                        await context.PublishAsync(new RainLevelChanged(level, level > 0.0f)).ConfigureAwait(false);
                    }

                    break;
                }
            case 8:
                {
                    float level = Math.Clamp(packet.Param, 0.0f, 1.0f);
                    if (level != world.ThunderLevel)
                    {
                        world.SetThunder(level);
                        await context.PublishAsync(new ThunderLevelChanged(level)).ConfigureAwait(false);
                    }

                    break;
                }
        }
    }
}
