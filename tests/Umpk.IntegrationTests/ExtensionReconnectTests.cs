using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client;
using Umpk.Client.Plugins;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary><see cref="IClientExtension"/> identity across a REAL reconnect: two whole hermetic logins driven by UMPK's own server-side login driver (<see cref="JavaServerLogin"/>) over in-memory pipes, the same hermetic pattern <see cref="PacketFeedHermeticTests"/> and <see cref="ObservePacketsHermeticTests"/> already prove the raw feed and packet-received wiring against. <see cref="Umpk.Client.Tests.ClientExtensionHostTests"/> (a different assembly) proves the mechanism with a scripted fake server; this suite proves it end to end against the real protocol driver, with a real dropped connection driving the real reconnect loop.</summary>
public sealed class ExtensionReconnectTests
{
    private const int Protocol = 770; // 1.21.5
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly ServerEndpoint Endpoint = new("localhost", 25565);

    private static JavaVersion Version()
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        return version!;
    }

    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = 256,
    };

    private static JavaServerLoginOptions ServerOptions() => new()
    {
        KeyPair = RSA.Create(2048),
        Verifier = null, // offline
        CompressionThreshold = -1,
    };

    [Fact]
    public async Task SessionStarted_FiresTwice_AcrossAReconnect()
    {
        JavaVersion version = Version();
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var policy = new ReconnectPolicy { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(10), BackoffFactor = 1.0 };
        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(policy) };
        var factory = new ReconnectingSessionFactory(version);
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        var extension = new RecordingExtension("ext-started-twice");
        await supervisor.Extensions.AddAsync(extension, ct);

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await factory.AcceptTasks[0].WaitAsync(Budget, ct);

        Assert.Single(extension.SessionCreatedClients);
        Assert.Single(extension.SessionStartedContexts);

        // Drop the first real connection: a genuine transport close, driving the real reconnect loop.
        await factory.ServerConnections[0].DisposeAsync();

        await secondPlaying.Task.WaitAsync(Budget, ct);
        await factory.AcceptTasks[1].WaitAsync(Budget, ct);

        Assert.Equal(2, extension.SessionCreatedClients.Count);
        Assert.NotSame(extension.SessionCreatedClients[0], extension.SessionCreatedClients[1]);
        Assert.Equal(2, extension.SessionStartedContexts.Count);
        Assert.NotSame(extension.SessionStartedContexts[0], extension.SessionStartedContexts[1]);
        Assert.Single(extension.SessionEndedInfos);

        await supervisor.StopAsync(ct);

        Assert.Equal(2, extension.SessionEndedInfos.Count);
    }

    [Fact]
    public async Task Cron_KeepsFiring_WhileDisconnected()
    {
        JavaVersion version = Version();
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        // Long enough that the disconnected gap is reliably observable against a cron firing every 50ms.
        var policy = new ReconnectPolicy { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(500), BackoffFactor = 1.0 };
        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(policy) };
        var factory = new ReconnectingSessionFactory(version);
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        int fireCount = 0;
        var extension = new RecordingExtension("ext-cron")
        {
            OnActivate = context => context.Cron.Every(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref fireCount)),
        };
        await supervisor.Extensions.AddAsync(extension, ct);

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await factory.AcceptTasks[0].WaitAsync(Budget, ct);

        int beforeDrop = Volatile.Read(ref fireCount);
        await factory.ServerConnections[0].DisposeAsync();

        // Give the cron a real chance to fire several times purely within the backoff gap, well before the second attempt could plausibly reach Play.
        await Task.Delay(300, ct);
        int duringGap = Volatile.Read(ref fireCount);

        Assert.NotEqual(ClientStatus.Playing, supervisor.Status);
        Assert.True(
            duringGap > beforeDrop,
            $"Expected the extension's cron to keep firing while no session was live; before={beforeDrop}, during={duringGap}.");

        await secondPlaying.Task.WaitAsync(Budget, ct);
        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task PacketFeed_ResubscribesOnTheSecondSession()
    {
        JavaVersion version = Version();
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var policy = new ReconnectPolicy { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(10), BackoffFactor = 1.0 };
        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(policy) };
        var factory = new ReconnectingSessionFactory(version);
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        var loginFrameCounts = new List<Counter>();
        var extension = new RecordingExtension("ext-feed")
        {
            OnSessionCreated = client =>
            {
                var counter = new Counter();
                lock (loginFrameCounts)
                    loginFrameCounts.Add(counter);

                client.ObservePackets((in PacketFrame frame) =>
                {
                    if (frame.Phase == ProtocolPhase.Login)
                        Interlocked.Increment(ref counter.Value);

                });
            },
        };
        await supervisor.Extensions.AddAsync(extension, ct);

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await factory.AcceptTasks[0].WaitAsync(Budget, ct);

        await factory.ServerConnections[0].DisposeAsync();

        await secondPlaying.Task.WaitAsync(Budget, ct);
        await factory.AcceptTasks[1].WaitAsync(Budget, ct);

        // Give the second session's own login frames, already exchanged inside ConnectAsync by the time it returned, a moment to finish dispatching to the observer on the connection's read/write path.
        await Task.Delay(100, ct);

        await supervisor.StopAsync(ct);

        Counter[] snapshot;
        lock (loginFrameCounts)
            snapshot = [.. loginFrameCounts];

        Assert.Equal(2, snapshot.Length);
        Assert.True(snapshot[0].Value > 0, "The first session's subscription never saw a login-phase frame.");
        Assert.True(snapshot[1].Value > 0, "The second session's subscription never saw a login-phase frame - it did not resubscribe.");
    }

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

    private sealed class Counter
    {
        public int Value;
    }

    /// <summary>Builds a fresh <see cref="UmpkClient"/> wired to a fresh in-memory pipe pair for every attempt, and concurrently drives the SERVER side of a real login/configuration exchange (<see cref="JavaServerLogin.AcceptAsync"/>) on that pipe, exactly as <see cref="PacketFeedHermeticTests"/> and <see cref="ObservePacketsHermeticTests"/> drive a single one. Every built server connection and its accept task is recorded so a test can await the login and later drop the connection to force a reconnect.</summary>
    private sealed class ReconnectingSessionFactory(JavaVersion version) : IClientSessionFactory
    {
        private readonly Lock _gate = new();

        public List<JavaConnection> ServerConnections { get; } = [];

        public List<Task<ServerLoginResult>> AcceptTasks { get; } = [];

        public ValueTask<UmpkClient> CreateAsync(SessionAttempt attempt, CancellationToken ct)
        {
            DuplexPipePair pipes = DuplexPipePair.Create();
            var server = new JavaConnection(pipes.Right, FrameMode());
            server.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Serverbound);
            server.Start();

            lock (_gate)
            {
                ServerConnections.Add(server);
                AcceptTasks.Add(HermeticPlayServer.AcceptAndAnnouncePlayAsync(server, version, ServerOptions(), ct));
            }

            UmpkClient client = new UmpkClientBuilder()
                .UseVersion(version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "ExtTester"))
                .UseConnectionFactory(new FixedPipeConnectionFactory(pipes.Left))
                .Build();

            return ValueTask.FromResult(client);
        }

        public ValueTask ReleaseAsync(UmpkClient client, DisconnectInfo? disconnect, CancellationToken ct) => client.DisposeAsync();
    }

    private sealed class FixedPipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    /// <summary>Records every extension-lifecycle event, in order, for assertions.</summary>
    private sealed class RecordingExtension(string id) : IClientExtension
    {
        public string Id { get; } = id;

        public ClientExtensionContext? Context { get; private set; }

        public List<UmpkClient> SessionCreatedClients { get; } = [];

        public List<ClientPluginContext> SessionStartedContexts { get; } = [];

        public List<DisconnectInfo?> SessionEndedInfos { get; } = [];

        public Action<ClientExtensionContext>? OnActivate { get; set; }

        public Action<UmpkClient>? OnSessionCreated { get; set; }

        public ValueTask ActivateAsync(ClientExtensionContext context, CancellationToken ct)
        {
            Context = context;
            context.SessionCreated += (_, e) =>
            {
                SessionCreatedClients.Add(e.Client);
                OnSessionCreated?.Invoke(e.Client);
            };
            context.SessionStarted += (_, e) => SessionStartedContexts.Add(e.Session);
            context.SessionEnded += (_, e) => SessionEndedInfos.Add(e.Disconnect);
            OnActivate?.Invoke(context);
            return ValueTask.CompletedTask;
        }
    }
}
