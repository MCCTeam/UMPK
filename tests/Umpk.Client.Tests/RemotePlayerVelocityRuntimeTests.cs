using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RemotePlayerVelocityRuntimeTests
{
    private const int RemoteId = 42;
    private static readonly Vec3d ShortVelocity = new(0.3, -0.2, 0.7);
    private static readonly Vec3d QuantizedVelocity = new(-0.39998779222364644, 0.36080083012879194, 0);

    public static IEnumerable<object[]> Protocols => JavaVersions.All.Select(version => new object[] { version.Version.Protocol });

    public static IEnumerable<object[]> GenericSpawnProtocols => Protocols.Where(row => (int)row[0] >= 764);

    public static IEnumerable<object[]> LegacyMembershipProtocols => Protocols.Where(row => (int)row[0] <= 404)
        .SelectMany(row => new[] { new object[] { row[0], false }, new object[] { row[0], true } });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SharedEmptyChunkBedContextSurvivesActorReplacementAndEndsWithItsWorld(bool overwritten, bool terrain)
    {
        const int protocol = 47;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain, physics: false);
        await JoinAsync(client, server, version, ct);
        using JsonDocument document = LegacyPlayerEmptyAliasTests.ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == "1.8.9");
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement scenario = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("point").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == (overwritten ? 0 : 6) && row.GetProperty("sleep_cell").GetInt32() == (overwritten ? 0 : 1));
        await Spawn();
        Entity actor = client.State.Entities.Get(RemoteId)!;
        foreach (JsonElement packet in scenario.GetProperty("world_packets").EnumerateArray())
        {
            byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
            await SendAndObserveAsync(client, server, version, Decode("block_update", body), ct);
        }
        var sleep = Assert.IsType<ClientboundUseBedPacket>(Decode("use_bed", Convert.FromHexString(scenario.GetProperty("packets")[0].GetProperty("body").GetString()!)));

        await SendAndObserveAsync(client, server, version, sleep, ct);

        bool known = terrain && !overwritten;
        Assert.Equal(known, await IsKnown(actor));
        JsonElement nativeSleep = scenario.GetProperty("observations").EnumerateArray().Single(row => row.GetProperty("operation").GetString() == "after_sleep").GetProperty("state").GetProperty("position");
        Vec3d expected = known ? new Vec3d(nativeSleep[0].GetDouble(), nativeSleep[1].GetDouble(), nativeSleep[2].GetDouble()) : new Vec3d(1.5, 64, 1.5);
        Assert.Equal(expected, actor.Position);
        await ticks.FireAsync(ct);
        Assert.Equal(known, await IsKnown(actor));
        Assert.Equal(expected, actor.Position);

        await Spawn();
        Entity replacement = client.State.Entities.Get(RemoteId)!;
        Assert.NotSame(actor, replacement);
        await SendAndObserveAsync(client, server, version, sleep, ct);
        Assert.Equal(known, await IsKnown(replacement));
        Assert.Equal(expected, replacement.Position);

        await JoinWorldAsync(client, server, version, ct);
        Assert.Null(client.State.Entities.Get(RemoteId));
        await Spawn();
        Entity nextWorld = client.State.Entities.Get(RemoteId)!;
        await SendAndObserveAsync(client, server, version, sleep, ct);
        Assert.False(await IsKnown(nextWorld));
        Assert.Equal(new Vec3d(1.5, 64, 1.5), nextWorld.Position);
        await client.DisconnectAsync(ct);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(ct);
        });
        return;

        Task Spawn() => SendAndObserveAsync(client, server, version, new ClientboundAddPlayerPacket(RemoteId, Guid.NewGuid(), 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([new(0, MetadataValue.Byte(2))], [])), ct);
        Task<bool> IsKnown(Entity entity) => client.InvokeAsync(c => c.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol), ct);
        IPacket Decode(string identifier, byte[] body) => Assert.IsAssignableFrom<IPacket>(BoundDescriptorCodec.Clientbound(protocol, identifier).Decode(body, new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty)));
    }

    [Theory]
    [MemberData(nameof(LegacyMembershipProtocols))]
    public async Task LegacyDeathLifetimeUsesOnlyEligibleSessionTicksAndRetainsPacketLookup(int protocol, bool terrain)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, NativeMembershipChunk(protocol, "initial_full"), ct);
        await Spawn();
        Entity actor = client.State.Entities.Get(RemoteId)!;
        Assert.True(JavaGameData.EntityMetadataKeys(protocol).TryResolveIndex(actor.Type, EntityMetadataKeys.Health, out int healthIndex));
        await SendAndObserveAsync(client, server, version, new ClientboundSetEntityDataPacket(RemoteId, new EntityMetadataList([new(healthIndex, MetadataValue.Float(0))], [])), ct);
        await client.InvokeAsync(_ => true, ct);
        Assert.True(await client.InvokeAsync(c => c.State.EntityMembership.CanTick(actor), ct));
        for (int tick = 0; tick < 19; tick++)
            await ticks.FireAsync(ct);
        Assert.True(await client.InvokeAsync(c => c.State.EntityMembership.CanTick(actor), ct));

        await ticks.FireAsync(ct);

        Assert.False(await client.InvokeAsync(c => c.State.EntityMembership.CanTick(actor), ct));
        Assert.False(await client.InvokeAsync(c => c.State.EntityMembership.IsWorldQueryMember(actor), ct));
        Assert.Same(actor, client.State.Entities.Get(RemoteId));
        Vec3d position = actor.Position;
        await SendAndObserveAsync(client, server, version, new ClientboundTeleportEntityPacket(RemoteId, 3.5, 65, 3.5, 90, 45, false, null, 0), ct);
        for (int tick = 0; tick < 3; tick++)
            await ticks.FireAsync(ct);
        Assert.Equal(position, actor.Position);

        await Spawn();
        Entity replacement = client.State.Entities.Get(RemoteId)!;
        Assert.NotSame(actor, replacement);
        for (int tick = 0; tick < 20; tick++)
            await ticks.FireAsync(ct);
        Assert.True(await client.InvokeAsync(c => c.State.EntityMembership.CanTick(replacement), ct));
        await JoinWorldAsync(client, server, version, ct);
        Assert.Null(client.State.Entities.Get(RemoteId));
        await Spawn();
        Entity nextWorld = client.State.Entities.Get(RemoteId)!;
        await ticks.FireAsync(ct);
        Assert.False(await client.InvokeAsync(c => c.State.EntityMembership.IsWorldQueryMember(nextWorld), ct));

        await client.DisconnectAsync(ct);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(ct);
        });

        Task Spawn() => SendAndObserveAsync(client, server, version, new ClientboundAddPlayerPacket(RemoteId, Guid.NewGuid(), 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([], [])), ct);
    }

    [Theory]
    [MemberData(nameof(LegacyMembershipProtocols))]
    public async Task LegacyMembershipAndSizeFollowFramedPacketsAndSessionTicks(int protocol, bool terrain)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain, physics: false);
        await JoinAsync(client, server, version, ct);
        var initial = new Vec3d(1.5, 64, 1.5);
        var target = initial.Add(2, 1, 2);
        await SpawnAndTarget();
        Entity actor = client.State.Entities.Get(RemoteId)!;
        await SendAndObserveAsync(client, server, version, new ClientboundBlockUpdatePacket(new BlockPos(1, 64, 1), 0), ct);
        Assert.Equal(terrain, client.State.HasWorld);
        if (terrain)
            Assert.NotNull(client.State.World.GetColumn(new ChunkPos(0, 0)));

        await ticks.FireAsync(ct);
        Assert.Equal(initial, actor.Position);
        await SendAndObserveAsync(client, server, version, NativeChunk("initial_full"), ct);
        await ticks.FireAsync(ct);
        Assert.Equal(protocol == 47 ? initial.Add(2.0 / 3, 1.0 / 3, 2.0 / 3) : initial, actor.Position);
        for (int i = 0; i < 3; i++)
            await ticks.FireAsync(ct);
        Assert.Equal(target, actor.Position);
        bool known = await client.InvokeAsync(c => c.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol), ct);
        Assert.Equal(terrain || protocol == 47, known);

        await SendAndObserveAsync(client, server, version, new ClientboundTeleportEntityPacket(RemoteId, target.X + 2, target.Y + 1, target.Z + 2, 90, 45, false, null, 0), ct);
        await SendAndObserveAsync(client, server, version, NativeChunk("unload"), ct);
        await ticks.FireAsync(ct);
        Assert.Equal(target, actor.Position);
        if (terrain)
            Assert.Null(client.State.World.GetColumn(new ChunkPos(0, 0)));
        await ticks.FireAsync(ct);
        Assert.NotEqual(target, actor.Position);

        Vec3d previous = actor.Position;
        await JoinWorldAsync(client, server, version, ct);
        Assert.Null(client.State.Entities.Get(RemoteId));
        await SpawnAndTarget();
        Entity replacement = client.State.Entities.Get(RemoteId)!;
        Assert.NotSame(actor, replacement);
        await ticks.FireAsync(ct);
        Assert.Equal(initial, replacement.Position);
        Assert.Equal(previous, actor.Position);

        await client.DisconnectAsync(ct);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(ct);
        });

        async Task SpawnAndTarget()
        {
            var metadata = new EntityMetadataList([new(0, MetadataValue.Byte(2))], []);
            await SendAndObserveAsync(client, server, version, new ClientboundAddPlayerPacket(RemoteId, Guid.NewGuid(), initial.X, initial.Y, initial.Z, 0, 0, 0, metadata), ct);
            await SendAndObserveAsync(client, server, version, new ClientboundTeleportEntityPacket(RemoteId, target.X, target.Y, target.Z, 90, 45, false, null, 0), ct);
        }

        IPacket NativeChunk(string action) => NativeMembershipChunk(protocol, action);
    }

    [Theory]
    [InlineData(755, false)]
    [InlineData(755, true)]
    [InlineData(762, false)]
    [InlineData(762, true)]
    [InlineData(764, false)]
    [InlineData(764, true)]
    [InlineData(768, false)]
    [InlineData(768, true)]
    [InlineData(769, false)]
    [InlineData(769, true)]
    public async Task PositionInterpolationUsesTheSessionClockWithoutLocalPhysics(int protocol, bool terrain)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d initial = entity.Position;
        var moved = new List<Vec3d>();
        using IDisposable subscription = client.Events.Subscribe<EntityMoved>(message =>
        {
            if (message.EntityId == RemoteId)
                moved.Add(entity.Position);
        });
        await SendAndObserveAsync(client, server, version, new ClientboundMoveEntityPosPacket(RemoteId, 4096, 0, 0, true), ct);
        Assert.Equal(initial, entity.Position);
        await client.InvokeAsync(_ => true, ct);
        Assert.Equal(initial, entity.Position);

        await ticks.FireAsync(ct);
        Assert.InRange(entity.Position.X, initial.X + 0.3, initial.X + 0.4);
        await ticks.FireAsync(ct);
        await ticks.FireAsync(ct);
        Assert.Equal(initial.Add(1, 0, 0), entity.Position);
        Assert.Equal(3, moved.Count(position => position != initial));

        await SendAndObserveAsync(client, server, version, new ClientboundMoveEntityPosPacket(RemoteId, 4096, 0, 0, true), ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        Entity replacement = client.State.Entities.Get(RemoteId)!;
        Assert.NotSame(entity, replacement);
        await ticks.FireAsync(ct);
        Assert.Equal(initial, replacement.Position);
        await SendAndObserveAsync(client, server, version, new ClientboundMoveEntityPosPacket(RemoteId, 4096, 0, 0, true), ct);
        await SendAndObserveAsync(client, server, version, new ClientboundRemoveEntitiesPacket([RemoteId]), ct);
        await ticks.FireAsync(ct);
        Assert.Null(client.State.Entities.Get(RemoteId));
        Assert.Equal(initial, replacement.Position);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MotionObservationsUseTheNativeRemotePlayerEra(int protocol)
    {
        ApplierHarness harness = Create(protocol);
        await harness.ApplyAsync(Spawn(harness, protocol));
        Entity entity = harness.State.Entities.Get(RemoteId)!;
        Assert.Equal(Vec3d.Zero, entity.Velocity);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "set_entity_motion", Motion(protocol)));

        Assert.Equal(protocol < 762 ? Target(protocol) : Vec3d.Zero, entity.Velocity);
        Assert.Equal(0, harness.VelocityPushCount);
    }

    [Theory]
    [InlineData(498)]
    [InlineData(754)]
    public async Task ActiveInterpolationReappliesPlayerBoundsEvenWithoutCoordinateChanges(int protocol)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain: false, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d position = entity.Position;
        IEntityGeometrySource source = JavaGameData.EntityGeometryData(protocol);

        await ChangePose(EntityPose.Sleeping);
        await ChangePose(EntityPose.Standing);
        AssertMinimum(position.X - .2F / 2.0);
        await ticks.FireAsync(ct);
        AssertMinimum(position.X - .2F / 2.0);

        await SendAndObserveAsync(client, server, version, new ClientboundMoveEntityPosPacket(RemoteId, 0, 0, 0, false), ct);
        await ticks.FireAsync(ct);

        AssertMinimum(position.X - .6F / 2.0);
        Assert.Equal(position, entity.Position);
        await ticks.FireAsync(ct);
        await ticks.FireAsync(ct);
        await ChangePose(EntityPose.Sleeping);
        await ChangePose(EntityPose.Standing);
        await ticks.FireAsync(ct);
        AssertMinimum(position.X - .2F / 2.0);

        Task ChangePose(EntityPose pose) => SendAndObserveAsync(client, server, version, new ClientboundSetEntityDataPacket(RemoteId, new EntityMetadataList([new(6, MetadataValue.Pose(pose))], [])), ct);
        void AssertMinimum(double minimum)
        {
            Assert.True(client.State.EntityGeometry.TryGet(entity, source, out Aabb box, out _, protocol));
            Assert.Equal(minimum, box.MinX);
        }
    }

    [Theory]
    [MemberData(nameof(GenericSpawnProtocols))]
    public async Task GenericPlayerSpawnAppliesVelocityOnlyFromTheNativeBoundary(int protocol)
    {
        ApplierHarness harness = Create(protocol);
        Vec3d? published = null;
        using IDisposable subscription = harness.Events.Subscribe<EntitySpawned>(message => published = message.Entity.Velocity);

        await harness.ApplyAsync(Spawn(harness, protocol, velocity: true));

        Assert.Equal(protocol < 770 ? Vec3d.Zero : Target(protocol), harness.State.Entities.Get(RemoteId)!.Velocity);
        Assert.Equal(harness.State.Entities.Get(RemoteId)!.Velocity, published);
    }

    [Theory]
    [InlineData(768)]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(777)]
    public async Task TeleportSetsCurrentVelocityWithoutCancellingTheMotionTarget(int protocol)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain: false, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        await ticks.FireAsync(ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d current = new(0.875, 0.5, 0.75);
        await SendAndObserveAsync(client, server, version,
            new ClientboundTeleportEntityPacket(RemoteId, 8.5, 65, -3.5, 0, 0, false,
                new PositionMoveRotation(new Vec3d(8.5, 65, -3.5), current, 0, 0), 0), ct);
        Assert.Equal(current, entity.Velocity);

        await ticks.FireAsync(ct);
        Vec3d second = Advance(current, Target(protocol), 2);
        Assert.Equal(second, entity.Velocity);
        await ticks.FireAsync(ct);
        Assert.Equal(Advance(second, Target(protocol), 1), entity.Velocity);
    }

    [Theory]
    [InlineData(770, false)]
    [InlineData(777, false)]
    [InlineData(770, true)]
    [InlineData(777, true)]
    public async Task SameWorldRespawnRetainsMotionWhileFreshJoinDiscardsIt(int protocol, bool join)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain: false, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        await ticks.FireAsync(ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d before = entity.Velocity;
        if (join)
            await JoinWorldAsync(client, server, version, ct);
        else
        {
            var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
            await SendAndObserveAsync(client, server, version, new ClientboundRespawnPacket(spawn, 0, null), ct);
        }

        await ticks.FireAsync(ct);

        if (join)
        {
            Assert.Null(client.State.Entities.Get(RemoteId));
            Assert.Equal(before, entity.Velocity);
            await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
            await ticks.FireAsync(ct);
            Assert.Equal(Vec3d.Zero, client.State.Entities.Get(RemoteId)!.Velocity);
        }
        else
        {
            Assert.Same(entity, client.State.Entities.Get(RemoteId));
            Assert.Equal(Advance(before, Target(protocol), 2), entity.Velocity);
        }
    }

    [Theory]
    [InlineData(764, false, false)]
    [InlineData(770, false, false)]
    [InlineData(773, false, false)]
    [InlineData(777, false, false)]
    [InlineData(764, true, false)]
    [InlineData(770, true, false)]
    [InlineData(773, true, false)]
    [InlineData(777, true, false)]
    [InlineData(764, true, true)]
    [InlineData(770, true, true)]
    [InlineData(773, true, true)]
    [InlineData(777, true, true)]
    public async Task OnlyTheSessionTickAdvancesRemoteVelocity(int protocol, bool terrain, bool physics)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain, physics);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d target = Target(protocol);
        Assert.Equal(Vec3d.Zero, entity.Velocity);
        await client.InvokeAsync(_ => true, ct);
        Assert.Equal(Vec3d.Zero, entity.Velocity);

        await ticks.FireAsync(ct);
        Vec3d first = new(target.X / 3, target.Y / 3, target.Z / 3);
        Assert.Equal(first, entity.Velocity);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        Assert.Equal(first, entity.Velocity);
        await ticks.FireAsync(ct);
        Assert.Equal(Advance(first, target, 3), entity.Velocity);

        // A direct setDeltaMovement changes current velocity without discarding the pending native lerpMotion target.
        Vec3d current = new(0.875, 0.5, 0.75);
        await client.InvokeAsync(_ => entity.Velocity = current, ct);
        await ticks.FireAsync(ct);
        Assert.Equal(Advance(current, target, 2), entity.Velocity);
        await ticks.FireAsync(ct);
        Assert.Equal(target, entity.Velocity);
        await ticks.FireAsync(ct);
        Assert.Equal(target, entity.Velocity);

        // Replacing an id creates a new native controller; the old pending target cannot attach to the replacement.
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        Entity replacement = client.State.Entities.Get(RemoteId)!;
        Assert.NotSame(entity, replacement);
        await ticks.FireAsync(ct);
        Assert.Equal(Vec3d.Zero, replacement.Velocity);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        await SendAndObserveAsync(client, server, version, new ClientboundRemoveEntitiesPacket([RemoteId]), ct);
        await ticks.FireAsync(ct);
        Assert.Null(client.State.Entities.Get(RemoteId));
        Assert.Equal(Vec3d.Zero, replacement.Velocity);
    }

    [Theory]
    [InlineData(764)]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(777)]
    public async Task RidingResetsCurrentVelocityBeforeEachRemotePlayerStep(int protocol)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain: false, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        await client.InvokeAsync(current =>
        {
            Assert.True(current.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("cow"), out RegistryEntry<EntityTypeDefinition> cow));
            current.State.Entities.Add(new Entity(43, Guid.NewGuid(), cow));
            return true;
        }, ct);
        await SendAndObserveAsync(client, server, version, new ClientboundSetPassengersPacket(43, [RemoteId]), ct);
        await SendAndObserveAsync(client, server, version, Motion(protocol), ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        Vec3d target = Target(protocol);
        foreach (int remaining in new[] { 3, 2, 1 })
        {
            await ticks.FireAsync(ct);
            Assert.Equal(Advance(Vec3d.Zero, target, remaining), entity.Velocity);
        }
        await ticks.FireAsync(ct);
        Assert.Equal(Vec3d.Zero, entity.Velocity);
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(760, true)]
    [InlineData(761, false)]
    [InlineData(761, true)]
    [InlineData(770, false)]
    [InlineData(770, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public async Task RemotePlayerTickResolvesInfoBeforeMotionEvenWithoutPhysics(int protocol, bool firstSpectator)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, terrain: false, physics: false);
        await JoinAsync(client, server, version, ct);
        await SendAndObserveAsync(client, server, version, (IPacket)Spawn(Create(protocol), protocol), ct);
        Entity entity = client.State.Entities.Get(RemoteId)!;
        await SendAndObserveAsync(client, server, version, Add(firstSpectator), ct);
        await ticks.FireAsync(ct);
        IPacket remove = protocol < 761
            ? new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.RemovePlayer, [new(entity.Uuid, null, null, 0, 0, null)])
            : new ClientboundPlayerInfoRemovePacket([entity.Uuid]);
        await SendAndObserveAsync(client, server, version, remove, ct);
        await SendAndObserveAsync(client, server, version, Add(!firstSpectator), ct);
        bool spectator = await client.InvokeAsync(current => current.State.EntityMotion.IsSpectator(entity, current.State.TabList), ct);
        Assert.Equal(protocol >= 761 ? firstSpectator : !firstSpectator, spectator);

        IPacket Add(bool spectator)
        {
            GameMode mode = spectator ? GameMode.Spectator : GameMode.Survival;
            return protocol < 761
                ? new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [new(entity.Uuid, "TickPeer", [], (int)mode, 0, null)])
                : new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode, [new(entity.Uuid, "TickPeer", [], false, null, mode, false, 0, null, 0, true)]);
        }
    }

    private static IPacket NativeMembershipChunk(int protocol, string action)
    {
        using Stream resource = typeof(RemotePlayerVelocityRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerTraversal.native.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement scenario = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 5);
        JsonElement packet = scenario.GetProperty("packets").EnumerateArray().Single(row => row.GetProperty("action").GetString() == action);
        byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
        return (IPacket)BoundDescriptorCodec.Clientbound(protocol, action == "unload" && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light")
            .Decode(body, new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty));
    }

    private static Vec3d Advance(Vec3d current, Vec3d target, int remaining)
        => new(current.X + (target.X - current.X) / remaining, current.Y + (target.Y - current.Y) / remaining, current.Z + (target.Z - current.Z) / remaining);

    private static UmpkClient CreateClient(FakeJavaServer server, ITickSource ticks, JavaVersion version, bool terrain, bool physics)
        => new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "MotionTester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(features =>
            {
                features.Terrain = terrain;
                features.Physics = physics;
                features.Pathfinding = false;
            })
            .Build();

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, JavaVersion version, CancellationToken ct)
    {
        await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), ct), ScriptedServer.DriveToPlayAsync(server, version, ct));
        await JoinWorldAsync(client, server, version, ct);
    }

    private static async Task JoinWorldAsync(UmpkClient client, FakeJavaServer server, JavaVersion version, CancellationToken ct)
    {
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await SendAndObserveAsync(client, server, version,
            new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, new LegacyLoginFields(0, 2, "flat")), ct);
    }

    private static async Task SendAndObserveAsync(UmpkClient client, FakeJavaServer server, JavaVersion version, IPacket packet, CancellationToken ct)
    {
        string marker = Guid.NewGuid().ToString();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.Events.Subscribe<ChatMessageReceived>(message =>
        {
            if (message.Message.ToPlainText() == marker)
                applied.TrySetResult();
        });
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play, packet, ct);
        bool systemChat = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).Packets.Any(entry => entry.Type.Id == Identifier.Minecraft("system_chat"));
        IPacket barrier = systemChat
            ? new ClientboundSystemChatPacket(Component.Text(marker), false)
            : new ClientboundLegacyChatPacket(Component.Text(marker), 1, Guid.Empty);
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play, barrier, ct);
        await applied.Task.WaitAsync(ct);
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public async Task FireAsync(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }

        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }

    private static ApplierHarness Create(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(protocol);
        return harness;
    }

    private static object Spawn(ApplierHarness harness, int protocol, bool velocity = false)
    {
        if (protocol < 764)
            return BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(RemoteId, Guid.NewGuid(), 8.5, 65, -3.5, 0, 0, 0, new EntityMetadataList([], [])));
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("player"), out RegistryEntry<EntityTypeDefinition> player));
        var spawn = new ClientboundAddEntityPacket(RemoteId, Guid.NewGuid(), player.NetworkId, 8.5, 65, -3.5, 0, 0, 0, 0,
            velocity ? (short)2400 : (short)0, velocity ? (short)-1600 : (short)0, velocity ? (short)5600 : (short)0,
            protocol >= 773 && velocity ? Convert.FromHexString("31337fff5c59") : null);
        return BoundDescriptorCodec.RoundTrip(protocol, "add_entity", spawn);
    }

    private static ClientboundSetEntityMotionPacket Motion(int protocol)
        => new(RemoteId, 2400, -1600, 5600, protocol >= 773 ? Convert.FromHexString("31337fff5c59") : null);

    private static Vec3d Target(int protocol) => protocol >= 773 ? QuantizedVelocity : ShortVelocity;
}
