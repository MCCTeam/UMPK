using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// A spawn frame's initial velocity has to reach the tracked entity. It was decoded on every protocol and applied on none: <c>EntityApplier</c>'s <c>add_entity</c> and <c>add_mob</c> arms forwarded position and rotation to <c>SpawnAsync</c> and never mentioned <c>VelocityX/Y/Z</c> or <c>ModernVelocityRaw</c>, so a thrown pearl, a fired arrow or a launched item tracked as stationary until the server sent a separate <c>set_entity_motion</c>, which for a projectile in flight it does not.
/// <para>The initial velocity applies to every supported entity type. On protocol 47 it applies only when the data field is positive, which is also when the velocity shorts are present.</para>
/// <para>The scale is <c>value * 8000</c>, clamped to +/-3.9.</para>
/// </summary>
public sealed class SpawnVelocityTests
{
    private const int EntityId = 4242;
    private const int SelfEntityId = 7;

    /// <summary>An ender-pearl-sized throw: 0.6 / 0.3 / -0.45 blocks per tick, times 8000.</summary>
    private const short ThrowX = 4800;
    private const short ThrowY = 2400;
    private const short ThrowZ = -3600;

    private static readonly Vec3d ExpectedThrow = new(0.6, 0.3, -0.45);

    /// <summary>The eras of <c>add_entity</c> that carry three velocity shorts: 1.9-1.18.2 (the pre-1.14 <c>SpawnObject</c> shape with an int data field), 1.19-1.21.4, and 1.21.5-1.21.8. Protocol 47 has its own gate and 773+ its own encoding, both covered separately below.</summary>
    public static TheoryData<int> ShortEraProtocols => [107, 404, 498, 758, 761, 767, 770, 772];

    [Theory]
    [MemberData(nameof(ShortEraProtocols))]
    public async Task AddEntity_SpawnVelocity_ReachesTheTrackedEntity(int protocol)
    {
        ApplierHarness harness = Harness(protocol);

        await harness.ApplyAsync(RoundTripAddEntity(protocol, ThrowX, ThrowY, ThrowZ, data: 1));

        Entity entity = Tracked(harness);
        AssertVelocity(ExpectedThrow, entity.Velocity);
    }

    /// <summary>Protocol 47 writes the three shorts ONLY when the data field is positive on 1.8.9 when the object data field is positive, and 1.8.9's client guards its <c>setVelocity</c> on the same field. A dropped item is item entities and arrows carry positive object data, so both are positive; a snowball, an egg, an ender pearl and primed TNT are all constructed with no data and therefore genuinely carry no velocity on 1.8.</summary>
    [Fact]
    public async Task AddEntity_On1_8_AppliesVelocityWhenTheDataFieldIsPositive()
    {
        ApplierHarness harness = Harness(47);

        await harness.ApplyAsync(RoundTripAddEntity(47, ThrowX, ThrowY, ThrowZ, data: 1));

        AssertVelocity(ExpectedThrow, Tracked(harness).Velocity);
    }

    /// <summary>The other half of the 1.8 gate: with data 0 the shorts are not on the wire at all, so the entity spawns at rest. Applying a velocity here would be inventing one, and asserting a nonzero value would pin a frame vanilla never sends.</summary>
    [Fact]
    public async Task AddEntity_On1_8_SpawnsAtRestWhenTheDataFieldIsZero()
    {
        ApplierHarness harness = Harness(47);

        await harness.ApplyAsync(RoundTripAddEntity(47, ThrowX, ThrowY, ThrowZ, data: 0));

        Assert.Equal(Vec3d.Zero, Tracked(harness).Velocity);
    }

    /// <summary>From 1.21.9 the three shorts are replaced by the low-precision quantized block, and the bound codec parks it raw with the shorts left at literal zero. Reading the shorts on those protocols yields zero for every entity, so the raw block has to be decoded, through the same <c>LowPrecisionVelocity.Decode</c> the <c>set_entity_motion</c> arm uses.</summary>
    /// <remarks>The fixed block <c>31337fff5c59</c> decodes to the standard knockback -0.39998779 / +0.36080083 / 0, making this a value assertion rather than a round trip.</remarks>
    [Theory]
    [InlineData(773)]
    [InlineData(776)]
    public async Task AddEntity_On1_21_9Plus_DecodesTheLowPrecisionBlock(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        byte[] block = Convert.FromHexString("31337fff5c59");

        await harness.ApplyAsync(RoundTripAddEntity(protocol, 0, 0, 0, data: 1, modernRaw: block));

        Vec3d velocity = Tracked(harness).Velocity;
        Assert.Equal(-0.39998779222364644, velocity.X, 12);
        Assert.Equal(0.36080083012879194, velocity.Y, 12);
        Assert.Equal(0.0, velocity.Z, 12);
    }

