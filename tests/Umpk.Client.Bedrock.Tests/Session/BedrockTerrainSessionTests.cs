using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.World;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockTerrainSessionTests
{
    [Fact]
    public async Task LevelChunkCommitsAndUpdateBlockAppliesLayeredEdit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, InlineChunk(), ct);

        Assert.Equal(0, session.TerrainDimension);
        Assert.False(session.TerrainHashes);
        await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(1, 5, 1, 77u, 2u, 0u)), ct);

        Assert.True(await WaitForConditionAsync(
            () => session.GetTerrainBlock(1, 5, 1) is { Availability: BedrockCellAvailability.Complete } query
                && query.Cell!.Value.Primary.RawId == 77,
            TimeSpan.FromSeconds(10),
            ct));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task MalformedLevelChunkTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, LevelChunk(), ct);

        await peer.SendAsync(58, BedrockWorldCodecs.Encode(new LevelChunkPacket(1, 1, 0, 0, null, false, [], [0xFF])), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task CacheEnabledLevelChunkTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, LevelChunk(), ct);

        await peer.SendAsync(58, BedrockWorldCodecs.Encode(new LevelChunkPacket(2, 2, 0, 0, null, true, [], [0x00])), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task UpdateBlockBeforeStartGameTerminatesSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await DriveToLoginSentAsync(peer, ct);

        await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(0, 70, 0, 9u, 2u, 0u)), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task SubChunkRequestResponseRoundTripAppliesMatchedSections()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, LevelChunk(), ct);

        (int requestId, byte[] requestPayload) = await peer.ReceiveAsync(ct);
        Assert.Equal(175, requestId);
        BedrockSubChunkRequestPacket request = BedrockSubChunkCodecs.DecodeRequest(requestPayload);
        Assert.Equal(0, request.Dimension);
        Assert.Equal(0, request.Center.X);
        Assert.Equal(8, request.Center.Y);
        Assert.Equal(0, request.Center.Z);
        Assert.Equal(24, request.Offsets.Count);
        Assert.Equal(1, session.TerrainRequestsSent);
        Assert.Contains((0, 0, 0), session.RequestedTerrainColumns);

        BedrockSubChunkOffset first = request.Offsets[0];
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(
            new BedrockChunkSection(
                request.Center.Y + first.Dy,
                [new BedrockBlockStorage(1, true, [12], new int[BedrockBlockStorage.CellCount])]));
        var entry = new BedrockSubChunkEntry(
            first,
            BedrockSubChunkResult.Success,
            section,
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);
        await peer.SendAsync(174, BedrockSubChunkCodecs.EncodeResponse(
            new BedrockSubChunkResponsePacket(false, 0, request.Center, [entry])), ct);

        Assert.True(await WaitForConditionAsync(() => session.TerrainSnapshot.SectionCount == 1, TimeSpan.FromSeconds(10), ct));
        Assert.Equal(12, session.GetTerrainBlock(0, -64, 0).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public async Task BatchedSubChunkRequestsShareOneGeneration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunksAsync(peer, session, [ColumnChunk(0, 0), ColumnChunk(7, 7)], ct);

        var requests = new List<BedrockSubChunkRequestPacket>();
        for (int index = 0; index < 2; index++)
        {
            (int requestId, byte[] requestPayload) = await peer.ReceiveAsync(ct);
            Assert.Equal(175, requestId);
            requests.Add(BedrockSubChunkCodecs.DecodeRequest(requestPayload));
        }

        Assert.Equal(2, session.TerrainRequestsSent);
        foreach (BedrockSubChunkRequestPacket request in requests)
        {
            BedrockSubChunkOffset first = request.Offsets[0];
            byte[] section = BedrockChunkPayloadCodecs.EncodeSection(
                new BedrockChunkSection(
                    request.Center.Y + first.Dy,
                    [new BedrockBlockStorage(1, true, [31], new int[BedrockBlockStorage.CellCount])]));
            var entry = new BedrockSubChunkEntry(
                first,
                BedrockSubChunkResult.Success,
                section,
                BedrockHeightMapDataType.NoData,
                null,
                BedrockHeightMapDataType.NoData,
                null,
                null);
            await peer.SendAsync(174, BedrockSubChunkCodecs.EncodeResponse(
                new BedrockSubChunkResponsePacket(false, request.Dimension, request.Center, [entry])), ct);
        }

        bool settled = await WaitForConditionAsync(() => session.TerrainSnapshot.ColumnCount == 2, TimeSpan.FromSeconds(10), ct);
        Assert.True(settled, $"columns={session.TerrainSnapshot.ColumnCount} sections={session.TerrainSnapshot.SectionCount} stale={session.TerrainStaleDropped} refused={session.TerrainApplyRefused} applied={session.TerrainApplied} dropped={session.TerrainWorkDropped} requests={session.TerrainRequestsSent} reqCenters={string.Join(";", requests.Select(static r => $"({r.Center.X},{r.Center.Y},{r.Center.Z})"))} results={string.Join(",", session.SubChunkResultCounts.Select(static kv => $"{kv.Key}:{kv.Value}"))} postJoin={string.Join(",", session.PostJoinPacketIds.GroupBy(static id => id).Select(static g => $"{g.Key}:{g.Count()}"))}");
        Assert.Equal(31, session.GetTerrainBlock(0, -64, 0).Cell!.Value.Primary.RawId);
        Assert.Equal(31, session.GetTerrainBlock(7 * 16, -64, 7 * 16).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public async Task ChunkBurstAccountsEveryQueuedObservation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair(512);
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, LevelChunk(), ct);

        const int Burst = 200;
        for (int index = 0; index < Burst; index++)
            await peer.SendAsync(58, LevelChunk(), ct);

        Assert.True(await WaitForConditionAsync(
            () => session.TerrainApplied + session.TerrainWorkDropped == Burst + 1,
            TimeSpan.FromSeconds(15),
            ct));
        Assert.Equal(0, session.TerrainStaleDropped);
        Assert.Equal(0, session.TerrainApplyRefused);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task BurstUpdateBlocksStayBoundedAndCommitInOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, InlineChunk(), ct);
        Assert.True(await WaitForConditionAsync(
            () => session.GetTerrainBlock(1, 5, 1).Availability == BedrockCellAvailability.Complete,
            TimeSpan.FromSeconds(10),
            ct));

        for (int index = 0; index < 50; index++)
            await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(900 + index, 70, 900, 3u, 2u, 0u)), ct);
        await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(1, 5, 1, 5u, 2u, 0u)), ct);
        await peer.SendAsync(21, BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(1, 5, 1, 6u, 2u, 0u)), ct);

        Assert.True(await WaitForConditionAsync(
            () => session.TerrainApplyRefused == 50
                && session.GetTerrainBlock(1, 5, 1) is { Availability: BedrockCellAvailability.Complete } query
                && query.Cell!.Value.Primary.RawId == 6,
            TimeSpan.FromSeconds(15),
            ct));
        Assert.False(session.Completion.IsCompleted);
        Assert.Equal(0, session.TerrainWorkDropped);
    }

    [Fact]
    public async Task SubChunkBlocksApplyInReceiveOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, InlineChunk(), ct);
        Assert.True(await WaitForConditionAsync(
            () => session.GetTerrainBlock(1, 5, 1).Availability == BedrockCellAvailability.Complete,
            TimeSpan.FromSeconds(10),
            ct));
        int appliedBefore = session.TerrainApplied;

        await peer.SendAsync(172, SubChunkBlocks(0, 0, 0, [(1, 5, 1, 100u)], []), ct);
        await peer.SendAsync(172, SubChunkBlocks(0, 0, 0, [(1, 5, 1, 200u)], []), ct);

        Assert.True(await WaitForConditionAsync(
            () => session.TerrainApplied == appliedBefore + 2
                && session.GetTerrainBlock(1, 5, 1) is { Availability: BedrockCellAvailability.Complete } query
                && query.Cell!.Value.Primary.RawId == 200,
            TimeSpan.FromSeconds(15),
            ct));
        Assert.Equal(0, session.TerrainApplyRefused);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SubChunkBlocksToAbsentTerrainAreRefusedWithoutTermination()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, InlineChunk(), ct);
        Assert.True(await WaitForConditionAsync(
            () => session.GetTerrainBlock(1, 5, 1).Availability == BedrockCellAvailability.Complete,
            TimeSpan.FromSeconds(10),
            ct));
        int appliedBefore = session.TerrainApplied;

        await peer.SendAsync(172, SubChunkBlocks(48, 48, 48, [(900, 70, 900, 3u), (901, 70, 900, 4u)], []), ct);

        Assert.True(await WaitForConditionAsync(() => session.TerrainApplyRefused == 2, TimeSpan.FromSeconds(15), ct));
        Assert.Equal(appliedBefore, session.TerrainApplied);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SubChunkBlocksBeforeStartGameTerminateSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await DriveToLoginSentAsync(peer, ct);

        await peer.SendAsync(172, SubChunkBlocks(0, 0, 0, [(0, 70, 0, 9u)], []), ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task MalformedSubChunkBlocksTerminateSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, InlineChunk(), ct);

        byte[] encoded = SubChunkBlocks(0, 0, 0, [(1, 5, 1, 100u)], []);
        await peer.SendAsync(172, encoded[..^1], ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
    }

    [Fact]
    public async Task DisconnectDuringTerrainBurstCompletesCleanly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinWithChunkAsync(peer, session, LevelChunk(), ct);

        for (int index = 0; index < 50; index++)
            await peer.SendAsync(58, LevelChunk(), ct);

        await session.DisconnectAsync(ct);
        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.LocalDisconnect, termination.Reason);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task AbsentTerrainNeverReadsAsAir()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

        Assert.Equal(BedrockCellAvailability.Absent, session.GetTerrainBlock(9999, 70, 9999).Availability);
        Assert.Null(session.TerrainDimension);

        await client.DisposeAsync();
    }

    private static async ValueTask CompleteJoinWithChunkAsync(BedrockFakePeer peer, BedrockClientSession session, byte[] chunk, CancellationToken ct) =>
        await CompleteJoinWithChunksAsync(peer, session, [chunk], ct).ConfigureAwait(false);

    private static byte[] ColumnChunk(int chunkX, int chunkZ) =>
        BedrockWorldCodecs.Encode(new LevelChunkPacket(chunkX, chunkZ, 0, 0, null, false, [], BiomeOnlyPayload()));

    private static async ValueTask CompleteJoinWithChunksAsync(BedrockFakePeer peer, BedrockClientSession session, IReadOnlyList<byte[]> chunks, CancellationToken ct)
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
        foreach (byte[] chunk in chunks)
            await peer.SendAsync(58, chunk, ct);
        await session.WaitUntilReadyAsync(ct);
    }

    private static async ValueTask DriveToLoginSentAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        await peer.ReceiveAsync(ct);
    }

    private static byte[] LevelChunk() =>
        BedrockWorldCodecs.Encode(new LevelChunkPacket(0, 0, 0, 0, null, false, [], BiomeOnlyPayload()));

    private static byte[] InlineChunk()
    {
        var storage = new BedrockBlockStorage(1, true, [5], new int[BedrockBlockStorage.CellCount]);
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(new BedrockChunkSection(0, [storage]));
        byte[] payload = [.. section, 0x01, 0x00, 0x00];
        return BedrockWorldCodecs.Encode(new LevelChunkPacket(0, 0, 0, 1, null, false, [], payload));
    }

    private static byte[] SubChunkBlocks(int x, int y, int z, IReadOnlyList<(int X, int Y, int Z, uint RuntimeId)> blocks, IReadOnlyList<(int X, int Y, int Z, uint RuntimeId)> extra) =>
        BedrockUpdateSubChunkBlocksCodecs.Encode(new BedrockUpdateSubChunkBlocksPacket(
            x,
            y,
            z,
            [.. blocks.Select(static change => new BedrockSubChunkBlockChange(change.X, change.Y, change.Z, change.RuntimeId, 2u, -1L, BedrockSubChunkBlockTransition.None))],
            [.. extra.Select(static change => new BedrockSubChunkBlockChange(change.X, change.Y, change.Z, change.RuntimeId, 0u, -1L, BedrockSubChunkBlockTransition.None))]));

    private static byte[] BiomeOnlyPayload()
    {
        var payload = new byte[26];
        payload[0] = 0x01;
        payload[1] = 0x00;
        Array.Fill(payload, (byte)0xFF, 2, 23);
        payload[25] = 0x00;
        return payload;
    }

    private static BedrockClient CreateClient(TestMessageConnectionPair pair) =>
        new(
            new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("TestPlayer", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                Timeouts = BedrockClientTimeouts.Default,
            },
            (endpoint, _) => ValueTask.FromResult<Umpk.Transport.IMessageConnection>(pair.First));

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

    private static byte[] Bootstrap(Func<BootstrapPackets, byte[]> encode) => encode(new BootstrapPackets());

    private static byte[] Join(Func<JoinPackets, byte[]> encode) => encode(new JoinPackets());

    private static byte[] ItemRegistry() =>
        BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket(
        [
            new BedrockItemState("minecraft:stone", 1, false, 2, new Umpk.Nbt.NbtCompound()),
        ]));

    private static byte[] EntityIdentifiers()
    {
        var pig = new Umpk.Nbt.NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var list = new Umpk.Nbt.NbtList(Umpk.Nbt.NbtTagType.Compound) { pig };
        var root = new Umpk.Nbt.NbtCompound();
        root.Put("idlist", list);
        return BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(root));
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
                new Umpk.Nbt.NbtCompound(),
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

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
                return true;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        return condition();
    }
}
