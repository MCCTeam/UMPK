using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The precondition for ticking the local player: the chunk column it stands in must have arrived. Driven through a real <see cref="UmpkClient"/> over a real <see cref="JavaConnection"/> against a scripted server, with the tick advanced by hand so a tick lands exactly where it is wanted.</summary>
/// <remarks>
/// <para>In the unloaded window, the client must run zero physics ticks and send zero movement. Otherwise a player arriving at <c>(100, 50, 0)</c> can fall at -0.098 per tick before the first destination chunk arrives and report those incorrect positions on <c>move_player_pos_rot</c>.</para>
/// <para>What the existing suite could not see: every physics test drives <c>PhysicsEngineHolder.TickIdle</c> directly and builds its floor with <c>SetBlockStateId</c>, which creates the column as a side effect, so the gate is always open there. The gate lives on the client's own tick loop, so only a test that drives the loop can observe it, and only a test that asserts the ARRIVAL POSITION rather than the dimension can see the fall.</para>
/// </remarks>
public sealed class UnloadedChunkTickGateTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>A tick taken before the player's own column has arrived must move nothing and send nothing, and the column arriving must un-gate both again. The second half is the control: a gate that never opens would pass the first assertion and break the client.</summary>
    [Fact]
    public async Task NoTickAndNoMovementUntilThePlayersOwnColumnHasArrived()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<int>();

        await JoinAsync(client, server, frames, ct);
        await TeleportAsync(client, server, frames, new Vec3d(8.5, 200.0, 8.5), ct);

        int movePosWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPos);
        int movePosRotWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPosRot);
        frames.Clear();
        for (int i = 0; i < 40; i++)
            await ticks.FireAsync(ct);

        await DrainAsync(ct);

        Assert.Equal(200.0, client.State.Self.Position.Y, 9);
        Assert.Equal(8.5, client.State.Self.Position.X, 9);
        Assert.DoesNotContain(movePosWire, frames);
        Assert.DoesNotContain(movePosRotWire, frames);

        // The control. The column arrives, so the very same ticks now run: the player falls (the column is empty, which is exactly a world with no floor) and the movement goes out again.
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);
        for (int i = 0; i < 40; i++)
            await ticks.FireAsync(ct);

        await WaitForAsync(() => client.State.Self.Position.Y < 199.0, ct);
        await WaitForAsync(() => frames.Contains(movePosWire) || frames.Contains(movePosRotWire), ct);
    }

    /// <summary>The register's own shape, asserted on the ARRIVAL POSITION rather than on the dimension: a dimension change lands the client on the destination's spawn coordinate, and 60 ticks with no terrain yet must leave it exactly there.</summary>
    /// <remarks>Without the gate this drifts: 60 ticks of unopposed gravity from y 50 reach roughly y 6 with the 1.8 profile and pass straight through the bottom of the world with a modern one, and the client sends every step of it. <c>world.end_enter</c> reads only the server's <c>Dimension</c> and is blind to all of it.</remarks>
    [Fact]
    public async Task ADimensionChangeKeepsItsArrivalPositionWhileTheNewWorldIsStillEmpty()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<int>();

        await JoinAsync(client, server, frames, ct);

        // Stand on real ground first, so the gate is demonstrably OPEN before the dimension change and the assertion below cannot pass merely because it was never open.
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);
        await TeleportAsync(client, server, frames, new Vec3d(8.5, 200.0, 8.5), ct);
        Assert.True(
            await client.InvokeAsync(c => c.State.IsLocalChunkLoaded, ct),
            "the column the player stands in was not loaded, so the control half proves nothing");
        for (int i = 0; i < 5; i++)
            await ticks.FireAsync(ct);

        await WaitForAsync(() => client.State.Self.Position.Y < 200.0, ct);

        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Respawned>(_ => arrived.TrySetResult());
        await SendAsync(server, new ClientboundRespawnPacket(
            new CommonPlayerSpawnInfo(
                DimensionTypeId: 0, Dimension: "minecraft:the_end", Seed: 0, GameType: 0, PreviousGameType: 0,
                IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63),
            DataToKeep: 0,
            Legacy: null), ct);
        await arrived.Task.WaitAsync(Budget, ct);

        // The modern End generator keeps the same platform at BlockPos(100, 50, 0).
        var endSpawn = new Vec3d(100.0, 50.0, 0.0);
        await TeleportAsync(client, server, frames, endSpawn, ct);

        int movePosWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPos);
        int movePosRotWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPosRot);
        frames.Clear();
        for (int i = 0; i < 60; i++)
            await ticks.FireAsync(ct);

        await DrainAsync(ct);

        Assert.Equal(endSpawn.X, client.State.Self.Position.X, 9);
        Assert.Equal(endSpawn.Y, client.State.Self.Position.Y, 9);
        Assert.Equal(endSpawn.Z, client.State.Self.Position.Z, 9);
        Assert.DoesNotContain(movePosWire, frames);
        Assert.DoesNotContain(movePosRotWire, frames);
    }

    private static int ServerboundWire(PacketType type) => new Internal.WireIndex(Version).ServerboundPlay(type.Id);

    /// <summary>Lets every frame the client has already written reach the collector before asserting.</summary>
    private static Task DrainAsync(CancellationToken ct) => Task.Delay(200, ct);

    private static async Task JoinAsync(
        UmpkClient client, FakeJavaServer server, ConcurrentQueue<int> frames, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        // Everything the client writes from here is recorded by wire id, so an assertion can say which packet did or did not go out without decoding it.
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    InboundFrame frame = await server.NextFrameAsync(ct).ConfigureAwait(false);
                    frames.Enqueue(frame.WireId);
                }
            }
            catch (Exception)
            {
                // The pipe closing at the end of the test is the expected way out.
            }
        }, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);
        _ = descriptor;
    }

    private static async Task TeleportAsync(
        UmpkClient client,
        FakeJavaServer server,
        ConcurrentQueue<int> frames,
        Vec3d target,
        CancellationToken ct)
    {
        int confirmWire = ServerboundWire(EntityPackets.Serverbound.AcceptTeleportation);
        int moveWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPosRot);
        int confirmsBefore = frames.Count(wire => wire == confirmWire);
        int movesBefore = frames.Count(wire => wire == moveWire);

        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        await SendAsync(server, ProtocolPhase.Play, new ClientboundPlayerPositionPacket(
            target.X, target.Y, target.Z, 0f, 0f, RelativeFlags: 0, TeleportId: 1,
            ModernValues: new PositionMoveRotation(target, Vec3d.Zero, 0f, 0f)), ct);
        await corrected.Task.WaitAsync(Budget, ct);

        // Applying a teleport intentionally writes both replies before PositionCorrected is published. The collector runs independently, so wait until it has observed those causal frames before a caller clears the queue to begin its no-background-movement measurement.
        await WaitForAsync(
            () => frames.Count(wire => wire == confirmWire) > confirmsBefore
                && frames.Count(wire => wire == moveWire) > movesBefore,
            ct);
        Assert.Equal(confirmsBefore + 1, frames.Count(wire => wire == confirmWire));
        Assert.Equal(movesBefore + 1, frames.Count(wire => wire == moveWire));
    }

    private static UmpkClient Client(FakeJavaServer server, ITickSource ticks) =>
        new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Physics = true;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static Task SendAsync<TPacket>(FakeJavaServer server, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
        => SendAsync(server, ProtocolPhase.Play, packet, ct);

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    /// <summary>A tick source the test advances by hand, so a tick lands exactly where it is wanted.</summary>
    private sealed class ManualTickSource : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();
        private long _next;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);

        public ValueTask FireAsync(CancellationToken ct) =>
            _ticks.Writer.WriteAsync(Interlocked.Increment(ref _next), ct);
    }
}
