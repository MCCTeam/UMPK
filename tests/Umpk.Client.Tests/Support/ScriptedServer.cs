using System.Buffers;
using System.IO.Pipelines;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Support;

/// <summary>Builds a <see cref="UmpkClient"/> wired to a <see cref="FakeJavaServer"/>'s pipe, and drives the server side of a login/configuration/play exchange (a clean join, a login-phase kick, a configuration-phase kick, or a live-session close) for the supervisor test suites. The client returned by <see cref="BuildClient"/> is unconnected; a script method has to be running (fire-and-forget) before or while the caller connects it, exactly the way <c>IClientSessionFactory.CreateAsync</c> is expected to hand back an unconnected client for the supervisor to dial.</summary>
internal static class ScriptedServer
{
    public static JavaVersion Version => JavaVersions.V1_21_11;

    public static UmpkClient BuildClient(FakeJavaServer server, GameProfile? profile = null) => new UmpkClientBuilder()
        .UseVersion(Version)
        .UseProfile(profile ?? new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
        .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
        .ConfigureFeatures(f =>
        {
            f.Physics = false;
            f.Pathfinding = false;
        })
        .Build();

    /// <summary>Drives handshake, login and configuration through to the start of play.</summary>
    public static async Task DriveToPlayAsync(FakeJavaServer server, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
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
        await SendPlayReadinessFrameAsync(server, descriptor, ct);
    }

    /// <summary>Drives one play-to-configuration-to-play re-entry on an already-playing session: the hop a proxy makes a client take when it moves the player to a different backend server.</summary>
    /// <remarks>The wire script is deliberately minimal: no registry data, no tags, no known-pack negotiation and no brand come back, because those belong to the login-only the start-configuration transition. No frames are skipped and none need to be: <c>client_tick_end</c> is gated on <c>Self.HasSpawned</c>, and the callers of this helper have driven a join-less <see cref="DriveToPlayAsync"/>, so the session generates no background traffic to race with.</remarks>
    public static async Task DriveThroughConfigurationReentryAsync(FakeJavaServer server, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        await SendAsync(server, descriptor, ProtocolPhase.Play, new ClientboundStartConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // configuration_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
    }

    /// <summary>Drives handshake and login through to a login-phase kick.</summary>
    public static async Task DriveToLoginKickAsync(FakeJavaServer server, Component reason, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login, new ClientboundLoginDisconnectPacket(reason), ct);
    }

    /// <summary>Drives a successful login through to a configuration-phase kick.</summary>
    public static async Task DriveToConfigKickAsync(FakeJavaServer server, Component reason, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundConfigDisconnectPacket(reason), ct);
    }

    /// <summary>Drives a full join, then a play-phase kick: a live session ending with a server reason.</summary>
    public static async Task DriveToPlayThenKickAsync(FakeJavaServer server, Component reason, CancellationToken ct)
    {
        await DriveToPlayAsync(server, ct).ConfigureAwait(false);
        await SendAsync(server, Version.Protocol, ProtocolPhase.Play, new ClientboundDisconnectPacket(reason), ct);
        await server.DisposeAsync();
    }

    /// <summary>Drives a full join, then drops the peer: a live session ending with a transport fault.</summary>
    public static async Task DriveToPlayThenDropAsync(FakeJavaServer server, CancellationToken ct)
    {
        await DriveToPlayAsync(server, ct).ConfigureAwait(false);
        await server.DisposeAsync();
    }

    public static async Task SendAsync<TPacket>(
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

    /// <summary>Sends a valid, response-free clientbound PLAY item to establish the same wire boundary as vanilla's first JoinGame. The client deliberately does not report a connected PLAY session until this write has crossed the wire, because vanilla switches its inbound decoder to PLAY at that boundary rather than at login success. These supervisor fixtures intentionally do not need to populate a world.</summary>
    public static Task SendPlayReadinessFrameAsync(
        FakeJavaServer server, ProtocolDescriptor descriptor, CancellationToken ct) =>
        SendAsync(server, descriptor, ProtocolPhase.Play,
            new ClientboundSetTimePacket(GameTime: 0, DayTime: 0, TickDayTime: false, ClockUpdates: []), ct);

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }
}

/// <summary>A connection factory whose <see cref="ConnectAsync"/> always fails before any I/O happens.</summary>
internal sealed class RefusingConnectionFactory : IConnectionFactory
{
    public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
        => throw new System.Net.Sockets.SocketException();
}
