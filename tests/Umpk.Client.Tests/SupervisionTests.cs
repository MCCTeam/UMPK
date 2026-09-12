using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClientSupervisor.StartAsync"/> and <see cref="UmpkClientSupervisor.StopAsync"/>: one inline attempt, its status sequence, its exception classification, and cleanup. The reconnect loop itself is <c>ReconnectSupervisionTests</c>.</summary>
public sealed class SupervisionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly ServerEndpoint Endpoint = new("test", 25565);

    [Fact]
    public async Task StartAsync_ReachesPlaying_AndReturnsWhileTheSessionIsStillLive()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        Assert.Equal(ClientStatus.Playing, supervisor.Status);
        Assert.NotNull(supervisor.Client);
        Assert.Equal(ClientStatus.Playing, supervisor.Client!.Status);
        Assert.False(supervisor.Completed.IsCompleted);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task StartAsync_ReportsAuthenticating_WhenTheFactoryAsksForIt()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory(async (attempt, ct) =>
        {
            attempt.ReportAuthenticating();
            await Task.Yield();
            return ScriptedServer.BuildClient(server);
        });

        var transitions = new List<ClientStatus>();
        await using var supervisor = new UmpkClientSupervisor(factory);
        supervisor.StatusChanged += (_, e) => transitions.Add(e.Current);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        int authenticating = transitions.IndexOf(ClientStatus.Authenticating);
        Assert.True(authenticating >= 0, "Authenticating never appeared in the recorded transitions.");

        // Authenticating precedes the LATER Connecting that leads into the actual dial: Connecting appears once before it (the initial transition) and again after it.
        int connectingAfter = transitions.IndexOf(ClientStatus.Connecting, authenticating + 1);
        Assert.True(connectingAfter > authenticating, "No Connecting transition followed Authenticating.");

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task StartAsync_ConnectRefused_ThrowsConnectFailed_AndReachesDisconnected()
    {
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(
            new UmpkClientBuilder()
                .UseVersion(ScriptedServer.Version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
                .UseConnectionFactory(new RefusingConnectionFactory())
                .UseStaticRegistries(JavaGameData.Registries(ScriptedServer.Version.Version.Protocol))
                .Build()));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);

        ConnectFailedException ex = await Assert.ThrowsAsync<ConnectFailedException>(
            () => supervisor.StartAsync(Endpoint, cts.Token));

        Assert.Equal(Endpoint, ex.Endpoint);
        Assert.IsType<System.Net.Sockets.SocketException>(ex.InnerException);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
        await supervisor.Completed.WaitAsync(Budget, cts.Token);
    }

    [Fact]
    public async Task StartAsync_KickedDuringLogin_ThrowsLoginRejected_CarryingTheServerReason()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToLoginKickAsync(server, Component.Text("You are not whitelisted"), ct);

        LoginRejectedException ex = await Assert.ThrowsAsync<LoginRejectedException>(() => start);

        Assert.NotNull(ex.Reason);
        Assert.Equal("You are not whitelisted", ex.Reason!.ToPlainText());
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
    }

    [Fact]
    public async Task StopAsync_EndsTheSession_AndCompletesTheSupervisor()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        await supervisor.StopAsync(ct);

        Assert.True(supervisor.Completed.IsCompletedSuccessfully);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
        Assert.NotNull(supervisor.LastDisconnect);
        Assert.True(supervisor.LastDisconnect!.WasLocal);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        await supervisor.StopAsync(ct);
        await supervisor.StopAsync(ct);
        await supervisor.StopAsync(ct);

        Assert.True(supervisor.Completed.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StatusChanged_CarriesTheDisconnect_OnTheTerminalTransition()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        var events = new List<ClientStatusChangedEventArgs>();
        await using var supervisor = new UmpkClientSupervisor(factory);
        supervisor.StatusChanged += (_, e) => events.Add(e);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        await supervisor.StopAsync(ct);

        ClientStatusChangedEventArgs last = events[^1];
        Assert.Equal(ClientStatus.Disconnected, last.Current);
        Assert.NotNull(last.Disconnect);
        Assert.True(last.Disconnect!.WasLocal);
    }

    [Fact]
    public async Task Supervisor_ReleasesEveryClientItBuilt()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        int created = 0;
        int released = 0;
        var factory = new CountingSessionFactory(
            new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server))),
            () => Interlocked.Increment(ref created),
            () => Interlocked.Increment(ref released));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        await supervisor.StopAsync(ct);

        Assert.Equal(created, released);
        Assert.True(created > 0);
    }

    /// <summary>A play-to-configuration re-entry must be visible on the supervisor's status, not only on the session's, because the supervisor is what a host reads.</summary>
    /// <remarks>A proxy backend switch can return the connection to configuration. During that window the supervisor must report configuration rather than playing so hosts do not attempt play packets.</remarks>
    [Fact]
    public async Task Status_FollowsTheLiveSessionIntoConfiguration_AndBackToPlaying()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        var transitions = new List<ClientStatus>();
        await using var supervisor = new UmpkClientSupervisor(factory);
        supervisor.StatusChanged += (_, e) => transitions.Add(e.Current);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);

        int before = transitions.Count;
        await ScriptedServer.DriveThroughConfigurationReentryAsync(server, ct);

        // The hop is driven by the session's own receive loop, so the transitions land asynchronously.
        await WaitForAsync(() => transitions.Count >= before + 2, ct);

        // Configuration and back, in that order, and nothing else in between: a Reconnecting or a Disconnected here would mean the supervisor had mistaken the hop for a lost session.
        Assert.Equal([ClientStatus.Configuring, ClientStatus.Playing], transitions.Skip(before).ToList());
        Assert.Equal(ClientStatus.Playing, supervisor.Status);
        Assert.False(supervisor.Completed.IsCompleted);

        await supervisor.StopAsync(ct);
    }

    /// <summary>The dial itself must NOT report a re-entry. The inner client walks Connecting -> Configuring -> Playing on its way in, and mirroring that walk would announce a configuration hop on every single connect.</summary>
    [Fact]
    public async Task Status_DoesNotReportConfiguring_ForTheInitialDial()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new DelegateClientSessionFactory((attempt, ct) => ValueTask.FromResult(ScriptedServer.BuildClient(server)));

        var transitions = new List<ClientStatus>();
        await using var supervisor = new UmpkClientSupervisor(factory);
        supervisor.StatusChanged += (_, e) => transitions.Add(e.Current);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        Assert.DoesNotContain(ClientStatus.Configuring, transitions);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task Dial_UsesTheAddressTheSupervisorWasGiven_WhenTheFactoryLeavesItAlone()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var connections = new RecordingConnectionFactory(server.ClientPipe);
        var factory = new DelegateClientSessionFactory(
            (attempt, ct) => ValueTask.FromResult(BuildClient(connections)));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        Assert.Equal([Endpoint], connections.Dialed);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task Redirect_SendsTheDialToTheAddressTheFactoryChose()
    {
        var elsewhere = new ServerEndpoint("backend.example", 25599);
        FakeJavaServer server = FakeJavaServer.Create();
        var connections = new RecordingConnectionFactory(server.ClientPipe);
        var factory = new DelegateClientSessionFactory((attempt, ct) =>
        {
            attempt.Redirect(elsewhere);
            return ValueTask.FromResult(BuildClient(connections));
        });

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Task start = supervisor.StartAsync(Endpoint, ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await start.WaitAsync(Budget, ct);

        Assert.Equal([elsewhere], connections.Dialed);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);

        await supervisor.StopAsync(ct);
    }

    private static UmpkClient BuildClient(IConnectionFactory connections) => new UmpkClientBuilder()
        .UseVersion(ScriptedServer.Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(connections)
        .UseStaticRegistries(JavaGameData.Registries(ScriptedServer.Version.Version.Protocol))
        .ConfigureFeatures(f =>
        {
            f.Physics = false;
            f.Pathfinding = false;
        })
        .Build();

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Hands out one fixed pipe and records the address it was asked for.</summary>
    private sealed class RecordingConnectionFactory(System.IO.Pipelines.IDuplexPipe pipe) : IConnectionFactory
    {
        public List<ServerEndpoint> Dialed { get; } = [];

        public ValueTask<System.IO.Pipelines.IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
        {
            Dialed.Add(endpoint);
            return ValueTask.FromResult(pipe);
        }
    }

    /// <summary>An <see cref="IClientSessionFactory"/> wrapper that counts create/release calls.</summary>
    private sealed class CountingSessionFactory(IClientSessionFactory inner, Action onCreate, Action onRelease) : IClientSessionFactory
    {
        public async ValueTask<UmpkClient> CreateAsync(SessionAttempt attempt, CancellationToken ct)
        {
            UmpkClient client = await inner.CreateAsync(attempt, ct).ConfigureAwait(false);
            onCreate();
            return client;
        }

        public async ValueTask ReleaseAsync(UmpkClient client, DisconnectInfo? disconnect, CancellationToken ct)
        {
            await inner.ReleaseAsync(client, disconnect, ct).ConfigureAwait(false);
            onRelease();
        }
    }
}
