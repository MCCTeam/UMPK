using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

public sealed class WorldTickPipelineTests
{
    [Theory]
    [InlineData(765)]
    [InlineData(777)]
    public async Task DefaultSessionClockUsesTheDeclaredSlowRate(int protocol)
    {
        await using Session session = await Session.CreateAsync(protocol, useDefaultClock: true);
        await session.JoinAsync();
        await session.SendAsync(new ClientboundTickingStatePacket(1, false));
        long start = session.Client.State.SessionTick;
        await session.AdvanceFrameAsync(300);
        Assert.Equal(start, session.Client.State.SessionTick);
        await session.AdvanceFrameAsync(1000);
        Assert.Equal(start + 1, session.Client.State.SessionTick);
        await session.AdvanceFrameAsync(2000);
        Assert.Equal(start + 2, session.Client.State.SessionTick);
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task BorderAdvancesOncePerWorldTickAndRetainsNativePreviousBounds(int protocol)
    {
        await using Session session = await Session.CreateAsync(protocol);
        await session.JoinAsync();
        await session.InitializeBorderAsync(4);
        int borderChanges = 0;
        using IDisposable changed = session.Client.Events.Subscribe<WorldBorderChanged>(_ => borderChanges++);

        foreach ((double size, double maximum) in new[] { (80d, 50d), (60d, 40d), (40d, 30d), (20d, 10d) })
        {
            await session.TickAsync();
            Assert.Equal(size, session.Client.State.World.Border.Size);
            Assert.Equal(maximum, session.Client.State.World.Border.MaxX);
        }

        Assert.Equal(0, borderChanges);
        await session.TickAsync();
        Assert.Equal(20, session.Client.State.World.Border.Size);
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task FrozenWorldConsumesExactlyTheGrantedStepsAndThenResumes(int protocol)
    {
        await using Session session = await Session.CreateAsync(protocol);
        await session.JoinAsync();
        await session.InitializeBorderAsync(4);
        await session.SendAsync(new ClientboundTickingStatePacket(20, true));
        await session.TickAsync();
        await session.TickAsync();
        Assert.Equal(100, session.Client.State.World.Border.Size);

        await session.SendAsync(new ClientboundTickingStepPacket(2));
        await session.TickAsync();
        Assert.Equal(80, session.Client.State.World.Border.Size);
        await session.TickAsync();
        Assert.Equal(60, session.Client.State.World.Border.Size);
        await session.TickAsync();
        Assert.Equal(60, session.Client.State.World.Border.Size);

        await session.SendAsync(new ClientboundTickingStatePacket(20, false));
        await session.TickAsync();
        Assert.Equal(40, session.Client.State.World.Border.Size);
        await session.TickAsync();
        Assert.Equal(20, session.Client.State.World.Border.Size);
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task FreezeBeforeJoinIsIgnoredAndNewLevelResetsTheTickManager(int protocol)
    {
        await using Session session = await Session.CreateAsync(protocol);
        await session.SendAsync(new ClientboundTickingStatePacket(1, true));
        await session.JoinAsync();
        await session.InitializeBorderAsync(2);
        await session.TickAsync();
        Assert.Equal(60, session.Client.State.World.Border.Size);

        await session.SendAsync(new ClientboundTickingStatePacket(1, true));
        await session.SendAsync(new ClientboundRespawnPacket(session.Spawn("minecraft:overworld"), 0, null));
        await session.TickAsync();
        Assert.Equal(60, session.Client.State.World.Border.Size);

        await session.SendAsync(new ClientboundRespawnPacket(session.Spawn("minecraft:the_nether"), 0, null));
        await session.InitializeBorderAsync(2);
        await session.TickAsync();
        Assert.Equal(60, session.Client.State.World.Border.Size);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task ShulkerPeekUsesLoadedWorldTicksAndHonorsFreezeAndStep(int protocol)
    {
        using Stream native = typeof(WorldTickPipelineTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.ShulkerGeometry.native.json")!;
        using JsonDocument document = JsonDocument.Parse(native);
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()].EnumerateArray().Single(row => row.GetProperty("scale").GetSingle() == 1F && row.GetProperty("face").GetString() == "DOWN" && row.GetProperty("input")[0].GetDouble() == .5);
        JsonElement[] phases = scenario.GetProperty("steps").EnumerateArray().Where(row => row[0].GetInt32() == 1).Take(3).Select(row => row[2]).ToArray();
        await using Session session = await Session.CreateAsync(protocol);
        await session.JoinAsync();
        Assert.True(session.Client.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("shulker"), out RegistryEntry<EntityTypeDefinition> type));
        var spawn = new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, .5, 64, .5, 0, 0, 0, 0, 0, 0, 0, null);
        await session.SendAsync(spawn);
        await session.SendAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(17, MetadataValue.Byte(100))], [])));
        AssertBox(scenario.GetProperty("initial"));
        await session.TickAsync();
        AssertBox(scenario.GetProperty("initial"));
        await session.Client.InvokeAsync(client => client.State.World.LoadColumn(new ChunkPos(0, 0)));
        await session.SendAsync(new ClientboundTickingStatePacket(20, true));
        await session.TickAsync();
        AssertBox(scenario.GetProperty("initial"));
        await session.SendAsync(new ClientboundTickingStepPacket(2));
        await session.TickAsync();
        AssertBox(phases[0]);
        await session.TickAsync();
        AssertBox(phases[1]);
        await session.TickAsync();
        AssertBox(phases[1]);
        await session.Client.InvokeAsync(client => client.State.World.UnloadColumn(new ChunkPos(0, 0)));
        await session.SendAsync(new ClientboundTickingStatePacket(20, false));
        await session.TickAsync();
        AssertBox(phases[1]);
        await session.Client.InvokeAsync(client => client.State.World.LoadColumn(new ChunkPos(0, 0)));
        await session.TickAsync();
        AssertBox(phases[2]);
        await session.SendAsync(new ClientboundRemoveEntitiesPacket([42]));
        await session.SendAsync(spawn);
        await session.TickAsync();
        AssertBox(scenario.GetProperty("initial"));

        void AssertBox(JsonElement expected)
        {
            Entity entity = Assert.IsType<Entity>(session.Client.State.Entities.Get(42));
            Assert.True(session.Client.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool blocking));
            Assert.True(blocking);
            Assert.Equal(new Aabb(expected[3].GetDouble(), expected[4].GetDouble(), expected[5].GetDouble(), expected[6].GetDouble(), expected[7].GetDouble(), expected[8].GetDouble()), box);
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTickSource _ticks = new();
        private readonly JavaVersion _version;
        private readonly ObservedFrames? _nativeFrames;

        private Session(JavaVersion version, bool useDefaultClock)
        {
            _version = version;
            UmpkClientBuilder builder = new UmpkClientBuilder()
                .UseVersion(version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "BorderTester"))
                .UseConnectionFactory(new PipeConnectionFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
                .ConfigureFeatures(features =>
                {
                    features.Physics = false;
                    features.Pathfinding = false;
                });
            if (useDefaultClock)
            {
                var time = new ManualTimeProvider();
                _nativeFrames = new ObservedFrames(time);
                Client = builder.BuildWithNativeFrames(time, _nativeFrames);
            }
            else
            {
                builder.UseTickSource(_ticks);
                Client = builder.Build();
            }
        }

        internal UmpkClient Client { get; }

        internal static async Task<Session> CreateAsync(int protocol, bool useDefaultClock = false)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
            var session = new Session(version!, useDefaultClock);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session._budget.Token), ScriptedServer.DriveToPlayAsync(session._server, version!, session._budget.Token));
                if (session._nativeFrames is { } frames)
                    await frames.Ready.WaitAsync(session._budget.Token);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal CommonPlayerSpawnInfo Spawn(string dimension)
        {
            int id = JavaGameData.Registries(_version.Version.Protocol).DimensionTypes.TryGet(Identifier.Parse(dimension), out RegistryEntry<DimensionTypeDefinition> type) ? type.NetworkId : 0;
            return new CommonPlayerSpawnInfo(id, dimension, 0, 0, -1, false, false, null, 0, 63) { DimensionTypeName = dimension };
        }

        internal Task JoinAsync() => SendAsync(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, Spawn("minecraft:overworld"), false, false, null));

        internal Task InitializeBorderAsync(long duration) => SendAsync(new ClientboundInitializeBorderPacket(0, 0, 100, 20, duration, 1000, 5, 15));

        internal Task TickAsync() => _ticks.FireAsync(_budget.Token);

        internal Task AdvanceFrameAsync(long milliseconds) => _nativeFrames!.PushAsync(milliseconds, _budget.Token);

        internal async Task SendAsync(IPacket packet)
        {
            string marker = Guid.NewGuid().ToString();
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = Client.Events.Subscribe<ChatMessageReceived>(message =>
            {
                if (message.Message.ToPlainText() == marker)
                    applied.TrySetResult();
            });
            await ScriptedServer.SendAsync(_server, _version.Protocol, ProtocolPhase.Play, packet, _budget.Token);
            await ScriptedServer.SendAsync(_server, _version.Protocol, ProtocolPhase.Play, new ClientboundSystemChatPacket(Component.Text(marker), false), _budget.Token);
            await applied.Task.WaitAsync(_budget.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            _budget.Dispose();
        }
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        internal async Task FireAsync(CancellationToken ct)
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

    private sealed class ObservedFrames(ManualTimeProvider time) : ITickSource
    {
        private sealed record Frame(long Milliseconds, TaskCompletionSource Processed);
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal Task Ready => _ready.Task;

        internal async Task PushAsync(long milliseconds, CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _frames.Writer.WriteAsync(new Frame(milliseconds, completed), ct);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        }

        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0, previous = 0;
            _ready.TrySetResult();
            await foreach (Frame frame in _frames.Reader.ReadAllAsync(ct))
            {
                time.Advance(TimeSpan.FromMilliseconds(frame.Milliseconds - previous));
                previous = frame.Milliseconds;
                yield return sequence++;
                // NativeClientTickSource returns to this frame enumerator only after the session has consumed every game tick produced by this timestamp.
                frame.Processed.TrySetResult();
            }
        }
    }
}
