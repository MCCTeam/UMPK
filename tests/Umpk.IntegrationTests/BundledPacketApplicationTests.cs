using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Time;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>
/// Verifies that <see cref="UmpkClient"/> applies every packet in a bundle in wire order and within one session-loop turn.
/// <para>Entity pairing uses bundles from protocol 762 onward. Applying a movement immediately after an entity spawn demonstrates both delivery and ordering through observable client state.</para>
/// </summary>
public sealed class BundledPacketApplicationTests
{
    private const int EntityId = 4242;

    private static JavaConnectionOptions ServerOptions() => new()
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

    /// <summary>Wraps a scheduler and stamps asynchronous work with a turn number so bundle atomicity is observable.</summary>
    private sealed class TurnStampingScheduler(ISessionScheduler inner) : ISessionScheduler
    {
        private int _turns;

        public int CurrentTurn { get; private set; }

        public bool IsCurrent => inner.IsCurrent;

        public void Post(Action work) => inner.Post(work);

        public Task InvokeAsync(Action work, CancellationToken cancellationToken)
            => inner.InvokeAsync(work, cancellationToken);

        public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken)
            => inner.InvokeAsync(work, cancellationToken);

        public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken)
            => inner.InvokeAsync(
                async () =>
                {
                    CurrentTurn = ++_turns;
                    await work().ConfigureAwait(false);
                },
                cancellationToken);

        public Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken)
            => inner.InvokeAsync(work, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>Protocol 762 introduces <c>bundle_delimiter</c>; the remaining rows span later bundle eras.</summary>
    [Theory]
    [InlineData(762)] // 1.19.4
    [InlineData(764)] // 1.20.2
    [InlineData(770)] // 1.21.5
    [InlineData(776)] // 26.2
    public async Task BundledAddEntity_ReachesTheEntityStore_InOrder_InOneLoopTurn(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var server = new JavaConnection(pipes.Right, ServerOptions());
        server.BindCodec(new DescriptorFrameCodecBinding(version!.Protocol), PacketFlow.Serverbound);
        server.Start();

        await using var scheduler = new TurnStampingScheduler(new ChannelSessionScheduler());
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version!)
            .UseProfile(new GameProfile(Guid.NewGuid(), "BundleTester"))
            .UseConnectionFactory(new FixedPipeConnectionFactory(pipes.Left))
            .UseScheduler(scheduler)
            // No ticks, so the only session-loop turns are the ones the receive loop creates.
            .UseTickSource(new TestTickSource())
            .Build();

        List<(int Turn, PacketReceived Event)> received = [];
        client.Events.Subscribe<PacketReceived>(e =>
        {
            lock (received)
                received.Add((scheduler.CurrentTurn, e));

        });

        // The independent witness: what the SERVER actually put on the wire, from the server's own send path. Nothing here re-derives the wire id from the table the client decodes with.
        int serverAddEntityWireId = -1;
        int serverAddEntityLength = -1;
        server.OutboundPacketObserved += obs =>
        {
            if (obs.DecodedPacket is ClientboundAddEntityPacket)
            {
                serverAddEntityWireId = obs.WireId;
                serverAddEntityLength = obs.PayloadLength;
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

        // Movement follows the spawn inside one bundle. Its applier needs a tracked entity, so the final position proves that the bundle was applied in wire order.
        await server.SendAsync(new ClientboundBundleDelimiterPacket(), cts.Token);
        await server.SendAsync(
            new ClientboundAddEntityPacket(
                EntityId, Guid.NewGuid(), TypeId: 1, X: 1, Y: 2, Z: 3,
                XRot: 0, YRot: 0, YHeadRot: 0, Data: 0,
                VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null),
            cts.Token);
        await server.SendAsync(
            new ClientboundMoveEntityPosPacket(EntityId, DeltaX: 4096, DeltaY: 0, DeltaZ: 0, OnGround: true),
            cts.Token);
        await server.SendAsync(new ClientboundBundleDelimiterPacket(), cts.Token);

        Entity? tracked = null;
        for (int attempt = 0; attempt < 400 && tracked is null; attempt++)
        {
            tracked = await client.InvokeAsync(c => c.State.Entities.Get(EntityId), cts.Token);
            if (tracked is null)
                await Task.Delay(25, cts.Token);

        }

        // A delivered spawn must create the entity before the following movement is applied.
        Assert.NotNull(tracked);

        // Applied in wire order: the spawn put the entity at x=1, the move that followed it inside the same bundle added one block. Out of order, the move would have found nothing and x would be 1.
        Assert.Equal(2.0, tracked!.Position.X, 6);
        Assert.Equal(2.0, tracked.Position.Y, 6);
        Assert.Equal(3.0, tracked.Position.Z, 6);

        (int Turn, PacketReceived Event)[] snapshot;
        lock (received)
            snapshot = [.. received];

        (int Turn, PacketReceived Event) spawn = Assert.Single(
            snapshot, r => r.Event.Packet is ClientboundAddEntityPacket);
        (int Turn, PacketReceived Event) move = Assert.Single(
            snapshot, r => r.Event.Packet is ClientboundMoveEntityPosPacket);

        // Frame identity: a bundled packet reports the wire id and byte count of the frame it actually arrived in. The bundle item's own frame is default, so publishing that would have been a fabricated id.
        Assert.True(serverAddEntityWireId >= 0, "The server never observed itself writing the add_entity.");
        Assert.Equal(serverAddEntityWireId, spawn.Event.WireId);
        Assert.Equal(serverAddEntityLength, spawn.Event.PayloadLength);
        Assert.NotEqual(-1, spawn.Event.WireId);
        Assert.NotEqual(spawn.Event.WireId, move.Event.WireId);
        Assert.Equal(ProtocolPhase.Play, spawn.Event.Phase);

        // Both bundled packets share one session-loop turn, preventing observers from seeing a partial bundle state.
        Assert.Equal(spawn.Turn, move.Turn);

        // And a packet outside the bundle is its own turn, so the shared turn above is not an artifact of everything landing on one turn.
        await server.SendAsync(new ClientboundPlayKeepAlivePacket(0x0102030405060708L), cts.Token);
        (int Turn, PacketReceived Event) keepAlive = default;
        for (int attempt = 0; attempt < 400 && keepAlive.Event is null; attempt++)
        {
            lock (received)
                keepAlive = received.Find(r => r.Event.Packet is ClientboundPlayKeepAlivePacket);

            if (keepAlive.Event is null)
                await Task.Delay(25, cts.Token);

        }

        Assert.NotNull(keepAlive.Event);
        Assert.NotEqual(spawn.Turn, keepAlive.Turn);
    }

    /// <summary>Verifies that a preserved frame without a decoded packet still reaches its frame-level consumer. The server brand arrives through <c>custom_payload</c>, which is a marker throughout its supported range, making the decoded brand observable proof of delivery.</summary>
    [Fact]
    public async Task FrameOnlyItem_StillReachesItsConsumer_SoTheSameContinueDropsNothing()
    {
        const int Protocol = 762; // 1.19.4: play custom_payload still carries the brand (1.13-1.20.1 band)
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var server = new JavaConnection(pipes.Right, ServerOptions());
        server.BindCodec(new DescriptorFrameCodecBinding(version!.Protocol), PacketFlow.Serverbound);
        server.Start();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version!)
            .UseProfile(new GameProfile(Guid.NewGuid(), "FrameOnlyTester"))
            .UseConnectionFactory(new FixedPipeConnectionFactory(pipes.Left))
            .UseTickSource(new TestTickSource())
            .Build();

        List<PacketReceived> received = [];
        client.Events.Subscribe<PacketReceived>(e => { lock (received) received.Add(e); });

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

        int customPayload = ClientboundPlayWireId(version!, Identifier.Minecraft("custom_payload"));
        Assert.True(customPayload >= 0, "custom_payload has no clientbound play wire id on 762.");

        // The frame is a marker on this version, so no codec decodes it and it arrives as a frame-only item. Body: channel string, then the brand string, exactly what PlayerList sends.
        byte[] body = [.. LengthPrefixed("minecraft:brand"), .. LengthPrefixed("BundleSeamBrand")];
        await server.SendFrameAsync(customPayload, body, cts.Token);

        string? brand = null;
        for (int attempt = 0; attempt < 400 && brand is null; attempt++)
        {
            brand = await client.InvokeAsync(c => c.State.Server.Brand, cts.Token);
            if (brand is null)
                await Task.Delay(25, cts.Token);

        }

        Assert.Equal("BundleSeamBrand", brand);

        // And nothing was published as a decoded packet for it, because there is no packet: the arm carries a frame, not state, so the continue is honest rather than lossy.
        lock (received)
            Assert.DoesNotContain(received, e => e.WireId == customPayload);

    }

    private static int ClientboundPlayWireId(JavaVersion version, Identifier id)
    {
        if (!version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry))
            return -1;

        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        return -1;
    }

    private static byte[] LengthPrefixed(string value)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
        Assert.True(utf8.Length < 128, "The test strings are short enough for a single-byte VarInt length.");
        return [(byte)utf8.Length, .. utf8];
    }
}
