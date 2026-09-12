using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary><see cref="UmpkClient.ObservePackets"/> against a whole real login/configuration exchange over in-memory pipes, using the same hermetic login driver <see cref="PacketFeedHermeticTests"/> already proves the raw feed against. A subscription made before <see cref="UmpkClient.ConnectAsync"/> is ever called must see frames from the very first one: login and configuration both run to completion inside <c>ConnectAsync</c> itself, before it returns, so a handler that only started listening afterward would miss both phases entirely.</summary>
public sealed class ObservePacketsHermeticTests
{
    private const int Protocol = 770; // 1.21.5

    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = 256,
    };

    private sealed class FixedPipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    [Fact]
    public async Task ObservePackets_SubscribedBeforeConnect_CapturesTheLoginAndConfigurationFrames()
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var server = new JavaConnection(pipes.Right, FrameMode());
        server.BindCodec(new DescriptorFrameCodecBinding(version!.Protocol), PacketFlow.Serverbound);
        server.Start();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version!)
            .UseProfile(new GameProfile(Guid.NewGuid(), "ObserverTester"))
            .UseConnectionFactory(new FixedPipeConnectionFactory(pipes.Left))
            .Build();

        var seenPhases = new List<ProtocolPhase>();

        // Subscribed BEFORE ConnectAsync: login and configuration both run to completion inside ConnectAsync itself, so this is the only placement that can see either of them at all.
        using IDisposable subscription = client.ObservePackets((in PacketFrame frame) =>
        {
            lock (seenPhases)
                seenPhases.Add(frame.Phase);

        });

        var serverOptions = new JavaServerLoginOptions
        {
            KeyPair = RSA.Create(2048),
            Verifier = null, // offline
            CompressionThreshold = -1,
        };

        Task<ServerLoginResult> accept = HermeticPlayServer.AcceptAndAnnouncePlayAsync(
            server, version!, serverOptions, cts.Token);
        await client.ConnectAsync(new ServerEndpoint("localhost", 25565), cts.Token);
        await accept;

        ProtocolPhase[] snapshot;
        lock (seenPhases)
            snapshot = [.. seenPhases];

        Assert.Contains(ProtocolPhase.Login, snapshot);
        Assert.Contains(ProtocolPhase.Configuration, snapshot);
    }
}
