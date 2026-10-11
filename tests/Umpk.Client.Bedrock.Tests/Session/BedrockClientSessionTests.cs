using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockClientSessionTests
{
    [Fact]
    public async Task OfflineJoinWithoutEncryptionReachesReady()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        var observedSends = new List<int>();
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        (int loginId, byte[] loginPayload) = await peer.ReceiveAsync(ct);
        Assert.Equal(1, loginId);
        LoginPacket login = BedrockBootstrapCodecs.DecodeLogin(loginPayload);
        Assert.Equal(2169, login.ProtocolVersion);
        Assert.NotEmpty(login.IdentityToken);
        Assert.NotEmpty(login.ClientToken);
        observedSends.AddRange([193, 1]);

        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.LoginSuccess)), ct);
        await peer.SendAsync(6, Join(helpers => helpers.EmptyResourceInfo()), ct);
        Assert.Equal(ResourcePackResponseStatus.Completed, await ExpectResourceResponseAsync(peer, ct));
        (int cacheId, byte[] cachePayload) = await peer.ReceiveAsync(ct);
        Assert.Equal(129, cacheId);
        Assert.False(BedrockBootstrapCodecs.DecodeClientCacheStatus(cachePayload).Enabled);
        (int radiusId, byte[] radiusPayload) = await peer.ReceiveAsync(ct);
        Assert.Equal(69, radiusId);
        Assert.Equal(4, BedrockBootstrapCodecs.DecodeRequestChunkRadius(radiusPayload).Radius);
        observedSends.AddRange([8, 129, 69]);

        await peer.SendAsync(7, Join(helpers => helpers.EmptyResourceStack()), ct);
        Assert.Equal(ResourcePackResponseStatus.Completed, await ExpectResourceResponseAsync(peer, ct));
        observedSends.Add(8);

        await peer.SendBatchAsync(
        [
            (162, ItemRegistry()),
            (119, EntityIdentifiers()),
            (122, Join(helpers => helpers.BiomeList())),
            (11, Join(helpers => helpers.StartGame())),
        ], ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        (int initializedId, byte[] initializedPayload) = await peer.ReceiveAsync(ct);
        Assert.Equal(113, initializedId);
        Assert.Equal(789UL, BedrockBootstrapCodecs.DecodeSetLocalPlayerAsInitialized(initializedPayload).RuntimeEntityId);
        observedSends.Add(113);

        await peer.SendAsync(58, LevelChunk(), ct);
        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.True(snapshot is
        {
            TransportConnected: true,
            CompressionInstalled: true,
            LoginSent: true,
            IdentityAccepted: true,
            ResourcesComplete: true,
            WorldKnown: true,
            SpawnAuthorized: true,
            ItemRegistryAvailable: true,
            BiomeRegistryAvailable: true,
            EntityRegistryAvailable: true,
            LocalPlayerInitializationSent: true,
        });
        Assert.Equal(456L, snapshot.LocalEntityId);
        Assert.Equal(789UL, snapshot.LocalRuntimeEntityId);
        Assert.Equal(1, snapshot.LevelChunksObserved);
        Assert.Equal([193, 1, 8, 129, 69, 8, 113], observedSends);
    }

    [Fact]
    public async Task EncryptedJoinDecryptsClientHandshakeBeforeReady()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        (_, byte[] loginPayload) = await peer.ReceiveAsync(ct);
        LoginPacket login = BedrockBootstrapCodecs.DecodeLogin(loginPayload);

        using var serverKey = BedrockConnectionKey.Create();
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        string token = BedrockHandshakeTestFactory.CreateServerToken(serverKey, salt);
        await peer.SendAsync(3, BedrockBootstrapCodecs.Encode(new ServerToClientHandshakePacket(token)), ct);
        byte[] peerSecret = serverKey.DeriveEncryptionKey(ReadClientPublicKey(login), salt);
        try
        {
            using var peerSend = new BedrockEncryptionState(peerSecret, peerSecret);
            using var peerReceive = new BedrockEncryptionState(peerSecret, peerSecret);
            await peer.EnableEncryptionAsync(peerSend, peerReceive, ct);

            (int handshakeId, byte[] handshakePayload) = await peer.ReceiveAsync(ct);
            Assert.Equal(4, handshakeId);
            Assert.Empty(handshakePayload);

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
            (int initializedId, _) = await peer.ReceiveAsync(ct);
            Assert.Equal(113, initializedId);
            await peer.SendAsync(58, LevelChunk(), ct);

            BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

            Assert.True(snapshot.IsReady);
            Assert.True(snapshot.EncryptionInstalled);
            Assert.True(snapshot.LoginSent);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(peerSecret);
        }
    }

    [Fact]
    public async Task AmbientPreSpawnTrafficDoesNotDisturbReady()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToBootstrapStreamingAsync(peer, ct);
        await peer.SendBatchAsync([(124, [0x00]), (63, [0x01]), (45, BedrockRespawnCodecs.Encode(new BedrockRespawnPacket(new BedrockVector3(0.5f, 32768f, 0.5f), 0, 0UL)))], ct);
        await peer.SendAsync(11, Join(helpers => helpers.StartGame()), ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        (int initializedId, _) = await peer.ReceiveAsync(ct);
        Assert.Equal(113, initializedId);
        await peer.SendAsync(58, LevelChunk(), ct);

        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 124);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 45);
        Assert.DoesNotContain(session.PostJoinPacketIds, static id => id is 124 or 63 or 45);
    }

    [Fact]
    public async Task WorldAmbientLevelEventBeforeReadyDoesNotDisturbJoin()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToBootstrapStreamingAsync(peer, ct);
        await peer.SendAsync(25, LevelEvent(), ct);
        await peer.SendAsync(11, Join(helpers => helpers.StartGame()), ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        (int initializedId, _) = await peer.ReceiveAsync(ct);
        Assert.Equal(113, initializedId);
        await peer.SendAsync(58, LevelChunk(), ct);

        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 25);
        Assert.DoesNotContain(session.PostJoinPacketIds, static id => id is 25);
    }

    [Fact]
    public async Task SpawnBeforeStartGameSendsSingleInitialization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToBootstrapStreamingAsync(peer, ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        await peer.SendAsync(11, Join(helpers => helpers.StartGame()), ct);
        (int initializedId, _) = await peer.ReceiveAsync(ct);
        Assert.Equal(113, initializedId);
        await peer.SendAsync(58, LevelChunk(), ct);

        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.True(snapshot.SpawnAuthorized);
        Assert.True(snapshot.WorldKnown);
    }

    [Fact]
    public async Task StartGameBeforeResourceCompletionFailsSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.SendAsync(11, Join(helpers => helpers.StartGame()), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, exception.Termination.Reason);
    }

    [Fact]
    public async Task DuplicateNetworkSettingsFailsSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, exception.Termination.Reason);
    }

    [Fact]
    public async Task DisconnectDuringBootstrapPreservesRemoteReason()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToLoginSentAsync(peer, ct);
        await peer.SendAsync(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, false, "server full", "server full")), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.RemoteDisconnect, exception.Termination.Reason);
        Assert.Contains("server full", exception.Termination.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedCompressionAlgorithmFailsSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, BedrockBootstrapCodecs.Encode(new NetworkSettingsPacket(1, BedrockCompressionAlgorithm.Snappy, false, 0, 0f)), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, exception.Termination.Reason);
    }

    [Fact]
    public async Task RequiredPacksWithDownloadsDisabledFailsSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToLoginSentAsync(peer, ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.LoginSuccess)), ct);
        await peer.SendAsync(6, Join(helpers => helpers.RequiredResourceInfo()), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, exception.Termination.Reason);
    }

    [Fact]
    public async Task NetworkSettingsTimeoutReportsExpiredPhase()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        BedrockClient client = CreateClient(pair, BedrockClientTimeouts.Default with { NetworkSettings = TimeSpan.FromMilliseconds(200) });
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.Timeout, exception.Termination.Reason);
        Assert.Contains("network-settings", exception.Termination.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonIPv4EndpointIsRejectedBeforeDial()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = timeout.Token;
        bool dialCalled = false;
        var client = new BedrockClient(
            new BedrockClientSettings
            {
                Host = "::1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("Player", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            },
            (endpoint, _) =>
            {
                dialCalled = true;
                return ValueTask.FromResult<IMessageConnection>(null!);
            });

        await Assert.ThrowsAsync<NotSupportedException>(() => client.ConnectAsync(ct).AsTask());

        Assert.False(dialCalled);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NonzeroSubclientInboundTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        SendRoutedEnvelope(pair.Second, 2, 1, 0, []);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task CancelledReadyWaitKeepsSessionAlive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        using var waitTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WaitUntilReadyAsync(waitTimeout.Token).AsTask());
        Assert.False(session.Completion.IsCompleted);

        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.Snapshot.IsReady);
    }

    [Fact]
    public async Task DisposeCompletesSessionAsLocalDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        BedrockClient client = CreateClient(pair);
        BedrockClientSession session = await client.ConnectAsync(ct);

        await session.DisposeAsync();

        BedrockTermination termination = await session.Completion;
        Assert.Equal(BedrockSessionEndReason.LocalDisconnect, termination.Reason);
    }

    [Fact]
    public async Task ReconnectAfterFailureStartsFreshSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair first = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer firstPeer = BedrockFakePeer.Create(first.Second);
        BedrockClient client = CreateClient(first);
        await using BedrockClientSession failed = await client.ConnectAsync(ct);
        await ExpectPacketAsync(firstPeer, 193, ct);
        await firstPeer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await firstPeer.SendAsync(11, Join(helpers => helpers.StartGame()), ct);
        await Assert.ThrowsAsync<BedrockSessionException>(() => failed.WaitUntilReadyAsync(ct).AsTask());
        await failed.DisposeAsync();

        await using TestMessageConnectionPair second = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer secondPeer = BedrockFakePeer.Create(second.Second);
        BedrockClient secondClient = CreateClient(second);
        await using BedrockClientSession session = await secondClient.ConnectAsync(ct);
        await CompleteJoinAsync(secondPeer, session, ct);

        Assert.True(session.Snapshot.IsReady);
        await secondClient.DisposeAsync();
    }

    [Fact]
    public async Task SameBatchChunkDoesNotAcceptInitialization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await DriveToBootstrapStreamingAsync(peer, ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        await peer.SendBatchAsync(
        [
            (11, Join(helpers => helpers.StartGame())),
            (58, LevelChunk()),
        ], ct);
        (int initializedId, _) = await peer.ReceiveAsync(ct);
        Assert.Equal(113, initializedId);

        await WaitUntilAsync(() => session.Snapshot.LevelChunksObserved == 1, ct);
        await Task.Delay(500, ct);
        Assert.False(session.Snapshot.LocalPlayerInitializationAccepted);
        Assert.False(session.Snapshot.IsReady);

        await peer.SendAsync(58, LevelChunk(), ct);
        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.Equal(2, snapshot.LevelChunksObserved);
    }

    [Fact]
    public async Task PostJoinPacketIdsStayBounded()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        Assert.True(session.Snapshot.IsReady);

        var members = new List<(int PacketId, byte[] Payload)>(3000);
        for (int index = 0; index < 3000; index++)
            members.Add((124, [0x00]));
        await peer.SendBatchAsync(members, ct);
        await peer.SendBatchAsync(members, ct);

        await WaitUntilAsync(() => session.PostJoinPacketIds.Count == 4096, ct);
        await Task.Delay(500, ct);
        Assert.Equal(4096, session.PostJoinPacketIds.Count);
    }

    [Fact]
    public async Task PostJoinUpdateBlockDoesNotTerminateSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);
        Assert.True(session.Snapshot.IsReady);

        await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(10, 64, -20, 42u, 2u, 0u)), ct);
        await WaitUntilAsync(() => session.PostJoinPacketIds.Contains(21), ct);

        Assert.True(session.Snapshot.IsReady);
        Assert.Contains(21, session.PostJoinPacketIds);
    }

    [Fact]
    public async Task RejectedJoinSendIsCountedAndNotTranscribed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair(1);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);

        BedrockSessionException exception = await Assert.ThrowsAsync<BedrockSessionException>(() => session.WaitUntilReadyAsync(ct).AsTask());

        Assert.Equal(BedrockSessionEndReason.TransportFailure, exception.Termination.Reason);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 193);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 143);
        Assert.DoesNotContain(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 1);
        Assert.Equal(1, session.RejectedSendAttempts);
    }

    [Fact]
    public async Task AdmittedJoinSendsAppearInWireTranscript()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.Snapshot.IsReady);
        Assert.Equal(0, session.RejectedSendAttempts);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 113);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 1);
    }

    private static async ValueTask WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    private static BedrockClient CreateClient(TestMessageConnectionPair pair, BedrockClientTimeouts? timeouts = null)
    {
        return new BedrockClient(
            new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("TestPlayer", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                Timeouts = timeouts ?? BedrockClientTimeouts.Default,
            },
            (endpoint, _) =>
            {
                Assert.Equal(IPAddress.Loopback, endpoint.Address);
                Assert.Equal(19132, endpoint.Port);
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

    private static async ValueTask DriveToLoginSentAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        await peer.ReceiveAsync(ct);
    }

    private static async ValueTask DriveToBootstrapStreamingAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        await DriveToLoginSentAsync(peer, ct);
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
        ], ct);
    }

    private static async ValueTask<(int PacketId, byte[] Payload)> ExpectPacketAsync(BedrockFakePeer peer, int expectedId, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(expectedId, packetId);
        return (packetId, payload);
    }

    private static async ValueTask<ResourcePackResponseStatus> ExpectResourceResponseAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(8, packetId);
        ResourcePackClientResponsePacket response = BedrockJoinCodecs.DecodeResourcePackClientResponse(payload);
        Assert.Empty(response.PackIds);
        return response.Status;
    }

    private static void SendRoutedEnvelope(IMessageConnection transport, int packetId, int sender, int target, byte[] payload)
    {
        var body = new System.IO.MemoryStream();
        uint headerValue = checked((uint)(packetId | (sender << 10) | (target << 12)));
        WriteVarInt(body, headerValue);
        body.Write(payload);
        byte[] member = body.ToArray();
        using var batch = new System.IO.MemoryStream();
        WriteVarInt(batch, checked((uint)member.Length));
        batch.Write(member);
        byte[] envelope = [BedrockEnvelopeCodec.Marker, .. batch.ToArray()];
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(envelope.Length);
        envelope.CopyTo(owner.Memory.Span);
        ValueTask<MessageAdmissionResult> send = transport.SendAsync(new OwnedMessage(owner, envelope.Length));
        Assert.True(send.IsCompletedSuccessfully);
        Assert.Equal(MessageAdmissionStatus.Accepted, send.Result.Status);

        static void WriteVarInt(System.IO.MemoryStream output, uint value)
        {
            while (value >= 0x80)
            {
                output.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            output.WriteByte((byte)value);
        }
    }

    private static byte[] ReadClientPublicKey(LoginPacket login)
    {
        using System.Text.Json.JsonDocument envelope = System.Text.Json.JsonDocument.Parse(login.IdentityToken);
        string token = envelope.RootElement.GetProperty("Token").GetString()!;
        using System.Text.Json.JsonDocument claims = BedrockLoginArtifactTestFactory.DecodePayload(token);
        return Convert.FromBase64String(claims.RootElement.GetProperty("cpk").GetString()!);
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

    private static byte[] LevelEvent()
    {
        using var output = new System.IO.MemoryStream();
        WriteZigZag32(output, 3601);
        WriteFloat32Le(output, 0f);
        WriteFloat32Le(output, 64f);
        WriteFloat32Le(output, 0f);
        WriteZigZag32(output, 5);
        return output.ToArray();

        static void WriteZigZag32(System.IO.MemoryStream stream, int value)
        {
            uint encoded = (uint)((value << 1) ^ (value >> 31));
            WriteVarInt(stream, encoded);
        }

        static void WriteVarInt(System.IO.MemoryStream stream, uint value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        static void WriteFloat32Le(System.IO.MemoryStream stream, float value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BitConverter.TryWriteBytes(scratch, value);
            if (!BitConverter.IsLittleEndian)
                scratch.Reverse();
            foreach (byte octet in scratch)
                stream.WriteByte(octet);
        }
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

        internal byte[] RequiredResourceInfo() =>
            BedrockJoinCodecs.Encode(new ResourcePacksInfoPacket(
                true, false, false, false, Guid.Empty, string.Empty,
                [new BedrockTexturePackInfo(Guid.NewGuid(), "1.0.0", 16UL, string.Empty, string.Empty, string.Empty, false, false, false, string.Empty)]));

        internal byte[] EmptyResourceStack() =>
            BedrockJoinCodecs.Encode(new ResourcePackStackPacket(false, [], string.Empty, [], false, false));

        internal byte[] BiomeList() =>
            BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3]));

        internal byte[] StartGame() =>
            BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(FakeTrailer())));

        internal static BedrockStartGameTrailer FakeTrailer() =>
            new(
                1,
                new BedrockVector3(0.5f, 70f, 0.5f),
                0f,
                0f,
                99UL,
                0,
                "minecraft:plains",
                0,
                1,
                0,
                false,
                1,
                new BedrockStartGamePosition(0, 70, 0),
                false,
                0,
                false,
                false,
                0,
                0U,
                false,
                string.Empty,
                0f,
                0f,
                false,
                true,
                false,
                0U,
                0U,
                false,
                false,
                [],
                [],
                false,
                false,
                false,
                1,
                4,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                "*",
                16,
                16,
                false,
                string.Empty,
                string.Empty,
                false,
                0,
                false,
                0,
                false,
                "test-level",
                "test-world",
                "00000000-0000-0000-0000-000000000000",
                false,
                0,
                false,
                0L,
                0,
                [],
                "correlation",
                false,
                "engine",
                new NbtCompound(),
                0UL,
                Guid.Empty,
                false,
                false,
                false,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
    }
}
