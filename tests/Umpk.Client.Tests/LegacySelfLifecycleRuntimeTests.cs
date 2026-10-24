using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacySelfLifecycleRuntimeTests
{
    public static IEnumerable<object[]> LegacyCases()
    {
        foreach (int protocol in new[] { 47, 107, 315, 404 })
            foreach (bool entities in new[] { false, true })
                yield return [protocol, entities];
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task OriginalUseBedBodyEyeAndVelocityReachTheRealSelfOwner(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement row = release.GetProperty("row");
        JsonElement sleep = row.GetProperty("transitions").EnumerateArray().Single(state => state.GetProperty("operation").GetString() == "sleep");
        byte[] bytes = Convert.FromHexString(row.GetProperty("packets")[0].GetProperty("body").GetString()!);
        ClientboundUseBedPacket bed = Assert.IsType<ClientboundUseBedPacket>(BoundDescriptorCodec.Clientbound(protocol, "use_bed").Decode(bytes, PacketCodecContext.Registryless));
        Assert.Equal(new BlockPos(1, 63, 1), bed.Position);
        await using var session = await Session.Create(protocol, entities, bed.PlayerId);
        await session.Column();
        await session.Send(new ClientboundBlockUpdatePacket(bed.Position, release.GetProperty("canonical_bed_state").GetInt32()));
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        await session.Send(new ClientboundSetEntityMotionPacket(bed.PlayerId, 1000, 2000, -3000, null));
        await session.Raw("use_bed", bytes);
        Assert.Equal(Point(sleep.GetProperty("state").GetProperty("position")), (await session.Client.Snapshots.SelfAsync(session.Token)).Position);
        Assert.Equal(sleep.GetProperty("eye_height").GetDouble(), await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        Assert.Equal(Point(sleep.GetProperty("velocity")), (await session.Client.Snapshots.SelfAsync(session.Token)).Velocity);
        Assert.Equal(Box(sleep.GetProperty("state").GetProperty("box")), (await session.Physics()).BoundingBox);
        Assert.Null(session.Client.State.EntitiesOrNull?.Get(bed.PlayerId));
        Assert.Equal(0, session.TickCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adjacent477HasNoLegacyUseBedAndKeepsTheDefaultSelfPath(bool entities)
    {
        await using var session = await Session.Create(477, entities, 1);
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        Assert.DoesNotContain(session.Version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).Packets, pair => pair.Type.Id == Identifier.Minecraft("use_bed"));
        Assert.Equal(new Vec3d(1.5, 63.3125, 1.5), (await session.Client.Snapshots.SelfAsync(session.Token)).Position);
        Assert.Equal((double)1.62F, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        Assert.Null(session.Client.State.EntitiesOrNull?.Get(1));
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task RemovedBedWakeKeepsTheNativeCacheUntilExplicitPositionReapplication(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        await using Session session = await Sleeper(release, entities);
        JsonElement row = release.GetProperty("row");
        await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(1, 63, 1), 0));
        JsonElement wake = row.GetProperty("packets").EnumerateArray().Single(packet => packet.GetProperty("kind").GetString() == "wake");
        await session.Raw("animate", Convert.FromHexString(wake.GetProperty("body").GetString()!));
        JsonElement expected = row.GetProperty("transitions").EnumerateArray().Single(state => state.GetProperty("operation").GetString() == "wake_after_bed_removal");
        await AssertNative(session, expected);
        await session.Correct(Point(expected.GetProperty("state").GetProperty("position")));
        await AssertNative(session, row.GetProperty("transitions").EnumerateArray().Single(state => state.GetProperty("operation").GetString() == "explicit_position"));
    }

    public static IEnumerable<object[]> HealthCases()
    {
        foreach (object[] row in LegacyCases())
            foreach (string kind in new[] { "metadata", "health_packet" })
                yield return [row[0], row[1], kind];
    }

    [Theory]
    [MemberData(nameof(HealthCases))]
    public async Task ZeroHealthAndNativeStatusThreeHaveDifferentBodyAndVelocityEffects(int protocol, bool entities, string kind)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        JsonElement row = release.GetProperty("health_rows").EnumerateArray().Single(value => value.GetProperty("kind").GetString() == kind);
        JsonElement status = row.GetProperty("packets").EnumerateArray().Single(value => value.GetProperty("kind").GetString() == "status");
        byte[] statusBody = Convert.FromHexString(status.GetProperty("body").GetString()!);
        var death = Assert.IsType<ClientboundEntityEventPacket>(BoundDescriptorCodec.Clientbound(protocol, "entity_event").Decode(statusBody, PacketCodecContext.Registryless));
        Assert.Equal(3, death.EventId);
        await using Session session = await Session.Create(protocol, entities, death.EntityId);
        await session.Column();
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        await session.Send(new ClientboundSetEntityMotionPacket(death.EntityId, 1000, 2000, -3000));
        foreach (JsonElement packet in row.GetProperty("packets").EnumerateArray().Where(value => value.GetProperty("kind").GetString() != "status"))
            await session.Raw(kind == "metadata" ? "set_entity_data" : "set_health", Convert.FromHexString(packet.GetProperty("body").GetString()!));
        JsonElement zero = row.GetProperty("steps").EnumerateArray().Single(value => value.GetProperty("operation").GetString() == (kind == "metadata" ? "metadata_zero" : "health_zero"));
        await AssertNative(session, zero);
        Assert.Equal(0F, session.Client.State.Self.Health);
        bool eventObserved = false;
        using IDisposable subscription = session.Client.Events.Subscribe<EntityStatusChanged>(value =>
        {
            if (value.EntityId != death.EntityId || value.EventId != 3)
                return;
            eventObserved = true;
            Assert.Equal(0F, session.Client.State.Self.Health);
            Assert.Equal(new Vec3d(-.1F, .1F, -0D), session.Client.State.Self.Velocity);
        });
        await session.Raw("entity_event", statusBody);
        await AssertNative(session, row.GetProperty("steps").EnumerateArray().Single(value => value.GetProperty("operation").GetString() == "status3"));
        Assert.True(eventObserved);
        Assert.True((await session.Physics()).EyeHeight > (await session.Physics()).BoundingBox.YSize);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(session.Client.State.Self.Velocity.Z));
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task SleepingExternalVelocityAdvancesOnlyThroughTheExistingSessionClock(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        await using Session session = await Sleeper(release, entities);
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
                if (x != 0 || z != 0)
                    await session.Column(x, z);
        await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(1, 63, 1), 0));
        await session.Send(new ClientboundSetEntityMotionPacket(session.Client.State.Self.EntityId, 1000, 2000, -3000));
        Vec3d before = (await session.Client.Snapshots.SelfAsync(session.Token)).Position;
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Equal(before, session.Client.State.Self.Position);
        JsonElement[] expected = release.GetProperty("living_row").GetProperty("snapshots").EnumerateArray().Where(value => value.GetProperty("operation").GetString()!.StartsWith("original_inherited_living_phase_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, expected.Length);
        for (int tick = 0; tick < expected.Length; tick++)
        {
            await session.Tick();
            await AssertNative(session, expected[tick]);
            Assert.Equal(tick + 1, session.TickCount);
        }
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task SameWorldRespawnAndDisconnectDropThePreviousSleepingBody(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        await using Session session = await Sleeper(Release(fixture, protocol), entities);
        object previous = Assert.IsType<Umpk.Client.Internal.TrackedLegacySelfGeometry>(session.Client.State.LegacySelfGeometry);
        await session.Send(new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(0, 2, 0, "flat")));
        await session.Correct(new Vec3d(2.5, 65, 2.5));
        Assert.NotSame(previous, session.Client.State.LegacySelfGeometry);
        Assert.False(session.Client.State.LegacySelfGeometry!.Sleeping);
        Assert.Equal((double)1.62F, (await session.Physics()).EyeHeight);
        Assert.Equal((double)1.8F, (await session.Physics()).BoundingBox.YSize);
        await session.Close();
        Assert.Null(session.Client.State.LegacySelfGeometry);
        Assert.Null(session.UnscheduledPhysics());
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task PlacementUsesTheOwnedSleepingAndWakingBody(int protocol)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        await using Session session = await Sleeper(release, true);
        BlockPos clicked = new(1, 63, 1);
        var stone = session.Client.State.Registries!.Items[Identifier.Minecraft("stone")];
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new Umpk.Game.Items.ItemStack(stone, 32)));
        await session.Send(new ClientboundBlockUpdatePacket(clicked, session.Client.State.Registries.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId));
        List<object> sleeping = await session.Use(clicked);
        Assert.Equal(2, sleeping.Count);
        AssertUse(sleeping[0], protocol, clicked);
        var swing = Assert.IsType<ServerboundSwingPacket>(sleeping[1]);
        Assert.Equal(0, swing.Hand);
        Assert.Equal(protocol != 47, swing.HasHand);
        await session.Send(new ClientboundAnimatePacket(session.Client.State.Self.EntityId, 2));
        List<object> waking = await session.Use(clicked);
        if (protocol < 393)
            Assert.Empty(waking);
        else
            AssertUse(Assert.Single(waking), protocol, clicked);
        Assert.True((await session.Physics()).BoundingBox.Intersects(Aabb.BlockAt(1, 64, 1)));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task UnknownLocalCallbackCannotAdvertiseStandingGeometryOrAProvenBlockPick(int protocol)
    {
        await using Session session = await Session.Create(protocol, true, 1);
        await session.Column();
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        BlockPos target = new(1, 64, 3);
        await session.Send(new ClientboundBlockUpdatePacket(target, session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId));
        await session.Client.Actions.Movement.LookAtLocalAsync(new Vec3d(1.5, 64.5, 3.5), session.Token);
        Assert.Equal(Umpk.Client.Internal.InteractionPickKind.Block, await session.PickKind());
        double eye = await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token);
        Vec3d before = session.Client.State.Self.Position;
        // This deliberately absent bed cannot supply the native facing property. The existing lifecycle adapter must retain an explicit unknown rather than invent a body.
        await session.Send(new ClientboundUseBedPacket(1, new BlockPos(1, 63, 1)));
        Assert.False(session.Client.State.LegacySelfGeometry!.IsKnown);
        Assert.Null(session.UnscheduledPhysics());
        Assert.Equal(Umpk.Client.Internal.InteractionPickKind.Unknown, await session.PickKind());
        Assert.Equal(eye, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.DigBlockInViewAsync(target, ct: session.Token));
        await session.Tick();
        Assert.Equal(before, session.Client.State.Self.Position);
        Assert.Null(session.UnscheduledPhysics());
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task UnknownCurrentEyeRetainsServerAirInsteadOfInventingDryEyeRecovery(int protocol)
    {
        await using Session session = await Session.Create(protocol, false, 1);
        await session.Column();
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        Assert.True(JavaGameData.EntityMetadataKeys(protocol).TryResolveIndex(default, Umpk.Game.Entities.EntityMetadataKeys.AirSupply, out int airIndex));
        await session.Send(new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new(airIndex, Umpk.Game.Entities.MetadataValue.VarInt(100))], [])));
        Assert.Equal(100, session.Client.State.Self.AirSupply);
        await session.Send(new ClientboundUseBedPacket(1, new BlockPos(1, 63, 1)));
        Assert.False(session.Client.State.LegacySelfGeometry!.IsKnown);
        await session.Tick();
        Assert.Equal(100, session.Client.State.Self.AirSupply);
        Assert.Null(session.UnscheduledPhysics());
    }

    private static void AssertUse(object packet, int protocol, BlockPos position)
    {
        if (protocol == 47)
        {
            var legacy = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packet);
            Assert.Equal(position, legacy.Position);
            Assert.Equal(1, legacy.Face);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
            Assert.Equal(position, use.Position);
            Assert.Equal(1, use.Face);
            Assert.Equal(0, use.Hand);
            Assert.Equal(0, use.Sequence);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task SelfCallbacksKeepNativeEyeWhenPhysicsAndRemoteEntitiesAreDisabled(int protocol)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        byte[] bytes = Convert.FromHexString(release.GetProperty("row").GetProperty("packets")[0].GetProperty("body").GetString()!);
        var bed = Assert.IsType<ClientboundUseBedPacket>(BoundDescriptorCodec.Clientbound(protocol, "use_bed").Decode(bytes, PacketCodecContext.Registryless));
        await using Session session = await Session.Create(protocol, false, bed.PlayerId, physics: false);
        await session.Column();
        await session.Send(new ClientboundBlockUpdatePacket(bed.Position, release.GetProperty("canonical_bed_state").GetInt32()));
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        await session.Raw("use_bed", bytes);
        JsonElement expected = release.GetProperty("row").GetProperty("transitions").EnumerateArray().Single(value => value.GetProperty("operation").GetString() == "sleep");
        Assert.Equal(Point(expected.GetProperty("state").GetProperty("position")), session.Client.State.Self.Position);
        Assert.Equal(expected.GetProperty("eye_height").GetDouble(), await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        Assert.Null(session.Client.State.EntitiesOrNull);
        Assert.Equal(0, session.TickCount);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task UnknownWakeRetainsTheLastObservedEyeAndStopsTheBody(int protocol)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        await using Session session = await Sleeper(release, true);
        double observed = await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token);
        await session.UnloadColumn();
        await session.Send(new ClientboundAnimatePacket(session.Client.State.Self.EntityId, 2));
        Assert.False(session.Client.State.LegacySelfGeometry!.IsKnown);
        Assert.Null(session.UnscheduledPhysics());
        Assert.Equal(observed, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        Assert.Equal(Umpk.Client.Internal.InteractionPickKind.Unknown, await session.PickKind());
        Vec3d position = session.Client.State.Self.Position;
        await session.Tick();
        Assert.Equal(position, session.Client.State.Self.Position);
        Assert.Null(session.UnscheduledPhysics());
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task HeldLocalSneakIsSuppressedDuringSleepAndReturnsOnWake(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        JsonElement release = Release(fixture, protocol);
        await using Session session = await Sleeper(release, entities, "sneaking_row", true);
        JsonElement row = release.GetProperty("sneaking_row");
        await AssertNative(session, row.GetProperty("transitions").EnumerateArray().Single(value => value.GetProperty("operation").GetString() == "sleep"));
        Assert.True(session.Client.State.Self.Sneaking);
        Assert.False(session.Client.State.LegacySelfGeometry!.IsSneaking);
        await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(1, 63, 1), 0));
        await session.Raw("animate", Convert.FromHexString(row.GetProperty("packets").EnumerateArray().Single(value => value.GetProperty("kind").GetString() == "wake").GetProperty("body").GetString()!));
        await AssertNative(session, row.GetProperty("transitions").EnumerateArray().Single(value => value.GetProperty("operation").GetString() == "wake_after_bed_removal"));
        Assert.True(session.Client.State.LegacySelfGeometry.IsSneaking);
        await session.Tick();
        Assert.Equal((double)(protocol == 47 ? 1.8F : 1.65F), (await session.Physics()).BoundingBox.YSize);
        Assert.Equal((double)(1.62F - .08F), (await session.Physics()).EyeHeight);
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task PeerSharedCrouchFlagDoesNotReplaceTheLocalInputOwner(int protocol, bool entities)
    {
        using JsonDocument fixture = Native();
        JsonElement expected = Release(fixture, protocol).GetProperty("peer_crouch_row").GetProperty("transitions")[0];
        await using Session session = await Session.Create(protocol, entities, 1);
        await session.Column();
        await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
        await session.Send(new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new(0, Umpk.Game.Entities.MetadataValue.Byte(2))], [])));
        await session.Tick();
        Assert.False(session.Client.State.Self.Sneaking);
        Assert.False(session.Client.State.LegacySelfGeometry!.IsSneaking);
        Assert.Equal(Box(expected.GetProperty("state").GetProperty("box")), (await session.Physics()).BoundingBox);
        Assert.Equal(expected.GetProperty("eye_height").GetDouble(), (await session.Physics()).EyeHeight);
    }

    private static JsonElement Release(JsonDocument fixture, int protocol) => fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);

    private static async Task<Session> Sleeper(JsonElement release, bool entities, string rowName = "row", bool sneak = false)
    {
        int protocol = release.GetProperty("protocol").GetInt32();
        byte[] bytes = Convert.FromHexString(release.GetProperty(rowName).GetProperty("packets")[0].GetProperty("body").GetString()!);
        var packet = Assert.IsType<ClientboundUseBedPacket>(BoundDescriptorCodec.Clientbound(protocol, "use_bed").Decode(bytes, PacketCodecContext.Registryless));
        Session session = await Session.Create(protocol, entities, packet.PlayerId);
        try
        {
            await session.Column();
            await session.Send(new ClientboundBlockUpdatePacket(packet.Position, release.GetProperty("canonical_bed_state").GetInt32()));
            await session.Correct(new Vec3d(1.5, 63.3125, 1.5));
            if (sneak)
                await (await session.Client.InvokeAsync(client => client.Actions.Movement.SetSneakingAsync(true, session.Token), session.Token));
            await session.Raw("use_bed", bytes);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    private static async Task AssertNative(Session session, JsonElement expected)
    {
        var self = await session.Client.Snapshots.SelfAsync(session.Token);
        Assert.Equal(Point(expected.GetProperty("state").GetProperty("position")), self.Position);
        Assert.Equal(Point(expected.GetProperty("velocity")), self.Velocity);
        Assert.Equal(expected.GetProperty("eye_height").GetDouble(), self.EyeHeight);
        Assert.Equal(Box(expected.GetProperty("state").GetProperty("box")), (await session.Physics()).BoundingBox);
        Assert.Null(session.Client.State.EntitiesOrNull?.Get(self.EntityId));
    }

    private static Vec3d Point(JsonElement point) => new(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble());
    private static Aabb Box(JsonElement box) => new(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble());

    private static JsonDocument Native()
    {
        using Stream resource = typeof(LegacySelfLifecycleRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyLocalPlayerLifecycle.native.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTicks _ticks = new();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _nonce;
        private bool _closed;
        public UmpkClient Client { get; }
        public JavaVersion Version { get; }
        public CancellationToken Token => _budget.Token;
        public int TickCount => _ticks.TickCount;

        private Session(int protocol, bool entities, bool physics)
        {
            Version = BoundPacketApplierHarness.Version(protocol);
            Client = new UmpkClientBuilder().UseVersion(Version).UseProfile(new GameProfile(Guid.NewGuid(), "LocalLifecycle"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe)).UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(_ticks)
                .ConfigureFeatures(features => { features.Entities = entities; features.Terrain = true; features.Physics = physics; features.Pathfinding = false; })
                .Build();
        }

        public static async Task<Session> Create(int protocol, bool entities, int id, bool physics = true)
        {
            var session = new Session(protocol, entities, physics);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session.Version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable subscription = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                await session.Send(new ClientboundLoginPacket(id, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63), false, false, new LegacyLoginFields(0, 2, "flat")), false);
                await joined.Task.WaitAsync(session.Token);
                await session.Barrier();
                return session;
            }
            catch { await session.DisposeAsync(); throw; }
        }

        public Task Correct(Vec3d at) => Send(new ClientboundPlayerPositionPacket(at.X, at.Y, at.Z, 0, 0, 0, Version.Version.Protocol == 47 ? null : 1, null));
        public Task Tick() => _ticks.Fire(Token);
        public PhysicsState? UnscheduledPhysics() => Assert.IsType<PhysicsEngineHolder>(typeof(UmpkClient).GetField("_physics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)).EngineState;
        public Task<PhysicsState> Physics() => Client.InvokeAsync(client =>
        {
            var owner = Assert.IsType<PhysicsEngineHolder>(typeof(UmpkClient).GetField("_physics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
            return Assert.IsType<PhysicsState>(owner.EngineState);
        }, Token);

        public async Task Column(int columnX = 0, int columnZ = 0)
        {
            using JsonDocument fixture = LegacyPlayerMembershipRuntimeTests.ReadNative();
            JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == Version.Version.Protocol);
            JsonElement native = fixture.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
            JsonElement first = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
            byte[] bytes = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(first.GetProperty("sha256").GetString()!).GetString()!);
            // The recorded native full-column body is reused; only the two fixed i32 column coordinates are changed for the controlled empty neighbourhood.
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), columnX);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), columnZ);
            var decoded = Assert.IsType<ClientboundLevelChunkPacket>(BoundDescriptorCodec.Clientbound(Version.Version.Protocol, "level_chunk_with_light").Decode(bytes, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty)));
            Assert.True(decoded.FullChunk);
            Assert.Equal(columnX, decoded.ChunkX);
            Assert.Equal(columnZ, decoded.ChunkZ);
            await Raw("level_chunk_with_light", bytes);
        }

        public async Task UnloadColumn()
        {
            using JsonDocument fixture = LegacyPlayerMembershipRuntimeTests.ReadNative();
            JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == Version.Version.Protocol);
            JsonElement native = fixture.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
            JsonElement packet = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 5).GetProperty("packets").EnumerateArray().Single(packet => packet.GetProperty("action").GetString() == "unload");
            byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
            await Raw(Version.Version.Protocol == 47 ? "level_chunk_with_light" : "forget_level_chunk", body);
        }

        public Task Send(IPacket packet, bool barrier = true)
        {
            var registry = Version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            string identifier = packet is ClientboundContainerSetSlotPacket && Version.Version.Protocol == 47 ? "set_slot" : packet.Type.Id.Path;
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft(identifier));
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
            return Raw(identifier, buffer.WrittenSpan.ToArray(), barrier);
        }

        public async Task Raw(string name, byte[] bytes, bool barrier = true)
        {
            var registry = Version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft(name));
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            object decoded = codec.Decode(bytes, context);
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(bytes, output.WrittenSpan.ToArray());
            if (decoded is ClientboundUseBedPacket bed)
            {
                Assert.Equal(Client.State.Self.EntityId, bed.PlayerId);
                Assert.Equal(new BlockPos(1, 63, 1), bed.Position);
            }
            await _server.SendFrameAsync(pair.WireId, bytes, Token);
            if (barrier) await Barrier();
        }

        public Task<Umpk.Client.Internal.InteractionPickKind> PickKind() => Client.InvokeAsync(client =>
        {
            var services = Assert.IsType<Umpk.Client.Internal.ClientSessionServices>(typeof(UmpkClient).GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
            return services.ReadInteractionPick(4.5).Kind;
        }, Token);

        public async Task<List<object>> Use(BlockPos clicked)
        {
            await Client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.Up, new Vec3d(.5, 1, .5), ct: Token);
            return await Barrier();
        }

        private async Task<List<object>> Barrier()
        {
            List<object> observed = [];
            long nonce = ++_nonce;
            await Send(new ClientboundPlayKeepAlivePacket(nonce), false);
            var registry = Version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                if (!registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec))
                    continue;
                object decoded = codec.Decode(frame.Payload, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                codec.Encode(ref writer, decoded, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
                Assert.Equal(frame.Payload.ToArray(), output.WrittenSpan.ToArray());
                if (decoded is ServerboundUseItemOnPacket or ServerboundLegacyBlockPlacePacket or ServerboundSwingPacket)
                    observed.Add(decoded);
                if (decoded is ServerboundPlayKeepAlivePacket reply && reply.Id == nonce)
                    return observed;
            }
        }

        public async Task Close()
        {
            if (_closed)
                return;
            await Client.DisconnectAsync(Token);
            await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await _server.NextFrameAsync(Token); });
            _closed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Close(); }
            finally
            {
                try { await Client.DisposeAsync(); }
                finally { await _server.DisposeAsync(); _budget.Dispose(); }
            }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public int TickCount { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        public async Task Fire(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++TickCount;
                completed.TrySetResult();
            }
        }
    }
}
