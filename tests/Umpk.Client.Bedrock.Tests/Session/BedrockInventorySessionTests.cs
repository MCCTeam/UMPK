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

public sealed class BedrockInventorySessionTests
{
    [Fact]
    public async Task InventoryContentAppliesAuthoritativeSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockContainerAddress(0, 12, null);
        var packet = new BedrockInventoryContentPacket(0, [StoneItem(5), AirItem()], address, AirItem());
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(address) is not null, ct);

        BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(address)!.Value;
        Assert.Equal(2, snapshot.Items.Length);
        Assert.Equal(5, snapshot.Items[0].Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task InventorySlotUpdatesResolvedWindowContainer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockContainerAddress(0, 12, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [AirItem(), AirItem()], address, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(address) is not null, ct);
        await peer.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 1, null, null, StoneItem(3))), ct);

        await WaitUntilAsync(() =>
            session.InventoryStore.GetSnapshot(address) is { Items.Length: 2 } snapshot && snapshot.Items[1].Count == 3, ct);

        Assert.Equal(0, session.InventoryStore.GetSnapshot(address)!.Value.Items[0].Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task MalformedInventoryContentTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(49, [0x00, 0x02, 0x01], ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Equal(BedrockSessionScope.Session, termination.Scope);
    }

    [Fact]
    public async Task SlotWithoutContainerWithMultipleWindowAddressesIsUnavailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var first = new BedrockContainerAddress(0, 12, null);
        var second = new BedrockContainerAddress(0, 29, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [StoneItem(1)], first, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(first) is not null, ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [StoneItem(2)], second, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(second) is not null, ct);
        await peer.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 0, null, null, StoneItem(9))), ct);

        await WaitUntilAsync(() => session.UnresolvedInventorySlots == 1, ct);

        Assert.Equal(2, session.GetInventoryWindowAddresses(0).Count);
        Assert.Equal(1, session.InventoryStore.GetSnapshot(first)!.Value.Items[0].Count);
        Assert.Equal(2, session.InventoryStore.GetSnapshot(second)!.Value.Items[0].Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task ExplicitContainerSlotUpdatesOneAddressOnly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var first = new BedrockContainerAddress(0, 12, null);
        var second = new BedrockContainerAddress(0, 29, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [AirItem()], first, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(first) is not null, ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [AirItem()], second, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(second) is not null, ct);
        await peer.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 0, second, null, StoneItem(7))), ct);

        await WaitUntilAsync(() =>
            session.InventoryStore.GetSnapshot(second) is { Items.Length: 1 } snapshot && snapshot.Items[0].Count == 7, ct);

        Assert.Equal(0, session.InventoryStore.GetSnapshot(first)!.Value.Items[0].Count);
        Assert.Equal(0, session.UnresolvedInventorySlots);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SlotForUnknownWindowIsUnavailableWithoutTermination()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(9, 0, null, null, AirItem())), ct);

        await WaitUntilAsync(() => session.UnresolvedInventorySlots == 1, ct);

        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.False(session.Completion.IsCompleted);
        Assert.Null(session.Snapshot.Termination);
    }

    [Fact]
    public async Task DisconnectInvalidatesInventoryAndWindowMappings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockContainerAddress(0, 12, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [StoneItem(5)], address, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(address) is not null, ct);
        int generationBefore = session.InventoryStore.Generation;
        Assert.Equal(1, session.InventoryWindowCount);

        await session.DisconnectAsync(ct);

        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.Equal(0, session.InventoryWindowCount);
        Assert.True(session.InventoryStore.Generation > generationBefore);
        Assert.Null(session.InventoryStore.GetSnapshot(address));
    }

    [Fact]
    public async Task DisposeInvalidatesInventoryAndWindowMappings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockContainerAddress(0, 12, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [StoneItem(5)], address, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(address) is not null, ct);

        await session.DisposeAsync();

        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.Equal(0, session.InventoryWindowCount);
        Assert.Null(session.InventoryStore.GetSnapshot(address));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectFollowedByDisposeKeepsSingleGenerationAndClearsUnresolvedSlots()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var address = new BedrockContainerAddress(0, 12, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [StoneItem(5)], address, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(address) is not null, ct);
        await peer.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(9, 0, null, null, AirItem())), ct);
        await WaitUntilAsync(() => session.UnresolvedInventorySlots == 1, ct);
        int generationBefore = session.InventoryStore.Generation;

        await session.DisconnectAsync(ct);
        int generationAfterDisconnect = session.InventoryStore.Generation;

        Assert.Equal(generationBefore + 1, generationAfterDisconnect);
        Assert.Equal(0, session.InventoryWindowCount);
        Assert.Equal(0, session.UnresolvedInventorySlots);
        Assert.Equal(0, session.InventoryStore.ContainerCount);

        await session.DisposeAsync();

        Assert.Equal(generationAfterDisconnect, session.InventoryStore.Generation);
        Assert.Equal(0, session.InventoryWindowCount);
        Assert.Equal(0, session.UnresolvedInventorySlots);
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NinthDistinctAddressIsRejectedWithoutRetainingIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        for (byte containerId = 0; containerId < 8; containerId++)
        {
            var address = new BedrockContainerAddress(0, containerId, null);
            await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [AirItem()], address, AirItem())), ct);
            byte captured = containerId;
            await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(new BedrockContainerAddress(0, captured, null)) is not null, ct);
        }

        Assert.Equal(8, session.GetInventoryWindowAddresses(0).Count);

        var ninth = new BedrockContainerAddress(0, 8, null);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, [AirItem()], ninth, AirItem())), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Empty(session.GetInventoryWindowAddresses(0));
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.PendingItemStackReconciliationCount);
        Assert.DoesNotContain(ninth, session.GetInventoryWindowAddresses(0));
    }

    [Fact]
    public async Task ContainerOpenRetainsLifecycleSeparateFromContainerAddress()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(1, 0, 10, 64, -20, -1L)), ct);

        await WaitUntilAsync(() => session.ContainerWindows.GetWindow(1) is not null, ct);

        BedrockOpenContainer open = session.ContainerWindows.GetWindow(1)!.Value;
        Assert.Equal(0, open.WindowType);
        Assert.Equal(10, open.X);
        Assert.Equal(64, open.Y);
        Assert.Equal(-20, open.Z);
        Assert.Equal(-1L, open.EntityUniqueId);
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.Empty(session.GetInventoryWindowAddresses(1));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task ContainerCloseRemovesEveryAddressForTheAddressedWindowOnly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var first = new BedrockContainerAddress(1, 12, null);
        var second = new BedrockContainerAddress(1, 29, null);
        var other = new BedrockContainerAddress(2, 12, null);
        await peer.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L)), ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(1, [StoneItem(1)], first, AirItem())), ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(1, [StoneItem(2)], second, AirItem())), ct);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(2, [StoneItem(3)], other, AirItem())), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(other) is not null, ct);
        await WaitUntilAsync(() => session.ContainerWindows.GetWindow(1) is not null, ct);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(1, 0, true)), ct);

        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(first) is null && session.ContainerWindows.GetWindow(1) is null, ct);

        Assert.Null(session.InventoryStore.GetSnapshot(second));
        Assert.Empty(session.GetInventoryWindowAddresses(1));
        Assert.NotNull(session.InventoryStore.GetSnapshot(other));
        Assert.Single(session.GetInventoryWindowAddresses(2));
        Assert.Equal(0, session.DuplicateContainerCloses);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task DuplicateAndUnknownCloseAreCountedWithoutTermination()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(9, 0, true)), ct);
        await WaitUntilAsync(() => session.DuplicateContainerCloses == 1, ct);

        await peer.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L)), ct);
        await WaitUntilAsync(() => session.ContainerWindows.GetWindow(1) is not null, ct);
        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(1, 0, false)), ct);
        await WaitUntilAsync(() => session.ContainerWindows.GetWindow(1) is null, ct);
        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(1, 0, false)), ct);
        await WaitUntilAsync(() => session.DuplicateContainerCloses == 2, ct);

        Assert.False(session.Completion.IsCompleted);
        Assert.Null(session.Snapshot.Termination);
    }

    [Fact]
    public async Task PlayerHotbarPreservesRawFieldsWithoutInventoryEffects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(48, BedrockInventoryCodecs.Encode(new BedrockPlayerHotbarPacket(2, 0, true)), ct);

        await WaitUntilAsync(() => session.HotbarObservationCount == 1, ct);

        Assert.Equal(2, session.LastHotbar!.SelectedSlot);
        Assert.Equal(0, session.LastHotbar!.WindowId);
        Assert.True(session.LastHotbar!.SelectSlot);
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task InventoryTransactionRetainsObservationWithoutMutatingState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.Normal, [], null);
        await peer.SendAsync(30, BedrockInventoryTransactionCodecs.Encode(packet), ct);

        await WaitUntilAsync(() => session.InventoryTransactionObservationCount == 1, ct);

        Assert.Equal(BedrockInventoryTransactionType.Normal, session.LastInventoryTransaction!.TransactionType);
        Assert.Equal(0, session.InventoryStore.ContainerCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task MalformedInventoryTransactionTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(30, [0x00, 0x02, 0x01], ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    private static BedrockItemInstance AirItem() => new(0, 0, 0u, null, 0u, []);

    private static BedrockItemInstance StoneItem(ushort count) => new(1, count, 0u, null, 1u, []);

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
