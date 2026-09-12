using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Movement;
using Umpk.Client.Plugins;
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

/// <summary><see cref="Navigator.MoveToAsync"/> and <see cref="Navigator.NavigateAsync"/> hold the exclusive movement lease (<see cref="MovementLeaseManager"/>) for their whole run. Position reporting must continue while that lease is held; otherwise the server sees the entire walk as one displacement at arrival. A server rejects a single displacement beyond 100 blocks squared from the last reported position, so a navigation must send intermediate positions on longer walks.</summary>
public sealed class PositionReportUnderLeaseTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>While a lease is held, a tick that moves the tracked position must still report it.</summary>
    [Fact]
    public async Task PositionIsReported_OnEveryTickAWalkMoves_WhileAMovementLeaseIsHeld()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new LeaseHoldingPlugin();
        await using UmpkClient client = Client(server, ticks, plugin);
        var frames = new ConcurrentQueue<int>();

        await JoinAsync(client, server, frames, ct);
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);
        await TeleportAsync(client, server, frames, new Vec3d(8.5, 200.0, 8.5), ct);

        IMovementLease lease = plugin.Context!.TryAcquireMovement("test")
            ?? throw new InvalidOperationException("The movement lease was not free.");

        int movePosWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPos);
        int movePosRotWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPosRot);
        frames.Clear();
        for (int i = 0; i < 10; i++)
        {
            await client.PostAsync(c => c.State.Self.Position = c.State.Self.Position.Add(0.2, 0.0, 0.0), ct);
            await ticks.FireAsync(ct);

            // ManualTickSource.FireAsync only enqueues the tick; it does not wait for TickLoopAsync to dequeue it and dispatch OnTickOnLoopAsync onto the session loop. Without a beat here every nudge in this loop lands before the tick loop's background task gets scheduled even once, so all 10 nudges land, THEN all 10 ticks fire back to back against the one final position: the first observes a real move (from wherever it was primed) and the remaining nine compare an unchanged position to itself and correctly report nothing. This settle keeps each tick paired with the position as of ITS OWN nudge, the way a real 20 TPS loop would see it.
            await DrainAsync(ct);
        }

        lease.Dispose();

        // The very first tick under a fresh lease only establishes the baseline and does not send (see UmpkClient.ShouldReportPosition); every tick after that reports because the position moved.
        Assert.True(
            frames.Count(w => w == movePosWire || w == movePosRotWire) >= 9,
            $"expected the walk to be reported on (nearly) every tick while the lease was held, saw {frames.Count(w => w == movePosWire || w == movePosRotWire)} of 10 ticks");
    }

    /// <summary>The control that prevents a naive unconditional send: a lease held with no movement emits only vanilla's 20-tick position reminder, not one position on every tick.</summary>
    [Fact]
    public async Task OnlyTheIdleReminderIsReported_WhileALeaseIsHeld_AndThePlayerHasNotMoved()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new LeaseHoldingPlugin();
        await using UmpkClient client = Client(server, ticks, plugin);
        var frames = new ConcurrentQueue<int>();

        await JoinAsync(client, server, frames, ct);
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);
        await TeleportAsync(client, server, frames, new Vec3d(8.5, 200.0, 8.5), ct);

        IMovementLease lease = plugin.Context!.TryAcquireMovement("test")
            ?? throw new InvalidOperationException("The movement lease was not free.");

        int movePosWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPos);
        int movePosRotWire = ServerboundWire(EntityPackets.Serverbound.MovePlayerPosRot);
        frames.Clear();
        for (int i = 0; i < 20; i++)
            await ticks.FireAsync(ct);

        await DrainAsync(ct);
        lease.Dispose();

        Assert.Equal(1, frames.Count(w => w == movePosWire || w == movePosRotWire));
    }

    [Fact]
    public void PositionReportPolicy_StaysSilentBelowVanillasThreshold()
    {
        var resting = new Vec3d(12.5, 64.0, -7.5);
        Assert.False(PositionReportPolicy.HasMoved(resting, resting));

        // Vanilla's own significance threshold is 4.0E-8 squared, so sub-micrometre physics jitter is not a movement and must not put a packet on the wire every tick.
        Assert.False(PositionReportPolicy.HasMoved(resting, resting.Add(1e-6, 0, 0)));
    }

    [Fact]
    public void PositionReportPolicy_ReportsAWalkingStep()
    {
        var from = new Vec3d(12.5, 64.0, -7.5);

        Assert.True(PositionReportPolicy.HasMoved(from, from.Add(0.02, 0, 0)));
        Assert.True(PositionReportPolicy.HasMoved(from, from.Add(0, -0.0784, 0)));
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

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static UmpkClient Client(FakeJavaServer server, ITickSource ticks, IClientPlugin plugin) =>
        new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .UseTickSource(ticks)
            .AddPlugin(plugin)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Physics = true;
                f.Pathfinding = false;
            })
            .Build();

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

    /// <summary>Attaches and holds onto its context so the test can acquire the movement lease on demand.</summary>
    private sealed class LeaseHoldingPlugin : IClientPlugin
    {
        public string Id => "test.lease-holder";

        public ClientPluginContext? Context { get; private set; }

        public void Attach(ClientPluginContext context) => Context = context;
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