    /// <summary><c>add_mob</c> carries the same three shorts on every era it exists (1.8 through 1.18.2) and was dropped the same way. 573+ has no trailing metadata block; 107-498 does, and the codec differs, so both shapes are covered.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(758)]
    public async Task AddMob_SpawnVelocity_ReachesTheTrackedEntity(int protocol)
    {
        ApplierHarness harness = Harness(protocol);

        var mob = new ClientboundAddMobPacket(
            EntityId, TypeId: 1, X: 8.5, Y: 65.0, Z: -3.5, Yaw: 0f, Pitch: 0f, HeadPitch: 0f,
            VelocityX: ThrowX, VelocityY: ThrowY, VelocityZ: ThrowZ,
            Metadata: new EntityMetadataList([], []), Uuid: Guid.NewGuid());
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_mob", mob));

        AssertVelocity(ExpectedThrow, Tracked(harness).Velocity);
    }

    /// <summary>The velocity must be set BEFORE <see cref="EntitySpawned"/> is published. A subscriber is the first thing a host writes against a spawn, and one that read a zero here would have to wait for an unrelated packet to correct it. Setting it after the publish would pass a state assertion and fail this one, which is exactly the ordering half of the decode-and-drop family.</summary>
    [Fact]
    public async Task SpawnVelocity_IsVisibleToTheEntitySpawnedSubscriber()
    {
        ApplierHarness harness = Harness(770);
        Vec3d seen = new(double.NaN, double.NaN, double.NaN);
        harness.Events.Subscribe<EntitySpawned>(e => seen = e.Entity.Velocity);

        await harness.ApplyAsync(RoundTripAddEntity(770, ThrowX, ThrowY, ThrowZ, data: 1));

        AssertVelocity(ExpectedThrow, seen);
    }

    /// <summary>A spawn naming the client's OWN entity id routes to self state and pushes the physics engine, the same path <c>set_entity_motion</c> takes. Self is not in the entity store, so a store-only write would drop it, and a self-state write without the engine push would be overwritten by the next physics tick. A server does not normally spawn the local player this way, so the arm is defensive.</summary>
    [Fact]
    public async Task SpawnVelocity_NamingSelf_RoutesToSelfStateAndPushesPhysics()
    {
        Assert.True(JavaVersions.TryGetByProtocol(770, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true, Physics = true });
        harness.State.Self.EntityId = SelfEntityId;
        harness.State.Self.Velocity = Vec3d.Zero;

        var add = new ClientboundAddEntityPacket(
            SelfEntityId, Guid.NewGuid(), TypeId: 1, X: 0.5, Y: 65.0, Z: 0.5,
            XRot: 0f, YRot: 0f, YHeadRot: 0f, Data: 1,
            VelocityX: ThrowX, VelocityY: ThrowY, VelocityZ: ThrowZ, ModernVelocityRaw: null);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(770, "add_entity", add));

        AssertVelocity(ExpectedThrow, harness.State.Self.Velocity);
        Assert.Equal(1, harness.VelocityPushCount);
    }

    /// <summary>The frames that carry no velocity on any protocol spawn at rest rather than at some invented value: <c>add_player</c>, <c>add_experience_orb</c> and <c>add_painting</c> have no such field.</summary>
    [Fact]
    public async Task KeyedSpawns_HaveNoVelocity()
    {
        ApplierHarness harness = Harness(107);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            107, "add_experience_orb",
            new ClientboundAddExperienceOrbPacket(EntityId, 1.5, 65.0, 2.5, 7)));

        Assert.Equal(Vec3d.Zero, Tracked(harness).Velocity);
    }

    private static ApplierHarness Harness(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        return new ApplierHarness(version!, new ClientFeatures { Entities = true });
    }

    private static Entity Tracked(ApplierHarness harness)
    {
        Assert.True(harness.State.Entities.TryGet(EntityId, out Entity? entity));
        Assert.NotNull(entity);
        return entity!;
    }

    /// <summary>The shorts quantize to 1/8000 of a block, so the tolerance is the quantization and not a fudge: 4800 / 8000 is exactly 0.6.</summary>
    private static void AssertVelocity(Vec3d expected, Vec3d actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    private static object RoundTripAddEntity(
        int protocol, short vx, short vy, short vz, int data, byte[]? modernRaw = null)
    {
        var add = new ClientboundAddEntityPacket(
            EntityId, Guid.NewGuid(), TypeId: 1, X: 8.5, Y: 65.0, Z: -3.5,
            XRot: 0f, YRot: 0f, YHeadRot: 0f, Data: data,
            VelocityX: vx, VelocityY: vy, VelocityZ: vz, ModernVelocityRaw: modernRaw);
        return BoundDescriptorCodec.RoundTrip(protocol, "add_entity", add);
    }
}
