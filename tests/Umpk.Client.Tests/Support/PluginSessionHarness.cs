using System.Buffers;
using System.IO.Pipelines;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests.Support;

/// <summary>Builds a <see cref="UmpkClient"/> against a <see cref="FakeJavaServer"/> and drives a full handshake/login/configuration exchange to the start of play, for suites that need a REAL live session (not just a directly-constructed <see cref="Umpk.Client.Plugins.PluginHost"/>) to prove session-relative behavior: attach timing, detach timing, reconnect, and the raw packet feed.</summary>
internal static class PluginSessionHarness
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    public static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>Builds an unconnected client wired to <paramref name="server"/>'s pipe.</summary>
    public static UmpkClient BuildClient(FakeJavaServer server, Action<UmpkClientBuilder>? configure = null)
    {
        var builder = new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            });
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Builds an unconnected client whose connection factory hands out one pipe per call, in order.</summary>
    public static UmpkClient BuildReconnectingClient(
        IReadOnlyList<IDuplexPipe> pipes, Action<UmpkClientBuilder>? configure = null)
    {
        var builder = new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new RotatingConnectionFactory(pipes))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            });
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Drives handshake, login and configuration through to the start of play, then awaits connect.</summary>
    public static async Task JoinAsync(UmpkClient client, FakeJavaServer server, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveLoginAsync(server, ct);
        await connect.WaitAsync(Budget, ct);
    }

    /// <summary>Drives handshake, login and configuration only (caller starts and awaits ConnectAsync itself).</summary>
    public static async Task DriveLoginAsync(FakeJavaServer server, CancellationToken ct)
    {
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
    }

    public static async Task SendAsync<TPacket>(FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
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

    /// <summary>Hands back one pipe per call, in order; the client's Nth connect gets pipes[N-1].</summary>
    private sealed class RotatingConnectionFactory(IReadOnlyList<IDuplexPipe> pipes) : IConnectionFactory
    {
        private int _index;

        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipes[Interlocked.Increment(ref _index) - 1]);
    }
}
