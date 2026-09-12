using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ConfigurationObligationRoundTripTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task LoginAndConfigurationObligationsUseTheirOwnPhaseAndFinishNormally()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        ProtocolDescriptor descriptor = ScriptedServer.Version.Protocol;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(
            server, new GameProfile(Guid.NewGuid(), "Tester"));
        var key = Identifier.Minecraft("phase_cookie");
        client.Cookies.Set(key, [4, 2]);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await server.NextFrameAsync(ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct);

        await ScriptedServer.SendAsync(
            server, descriptor, ProtocolPhase.Login, new ClientboundLoginCookieRequestPacket(key), ct);
        var loginCookie = Assert.IsType<ServerboundLoginCookieResponsePacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), descriptor, ProtocolPhase.Login));
        Assert.Equal(new byte[] { 4, 2 }, loginCookie.Payload);

        await ScriptedServer.SendAsync(
            server,
            descriptor,
            ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null),
            ct);
        await server.NextFrameAsync(ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct);

        byte[] replacement = [8, 9];
        await ScriptedServer.SendAsync(
            server, descriptor, ProtocolPhase.Configuration, new ClientboundConfigStoreCookiePacket(key, replacement), ct);
        await ScriptedServer.SendAsync(
            server, descriptor, ProtocolPhase.Configuration, new ClientboundConfigCookieRequestPacket(key), ct);
        var configCookie = Assert.IsType<ServerboundConfigCookieResponsePacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), descriptor, ProtocolPhase.Configuration));
        Assert.Equal(replacement, configCookie.Payload);

        Guid packId = Guid.NewGuid();
        await ScriptedServer.SendAsync(
            server,
            descriptor,
            ProtocolPhase.Configuration,
            new ClientboundConfigResourcePackPushPacket(
                packId, "https://example.invalid/pack.zip", string.Empty, Required: false, Prompt: null),
            ct);
        var pack = Assert.IsType<ServerboundConfigResourcePackPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), descriptor, ProtocolPhase.Configuration));
        Assert.Equal(packId, pack.Id);
        Assert.Equal((int)ResourcePackAction.Declined, pack.Action);

        await ScriptedServer.SendAsync(
            server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        Assert.IsType<ServerboundFinishConfigurationPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), descriptor, ProtocolPhase.Configuration));
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await connect.WaitAsync(Budget, ct);
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    private static object DecodeServerbound(
        InboundFrame frame, ProtocolDescriptor descriptor, ProtocolPhase phase)
    {
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Serverbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
        return codec.Decode(frame.Payload, PacketCodecContext.Registryless);
    }
}
