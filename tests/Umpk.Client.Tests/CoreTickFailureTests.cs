using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Terminal behavior owned by the core session tick, distinct from isolated plugin ticks.</summary>
public sealed class CoreTickFailureTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task UnexpectedCoreTickFault_EndsSessionOnce_RetainsFault_AndDisposesBoundedly()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        var failure = new InvalidOperationException("deterministic core tick failure");
        var ticks = new FaultingTickSource(failure);

        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = Client(server.ClientPipe, ticks);

        int notifications = 0;
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Events.Disconnected>(message =>
        {
            Interlocked.Increment(ref notifications);
            disconnected.TrySetResult(message.Info);
        });

        Task drive = ScriptedServer.DriveToPlayAsync(server, ct);
        await Task.WhenAll(
            client.ConnectAsync(new ServerEndpoint("test", 25565), ct),
            drive).WaitAsync(Budget, ct);

        await ticks.FailAfterOneTickAsync(ct);
        DisconnectInfo info = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.Local, info.Reason);
        Assert.Same(failure, info.Fault);
        Assert.Equal(DisconnectKind.LocalStop, info.Kind);
        Assert.Equal(ClientStatus.Disconnected, client.Status);
        Assert.True(ticks.SessionCancellationRequested);
        Assert.Equal(1, Volatile.Read(ref notifications));

        await client.DisposeAsync().AsTask().WaitAsync(Budget, ct);
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task NormalSessionCancellation_RemainsQuietAndPublishesOnce()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        var ticks = new BlockingTickSource();

        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = Client(server.ClientPipe, ticks);

        int notifications = 0;
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Events.Disconnected>(message =>
        {
            Interlocked.Increment(ref notifications);
            disconnected.TrySetResult(message.Info);
        });

        Task drive = ScriptedServer.DriveToPlayAsync(server, ct);
        await Task.WhenAll(
            client.ConnectAsync(new ServerEndpoint("test", 25565), ct),
            drive).WaitAsync(Budget, ct);

        await client.DisconnectAsync(ct).WaitAsync(Budget, ct);
        DisconnectInfo info = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.Local, info.Reason);
        Assert.Null(info.Fault);
        Assert.True(ticks.SessionCancellationRequested);
        Assert.Equal(1, Volatile.Read(ref notifications));

        await client.DisposeAsync().AsTask().WaitAsync(Budget, ct);
    }

    [Fact]
    public async Task EarlyConnectFault_RecordsLocalFailure_AndLeavesBoundedDisposal()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        UmpkClient client = new UmpkClientBuilder()
            .UseVersion(ScriptedServer.Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseStaticRegistries(JavaGameData.Registries(ScriptedServer.Version.Version.Protocol))
            .UseConnectionFactory(new RefusingConnectionFactory())
            .ConfigureFeatures(features =>
            {
                features.Physics = false;
                features.Pathfinding = false;
            })
            .Build();

        SocketException failure = await Assert.ThrowsAsync<SocketException>(
            () => client.ConnectAsync(new ServerEndpoint("test", 25565), ct));

        Assert.Equal(ClientStatus.Disconnected, client.Status);
        Assert.NotNull(client.LastDisconnect);
        Assert.Equal(CloseReason.Local, client.LastDisconnect!.Reason);
        Assert.Same(failure, client.LastDisconnect.Fault);
        Assert.False(await client.Spawned.WaitAsync(Budget, ct));

        await client.DisposeAsync().AsTask().WaitAsync(Budget, ct);
    }

    [Fact]
    public async Task FatalMappedDecode_IsBridgedToAllClientSubscribersBeforeDisconnect()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server.ClientPipe, new BlockingTickSource());

        int throwingCalls = 0;
        int receivingCalls = 0;
        PacketDecodeFailure? reported = null;
        client.PacketDecodeFailed += _ =>
        {
            Interlocked.Increment(ref throwingCalls);
            throw new InvalidOperationException("client diagnostic subscriber failed");
        };
        client.PacketDecodeFailed += failure =>
        {
            Interlocked.Increment(ref receivingCalls);
            reported = failure;
        };
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Events.Disconnected>(message => disconnected.TrySetResult(message.Info));

        Task drive = ScriptedServer.DriveToPlayAsync(server, ct);
        await Task.WhenAll(
            client.ConnectAsync(new ServerEndpoint("test", 25565), ct),
            drive).WaitAsync(Budget, ct);

        PhaseRegistry play = ScriptedServer.Version.Protocol.GetRegistry(
            ProtocolPhase.Play, PacketFlow.Clientbound);
        int keepAliveId = play.Packets.Single(pair =>
            pair.Type.Id == Identifier.Minecraft("keep_alive")).WireId;
        await server.SendFrameAsync(keepAliveId, [], ct); // keep_alive requires a long

        DisconnectInfo info = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.ProtocolViolation, info.Reason);
        Assert.Equal(1, Volatile.Read(ref throwingCalls));
        Assert.Equal(1, Volatile.Read(ref receivingCalls));
        Assert.NotNull(reported);
        Assert.Equal(ScriptedServer.Version.Version.Protocol, reported.Protocol);
        Assert.Equal(Identifier.Minecraft("keep_alive"), reported.PacketId);
        Assert.Equal(0, reported.PayloadLength);
    }

    [Fact]
    public async Task SessionCancellation_CancelsActiveNavigationAndReleasesItsLease()
    {
        using var session = new CancellationTokenSource();
        await using var scheduler = new BlockingScheduler();
        JavaVersion version = ScriptedServer.Version;
        var state = new ClientState(new ClientFeatures
        {
            Physics = true,
            Pathfinding = true,
            Terrain = true,
        });
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
            CurrentSessionCancellation = () => session.Token,
        };
        var leases = new MovementLeaseManager();
        var physics = new PhysicsEngineHolder(
            services, JavaGameData.BlockShapes(version.Version.Protocol), NullLogger.Instance);
        var navigator = new Navigator(services, physics, leases, NullLogger.Instance);

        Task movement = navigator.NavigateAsync(
            new GoalBlock(new BlockPos(128, 64, 128)), CancellationToken.None);
        await scheduler.Entered.WaitAsync(Budget);
        Assert.Equal("Navigator.Navigate", leases.CurrentOwner);

        session.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => movement);
        Assert.Null(leases.CurrentOwner);
    }

    private static UmpkClient Client(IDuplexPipe pipe, ITickSource ticks) => new UmpkClientBuilder()
        .UseVersion(ScriptedServer.Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(new PipeConnectionFactory(pipe))
        .UseStaticRegistries(JavaGameData.Registries(ScriptedServer.Version.Version.Protocol))
        .UseTickSource(ticks)
        .ConfigureFeatures(features =>
        {
            features.Physics = false;
            features.Pathfinding = false;
        })
        .Build();

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) =>
            ValueTask.FromResult(pipe);
    }

    private sealed class FaultingTickSource(Exception failure) : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();
        private CancellationToken _sessionToken;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public bool SessionCancellationRequested => _sessionToken.IsCancellationRequested;

        public async IAsyncEnumerable<long> Ticks(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _sessionToken = cancellationToken;
            yield return await _ticks.Reader.ReadAsync(cancellationToken);
            throw failure;
        }

        public ValueTask FailAfterOneTickAsync(CancellationToken ct) => _ticks.Writer.WriteAsync(1, ct);
    }

    private sealed class BlockingTickSource : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();
        private CancellationToken _sessionToken;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public bool SessionCancellationRequested => _sessionToken.IsCancellationRequested;

        public async IAsyncEnumerable<long> Ticks(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _sessionToken = cancellationToken;
            await foreach (long tick in _ticks.Reader.ReadAllAsync(cancellationToken))
                yield return tick;
        }
    }

    private sealed class BlockingScheduler : ISessionScheduler
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public bool IsCurrent => false;

        public void Post(Action work) => throw new NotSupportedException();

        public Task InvokeAsync(Action work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            return WaitForCancellation<TResult>(cancellationToken);
        }

        public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResult> InvokeAsync<TResult>(
            Func<ValueTask<TResult>> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async Task<TResult> WaitForCancellation<TResult>(CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
        }
    }
}
