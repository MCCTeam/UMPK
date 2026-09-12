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

/// <summary><see cref="ClientStatus"/>'s lifecycle ordinals, <see cref="DisconnectInfo.Classify"/>'s close-reason classification, <see cref="ReconnectPolicy.IsRetryable"/>'s retry veto, and <see cref="UmpkClient.StatusChanged"/> raised over a real join and a real close.</summary>
public sealed class ClientStatusTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>A literal ordinal table, not a round trip through the enum: the next insertion into this enum has to change this table by hand, which is the point. Nothing serializes <see cref="ClientStatus"/> today, but a <see cref="Reconnecting"/> sitting after <see cref="ClientStatus.Disconnected"/> is a trap for the first ordinal comparison someone writes.</summary>
    [Fact]
    public void ClientStatusOrdinals_AreTheLifecycleOrder()
    {
        Assert.Equal(0, (int)ClientStatus.Created);
        Assert.Equal(1, (int)ClientStatus.Authenticating);
        Assert.Equal(2, (int)ClientStatus.Connecting);
        Assert.Equal(3, (int)ClientStatus.Configuring);
        Assert.Equal(4, (int)ClientStatus.Playing);
        Assert.Equal(5, (int)ClientStatus.Reconnecting);
        Assert.Equal(6, (int)ClientStatus.Disconnected);
        Assert.Equal(7, Enum.GetValues<ClientStatus>().Length);
    }

    /// <summary>Maps every close reason to the public disconnect category.</summary>
    [Theory]
    [InlineData(CloseReason.Local, DisconnectKind.LocalStop)]
    [InlineData(CloseReason.Cancelled, DisconnectKind.Cancelled)]
    [InlineData(CloseReason.DisconnectMessage, DisconnectKind.Kick)]
    [InlineData(CloseReason.Transferred, DisconnectKind.Transferred)]
    [InlineData(CloseReason.SocketEof, DisconnectKind.ConnectionLost)]
    [InlineData(CloseReason.IdleTimeout, DisconnectKind.ConnectionLost)]
    [InlineData(CloseReason.ProtocolViolation, DisconnectKind.ConnectionLost)]
    public void Classify_MapsEveryCloseReasonOntoAKind(CloseReason reason, DisconnectKind expected)
        => Assert.Equal(expected, DisconnectInfo.Classify(reason, wasLocal: false));

    [Fact]
    public void Classify_WasLocal_WinsOverTheCloseReason()
        => Assert.Equal(DisconnectKind.LocalStop, DisconnectInfo.Classify(CloseReason.SocketEof, wasLocal: true));

    [Fact]
    public void IsRetryable_NoPolicy_NeverRetries()
        => Assert.False(ReconnectPolicy.IsRetryable(null, new DisconnectInfo { Reason = CloseReason.SocketEof }));

    [Theory]
    [InlineData(CloseReason.Local)]
    [InlineData(CloseReason.Cancelled)]
    public void IsRetryable_LocalStopOrCancelled_IsNotRetryable(CloseReason reason)
    {
        var policy = new ReconnectPolicy { MaxAttempts = 3 };
        Assert.False(ReconnectPolicy.IsRetryable(policy, new DisconnectInfo { Reason = reason }));
    }

    [Theory]
    [InlineData(CloseReason.SocketEof)]
    [InlineData(CloseReason.ProtocolViolation)]
    [InlineData(CloseReason.IdleTimeout)]
    [InlineData(CloseReason.DisconnectMessage)]
    public void IsRetryable_ConnectionLostOrKick_IsRetryable(CloseReason reason)
    {
        var policy = new ReconnectPolicy { MaxAttempts = 3 };
        Assert.True(ReconnectPolicy.IsRetryable(policy, new DisconnectInfo { Reason = reason }));
    }

    [Fact]
    public void IsRetryable_PolicyVeto_IsConsulted()
    {
        // A veto that refuses a kick specifically, not a blanket refusal: the same policy still retries a transport fault, which is the whole point of keeping kick and fault as different kinds.
        var policy = new ReconnectPolicy
        {
            MaxAttempts = 3,
            ShouldRetry = info => info.Reason != CloseReason.DisconnectMessage,
        };

        Assert.False(ReconnectPolicy.IsRetryable(policy, new DisconnectInfo { Reason = CloseReason.DisconnectMessage }));
        Assert.True(ReconnectPolicy.IsRetryable(policy, new DisconnectInfo { Reason = CloseReason.SocketEof }));
    }

    [Fact]
    public async Task StatusChanged_RaisesEveryTransition_InOrder()
    {
        (List<(ClientStatus Previous, ClientStatus Current)> transitions, _) = await RunJoinThenCloseAsync();

        Assert.Equal(
            [
                (ClientStatus.Created, ClientStatus.Connecting),
                (ClientStatus.Connecting, ClientStatus.Configuring),
                (ClientStatus.Configuring, ClientStatus.Playing),
                (ClientStatus.Playing, ClientStatus.Disconnected),
            ],
            transitions);
    }

    [Fact]
    public async Task StatusChanged_CarriesTheDisconnect_OnTheTerminalTransition()
    {
        (_, List<ClientStatusChangedEventArgs> events) = await RunJoinThenCloseAsync();

        ClientStatusChangedEventArgs last = events[^1];
        Assert.Equal(ClientStatus.Disconnected, last.Current);
        Assert.NotNull(last.Disconnect);
        Assert.Equal(CloseReason.SocketEof, last.Disconnect!.Reason);
    }

    /// <summary>Joins a <see cref="FakeJavaServer"/> through login and configuration into play, then drops the peer, recording every <see cref="UmpkClient.StatusChanged"/> transition along the way.</summary>
    private static async Task<(List<(ClientStatus Previous, ClientStatus Current)> Transitions, List<ClientStatusChangedEventArgs> Events)>
        RunJoinThenCloseAsync()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        FakeJavaServer server = FakeJavaServer.Create();

        await using UmpkClient client = Client(server);

        var transitions = new List<(ClientStatus Previous, ClientStatus Current)>();
        var events = new List<ClientStatusChangedEventArgs>();
        var reachedDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.StatusChanged += (_, e) =>
        {
            transitions.Add((e.Previous, e.Current));
            events.Add(e);
            if (e.Current == ClientStatus.Disconnected)
                reachedDisconnected.TrySetResult();

        };

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
        await Support.ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await connect.WaitAsync(Budget, ct);

        await server.DisposeAsync();
        await reachedDisconnected.Task.WaitAsync(Budget, ct);

        return (transitions, events);
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
