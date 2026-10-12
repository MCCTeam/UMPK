using System.Net;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockItemStackSessionTests
{
    [Fact]
    public async Task ItemStackResponseAppliesTrackerBaseline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.ItemStackTracker.TryRegister(31, session.ItemStackTracker.Generation));
        var slot = new BedrockItemStackResponseSlot(2, 2, 4, 13, string.Empty, string.Empty, 0);
        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(12, null), [slot]);
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 31, true, [container])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);

        Assert.Equal(4, session.ItemStackTracker.GetBaseline(12, null, 2)!.Value.Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task ItemStackErrorResponseCountsWithoutBaseline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.ItemStackTracker.TryRegister(32, session.ItemStackTracker.Generation));
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(1, 32, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.RejectedCount == 1, ct);

        Assert.Null(session.ItemStackTracker.GetBaseline(12, null, 0));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task InboundItemStackRequestTerminatesOnDirectionViolation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockStackRequestSlot(new BedrockFullContainerName(12, null), 0, 7);
        var action = new BedrockItemStackAction(
            BedrockItemStackActionType.Take, 0, new BedrockTakePayload(1, address, address));
        var packet = new BedrockItemStackRequestPacket([new BedrockItemStackRequest(33, [action], [], 0)]);
        await peer.SendAsync(147, BedrockItemStackCodecs.Encode(packet), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
    }

    [Fact]
    public async Task ResponseBatchWithOverflowCommitsNothing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        for (int index = 0; index < BedrockItemStackRequestTracker.MaxBaselineEntries - 1; index++)
        {
            byte slot = (byte)(index % 256);
            byte container = (byte)(index / 256);
            Assert.True(session.ItemStackTracker.TryRegister(300000 + index, session.ItemStackTracker.Generation));
            Assert.Equal(
                BedrockItemStackResponseOutcome.Accepted,
                session.ItemStackTracker.ApplyResponse(
                    new BedrockItemStackResponse(
                        0,
                        300000 + index,
                        true,
                        [new BedrockItemStackResponseContainer(
                            new BedrockFullContainerName(container, null),
                            [new BedrockItemStackResponseSlot(slot, slot, 1, index, string.Empty, string.Empty, 0)])]),
                    session.ItemStackTracker.Generation));
        }

        Assert.True(session.ItemStackTracker.TryRegister(400001, session.ItemStackTracker.Generation));
        Assert.True(session.ItemStackTracker.TryRegister(400002, session.ItemStackTracker.Generation));
        var batch = new BedrockItemStackResponsePacket(
            [new BedrockItemStackResponse(
                0,
                400001,
                true,
                [new BedrockItemStackResponseContainer(
                    new BedrockFullContainerName(200, null),
                    [new BedrockItemStackResponseSlot(1, 1, 1, 1, string.Empty, string.Empty, 0)])]),
             new BedrockItemStackResponse(
                0,
                400002,
                true,
                [new BedrockItemStackResponseContainer(
                    new BedrockFullContainerName(201, null),
                    [new BedrockItemStackResponseSlot(2, 2, 1, 2, string.Empty, string.Empty, 0)])])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(batch), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Equal(0, session.ItemStackTracker.AcceptedCount - (BedrockItemStackRequestTracker.MaxBaselineEntries - 1));
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Equal(0, session.PendingItemStackReconciliationCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(200, null, 1));
        Assert.Null(session.ItemStackTracker.GetBaseline(201, null, 2));
    }

    [Fact]
    public async Task UnknownResponseKeepsCoordinatorClaim()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.ActionCoordinator.TryClaim(35, BedrockActionEmission.Standalone));
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 35, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.True(session.ActionCoordinator.IsClaimed(35));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task DuplicateResponseKeepsCoordinatorReleased()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.ActionCoordinator.TryClaim(36, BedrockActionEmission.Standalone));
        Assert.True(session.ItemStackTracker.TryRegister(36, session.ItemStackTracker.Generation));
        var first = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 36, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(first), ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(36), ct);
        Assert.Equal(1, session.ItemStackTracker.AcceptedCount);

        var duplicate = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 36, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(duplicate), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.DuplicateCount == 1, ct);

        Assert.False(session.ActionCoordinator.IsClaimed(36));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task PreReadyItemStackResponseIsCountedWithoutMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 77, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        await CompleteJoinAsync(peer, session, ct);

        Assert.Null(session.ItemStackTracker.GetBaseline(12, null, 0));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task AcceptedResponseReleasesCoordinatorClaim()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        Assert.True(session.ActionCoordinator.TryClaim(34, BedrockActionEmission.Standalone));
        Assert.True(session.ItemStackTracker.TryRegister(34, session.ItemStackTracker.Generation));
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 34, false, [])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(34), ct);

        Assert.Equal(1, session.ItemStackTracker.AcceptedCount);
        Assert.False(session.Completion.IsCompleted);
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
