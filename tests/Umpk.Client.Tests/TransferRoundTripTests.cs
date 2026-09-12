using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class TransferRoundTripTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly ServerEndpoint Source = new("source", 25565);
    private static readonly ServerEndpoint Destination = new("destination", 25566);

    [Fact]
    public async Task PlayTransferIsOptIn_AndPreservesProfileAndCookies()
    {
        var profile = new GameProfile(Guid.NewGuid(), "Tester");
        await using FakeJavaServer source = FakeJavaServer.Create();
        await using FakeJavaServer destination = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then((_, ct) => StartedClient(source, profile, ct))
            .Then((_, ct) => StartedClient(destination, profile, ct));
        await using var supervisor = new UmpkClientSupervisor(factory, new ClientSupervisorOptions
        {
            FollowServerTransfers = true,
        });
        using var cts = new CancellationTokenSource(Budget);

        await supervisor.StartAsync(Source, cts.Token);
        byte[] cookie = [1, 3, 3, 7];
        supervisor.Client!.Cookies.Set(Identifier.Minecraft("transfer_test"), cookie);
        TaskCompletionSource playingAgain = NextPlaying(supervisor);

        await ScriptedServer.SendAsync(
            source,
            ScriptedServer.Version.Protocol,
            ProtocolPhase.Play,
            new ClientboundTransferPacket(Destination.Host, Destination.Port),
            cts.Token);
        await playingAgain.Task.WaitAsync(Budget, cts.Token);

        Assert.Equal(2, factory.CreateCalls);
        Assert.True(factory.Attempts[1].IsTransfer);
        Assert.Equal(Destination, factory.Attempts[1].Endpoint);
        Assert.Equal(cookie, supervisor.Client!.Cookies.Get(Identifier.Minecraft("transfer_test")));
    }

    [Fact]
    public async Task DefaultSupervisorSurfacesButDoesNotFollowTransfer()
    {
        var profile = new GameProfile(Guid.NewGuid(), "Tester");
        await using FakeJavaServer source = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then((_, ct) => StartedClient(source, profile, ct));
        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await supervisor.StartAsync(Source, cts.Token);
        supervisor.Client!.Events.Subscribe<Events.ServerTransferRequested>(_ => seen.TrySetResult());
        await ScriptedServer.SendAsync(
            source,
            ScriptedServer.Version.Protocol,
            ProtocolPhase.Play,
            new ClientboundTransferPacket(Destination.Host, Destination.Port),
            cts.Token);
        await seen.Task.WaitAsync(Budget, cts.Token);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(Source, supervisor.Endpoint);
        Assert.Equal(ClientStatus.Playing, supervisor.Status);
    }

    [Fact]
    public async Task ConfigurationTransferCompletesOnDestinationWithTransferHandshakeIntent()
    {
        var profile = new GameProfile(Guid.NewGuid(), "Tester");
        await using FakeJavaServer source = FakeJavaServer.Create();
        await using FakeJavaServer destination = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then((attempt, ct) =>
            {
                _ = attempt;
                UmpkClient client = ScriptedServer.BuildClient(source, profile);
                _ = DriveToConfigurationTransferAsync(source, ct);
                return ValueTask.FromResult(client);
            })
            .Then((_, ct) => StartedClient(destination, profile, ct));
        await using var supervisor = new UmpkClientSupervisor(factory, new ClientSupervisorOptions
        {
            FollowServerTransfers = true,
        });
        using var cts = new CancellationTokenSource(Budget);

        await supervisor.StartAsync(Source, cts.Token);

        Assert.Equal(ClientStatus.Playing, supervisor.Status);
        Assert.Equal(Destination, supervisor.Endpoint);
        Assert.Equal(2, factory.CreateCalls);
        Assert.True(factory.Attempts[1].IsTransfer);
    }

    private static ValueTask<UmpkClient> StartedClient(
        FakeJavaServer server, GameProfile profile, CancellationToken ct)
    {
        UmpkClient client = ScriptedServer.BuildClient(server, profile);
        _ = ScriptedServer.DriveToPlayAsync(server, ct);
        return ValueTask.FromResult(client);
    }

    private static async Task DriveToConfigurationTransferAsync(FakeJavaServer server, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = ScriptedServer.Version.Protocol;
        await server.NextFrameAsync(ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct);
        await ScriptedServer.SendAsync(
            server,
            descriptor,
            ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null),
            ct);
        await server.NextFrameAsync(ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct);
        await ScriptedServer.SendAsync(
            server,
            descriptor,
            ProtocolPhase.Configuration,
            new ClientboundConfigTransferPacket(Destination.Host, Destination.Port),
            ct);
    }

    private static TaskCompletionSource NextPlaying(UmpkClientSupervisor supervisor)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.StatusChanged += (_, args) =>
        {
            if (args.Current == ClientStatus.Playing)
                result.TrySetResult();
        };
        return result;
    }
}
