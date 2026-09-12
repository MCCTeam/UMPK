using System.Buffers;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><c>JavaConnectionOptions.ReadIdleTimeout</c> defaults to 30 seconds and <c>JavaConnection.ReadFrameWithTimeoutAsync</c> already turns its elapse into <c>ConnectionClosedException(CloseReason.IdleTimeout)</c>, recorded into <see cref="DisconnectInfo"/> by the receive loop. <c>UmpkClient.ConnectAsync</c> hardcoded <c>ReadIdleTimeout = TimeSpan.Zero</c> in its own <see cref="JavaConnectionOptions"/> literal regardless of what a caller configured, and <see cref="ClientOptions"/> had no knob for it at all, so the backstop could never be armed from <see cref="UmpkClient"/>: a server that stopped writing (a hang, a firewall black hole, a `/tick freeze` that also wedged the network thread) left the session open forever with no signal a reconnect policy could act on. <see cref="ClientOptions.ReadIdleTimeout"/> passes through to the existing <see cref="JavaConnectionOptions.ReadIdleTimeout"/> rather than adding a second watchdog.</summary>
public sealed class ReadIdleTimeoutTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>The client installs a 30-second read timeout on the channel pipeline before anything is sent.</summary>
    [Fact]
    public void ReadIdleTimeoutDefaultsToVanillasThirtySecondWindow()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new ClientOptions().ReadIdleTimeout);
    }

    /// <summary>With the backstop armed, a server that never sends another byte and never closes the socket ends the session on its own, with a reason a reconnect policy can classify as a transport fault.</summary>
    [Fact]
    public async Task ASilentServerEndsTheSession_WithIdleTimeout()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, o => o.ReadIdleTimeout = TimeSpan.FromSeconds(2));

        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Disconnected>(e => disconnected.TrySetResult(e.Info));

        await JoinAsync(client, server, ct);

        // The server sends nothing further and never closes the pipe; only the read-idle backstop can end this session.
        DisconnectInfo info = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.IdleTimeout, info.Reason);
        Assert.False(info.WasLocal);
        Assert.Equal(ClientStatus.Disconnected, client.Status);
    }

    /// <summary>Zero switches the backstop off entirely: a silent server never times the session out.</summary>
    [Fact]
    public async Task ASilentServerNeverTimesOut_WhenTheBackstopIsSwitchedOff()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, o => o.ReadIdleTimeout = TimeSpan.Zero);

        bool disconnected = false;
        client.Events.Subscribe<Disconnected>(_ => disconnected = true);

        await JoinAsync(client, server, ct);

        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        Assert.False(disconnected);
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    /// <summary>A server that keeps writing, even a frame the client cannot decode, resets the read timer: the backstop watches for inbound TRAFFIC, not for traffic the client understands.</summary>
    /// <remarks><c>UmpkClient.ConnectAsync</c> always builds its <see cref="JavaConnection"/> with <c>UnknownPacketPolicy.Preserve</c>, so an unmapped wire id is a successful frame read (surfaced as an <c>UnknownPacket</c>) rather than a protocol violation, and a successful read is exactly what <c>ReadFrameWithTimeoutAsync</c> restarts its window on.</remarks>
    [Fact]
    public async Task ATalkingServerNeverTimesOut_PastTheWindow()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        // The solution runner executes test assemblies concurrently. Tolerate short scheduler stalls while keeping traffic active for more than two complete idle windows.
        TimeSpan idleWindow = TimeSpan.FromSeconds(5);
        TimeSpan trafficWindow = TimeSpan.FromSeconds(12);
        Assert.True(trafficWindow > idleWindow + idleWindow);
        await using UmpkClient client = Client(server, o => o.ReadIdleTimeout = idleWindow);

        bool disconnected = false;
        client.Events.Subscribe<Disconnected>(_ => disconnected = true);

        await JoinAsync(client, server, ct);

        const int UnmappedWireId = 0x7FFF;
        using var stop = new CancellationTokenSource(trafficWindow);
        using CancellationTokenSource pump = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        try
        {
            while (!pump.IsCancellationRequested)
            {
                await server.SendFrameAsync(UnmappedWireId, [], pump.Token);
                await Task.Delay(200, pump.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The traffic window elapsed; that is the end of the scripted traffic, not a failure.
        }

        Assert.False(disconnected);
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    private static UmpkClient Client(FakeJavaServer server, Action<ClientOptions> configureOptions) =>
        new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureOptions(configureOptions)
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
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

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, descriptor, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);
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

    private sealed class PipeConnectionFactory(System.IO.Pipelines.IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<System.IO.Pipelines.IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }
}
