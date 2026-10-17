using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Hosting;
using Umpk.Protocol.Java;
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
        await Task.Delay(300);
        // Permit one frame already queued when the state packet was applied; a fixed 20 TPS source produces about six.
        Assert.InRange(session.Client.State.SessionTick - start, 0, 1);
        long deadline = Environment.TickCount64 + 3000;
        while (session.Client.State.SessionTick == start && Environment.TickCount64 < deadline)
            await Task.Delay(10);
        Assert.True(session.Client.State.SessionTick > start);
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

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTickSource _ticks = new();
        private readonly JavaVersion _version;

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
            if (!useDefaultClock)
                builder.UseTickSource(_ticks);
            Client = builder.Build();
        }

        internal UmpkClient Client { get; }

        internal static async Task<Session> CreateAsync(int protocol, bool useDefaultClock = false)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
            var session = new Session(version!, useDefaultClock);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session._budget.Token), ScriptedServer.DriveToPlayAsync(session._server, version!, session._budget.Token));
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
}
