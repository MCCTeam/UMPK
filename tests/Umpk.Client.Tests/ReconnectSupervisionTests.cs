using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClientSupervisor"/>'s background reconnect loop: the retry decision, the per-attempt classification, backoff timing, <see cref="UmpkClientSupervisor.ReconnectAsync"/> and <see cref="UmpkClientSupervisor.ReportSessionFault"/>. <see cref="SupervisionTests"/> covers the single inline attempt these all build on.</summary>
public sealed class ReconnectSupervisionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly ServerEndpoint Endpoint = new("test", 25565);
    private static readonly ServerEndpoint OtherEndpoint = new("other", 25566);

    private static ReconnectPolicy ShortPolicy(int maxAttempts = 3, Func<DisconnectInfo, bool>? shouldRetry = null) => new()
    {
        MaxAttempts = maxAttempts,
        InitialDelay = TimeSpan.FromMilliseconds(10),
        BackoffFactor = 1.0,
        ShouldRetry = shouldRetry,
    };

    [Fact]
    public async Task ServerCloses_SupervisorReconnects_AndReturnsToPlaying()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();

        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server1))
            .Then(Succeed(server2));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await secondPlaying.Task.WaitAsync(Budget, ct);

        Assert.Equal(ClientStatus.Playing, supervisor.Status);
        Assert.Equal(2, factory.Attempts.Count);
        Assert.Equal(1, factory.Attempts[1].Attempt);
        Assert.NotNull(factory.Attempts[1].PreviousDisconnect);
        Assert.Equal(CloseReason.SocketEof, factory.Attempts[1].PreviousDisconnect!.Reason);
    }

    [Fact]
    public async Task NoPolicy_MeansNoReconnect()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(SucceedThenDrop(server));

        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
    }

    [Fact]
    public async Task PolicyIsPulledPerAttempt_SoAProviderCanChangeItsMind()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server))
            .Then(Refuse());

        var provider = new TwoAnswerPolicyProvider(ShortPolicy(maxAttempts: 5), null);
        var options = new ClientSupervisorOptions { ReconnectPolicy = provider };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        // Attempt 0 (the initial start) plus exactly one failed retry: the re-pull after that failure answered null, which stops the loop well short of MaxAttempts = 5.
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
    }

    [Fact]
    public async Task MaxAttempts_BoundsTheLoop()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server))
            .Then(Refuse());

        var options = new ClientSupervisorOptions
        {
            ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy(maxAttempts: 2)),
        };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.Equal(3, factory.CreateCalls);
    }

    [Fact]
    public async Task Backoff_WaitsTheConfiguredDelayBetweenAttempts()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server))
            .Then(Refuse());

        var policy = new ReconnectPolicy
        {
            MaxAttempts = 2,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            BackoffFactor = 1.0,
        };
        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(policy) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(400),
            $"Expected at least 400ms of backoff delay across two retries; measured {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task AKick_RefusedByTheVeto_StopsTheLoop()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        Component reason = Component.Text("Get out");
        var factory = new SequencedSessionFactory().Then(SucceedThenKick(server, reason));

        var options = new ClientSupervisorOptions
        {
            ReconnectPolicy = new FixedReconnectPolicyProvider(
                ShortPolicy(shouldRetry: info => info.Kind != DisconnectKind.Kick)),
        };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.Equal(1, factory.CreateCalls);
        Assert.NotNull(supervisor.LastDisconnect);
        Assert.Equal(DisconnectKind.Kick, supervisor.LastDisconnect!.Kind);
    }

    [Fact]
    public async Task AKick_AllowedByTheVeto_IsRetried()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        Component reason = Component.Text("Just kidding");
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenKick(server1, reason))
            .Then(Succeed(server2));

        var options = new ClientSupervisorOptions
        {
            ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy(shouldRetry: _ => true)),
        };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await secondPlaying.Task.WaitAsync(Budget, ct);

        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);
    }

    [Fact]
    public async Task LoginRejectedOnARetry_StopsTheLoop()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server1))
            .Then(KickDuringLogin(server2, Component.Text("Banned")));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        // Two attempts, not three: MaxAttempts would allow more, but a rejected login on the retry stops the loop outright.
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
    }

    [Fact]
    public async Task AFactoryFailure_StopsTheLoop_AndCompletesWithoutFaulting()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server))
            .Then(ThrowFactory(() => new InvalidOperationException("the host's own build step failed")));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(ClientStatus.Disconnected, supervisor.Status);
        Assert.True(supervisor.Completed.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AVersionResolutionFailure_IsRetried()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server1))
            .Then(ThrowFactory(() => new VersionResolutionException(
                "no ping answer", Endpoint.Host, Endpoint.Port, protocol: null, VersionNegotiationFailure.StatusPingFailed)))
            .Then(Succeed(server2));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await secondPlaying.Task.WaitAsync(Budget, ct);

        Assert.Equal(3, factory.CreateCalls);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);
    }

    [Fact]
    public async Task ReconnectAsync_SwitchesTheEndpoint_AndRearmsSupervision()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        FakeJavaServer server3 = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(Succeed(server1))
            .Then(SucceedThenDrop(server2))
            .Then(Succeed(server3));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);

        // Subscribed after StartAsync's own Playing transition, so this counts fresh from here: ReconnectAsync's own Playing is 1, and the later auto-retry's Playing is 2.
        TaskCompletionSource playingAgainTwice = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.ReconnectAsync(OtherEndpoint, ct);

        Assert.Equal(2, factory.Attempts.Count);
        Assert.Equal(OtherEndpoint, factory.Attempts[1].Endpoint);
        Assert.Equal(OtherEndpoint, supervisor.Endpoint);

        // server2 (the reconnected-to target) drops on its own; the background loop must still be armed.
        await playingAgainTwice.Task.WaitAsync(Budget, ct);
        Assert.Equal(3, factory.CreateCalls);
    }

    [Fact]
    public async Task ReconnectAsync_BeforeStart_Throws()
    {
        var factory = new SequencedSessionFactory().Then(
            (_, _) => throw new InvalidOperationException("the factory must never be called"));

        await using var supervisor = new UmpkClientSupervisor(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => supervisor.ReconnectAsync());
    }

    [Fact]
    public async Task ReportSessionFault_EndsTheSession_AndTheReconnectPolicyApplies()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(Succeed(server1))
            .Then(Succeed(server2));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);

        supervisor.ReportSessionFault(new DisconnectInfo { Reason = CloseReason.ProtocolViolation });

        await secondPlaying.Task.WaitAsync(Budget, ct);

        Assert.Equal(2, factory.CreateCalls);
        Assert.NotNull(factory.Attempts[1].PreviousDisconnect);
        Assert.Equal(CloseReason.ProtocolViolation, factory.Attempts[1].PreviousDisconnect!.Reason);
    }

    // Scripted attempt steps

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> Succeed(FakeJavaServer server) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToPlayAsync(server, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> SucceedThenDrop(FakeJavaServer server) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToPlayThenDropAsync(server, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> SucceedThenKick(FakeJavaServer server, Component reason) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToPlayThenKickAsync(server, reason, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> KickDuringLogin(FakeJavaServer server, Component reason) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToLoginKickAsync(server, reason, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> Refuse() =>
        (_, _) => ValueTask.FromResult(
            new UmpkClientBuilder()
                .UseVersion(ScriptedServer.Version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
                .UseConnectionFactory(new RefusingConnectionFactory())
                .UseStaticRegistries(JavaGameData.Registries(ScriptedServer.Version.Version.Protocol))
                .Build());

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> ThrowFactory(Func<Exception> makeException) =>
        (_, _) => throw makeException();

    // Helpers

    /// <summary>Completes once <see cref="UmpkClientSupervisor.StatusChanged"/> has reported Playing at least <paramref name="atLeast"/> times.</summary>
    private static TaskCompletionSource PlayingCounter(UmpkClientSupervisor supervisor, int atLeast)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        supervisor.StatusChanged += (_, e) =>
        {
            if (e.Current != ClientStatus.Playing)
                return;

            if (Interlocked.Increment(ref count) >= atLeast)
                tcs.TrySetResult();

        };
        return tcs;
    }

    /// <summary>A policy provider that answers <paramref name="first"/> once, then <paramref name="rest"/> forever.</summary>
    private sealed class TwoAnswerPolicyProvider(ReconnectPolicy? first, ReconnectPolicy? rest) : IReconnectPolicyProvider
    {
        private int _calls;

        public ReconnectPolicy? GetReconnectPolicy() => Interlocked.Increment(ref _calls) == 1 ? first : rest;
    }
}
