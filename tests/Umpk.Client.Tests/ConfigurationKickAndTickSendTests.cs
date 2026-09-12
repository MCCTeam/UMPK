using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A real <see cref="UmpkClient"/> and <see cref="JavaConnection"/> must surface both a configuration-phase kick and a failed per-tick send from a scripted server.</summary>
public sealed class ConfigurationKickAndTickSendTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>A <c>disconnect</c> during the configuration phase reaches the consumer WITH its reason.</summary>
    /// <remarks>The supervisor reaches play, re-enters configuration through <c>ClientboundStartConfigurationPacket</c>, and receives a kick there. The kick reason is recorded before the connection closes, so the reconnect policy receives the decoded reason rather than an unrelated transport fault. A configuration close must also escape applier exception isolation so the receive loop can publish <c>Disconnected</c>.</remarks>
    [Fact]
    public async Task AConfigurationKick_ReachesTheReconnectPolicyWithItsReason()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        FakeJavaServer server = FakeJavaServer.Create();

        DisconnectInfo? seen = null;
        var options = new ClientSupervisorOptions
        {
            ReconnectPolicy = new FixedReconnectPolicyProvider(new ReconnectPolicy
            {
                MaxAttempts = 0,
                ShouldRetry = info =>
                {
                    seen = info;
                    return false;
                },
            }),
        };

        var factory = new DelegateClientSessionFactory((_, _) => ValueTask.FromResult(Client(server)));
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        Task start = supervisor.StartAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello

        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await start.WaitAsync(Budget, ct);

        // Re-enter configuration from play, then kick during it.
        await SendAsync(server, descriptor, ProtocolPhase.Play, new ClientboundStartConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // configuration_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);

        Component reason = Component.Text("You are not whitelisted on this server!");
        await SendAsync(server, descriptor, ProtocolPhase.Configuration,
            new ClientboundConfigDisconnectPacket(reason), ct);

        // Vanilla writes the disconnect frame and then closes the socket; the second failure is what the receive loop actually sees (the kick itself is swallowed by the applier dispatch's own isolation
        // - see the remarks above).
        await server.DisposeAsync();

        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.NotNull(seen);
        Assert.Equal(CloseReason.DisconnectMessage, seen!.Reason);
        Assert.NotNull(seen.Message);
        Assert.Equal("You are not whitelisted on this server!", seen.Message!.ToPlainText());

        // The applier's record wins over the exception that unwinds behind it (DisconnectInfo is first-wins), so there is no fault attached: the reason is the component, not a stack trace.
        Assert.Null(seen.Fault);
    }

    /// <summary>A configuration-phase kick reached via the 1.20.2+ play-to-configuration re-entry fires <see cref="Events.Disconnected"/> on its own, with no need for the peer to also close its socket.</summary>
    /// <remarks>
    /// <para><see cref="AConfigurationKick_ReachesTheReconnectPolicyWithItsReason"/> above only reaches <c>Disconnected</c> because the fake server closes the socket right behind the kick, producing a SECOND failure (a closed pipe) that the receive loop sees; the kick's own <c>ConnectionClosedException</c>, thrown out of <c>JavaClientLogin.RunConfigurationPhaseAsync</c> through <c>EnterConfigurationAsync</c> and the <c>start_configuration</c> applier, unwinds into <c>ApplyPacketAsync</c>'s catch-all for the PLAY-phase <c>ClientboundStartConfigurationPacket</c> it is still dispatching. That guard exists to keep one bad applier from tearing the whole session down, which is the right call for an applier bug; but a <see cref="ConnectionClosedException"/> is not an applier bug, it is the transport itself saying the session is over, and swallowing it left the receive loop's own <c>catch (ConnectionClosedException)</c> arm - the one that records <see cref="DisconnectInfo"/> and publishes <see cref="Events.Disconnected"/> - never reached for this path. This test drives the same re-entry and kick, but leaves the socket open, so the only way <c>Disconnected</c> can fire at all is if the exception is allowed to propagate out of the applier dispatch instead of being logged and discarded there.</para>
    /// <para>The applier's <c>RecordKick</c> still runs first (it is on the observer path inside <c>ConfigurationKickAsync</c>, before the exception is thrown), so <see cref="DisconnectInfo.Reason"/> and <see cref="DisconnectInfo.Message"/> come from the kick component, and <see cref="DisconnectInfo.Fault"/> stays null: <c>RecordDisconnect</c> is first-wins, so the exception that unwinds behind the recorded kick does not overwrite it.</para>
    /// </remarks>
    [Fact]
    public async Task AConfigReentryKick_EndsTheSession_WithoutNeedingASocketClose()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        FakeJavaServer server = FakeJavaServer.Create();

        await using UmpkClient client = Client(server);

        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Events.Disconnected>(e => disconnected.TrySetResult(e.Info));

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello

        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await connect.WaitAsync(Budget, ct);

        // Re-enter configuration from play, then kick during it, WITHOUT closing the socket behind the kick: the whole point of this fact is that nothing else needs to happen for Disconnected to fire.
        await SendAsync(server, descriptor, ProtocolPhase.Play, new ClientboundStartConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // configuration_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);

        Component reason = Component.Text("You are not whitelisted on this server!");
        await SendAsync(server, descriptor, ProtocolPhase.Configuration,
            new ClientboundConfigDisconnectPacket(reason), ct);

        DisconnectInfo info = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.DisconnectMessage, info.Reason);
        Assert.NotNull(info.Message);
        Assert.Equal("You are not whitelisted on this server!", info.Message!.ToPlainText());

        // The applier's record wins over the exception that unwinds behind it (DisconnectInfo is first-wins), so there is no fault attached here either.
        Assert.Null(info.Fault);
    }

    /// <summary>A kick during the LOGIN phase keeps <see cref="CloseReason.DisconnectMessage"/> and names the server's reason.</summary>
    /// <remarks>There is no login-phase packet observer, so the reason travels on the exception rather than as a component; <see cref="ConnectionClosedException.Disconnect"/> is what keeps it structured instead of flattening it to a string. A kick during login is always the FIRST attempt of a fresh dial, so <see cref="UmpkClientSupervisor.StartAsync"/> never consults the reconnect policy for it at all - it throws <see cref="LoginRejectedException"/> directly, which is precisely the verdict a reconnect loop keyed on a deliberate kick must be able to tell apart from a transport fault without asking a policy that was never built to answer it before a session has even started.</remarks>
    [Fact]
    public async Task ALoginKick_KeepsItsCloseReasonAndNamesTheServersReason()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        FakeJavaServer server = FakeJavaServer.Create();

        DisconnectInfo? seen = null;
        var options = new ClientSupervisorOptions
        {
            ReconnectPolicy = new FixedReconnectPolicyProvider(new ReconnectPolicy
            {
                MaxAttempts = 0,
                ShouldRetry = info =>
                {
                    seen = info;
                    return false;
                },
            }),
        };

        var factory = new DelegateClientSessionFactory((_, _) => ValueTask.FromResult(Client(server)));
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        Task start = supervisor.StartAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello

        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginDisconnectPacket(Component.Text("Banned until the heat death of the universe")), ct);

        LoginRejectedException ex = await Assert.ThrowsAsync<LoginRejectedException>(() => start);

        Assert.NotNull(ex.Reason);
        Assert.Equal("Banned until the heat death of the universe", ex.Reason!.ToPlainText());
        Assert.IsType<ConnectionClosedException>(ex.InnerException);
        Assert.Contains("Banned until the heat death", ex.InnerException!.Message, StringComparison.Ordinal);

        // The policy is never consulted for a kick on the very first attempt.
        Assert.Null(seen);
    }

    /// <summary>A per-tick send that loses its connection is REPORTED rather than thrown into a discarded task, but reported as the ordinary end of a session: at Debug, with no exception attached.</summary>
    /// <remarks><c>client_tick_end</c> and the auto-position send were both fire-and-forget from the tick loop, so a send that failed once the connection was gone produced no log, no event and no reason: it vanished into an unobserved task. Reporting it fixed that and overcorrected: EVERY normal disconnect then ended with a warning carrying a full <c>ConnectionClosedException: Connection closed: SocketEof</c> stack trace, which reads as a crash at the end of an ordinary quit. The tick racing the receive loop's teardown is expected, not a fault, so it stays visible without the exception that makes a console logger print a stack trace. The tick loop must also survive, because the receive loop, not the tick, owns the disconnect verdict.</remarks>
    [Fact]
    public async Task APerTickSend_LosingItsConnection_IsReportedWithoutAStackTrace()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        FakeJavaServer server = FakeJavaServer.Create();
        var ticks = new ManualTickSource();
        var logs = new CapturingLoggerFactory();

        await using UmpkClient client = Client(server, loggerFactory: logs, tickSource: ticks);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Events.Disconnected>(_ => disconnected.TrySetResult());

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await connect.WaitAsync(Budget, ct);

        // Spawn the player: client_tick_end is only sent once the join has landed.
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        await SendAsync(server, descriptor, ProtocolPhase.Play, Join(), ct);
        await joined.Task.WaitAsync(Budget, ct);

        // Drop the peer. Once the client's receive loop has published Disconnected, its connection is definitively closed, so the next tick's send throws rather than racing.
        await server.DisposeAsync();
        await disconnected.Task.WaitAsync(Budget, ct);

        await ticks.FireAsync(ct);

        await WaitForAsync(
            () => logs.Entries.Any(e => e.Contains("per-tick client_tick_end send was dropped", StringComparison.Ordinal)),
            ct);

        // Still visible, but as a note and not an alarm: Debug level, no exception attached, and the close reason named in the text so the record is still diagnosable.
        string record = logs.Entries.First(e => e.Contains("per-tick client_tick_end send was dropped", StringComparison.Ordinal));
        Assert.StartsWith("Debug: [noexc]", record, StringComparison.Ordinal);
        Assert.Contains("SocketEof", record, StringComparison.Ordinal);

        // Nothing anywhere reported this teardown as a failure.
        Assert.DoesNotContain(logs.Entries, e => e.Contains("per-tick client_tick_end send failed", StringComparison.Ordinal));

        // The tick loop is still alive: a second tick still runs and still reports.
        int before = logs.Entries.Count(e => e.Contains("per-tick", StringComparison.Ordinal));
        await ticks.FireAsync(ct);
        await WaitForAsync(
            () => logs.Entries.Count(e => e.Contains("per-tick", StringComparison.Ordinal)) > before, ct);
    }

    /// <summary>Every close reason except <see cref="CloseReason.ProtocolViolation"/> is a session ending, as are cancellation and a disposed connection. A protocol violation is a real fault and stays a warning.</summary>
    /// <remarks>The expectations are written out here rather than derived from the enum, so adding a new <see cref="CloseReason"/> without deciding which side of this line it falls on fails the build instead of silently inheriting a default.</remarks>
    [Theory]
    [InlineData(CloseReason.Local, true)]
    [InlineData(CloseReason.SocketEof, true)]
    [InlineData(CloseReason.DisconnectMessage, true)]
    [InlineData(CloseReason.Cancelled, true)]
    [InlineData(CloseReason.Transferred, true)]
    [InlineData(CloseReason.IdleTimeout, true)]
    [InlineData(CloseReason.ProtocolViolation, false)]
    public void CloseReasons_AreClassifiedAsTeardownOrFault(CloseReason reason, bool expected)
        => Assert.Equal(expected, UmpkClient.IsExpectedSendTeardown(new ConnectionClosedException(reason)));

    [Fact]
    public void CancellationAndDisposal_AreTeardown()
    {
        Assert.True(UmpkClient.IsExpectedSendTeardown(new OperationCanceledException()));
        Assert.True(UmpkClient.IsExpectedSendTeardown(new ObjectDisposedException("connection")));
    }

    [Fact]
    public void AnUnexpectedFault_IsNotTeardown()
    {
        Assert.False(UmpkClient.IsExpectedSendTeardown(new InvalidOperationException("boom")));
        Assert.False(UmpkClient.IsExpectedSendTeardown(new NotSupportedException()));
    }

    private static UmpkClient Client(
        FakeJavaServer server,
        ILoggerFactory? loggerFactory = null,
        ITickSource? tickSource = null,
        Action<ClientPolicies>? policies = null)
    {
        var builder = new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureFeatures(f =>
            {
                // Physics off: the idle tick would otherwise put position packets on the wire on its own clock, which neither test is about.
                f.Physics = false;
                f.Pathfinding = false;
            });

        if (loggerFactory is not null)
            builder = builder.UseLoggerFactory(loggerFactory);

        if (tickSource is not null)
            builder = builder.UseTickSource(tickSource);

        if (policies is not null)
            builder = builder.ConfigurePolicies(policies);

        return builder.Build();
    }

    private static ClientboundLoginPacket Join()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
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

    /// <summary>An in-memory logger factory that keeps formatted entries for assertion.</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Recorder(_entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                // The exception marker matters on its own: an attached exception is what makes a console logger print a stack trace, which is the difference between a note and an alarm.
                string marker = exception is null ? "noexc" : "exc";
                entries.Enqueue($"{logLevel}: [{marker}] {formatter(state, exception)}");
            }
        }
    }
}
