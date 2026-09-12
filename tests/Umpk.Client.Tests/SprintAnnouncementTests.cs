using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The client has to TELL the server it is sprinting. An edge trigger on the entity's sprint flag sends a START_SPRINTING / STOP_SPRINTING <c>player_command</c> on each transition and nothing in between.
///
/// <para>It is not cosmetic. The server installs its own <c>minecraft:sprinting</c> movement-speed modifier when START_SPRINTING arrives, so a client that runs at 0.28062 blocks a tick without announcing is a client whose every position report is 30% beyond what the server's move-distance check budgets for a walker. The engine has honoured the sprint bit since the sprint package; this is the other half.</para>
///
/// <para>The sprint is produced by a REAL navigation over a real floor, so what is under test is the whole chain the client actually uses: the planner emits Sprint, <c>PhysicsEngineHolder.TickNavigation</c> steps the engine with it, <c>PhysicsState.IsSprinting</c> carries it back, and the tick loop announces the edge. Frames are grouped into ticks by <c>client_tick_end</c>, which vanilla's client sends last in every tick (1.21.2+), so intra-tick ORDER is observable rather than assumed.</para>
/// </summary>
public sealed class SprintAnnouncementTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private const int FloorY = 63;
    private const int StandY = 64;

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>Vanilla's action ordinals on 1.21.6+ (no shift entries); see PlayerCommandActionCompatibilityTests.</summary>
    private const int StartSprinting = 1;
    private const int StopSprinting = 2;

    /// <summary>One packet per transition, and the transitions alternate. A per-tick send rather than an edge trigger would put a START_SPRINTING on the wire on every one of the ~50 sprinting ticks of this run; the alternation assertion is what says it did not.</summary>
    [Fact]
    public async Task SprintIsAnnouncedOncePerTransition_AndTheTransitionsAlternate()
    {
        using var cts = new CancellationTokenSource(Budget);
        await using Harness harness = await Harness.StartAsync(autoSendPosition: true, cts.Token);

        await harness.NavigateAsync(new Vec3d(14.5, StandY, 0.5), cts.Token);
        await harness.IdleTicksAsync(6, cts.Token);

        IReadOnlyList<int> actions = harness.SprintActions();
        Assert.NotEmpty(actions);
        Assert.Equal(StartSprinting, actions[0]);
        for (int i = 1; i < actions.Count; i++)
            Assert.True(
                actions[i] != actions[i - 1],
                $"the sprint announcement repeated action {actions[i]} at index {i}: [{string.Join(", ", actions)}]");

        // And the run does not leave the server believing the bot is still sprinting after it stopped.
        Assert.Equal(StopSprinting, actions[^1]);
    }

    /// <summary>The announcement is a SIBLING of the position send, not a passenger on it. With <c>AutoSendPosition</c> off not one <c>move_player</c> leaves the client, and the sprint announcement still does. Placing the edge trigger inside the position-send branch, which is where vanilla's own call sits lexically, would make this test silent.</summary>
    [Fact]
    public async Task SprintIsAnnounced_EvenWhenNoPositionPacketIsSentAtAll()
    {
        using var cts = new CancellationTokenSource(Budget);
        await using Harness harness = await Harness.StartAsync(autoSendPosition: false, cts.Token);

        await harness.NavigateAsync(new Vec3d(14.5, StandY, 0.5), cts.Token);
        await harness.IdleTicksAsync(6, cts.Token);

        Assert.Empty(harness.FramesOfType(EntityPackets.Serverbound.MovePlayerPosRot));
        Assert.Empty(harness.FramesOfType(EntityPackets.Serverbound.MovePlayerPos));
        Assert.Contains(StartSprinting, harness.SprintActions());
    }

    /// <summary>Intra-tick order: within the tick that carries both, the sprint announcement precedes the movement packet. Vanilla sends them in that order and the server reads them in arrival order, so a position report that arrives before the modifier is installed is the one report the move-distance check sees as a walker moving at a sprinter's speed.</summary>
    [Fact]
    public async Task SprintIsAnnouncedBeforeTheMovementPacketInTheSameTick()
    {
        using var cts = new CancellationTokenSource(Budget);
        await using Harness harness = await Harness.StartAsync(autoSendPosition: true, cts.Token);

        await harness.NavigateAsync(new Vec3d(14.5, StandY, 0.5), cts.Token);
        await harness.IdleTicksAsync(6, cts.Token);

        int playerCommand = harness.Wire(EntityPackets.Serverbound.PlayerCommand);
        int movePos = harness.Wire(EntityPackets.Serverbound.MovePlayerPos);
        int movePosRot = harness.Wire(EntityPackets.Serverbound.MovePlayerPosRot);

        int compared = 0;
        foreach (IReadOnlyList<RecordedFrame> tick in harness.Ticks())
        {
            int sprintAt = IndexOf(tick, playerCommand);
            int moveAt = Math.Min(IndexOf(tick, movePos), IndexOf(tick, movePosRot));
            if (sprintAt == NotPresent || moveAt == NotPresent)
                continue;

            compared++;
            Assert.True(
                sprintAt < moveAt,
                $"the sprint announcement was frame {sprintAt} of its tick and the movement packet was {moveAt}");
        }

        Assert.True(compared > 0, "no single tick carried both a sprint announcement and a movement packet");
    }

    /// <summary>Sentinel for "this tick carried no such frame", chosen so Math.Min over two forms works.</summary>
    private const int NotPresent = int.MaxValue;

    private static int IndexOf(IReadOnlyList<RecordedFrame> frames, int wireId)
    {
        for (int i = 0; i < frames.Count; i++)
            if (frames[i].WireId == wireId)
                return i;

        return NotPresent;
    }

    private readonly record struct RecordedFrame(int WireId, byte[] Payload);

    /// <summary>A joined client on a stone floor, with a hand-advanced tick source, recording every serverbound frame it writes in order.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly FakeJavaServer _server;
        private readonly UmpkClient _client;
        private readonly ManualTickSource _ticks;
        private readonly ConcurrentQueue<RecordedFrame> _frames = new();
        private readonly WireIndex _wire = new(Version);

        private Harness(FakeJavaServer server, UmpkClient client, ManualTickSource ticks)
        {
            _server = server;
            _client = client;
            _ticks = ticks;
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _server.DisposeAsync();
        }

        public int Wire(PacketType type) => _wire.ServerboundPlay(type.Id);

        public IEnumerable<RecordedFrame> FramesOfType(PacketType type)
        {
            int wire = Wire(type);
            return _frames.Where(f => f.WireId == wire);
        }

        /// <summary>The recorded stream cut into ticks. Vanilla's client closes every tick with <c>client_tick_end</c> (1.21.2+) and this client sends it last in <c>OnTickOnLoopAsync</c>, so the frames between two markers are exactly one tick's output, in the order it produced them.</summary>
        public IReadOnlyList<IReadOnlyList<RecordedFrame>> Ticks()
        {
            int end = Wire(PlayPackets.Serverbound.ClientTickEnd);
            var ticks = new List<IReadOnlyList<RecordedFrame>>();
            var current = new List<RecordedFrame>();
            foreach (RecordedFrame frame in _frames)
            {
                if (frame.WireId == end)
                {
                    ticks.Add(current);
                    current = [];
                    continue;
                }

                current.Add(frame);
            }

            return ticks;
        }

        /// <summary>The action ordinal of every <c>player_command</c> frame that carries a sprint action.</summary>
        public IReadOnlyList<int> SprintActions()
        {
            var actions = new List<int>();
            foreach (RecordedFrame frame in FramesOfType(EntityPackets.Serverbound.PlayerCommand))
            {
                var reader = new PacketReader(frame.Payload);
                _ = reader.ReadVarInt(); // entity id
                int action = reader.ReadVarInt();
                if (action is StartSprinting or StopSprinting)
                    actions.Add(action);

            }

            return actions;
        }

        /// <summary>Runs a navigation while the tick loop is being advanced underneath it.</summary>
        public async Task NavigateAsync(Vec3d target, CancellationToken ct)
        {
            using var done = new CancellationTokenSource();
            Task pump = PumpAsync(done.Token);
            try
            {
                await _client.Navigation.MoveToAsync(target, ct);
            }
            finally
            {
                await done.CancelAsync();
                await pump;
            }
        }

        /// <summary>Fires ticks with nothing steering, which is what makes the engine stop sprinting.</summary>
        public async Task IdleTicksAsync(int count, CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                await _ticks.FireAsync(ct);
                await Task.Delay(30, ct);
            }

            await Task.Delay(200, ct);
        }

        private async Task PumpAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await _ticks.FireAsync(ct);
                    await Task.Delay(10, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static async Task<Harness> StartAsync(bool autoSendPosition, CancellationToken ct)
        {
            var ticks = new ManualTickSource();
            FakeJavaServer server = FakeJavaServer.Create();
            UmpkClient client = new UmpkClientBuilder()
                .UseVersion(Version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
                .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
                .UseTickSource(ticks)
                .ConfigureOptions(o => o.AutoSendPosition = autoSendPosition)
                .ConfigureFeatures(f =>
                {
                    f.Terrain = true;
                    f.Physics = true;
                    f.Pathfinding = true;
                })
                .Build();

            var harness = new Harness(server, client, ticks);
            await harness.JoinAsync(ct);
            await harness.BuildFloorAsync(ct);
            await harness.TeleportAsync(new Vec3d(0.5, StandY, 0.5), ct);
            return harness;
        }

        private async Task JoinAsync(CancellationToken ct)
        {
            Task connect = _client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

            await _server.NextFrameAsync(ct); // handshake
            _server.ServerConnection.SetPhase(ProtocolPhase.Login);
            await _server.NextFrameAsync(ct); // hello
            await SendAsync(ProtocolPhase.Login,
                new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
            await _server.NextFrameAsync(ct); // login_acknowledged
            _server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
            await _server.NextFrameAsync(ct); // client_information
            await SendAsync(ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
            await _server.NextFrameAsync(ct); // finish_configuration
            _server.ServerConnection.SetPhase(ProtocolPhase.Play);
            await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(_server, Version.Protocol, ct);
            await connect.WaitAsync(Budget, ct);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        InboundFrame frame = await _server.NextFrameAsync(ct).ConfigureAwait(false);
                        _frames.Enqueue(new RecordedFrame(frame.WireId, frame.CopyPayload()));
                    }
                }
                catch (Exception)
                {
                    // The pipe closing at the end of the test is the expected way out.
                }
            }, ct);

            var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
            var spawn = new CommonPlayerSpawnInfo(
                DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
                IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
            await SendAsync(ProtocolPhase.Play, new ClientboundLoginPacket(
                PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
                ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
                DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
                Legacy: null), ct);
            await joined.Task.WaitAsync(Budget, ct);
        }

        /// <summary>A stone runway the planner can sprint down, in the client's own world.</summary>
        private async Task BuildFloorAsync(CancellationToken ct)
        {
            Registry<BlockDefinition> blocks = JavaGameData.Registries(Version.Version.Protocol).Blocks;
            Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            int stoneState = stone.DefaultStateId;

            await _client.InvokeAsync(
                c =>
                {
                    c.State.World.LoadColumn(new ChunkPos(0, 0));
                    c.State.World.LoadColumn(new ChunkPos(1, 0));
                    for (int x = -4; x <= 24; x++)
                        for (int z = -4; z <= 4; z++)
                            c.State.World.SetBlockStateId(new BlockPos(x, FloorY, z), stoneState);

                    return true;
                },
                ct);
        }

        private async Task TeleportAsync(Vec3d target, CancellationToken ct)
        {
            var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = _client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
            await SendAsync(ProtocolPhase.Play, new ClientboundPlayerPositionPacket(
                target.X, target.Y, target.Z, 0f, 0f, RelativeFlags: 0, TeleportId: 1,
                ModernValues: new PositionMoveRotation(target, Vec3d.Zero, 0f, 0f)), ct);
            await corrected.Task.WaitAsync(Budget, ct);

            // A server correction is acknowledged with an immediate position report.  It is not an AutoSendPosition tick, so consume that causal acknowledgement before assertions about movement generated by this harness begin.
            await WaitForFrameAsync(EntityPackets.Serverbound.MovePlayerPosRot, ct);
            _frames.Clear();
        }

        private async Task WaitForFrameAsync(PacketType type, CancellationToken ct)
        {
            int wireId = Wire(type);
            while (!_frames.Any(frame => frame.WireId == wireId))
                await Task.Delay(10, ct);

        }

        private async Task SendAsync<TPacket>(ProtocolPhase phase, TPacket packet, CancellationToken ct)
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
                await _server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
                return;
            }

            throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
        }
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

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
