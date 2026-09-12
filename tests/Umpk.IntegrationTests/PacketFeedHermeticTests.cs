using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>
/// Verifies the decoded and raw packet feeds of a complete <see cref="UmpkClient"/> session over in-memory pipes.
/// <para>The server's observed wire id must match the client's decoded event, while the client's response must remain available through the raw feed with its original bytes.</para>
/// </summary>
public sealed class PacketFeedHermeticTests
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
    public async Task PacketReceived_Carries_The_Real_WireId_And_ByteCount()
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var server = new JavaConnection(pipes.Right, FrameMode());
        server.BindCodec(new DescriptorFrameCodecBinding(version!.Protocol), PacketFlow.Serverbound);
        server.Start();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version!)
            .UseProfile(new GameProfile(Guid.NewGuid(), "FeedTester"))
            .UseConnectionFactory(new FixedPipeConnectionFactory(pipes.Left))
            .Build();

        // What the client reports, and what the client's own transport saw in each direction.
        List<PacketReceived> decoded = [];
        client.Events.Subscribe<PacketReceived>(e => { lock (decoded) decoded.Add(e); });

        List<(PacketFlow Flow, int WireId, byte[] Payload)> frames = [];
        client.PacketFrameObserved += obs =>
        {
            lock (frames)
                frames.Add((obs.Flow, obs.WireId, obs.CopyPayload()));

        };

        // What the server actually put on the wire, straight from the server's own send path. This is the independent witness: nothing here re-derives the id from the same table the client uses.
        int serverWroteWireId = -1;
        int serverWroteLength = -1;
        server.OutboundPacketObserved += obs =>
        {
            if (obs.DecodedPacket is ClientboundPlayKeepAlivePacket)
            {
                serverWroteWireId = obs.WireId;
                serverWroteLength = obs.PayloadLength;
            }
        };

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

        // One play packet the client is obliged to answer, so the exchange covers both directions.
        await server.SendAsync(new ClientboundPlayKeepAlivePacket(0x0102030405060708L), cts.Token);
        InboundItem reply = await server.ReceiveAsync(cts.Token);

        Assert.True(serverWroteWireId >= 0, "The server never observed itself writing the keep_alive.");
        Assert.Equal(8, serverWroteLength); // a single long

        PacketReceived? keepAlive = null;
        for (int attempt = 0; attempt < 200 && keepAlive is null; attempt++)
        {
            lock (decoded)
                keepAlive = decoded.Find(e => e.Packet is ClientboundPlayKeepAlivePacket);

            if (keepAlive is null)
                await Task.Delay(25, cts.Token);

        }

        Assert.NotNull(keepAlive);

        // The feed must preserve the decoded frame identity and payload length.
        Assert.Equal(serverWroteWireId, keepAlive!.WireId);
        Assert.NotEqual(-1, keepAlive.WireId);
        Assert.Equal(8, keepAlive.PayloadLength);
        Assert.Equal(ProtocolPhase.Play, keepAlive.Phase);

        // The raw feed saw the same inbound frame, with its bytes, and the outbound answer too.
        (PacketFlow Flow, int WireId, byte[] Payload)[] snapshot;
        lock (frames)
            snapshot = [.. frames];

        (PacketFlow Flow, int WireId, byte[] Payload) inbound = Assert.Single(
            snapshot,
            f => f.Flow == PacketFlow.Clientbound && f.WireId == serverWroteWireId && f.Payload.Length == 8);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, inbound.Payload);

        (PacketFlow Flow, int WireId, byte[] Payload) outbound = Assert.Single(
            snapshot,
            f => f.Flow == PacketFlow.Serverbound && f.WireId == reply.Frame.WireId && f.Payload.Length == 8);
        Assert.Equal(reply.Frame.Payload.ToArray(), outbound.Payload);
    }
}
