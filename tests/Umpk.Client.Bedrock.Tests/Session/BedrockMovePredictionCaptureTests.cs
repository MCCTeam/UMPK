using System.Net;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

[Collection("Bedrock motion capture")]
public sealed class BedrockMovePredictionCaptureTests
{
    private static BedrockCorrectPlayerMovePredictionPacket Prediction(ulong tick) =>
        new(0, new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(10f, 20f), null, true, tick);

    [Fact]
    public async Task EnabledCaptureRetainsSingleDecodedPacketAndByteIdenticalPayload()
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
            await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            BedrockClient client = CreateClient(pair);
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            await CompleteJoinAsync(peer, session, ct);
            byte[] payload = BedrockPlayerAuthInputCodecs.Encode(Prediction(12UL));
            await peer.SendAsync(161, payload, ct);
            await WaitUntilAsync(() => session.MovePredictionObservationCount == 1, ct);
            Assert.Single(session.CapturedMovePredictions);
            Assert.Equal(Prediction(12UL), session.CapturedMovePredictions[0]);
            Assert.Single(session.CapturedMovePredictionPayloads);
            Assert.Equal(payload, session.CapturedMovePredictionPayloads[0]);
            Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.DecodePrediction(session.CapturedMovePredictionPayloads[0])));
            Assert.Contains(161, session.PostJoinPacketIds);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            session.ResetMotionCapture();
            Assert.Empty(session.CapturedMovePredictions);
            Assert.Empty(session.CapturedMovePredictionPayloads);
            Assert.Equal(0, session.MovePredictionObservationCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previous);
        }
    }

    [Fact]
    public async Task DisabledCaptureCountsWithoutRetaining()
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", null);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
            await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            BedrockClient client = CreateClient(pair);
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            await CompleteJoinAsync(peer, session, ct);
            await peer.SendAsync(161, BedrockPlayerAuthInputCodecs.Encode(Prediction(13UL)), ct);
            await WaitUntilAsync(() => session.MovePredictionObservationCount == 1, ct);
            Assert.Empty(session.CapturedMovePredictions);
            Assert.Empty(session.CapturedMovePredictionPayloads);
            Assert.False(session.Completion.IsCompleted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previous);
        }
    }

    [Fact]
    public async Task MalformedPredictionTerminatesSession()
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
            await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            BedrockClient client = CreateClient(pair);
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            await CompleteJoinAsync(peer, session, ct);
            byte[] payload = BedrockPlayerAuthInputCodecs.Encode(Prediction(14UL));
            byte[] truncated = payload[..^1];
            await peer.SendAsync(161, truncated, ct);
            BedrockTermination termination = await session.Completion.WaitAsync(ct);
            Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previous);
        }
    }

    [Fact]
    public async Task CaptureIsBoundedAtThirtyTwoPackets()
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
            await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
            BedrockClient client = CreateClient(pair);
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            await CompleteJoinAsync(peer, session, ct);
            for (ulong tick = 0; tick < 40; tick++)
                await peer.SendAsync(161, BedrockPlayerAuthInputCodecs.Encode(Prediction(tick)), ct);
            await WaitUntilAsync(() => session.MovePredictionObservationCount == 40, ct);
            Assert.Equal(32, session.CapturedMovePredictions.Count);
            Assert.Equal(32, session.CapturedMovePredictionPayloads.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previous);
        }
    }

    private static async ValueTask WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
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
