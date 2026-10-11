using System.Net;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockPlayerAuthInputSessionTests
{
    private static BedrockPlayerAuthInputPacket Idle(ulong tick, IReadOnlyList<int>? inputs = null) =>
        new(0f, 0f, new BedrockVector3(8.5f, 91.62001f, 8.5f), new BedrockVector2(0f, 0f), 0f, inputs, 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    [Fact]
    public async Task SubmitRequiresReady()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await ExpectPacketAsync(peer, 193, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(1UL), ct).AsTask());
        Assert.Equal(0, session.RejectedSendAttempts);
        await session.DisposeAsync();
        await peer.DisposeAsync();
    }

    [Fact]
    public async Task SubmitAdmitsPacket144WithByteIdenticalRoundTrip()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(Idle(11UL, [10]), ct);
        Assert.Equal(11UL, submit.Tick);
        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 144, ct);
        Assert.Equal(144, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(payload);
        Assert.Equal(11UL, decoded.Tick);
        Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(decoded));
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 144);
        Assert.Equal(0, session.RejectedSendAttempts);
        Assert.Equal(1, session.PlayerAuthInputSendCount);
        Assert.False(session.ActionCoordinator.IsClaimed(-1));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SubmitEnforcesStrictTickMonotonicity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await session.SubmitPlayerAuthInputAsync(Idle(5UL), ct);
        await ReceiveUntilPacketAsync(peer, 144, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(5UL), ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(4UL), ct).AsTask());
        await session.SubmitPlayerAuthInputAsync(Idle(6UL), ct);
        await ReceiveUntilPacketAsync(peer, 144, ct);
        Assert.Equal(2, session.PlayerAuthInputSendCount);
    }

    [Fact]
    public async Task SubmitHandlesTickWrapExplicitly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await session.SubmitPlayerAuthInputAsync(Idle(ulong.MaxValue), ct);
        await ReceiveUntilPacketAsync(peer, 144, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(ulong.MaxValue), ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(1UL), ct).AsTask());
        await session.SubmitPlayerAuthInputAsync(Idle(0UL), ct);
        await ReceiveUntilPacketAsync(peer, 144, ct);
        Assert.Equal(2, session.PlayerAuthInputSendCount);
    }

    [Fact]
    public async Task RejectedSubmitCountsWithoutTranscribing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await DrainPeerAsync(peer, ct);
        int successes = 0;
        bool rejected = false;
        for (ulong tick = 100; tick < 220; tick++)
        {
            try
            {
                await session.SubmitPlayerAuthInputAsync(Idle(tick), ct);
                successes++;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase))
            {
                rejected = true;
                break;
            }
        }

        Assert.True(rejected);
        Assert.Equal(1, session.RejectedSendAttempts);
        Assert.Equal(successes, session.PlayerAuthInputSendCount);
        int outbound144 = session.WireTranscript.Count(static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 144);
        Assert.Equal(successes, outbound144);
    }

    [Fact]
    public async Task RejectedTickRemainsReusableAfterCapacityRejection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await DrainPeerAsync(peer, ct);
        ulong rejectedTick = 0;
        bool rejected = false;
        for (ulong tick = 100; tick < 220; tick++)
        {
            try
            {
                await session.SubmitPlayerAuthInputAsync(Idle(tick), ct);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase))
            {
                rejectedTick = tick;
                rejected = true;
                break;
            }
        }

        Assert.True(rejected);
        Assert.Equal(1, session.RejectedSendAttempts);
        await DrainPeerAsync(peer, ct);
        BedrockPlayerAuthInputSubmitResult retry = await session.SubmitPlayerAuthInputAsync(Idle(rejectedTick), ct);
        Assert.Equal(rejectedTick, retry.Tick);
        Assert.Equal(1, session.RejectedSendAttempts);
    }

    [Fact]
    public async Task ConcurrentSubmitsArriveInTickOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair(capacity: 64);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await DrainPeerAsync(peer, ct);
        const int count = 8;
        Task<BedrockPlayerAuthInputSubmitResult>[] sends = new Task<BedrockPlayerAuthInputSubmitResult>[count];
        for (int index = 0; index < count; index++)
        {
            ulong tick = (ulong)(index + 1);
            sends[index] = session.SubmitPlayerAuthInputAsync(Idle(tick), ct).AsTask();
        }

        BedrockPlayerAuthInputSubmitResult[] results = await Task.WhenAll(sends);
        Assert.Equal(count, results.Length);
        var ticks = new List<ulong>(count);
        for (int index = 0; index < count; index++)
        {
            (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
            Assert.Equal(144, packetId);
            ticks.Add(BedrockPlayerAuthInputCodecs.Decode(payload).Tick);
        }

        Assert.Equal(Enumerable.Range(1, count).Select(static value => (ulong)value), ticks);
    }

    [Fact]
    public async Task SubmitRefusesAfterDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await session.DisconnectAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(31UL), ct).AsTask());
    }

    [Fact]
    public async Task SubmitAfterDisposeThrowsInvalidOperation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        await session.DisposeAsync();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitPlayerAuthInputAsync(Idle(32UL), ct).AsTask());
        Assert.DoesNotContain("disposed", failure.Message, StringComparison.OrdinalIgnoreCase);
        await peer.DisposeAsync();
    }

    private static async ValueTask WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    private static async ValueTask<(int PacketId, byte[] Payload)> ReceiveUntilPacketAsync(BedrockFakePeer peer, int expectedId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
            if (packetId == expectedId)
                return (packetId, payload);
        }

        throw new InvalidOperationException($"The fake peer did not deliver packet {expectedId}.");
    }

    private static async ValueTask DrainPeerAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                using var attemptTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, attemptTimeout.Token);
                await peer.ReceiveAsync(linked.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static BedrockClient CreateClient(TestMessageConnectionPair pair)
    {
        return new BedrockClient(
            new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("TestPlayer", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            },
            (endpoint, _) =>
            {
                Assert.Equal(IPAddress.Loopback, endpoint.Address);
                return ValueTask.FromResult<IMessageConnection>(pair.First);
            });
    }

    private static async ValueTask CompleteJoinAsync(BedrockFakePeer peer, BedrockClientSession session, CancellationToken ct)
    {
        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.LoginSuccess)), ct);
        await peer.SendAsync(6, Join(helpers => helpers.EmptyResourceInfo()), ct);
        await ExpectResourceResponseAsync(peer, ct);
        await peer.ReceiveAsync(ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(7, Join(helpers => helpers.EmptyResourceStack()), ct);
        await ExpectResourceResponseAsync(peer, ct);
        await peer.SendBatchAsync(
        [
            (162, ItemRegistry()),
            (119, EntityIdentifiers()),
            (122, Join(helpers => helpers.BiomeList())),
            (11, Join(helpers => helpers.StartGame())),
        ], ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(58, LevelChunk(), ct);
        await session.WaitUntilReadyAsync(ct);
    }

    private static async ValueTask<(int PacketId, byte[] Payload)> ExpectPacketAsync(BedrockFakePeer peer, int expectedId, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(expectedId, packetId);
        return (packetId, payload);
    }

    private static async ValueTask ExpectResourceResponseAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(8, packetId);
        ResourcePackClientResponsePacket response = BedrockJoinCodecs.DecodeResourcePackClientResponse(payload);
        Assert.Empty(response.PackIds);
        Assert.Equal(ResourcePackResponseStatus.Completed, response.Status);
    }

    private static byte[] Bootstrap(Func<BootstrapPackets, byte[]> encode) => encode(new BootstrapPackets());

    private static byte[] Join(Func<JoinPackets, byte[]> encode) => encode(new JoinPackets());

    private static byte[] ItemRegistry() =>
        BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket(
        [
            new BedrockItemState("minecraft:stone", 1, false, 2, new NbtCompound()),
        ]));

    private static byte[] EntityIdentifiers()
    {
        var pig = new NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var list = new NbtList(NbtTagType.Compound) { pig };
        var root = new NbtCompound();
        root.Put("idlist", list);
        return BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(root));
    }

    private static byte[] LevelChunk() =>
        BedrockWorldCodecs.Encode(new LevelChunkPacket(0, 0, 0, 0, null, false, [], BiomeOnlyPayload()));

    private static byte[] BiomeOnlyPayload()
    {
        var payload = new byte[26];
        payload[0] = 0x01;
        payload[1] = 0x00;
        Array.Fill(payload, (byte)0xFF, 2, 23);
        payload[25] = 0x00;
        return payload;
    }

    private sealed class BootstrapPackets
    {
        internal byte[] NetworkSettings() =>
            BedrockBootstrapCodecs.Encode(new NetworkSettingsPacket(1, BedrockCompressionAlgorithm.Deflate, false, 0, 0f));

        internal byte[] PlayStatus(PlayStatusCode status) =>
            BedrockBootstrapCodecs.Encode(new PlayStatusPacket(status));
    }

    private sealed class JoinPackets
    {
        internal byte[] EmptyResourceInfo() =>
            BedrockJoinCodecs.Encode(new ResourcePacksInfoPacket(false, false, false, false, Guid.Empty, string.Empty, []));

        internal byte[] EmptyResourceStack() =>
            BedrockJoinCodecs.Encode(new ResourcePackStackPacket(false, [], string.Empty, [], false, false));

        internal byte[] BiomeList() =>
            BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3]));

        internal byte[] StartGame() =>
            BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(FakeTrailer())));

        internal static BedrockStartGameTrailer FakeTrailer() =>
            new(1, new BedrockVector3(0.5f, 70f, 0.5f), 0f, 0f, 99UL, 0, "minecraft:plains", 0, 1, 0, false, 1, new BedrockStartGamePosition(0, 70, 0), false, 0, false, false, 0, 0U, false, string.Empty, 0f, 0f, false, true, false, 0U, 0U, false, false, [], [], false, false, false, 1, 4, false, false, false, false, false, false, false, false, false, false, "*", 16, 16, false, string.Empty, string.Empty, false, 0, false, 0, false, "test-level", "test-world", "00000000-0000-0000-0000-000000000000", false, 0, false, 0L, 0, [], "correlation", false, "engine", new NbtCompound(), 0UL, Guid.Empty, false, false, false, null, string.Empty, string.Empty, string.Empty, string.Empty);
    }
}
