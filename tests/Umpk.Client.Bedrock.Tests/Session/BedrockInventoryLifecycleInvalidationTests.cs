using System.Net;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockInventoryLifecycleInvalidationTests
{
    private static BedrockItemInstance Diamond(byte count, int stackId) =>
        new(306, count, 0, stackId, 0, []);

    private static BedrockItemInstance Air() =>
        new(0, 0, 0, null, 0, []);

    private static BedrockContainerAddress PlayerContentAddress() =>
        new(0, 0, null);

    private static BedrockInventoryContentPacket PlayerContent(params BedrockItemInstance[] items) =>
        new(0, items, PlayerContentAddress(), Air());

    private static BedrockItemInstance[] HotbarWithDiamond(byte count, int stackId)
    {
        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(count, stackId);
        return items;
    }

    [Fact]
    public async Task PendingRequestAndCoordinatorDoNotCrossRespawn()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        int storeGenerationBefore = session.InventoryStore.Generation;
        int trackerGenerationBefore = session.ItemStackTracker.Generation;

        session.HandleRespawn();

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.InventoryStore.Generation > storeGenerationBefore);
        Assert.True(session.ItemStackTracker.Generation > trackerGenerationBefore);
        Assert.Equal(session.InventoryStore.Generation, session.ItemStackTracker.Generation);
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task PendingRequestAndCoordinatorDoNotCrossWorldReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        int actorGenerationBefore = session.Snapshot.ActorWorldGeneration;
        int terrainGenerationBefore = session.TerrainWorldGeneration;
        int storeGenerationBefore = session.InventoryStore.Generation;

        session.HandleWorldReplacement();

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.Snapshot.ActorWorldGeneration > actorGenerationBefore);
        Assert.True(session.TerrainWorldGeneration > terrainGenerationBefore);
        Assert.True(session.InventoryStore.Generation > storeGenerationBefore);
        Assert.Equal(session.InventoryStore.Generation, session.ItemStackTracker.Generation);
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task DimensionChangePreservesPlayerInventoryButClearsDynamicContainer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        var dynamicAddress = new BedrockContainerAddress(1, 12, 7u);
        await peer.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L)), ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(1, [Diamond(2, 9)], dynamicAddress, Air())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(dynamicAddress) is not null, ct);
        await WaitUntilAsync(() => session.ContainerWindows.GetWindow(1) is not null, ct);
        int storeGenerationBefore = session.InventoryStore.Generation;

        session.HandleWorldReplacement();

        Assert.True(session.InventoryStore.Generation > storeGenerationBefore);
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.Equal(5, session.InventoryStore.GetSnapshot(PlayerContentAddress())!.Value.Items[0].Count);
        Assert.Null(session.InventoryStore.GetSnapshot(dynamicAddress));
        Assert.Null(session.ContainerWindows.GetWindow(1));
        Assert.Empty(session.GetInventoryWindowAddresses(1));
        Assert.Single(session.GetInventoryWindowAddresses(0));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task LateAcceptedResponseAfterWorldReplacementIsUnknownWithoutMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        session.HandleWorldReplacement();

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(28, null, 1));
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task LateErrorResponseAfterWorldReplacementIsUnknownWithoutMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        session.HandleWorldReplacement();

        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(59, submit.RequestId, false, [])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.RejectedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task RepeatedLifecycleTransitionsAdvanceGenerationsAndRetireEgress()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        int storeGeneration = session.InventoryStore.Generation;
        int actorGeneration = session.Snapshot.ActorWorldGeneration;

        session.HandleRespawn();
        Assert.True(session.InventoryStore.Generation > storeGeneration);
        storeGeneration = session.InventoryStore.Generation;

        session.HandleRespawn();
        Assert.True(session.InventoryStore.Generation > storeGeneration);
        storeGeneration = session.InventoryStore.Generation;

        session.HandleWorldReplacement();
        Assert.True(session.InventoryStore.Generation > storeGeneration);
        Assert.True(session.Snapshot.ActorWorldGeneration > actorGeneration);
        storeGeneration = session.InventoryStore.Generation;
        actorGeneration = session.Snapshot.ActorWorldGeneration;

        session.HandleWorldReplacement();
        Assert.True(session.InventoryStore.Generation > storeGeneration);
        Assert.True(session.Snapshot.ActorWorldGeneration > actorGeneration);

        Assert.Equal(session.InventoryStore.Generation, session.ItemStackTracker.Generation);
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(4, 11))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is { Items.Length: 36 } snapshot && snapshot.Items[0].Count == 4, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task RepeatedDisconnectAndDisposeStayIdempotent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);
        int generationBefore = session.InventoryStore.Generation;

        await session.DisconnectAsync(ct);
        int generationAfterFirst = session.InventoryStore.Generation;
        Assert.Equal(generationBefore + 1, generationAfterFirst);

        await session.DisconnectAsync(ct);
        Assert.Equal(generationAfterFirst, session.InventoryStore.Generation);
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.Equal(0, session.InventoryWindowCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);

        await session.DisposeAsync();
        Assert.Equal(generationAfterFirst, session.InventoryStore.Generation);
        await client.DisposeAsync();
    }

    [Fact]
    public void StaleGenerationBatchAppliesNothing()
    {
        var tracker = new BedrockItemStackRequestTracker();
        int oldGeneration = tracker.Generation;
        Assert.True(tracker.TryRegister(-1, oldGeneration));
        tracker.Invalidate();
        int newGeneration = tracker.Generation;
        Assert.True(newGeneration > oldGeneration);

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        IReadOnlyList<BedrockItemStackResponseOutcome> outcomes = tracker.ApplyResponses([new BedrockItemStackResponse(0, -1, true, [container])], oldGeneration);

        Assert.Equal(BedrockItemStackResponseOutcome.StaleGeneration, outcomes[0]);
        Assert.Equal(1, tracker.StaleCount);
        Assert.Equal(0, tracker.AcceptedCount);
        Assert.Equal(0, tracker.BaselineCount);
        Assert.Null(tracker.GetBaseline(28, null, 1));

        IReadOnlyList<BedrockItemStackResponseOutcome> fresh = tracker.ApplyResponses([new BedrockItemStackResponse(0, -1, true, [container])], newGeneration);
        Assert.Equal(BedrockItemStackResponseOutcome.UnknownRequest, fresh[0]);
        Assert.Equal(0, tracker.AcceptedCount);
        Assert.Equal(0, tracker.BaselineCount);
    }

    [Fact]
    public async Task OldAcceptedResponseCannotCompleteNewRequestAfterWorldReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult old = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        session.HandleWorldReplacement();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, old.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(28, null, 1));
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task OldRejectedResponseCannotCompleteNewRequestAfterWorldReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult old = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        session.HandleWorldReplacement();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(59, old.RequestId, false, [])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.RejectedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task OldAcceptedResponseCannotCompleteNewRequestAfterRespawn()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(HotbarWithDiamond(5, 7))), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult old = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        session.HandleRespawn();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, old.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(28, null, 1));
        Assert.NotNull(session.InventoryStore.GetSnapshot(PlayerContentAddress()));
        Assert.False(session.Completion.IsCompleted);
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
        for (int attempt = 0; attempt < 16; attempt++)
        {
            (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
            if (packetId == expectedId)
                return (packetId, payload);
        }

        throw new InvalidOperationException($"The fake peer did not emit packet {expectedId}.");
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
