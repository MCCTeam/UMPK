using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
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
