using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
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
    private const int RelativeDeltaX = 1 << 5;
    private const int RelativeDeltaY = 1 << 6;
    private const int RelativeDeltaZ = 1 << 7;
    private const int RelativeRotateDelta = 1 << 8;

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
                await context.PublishAsync(new HealthChanged(health.Health, health.Food, health.Saturation)).ConfigureAwait(false);
                if (wasAlive && health.Health <= 0)
                    await context.PublishAsync(new Died()).ConfigureAwait(false);

                return true;

            case ClientboundPlayerPositionPacket pos:
                await ApplyPositionAsync(pos, context, ct).ConfigureAwait(false);
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
                context.PhysicsConditionsDirty();
                await context.PublishAsync(new AbilitiesChanged(
                    self.Invulnerable, self.Flying, self.MayFly, self.InstantBuild, self.FlyingSpeed, self.WalkingSpeed))
                    .ConfigureAwait(false);
                return true;

            case ClientboundCooldownPacket cooldown:
                // Below 1.21.2 the cooldown is per ITEM and the wire carries the item's registry id;
                // from 1.21.2 it is a cooldown-GROUP identifier. Keying on the group alone would fold every pre-1.21.2 cooldown onto one bucket (the default identifier's hash), so the item id wins wherever the era provides one.
                int cooldownKey = cooldown.ItemId ?? cooldown.CooldownGroup.GetHashCode();
                self.SetItemCooldown(cooldownKey, cooldown.Ticks);
                await context.PublishAsync(new ItemCooldownChanged(cooldownKey, cooldown.Ticks)).ConfigureAwait(false);
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
    /// 1.21.2+: the packet carries a <c>PositionMoveRotation</c> whose velocity rides the wire. The new pitch is clamped to [-90, 90]. When <c>ROTATE_DELTA</c> (bit 8) is set, the current velocity is first rotated by the rotation change (xRot by oldPitch - newPitch, then yRot by oldYaw - newYaw, in radians); then each axis adds the packet delta to that rotated current value when its <c>DELTA_*</c> bit (5/6/7) is set, and otherwise takes the packet delta outright.
    /// </description></item>
    /// </list>
    /// <para>Bit 8 only survives to this method because the modern codec carries the full-width bitset; a byte field would have dropped <c>ROTATE_DELTA</c> before any of this ran.</para>
    /// </remarks>
    private static async ValueTask ApplyPositionAsync(ClientboundPlayerPositionPacket pos, ApplierContext context, CancellationToken ct)
    {
        SelfState self = context.State.Self;
        int flags = pos.RelativeFlags;

        double x = (flags & RelativeX) != 0 ? self.Position.X + pos.X : pos.X;
        double y = (flags & RelativeY) != 0 ? self.Position.Y + pos.Y : pos.Y;
        double z = (flags & RelativeZ) != 0 ? self.Position.Z + pos.Z : pos.Z;
        float yaw = (flags & RelativeYaw) != 0 ? self.Yaw + pos.Yaw : pos.Yaw;
        float pitch = (flags & RelativePitch) != 0 ? self.Pitch + pos.Pitch : pos.Pitch;

        Vec3d velocity;
        if (pos.ModernValues is { } modern)
        {
            pitch = Math.Clamp(pitch, -90f, 90f);
            velocity = ResolveModernDelta(self, modern.DeltaMovement, flags, yaw, pitch);
        }
        else
            velocity = new Vec3d(
                (flags & RelativeX) != 0 ? self.Velocity.X : 0.0,
                (flags & RelativeY) != 0 ? self.Velocity.Y : 0.0,
                (flags & RelativeZ) != 0 ? self.Velocity.Z : 0.0);

        self.Position = new Vec3d(x, y, z);
        self.Velocity = velocity;
        self.Yaw = yaw;
        self.Pitch = pitch;
        self.HasSpawned = true;

        // Re-seed the physics engine at the absolute position resolved above (never at the raw packet fields, which may be relative offsets). The engine steps from its own position and syncs the result back into SelfState every tick, so without this the teleport survives less than one tick and the client reports the engine's stale position instead of the server's. The resolved velocity rides along, so a teleport that hands the client momentum keeps it.
        context.PhysicsPositionDirty();

        if (pos.TeleportId is { } teleportId)
        {
            await context.Sink.SendAsync(new ServerboundAcceptTeleportationPacket(teleportId), ct).ConfigureAwait(false);
            await context.Sink.SendAsync(
                    new ServerboundMovePlayerPosRotPacket(
                        x,
                        y,
                        z,
                        yaw,
                        pitch,
                        OnGround: false,
                        HorizontalCollision: false),
                    ct)
                .ConfigureAwait(false);
        }

        await context.PublishAsync(new PositionCorrected(self.Position, yaw, pitch)).ConfigureAwait(false);
    }

    /// <summary>The modern (1.21.2+) delta-movement resolution of <c>absolute teleport resolution</c>. <paramref name="newYaw"/> / <paramref name="newPitch"/> are the already-resolved absolute rotation; the self state still holds the pre-teleport rotation and delta, which is exactly what vanilla reads for the <c>ROTATE_DELTA</c> correction.</summary>
    private static Vec3d ResolveModernDelta(SelfState self, Vec3d packetDelta, int flags, float newYaw, float newPitch)
    {
        Vec3d current = self.Velocity;
        if ((flags & RelativeRotateDelta) != 0)
        {
            current = RotateX(current, float.DegreesToRadians(self.Pitch - newPitch));
            current = RotateY(current, float.DegreesToRadians(self.Yaw - newYaw));
        }

        return new Vec3d(
            (flags & RelativeDeltaX) != 0 ? current.X + packetDelta.X : packetDelta.X,
            (flags & RelativeDeltaY) != 0 ? current.Y + packetDelta.Y : packetDelta.Y,
            (flags & RelativeDeltaZ) != 0 ? current.Z + packetDelta.Z : packetDelta.Z);
    }

    /// <summary>Rotates a vector around the X axis using the game's sign convention.</summary>
    private static Vec3d RotateX(Vec3d v, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec3d(v.X, (v.Y * cos) + (v.Z * sin), (v.Z * cos) - (v.Y * sin));
    }

    /// <summary>Rotates a vector around the Y axis using the game's sign convention.</summary>
    private static Vec3d RotateY(Vec3d v, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec3d((v.X * cos) + (v.Z * sin), v.Y, (v.Z * cos) - (v.X * sin));
    }

    /// <summary>The entity type the local player is, for tier-2 metadata index resolution.</summary>
    private static readonly Identifier PlayerType = Identifier.Minecraft("player");

    /// <summary>Applies the fields of a self <c>set_entity_data</c> frame that <see cref="SelfState"/> tracks. Today that is the air supply alone; every other index stays where the entity store would have put it, which for self is nowhere.</summary>
    /// <remarks>
    /// <para>The index is resolved through the version's tier-2 key table rather than written as a literal. It is 1 on all 49 protocols because air is the second field and nothing has ever been inserted above it, but that is the TABLE's fact to state, not this applier's.</para>
    /// <para>Only a <see cref="MetadataValueKind.VarInt"/> is taken. On 1.8 the field is a 16-bit short rather than a VarInt, and the legacy metadata decoder already normalises both onto that one kind (<c>EntityMetadataCodec.ReadLegacy</c>: type 1 short and type 2 int both build <c>MetadataValue.VarInt</c>), so the check costs nothing on any era and refuses a value whose shape says the index does not mean what this table thinks it means.</para>
    /// <para>A frame carries only dirty metadata entries, so "this frame has no air in it" is the ordinary case and must leave the tracked value alone rather than reset it.</para>
    /// </remarks>
    private static void ApplySelfMetadata(ClientboundSetEntityDataPacket packet, ApplierContext context, SelfState self)
    {
        RegistryEntry<EntityTypeDefinition> playerType =
            context.EntityTypes.ResolveByKey(context.State.Registries, PlayerType);
        if (!context.MetadataKeys.TryResolveIndex(playerType, EntityMetadataKeys.AirSupply, out int airIndex))
            return;

        foreach (EntityDataEntry entry in packet.Metadata.Entries)
            if (entry.Index == airIndex && entry.Value.Kind == MetadataValueKind.VarInt)
                self.AirSupply = entry.Value.AsVarInt();

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
        }
    }
}
