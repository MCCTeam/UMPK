using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Seeded byte-level round-trip tests for the entity packet family across representative protocol versions.</summary>
public class EntityCodecTests
{
    // A rotation degree value that came from a wire angle byte (so the packed-angle round-trip is exact).
    private static float AngleFromByte(Random rng) => rng.Next(256) * 360.0f / 256.0f;

    // A position that is an exact multiple of 1/32 (so the 1.8 fixed-point round-trip is exact).
    private static double FixedPos(Random rng) => rng.Next(-100000, 100000) / 32.0;

    // add_entity.

    [Theory]
    [InlineData(1)]
    [InlineData(77)]
    public void AddEntity_V1_8_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        int data = rng.Next(2) == 0 ? 0 : rng.Next(1, 50);
        var p = new ClientboundAddEntityPacket(
            rng.Next(), Guid.Empty, rng.Next(0, 60), FixedPos(rng), FixedPos(rng), FixedPos(rng),
            AngleFromByte(rng), AngleFromByte(rng), 0, data,
            (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), null);

        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddEntityV1_8, p);
        Assert.Equal(p.EntityId, d.EntityId);
        Assert.Equal(p.X, d.X);
        Assert.Equal(p.Data, d.Data);
        if (data > 0)
            Assert.Equal(p.VelocityX, d.VelocityX);

    }

    [Fact]
    public void AddEntity_V1_8_OmitsVelocityWhenDataZero()
    {
        var p = new ClientboundAddEntityPacket(5, Guid.Empty, 3, 1.0, 2.0, 3.0, 0, 0, 0, 0, 111, 222, 333, null);
        byte[] bytes = CodecRoundTrip.Encode(EntitySpawnCodecs.AddEntityV1_8, p);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddEntityV1_8, p);
        Assert.Equal(0, d.VelocityX); // dropped because data == 0
        // id(1) type(1) xyz(12) pitch/yaw(2) data(4) = 20 bytes, no velocity
        Assert.True(bytes.Length == 20);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(88)]
    public void AddEntity_V1_21_5_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        var p = new ClientboundAddEntityPacket(
            rng.Next(), Guid.NewGuid(), rng.Next(0, 100), rng.NextDouble() * 1000, rng.NextDouble() * 1000, rng.NextDouble() * 1000,
            AngleFromByte(rng), AngleFromByte(rng), AngleFromByte(rng), rng.Next(0, 1000),
            (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), null);

        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddEntityV1_19, p);
        Assert.Equal(p, d);
    }

    [Fact]
    public void AddEntity_V26_1_CarriesLpVelocityRawAndRoundTripsBytes()
    {
        // A non-zero LpVec3 payload: lowest byte with no continuation bit, then middle + 4-byte int.
        byte[] lp = [0x03, 0x11, 0x22, 0x33, 0x44, 0x55];
        var p = new ClientboundAddEntityPacket(
            9, Guid.NewGuid(), 4, 10.0, 20.0, 30.0, 45f, 90f, 135f, 7, 0, 0, 0, lp);
        byte[] first = CodecRoundTrip.Encode(EntitySpawnCodecs.AddEntityV1_21_9, p);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddEntityV1_21_9, p);
        Assert.Equal(lp, d.ModernVelocityRaw);
        byte[] second = CodecRoundTrip.Encode(EntitySpawnCodecs.AddEntityV1_21_9, d);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AddEntity_V26_1_ZeroLpVelocityIsSingleByte()
    {
        var p = new ClientboundAddEntityPacket(1, Guid.NewGuid(), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [0x00]);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddEntityV1_21_9, p);
        Assert.NotNull(d.ModernVelocityRaw);
        Assert.Equal([(byte)0x00], d.ModernVelocityRaw!);
    }

    // Movement.

    [Theory]
    [InlineData(3)]
    public void MoveEntityPos_LegacyAndModern_RoundTrip(int seed)
    {
        var rng = new Random(seed);
        var legacy = new ClientboundMoveEntityPosPacket(rng.Next(), (sbyte)rng.Next(-128, 128), (sbyte)rng.Next(-128, 128), (sbyte)rng.Next(-128, 128), rng.Next(2) == 0);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityMoveCodecs.MoveEntityPosV1_8, legacy));

        var modern = new ClientboundMoveEntityPosPacket(rng.Next(), (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), (short)rng.Next(short.MinValue, short.MaxValue), rng.Next(2) == 0);
        Assert.Equal(modern, CodecRoundTrip.Cycle(EntityMoveCodecs.MoveEntityPosV1_9, modern));
    }

    [Fact]
    public void MoveEntityRot_RoundTrips()
    {
        var rng = new Random(5);
        var p = new ClientboundMoveEntityRotPacket(rng.Next(), AngleFromByte(rng), AngleFromByte(rng), true);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityMoveCodecs.MoveEntityRot, p));
    }

    [Fact]
    public void MoveEntityPosRot_LegacyAndModern_RoundTrip()
    {
        var rng = new Random(9);
        var legacy = new ClientboundMoveEntityPosRotPacket(rng.Next(), (sbyte)-5, (sbyte)7, (sbyte)-1, AngleFromByte(rng), AngleFromByte(rng), false);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityMoveCodecs.MoveEntityPosRotV1_8, legacy));

        var modern = legacy with { DeltaX = 2000, DeltaY = -3000 };
        Assert.Equal(modern, CodecRoundTrip.Cycle(EntityMoveCodecs.MoveEntityPosRotV1_9, modern));
    }

    [Fact]
    public void TeleportEntity_Legacy_RoundTrips()
    {
        var rng = new Random(11);
        var p = new ClientboundTeleportEntityPacket(rng.Next(), FixedPos(rng), FixedPos(rng), FixedPos(rng), AngleFromByte(rng), AngleFromByte(rng), true, null, 0);
        var d = CodecRoundTrip.Cycle(EntityMoveCodecs.TeleportEntityV1_8, p);
        Assert.Equal(p.EntityId, d.EntityId);
        Assert.Equal(p.X, d.X);
        Assert.Equal(p.Yaw, d.Yaw);
    }

    [Fact]
    public void TeleportEntity_Modern_RoundTrips()
    {
        var values = new PositionMoveRotation(new Vec3d(1, 2, 3), new Vec3d(0.1, 0.2, 0.3), 45f, 22.5f);
        var p = new ClientboundTeleportEntityPacket(42, 1, 2, 3, 45f, 22.5f, true, values, 0b10101);
        var d = CodecRoundTrip.Cycle(EntityMoveCodecs.TeleportEntityV1_21_2, p);
        Assert.Equal(values, d.ModernValues);
        Assert.Equal(0b10101, d.Relatives);
        Assert.True(d.OnGround);
    }

    [Fact]
    public void EntityPositionSync_RoundTrips()
    {
        var values = new PositionMoveRotation(new Vec3d(-10, 64, 200), new Vec3d(1, -1, 0), 180f, -90f);
        var p = new ClientboundEntityPositionSyncPacket(7, values, false);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityMoveCodecs.EntityPositionSync, p));
    }

    [Fact]
    public void RotateHeadAndVelocity_RoundTrip()
    {
        var rng = new Random(13);
        var head = new ClientboundRotateHeadPacket(rng.Next(), AngleFromByte(rng));
        Assert.Equal(head, CodecRoundTrip.Cycle(EntityMoveCodecs.RotateHead, head));

        var motion = new ClientboundSetEntityMotionPacket(rng.Next(), (short)1, (short)-2, (short)3);
        Assert.Equal(motion, CodecRoundTrip.Cycle(EntityMoveCodecs.SetEntityMotion, motion));
    }

    // Remove, passengers, and links.

    [Fact]
    public void RemoveEntities_EmptyAndPopulated_RoundTrip()
    {
        var empty = new ClientboundRemoveEntitiesPacket([]);
        Assert.Equal(empty.EntityIds, CodecRoundTrip.Cycle(EntitySpawnCodecs.RemoveEntities, empty).EntityIds);

        var populated = new ClientboundRemoveEntitiesPacket([1, 2, 3, 999999]);
        Assert.Equal(populated.EntityIds, CodecRoundTrip.Cycle(EntitySpawnCodecs.RemoveEntities, populated).EntityIds);
    }

    [Fact]
    public void SetPassengers_RoundTrips()
    {
        var p = new ClientboundSetPassengersPacket(10, [20, 30]);
        var d = CodecRoundTrip.Cycle(EntityStateCodecs.SetPassengers, p);
        Assert.Equal(p.VehicleId, d.VehicleId);
        Assert.Equal(p.Passengers, d.Passengers);
    }

    [Fact]
    public void SetEntityLink_LegacyHasLeashModernDoesNot()
    {
        var legacy = new ClientboundSetEntityLinkPacket(3, 4, true);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityStateCodecs.SetEntityLinkV1_8, legacy));

        var modern = new ClientboundSetEntityLinkPacket(3, -1, null);
        var d = CodecRoundTrip.Cycle(EntityStateCodecs.SetEntityLinkV1_9, modern);
        Assert.Equal(3, d.SourceId);
        Assert.Equal(-1, d.DestId);
        Assert.Null(d.LegacyLeash);
    }

    // Events.

    [Fact]
    public void Animate_And_EntityEvent_RoundTrip()
    {
        var anim = new ClientboundAnimatePacket(55, 2);
        Assert.Equal(anim, CodecRoundTrip.Cycle(EntityStateCodecs.Animate, anim));

        var ev = new ClientboundEntityEventPacket(-12345, (sbyte)7);
        Assert.Equal(ev, CodecRoundTrip.Cycle(EntityStateCodecs.EntityEvent, ev));
    }

    [Fact]
    public void HurtAnimation_RoundTrips()
    {
        var p = new ClientboundHurtAnimationPacket(9, 123.5f);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityStateCodecs.HurtAnimation, p));
    }

    [Fact]
    public void DamageEvent_OptionalIdsAndPosition_RoundTrip()
    {
        var withAll = new ClientboundDamageEventPacket(1, 5, 6, 7, new Vec3d(1.5, 2.5, 3.5));
        Assert.Equal(withAll, CodecRoundTrip.Cycle(EntityStateCodecs.DamageEvent, withAll));

        var absent = new ClientboundDamageEventPacket(1, 5, null, null, null);
        Assert.Equal(absent, CodecRoundTrip.Cycle(EntityStateCodecs.DamageEvent, absent));
    }

    [Fact]
    public void TakeItemEntity_LegacyAndModern_RoundTrip()
    {
        var legacy = new ClientboundTakeItemEntityPacket(3, 4, null);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityStateCodecs.TakeItemEntityV1_8, legacy));

        var modern = new ClientboundTakeItemEntityPacket(3, 4, 64);
        Assert.Equal(modern, CodecRoundTrip.Cycle(EntityStateCodecs.TakeItemEntityV1_11, modern));
    }

    // Equipment.

    [Fact]
    public void SetEquipment_Legacy_EmptyItem_RoundTrips()
    {
        var p = new ClientboundSetEquipmentPacket(7, EquipmentSlot.Head, EntityItemSlot.Empty, null);
        var d = CodecRoundTrip.Cycle(EntityEquipmentCodecs.SetEquipmentV1_8, p);
        Assert.Equal(EquipmentSlot.Head, d.LegacySlot);
        Assert.True(d.LegacyItem!.IsEmpty);
    }

    [Fact]
    public void SetEquipment_Modern_RawTail_RoundTripsBytes()
    {
        byte[] raw = [0x05, 0x00, 0x80, 0x00]; // an opaque modern equipment list body
        var p = new ClientboundSetEquipmentPacket(7, EquipmentSlot.MainHand, null, raw);
        var d = CodecRoundTrip.Cycle(EntityEquipmentCodecs.SetEquipmentV1_16, p);
        Assert.Equal(raw, d.ModernRaw);
    }

    // Attributes.

    [Fact]
    public void UpdateAttributes_Legacy_RoundTrips()
    {
        var mods = new[] { new AttributeModifierEntry(Guid.NewGuid(), null, 0.25, 1) };
        var attrs = new[] { new AttributeSnapshot("generic.maxHealth", 0, 20.0, mods) };
        var p = new ClientboundUpdateAttributesPacket(5, attrs);
        var d = CodecRoundTrip.Cycle(EntityEquipmentCodecs.UpdateAttributesV1_8, p);
        Assert.Single(d.Attributes);
        Assert.Equal("generic.maxHealth", d.Attributes[0].LegacyKey);
        Assert.Equal(0.25, d.Attributes[0].Modifiers[0].Amount);
    }

    [Fact]
    public void UpdateAttributes_Modern_RoundTrips()
    {
        var mods = new[] { new AttributeModifierEntry(Guid.Empty, "minecraft:base_speed", -0.1, 2) };
        var attrs = new[] { new AttributeSnapshot(null, 3, 0.1, mods), new AttributeSnapshot(null, 5, 16.0, []) };
        var p = new ClientboundUpdateAttributesPacket(9, attrs);
        var d = CodecRoundTrip.Cycle(EntityEquipmentCodecs.UpdateAttributesV1_21, p);
        Assert.Equal(2, d.Attributes.Count);
        Assert.Equal("minecraft:base_speed", d.Attributes[0].Modifiers[0].ModernId);
        Assert.Empty(d.Attributes[1].Modifiers);
    }

    // Mob effects.

    [Fact]
    public void MobEffect_Legacy_AmplifierByte()
    {
        var p = new ClientboundUpdateMobEffectPacket(5, 3, 2, 600, 0x01);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_8, p));
    }

    [Fact]
    public void MobEffect_Modern_AmplifierVarInt()
    {
        // Modern amplifier is a VarInt (not a byte); a large amplifier must not corrupt duration/flags.
        var p = new ClientboundUpdateMobEffectPacket(5, 30, 200, 100000, 0x0F);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_20_5, p);
        Assert.Equal(p, d);
    }

    [Fact]
    public void RemoveMobEffect_LegacyAndModern_RoundTrip()
    {
        var legacy = new ClientboundRemoveMobEffectPacket(5, 12);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityEffectCodecs.RemoveMobEffectV1_8, legacy));

        var modern = new ClientboundRemoveMobEffectPacket(5, 500);
        Assert.Equal(modern, CodecRoundTrip.Cycle(EntityEffectCodecs.RemoveMobEffectV1_18, modern));
    }

    // Player state.

    [Fact]
    public void CameraExperienceHealth_RoundTrip()
    {
        Assert.Equal(new ClientboundSetCameraPacket(99), CodecRoundTrip.Cycle(EntityStateCodecs.SetCamera, new ClientboundSetCameraPacket(99)));
        var xp = new ClientboundSetExperiencePacket(0.5f, 30, 1395);
        Assert.Equal(xp, CodecRoundTrip.Cycle(EntityStateCodecs.SetExperience, xp));
        var hp = new ClientboundSetHealthPacket(19.5f, 18, 4.0f);
        Assert.Equal(hp, CodecRoundTrip.Cycle(EntityStateCodecs.SetHealth, hp));
    }

    [Fact]
    public void SetHeldSlot_LegacyByteModernVarInt()
    {
        var legacy = new ClientboundSetHeldSlotPacket(3);
        Assert.Equal([(byte)3], CodecRoundTrip.Encode(EntityStateCodecs.SetHeldSlotV1_8, legacy)); // single byte on 1.8
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityStateCodecs.SetHeldSlotV1_8, legacy));
        Assert.Equal(legacy, CodecRoundTrip.Cycle(EntityStateCodecs.SetHeldSlotV1_21_4, legacy));
    }

    [Fact]
    public void PlayerPosition_LegacyAndModern_RoundTrip()
    {
        var legacy = new ClientboundPlayerPositionPacket(1.0, 64.0, -3.0, 90f, 0f, 0x1F, null, null);
        var dl = CodecRoundTrip.Cycle(EntityMoveCodecs.PlayerPositionV1_8, legacy);
        Assert.Equal(legacy.X, dl.X);
        Assert.Equal((byte)0x1F, dl.RelativeFlags);

        var values = new PositionMoveRotation(new Vec3d(1, 64, -3), Vec3d.Zero, 90f, 0f);
        var modern = new ClientboundPlayerPositionPacket(1, 64, -3, 90f, 0f, 0, 55, values);
        var dm = CodecRoundTrip.Cycle(EntityMoveCodecs.PlayerPositionV1_21_2, modern);
        Assert.Equal(55, dm.TeleportId);
        Assert.Equal(values, dm.ModernValues);
    }

    [Fact]
    public void UseBed_Legacy_RoundTrips()
    {
        var p = new ClientboundUseBedPacket(5, new BlockPos(10, 64, -20));
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityStateCodecs.UseBedV1_8, p));
    }

    // Dedicated 1.8 spawns present in the protocol-47 dataset.

    [Theory]
    [InlineData(101)]
    [InlineData(202)]
    public void AddPlayer_V1_8_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        var p = new ClientboundAddPlayerPacket(
            rng.Next(), Guid.NewGuid(), FixedPos(rng), FixedPos(rng), FixedPos(rng),
            AngleFromByte(rng), AngleFromByte(rng), (short)rng.Next(0, 400), new EntityMetadataList([], []));

        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddPlayerV1_8, p);
        Assert.Equal(p.EntityId, d.EntityId);
        Assert.Equal(p.Uuid, d.Uuid);
        Assert.Equal(p.X, d.X);
        Assert.Equal(p.Yaw, d.Yaw);
        Assert.Equal(p.CurrentItem, d.CurrentItem);
        Assert.Empty(d.Metadata.Entries);
    }

    [Fact]
    public void AddPainting_V1_8_RoundTrips()
    {
        var p = new ClientboundAddPaintingPacket(42, "Kebab", new BlockPos(3, 64, -7), 2, Uuid: null, MotiveId: null);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntitySpawnCodecs.AddPaintingV1_8, p));
    }

    [Theory]
    [InlineData(303)]
    [InlineData(404)]
    public void AddExperienceOrb_V1_8_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        var p = new ClientboundAddExperienceOrbPacket(rng.Next(), FixedPos(rng), FixedPos(rng), FixedPos(rng), (short)rng.Next(0, 5000));
        Assert.Equal(p, CodecRoundTrip.Cycle(EntitySpawnCodecs.AddExperienceOrbV1_8, p));
    }

    [Fact]
    public void AddGlobalEntity_V1_8_RoundTrips()
    {
        var rng = new Random(505);
        var p = new ClientboundAddGlobalEntityPacket(rng.Next(), 1, FixedPos(rng), FixedPos(rng), FixedPos(rng));
        Assert.Equal(p, CodecRoundTrip.Cycle(EntitySpawnCodecs.AddGlobalEntityV1_8, p));
    }

    [Fact]
    public void Entity_V1_8_RoundTrips()
    {
        var p = new ClientboundEntityPacket(123456);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityMoveCodecs.EntityV1_8, p));
    }

    // 1.8 serverbound rotation and position-rotation move, outside the protocol-47 dataset surface.

    [Fact]
    public void MovePlayerRot_V1_8_RoundTrips()
    {
        var rng = new Random(606);
        var p = new ServerboundMovePlayerRotPacket((float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 180 - 90), true, false);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerRotV1_8, p));
    }

    [Fact]
    public void MovePlayerPosRot_V1_8_RoundTrips()
    {
        var rng = new Random(707);
        var p = new ServerboundMovePlayerPosRotPacket(
            rng.NextDouble() * 1000, rng.NextDouble() * 256, rng.NextDouble() * 1000,
            (float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 180 - 90), false, false);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerPosRotV1_8, p));
    }
}
