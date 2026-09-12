using System.Buffers;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClient.LastDisconnect"/>: null before any session, and populated even when the ending session never reached <see cref="ClientStatus.Playing"/> - a configuration-phase kick throws out of <see cref="UmpkClient.ConnectAsync"/> before the receive loop starts, so <see cref="Events.Disconnected"/> never fires, but the applier that decoded the kick already recorded it.</summary>
public sealed class LastDisconnectTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    [Fact]
    public async Task LastDisconnect_IsNullBeforeAnySession()
    {
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .Build();

        Assert.Null(client.LastDisconnect);
    }

    [Fact]
    public async Task LastDisconnect_CarriesTheConfigurationKickReason()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information

        Component reason = Component.Text("You are not whitelisted");
        await SendAsync(server, descriptor, ProtocolPhase.Configuration,
            new ClientboundConfigDisconnectPacket(reason), ct);

        await Assert.ThrowsAsync<ConnectionClosedException>(() => connect);

        Assert.NotNull(client.LastDisconnect);
        Assert.NotNull(client.LastDisconnect!.Message);
        Assert.Equal("You are not whitelisted", client.LastDisconnect.Message!.ToPlainText());
        Assert.Equal(DisconnectKind.Kick, client.LastDisconnect.Kind);
    }

    private static UmpkClient Client(FakeJavaServer server)
    {
        return new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();
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
