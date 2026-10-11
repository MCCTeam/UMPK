using System.Net;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Actors;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockActorSessionTests
{
    private static readonly Guid s_secondBotId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

    [Fact]
    public async Task PostJoinActorObservationsAreCoherentOnScheduler()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(12, AddPlayerPayload(), ct);
        await peer.SendAsync(13, AddEntityPayload(), ct);
        await peer.SendAsync(40, BedrockActorCodecs.Encode(new BedrockSetEntityMotionPacket(3UL, new BedrockVector3(0f, 1f, 0f), 7UL)), ct);
        await peer.SendAsync(19, BedrockActorCodecs.Encode(
            new BedrockMovePlayerPacket(2UL, new BedrockVector3(9f, 65f, 8f), 0f, 90f, 90f, BedrockMovePlayerMode.Normal, true, 0UL, null, null, 12UL)), ct);
        await peer.SendAsync(39, BedrockActorCodecs.Encode(new BedrockSetEntityDataPacket(3UL, [], BedrockEntityProperties.Empty, 8UL)), ct);
        await peer.SendAsync(326, BedrockActorCodecs.Encode(new BedrockPlayerLocationPacket(100L, false, new BedrockVector3(9f, 65f, 8f), 0)), ct);
        await peer.SendAsync(28, BedrockObservationCodecs.Encode(new BedrockMobEffectPacket(3UL, BedrockMobEffectEvent.Add, 10, 1, true, 600, 9UL, false)), ct);
        await peer.SendAsync(29, BedrockObservationCodecs.Encode(new BedrockUpdateAttributesPacket(3UL, [new BedrockPlayerAttribute(0f, 20f, 14f, 0f, 20f, 20f, "minecraft:health", [])], 10UL)), ct);
        await peer.SendAsync(31, BedrockObservationCodecs.Encode(new BedrockMobEquipmentPacket(3UL, new BedrockItemInstance(7, 1, 0U, null, 0U, []), 1, 2, 0)), ct);
        var emptyItem = new BedrockItemInstance(0, 0, 0U, null, 0U, []);
        await peer.SendAsync(32, BedrockObservationCodecs.Encode(new BedrockMobArmorEquipmentPacket(3UL, new BedrockItemInstance(8, 1, 0U, null, 0U, []), emptyItem, emptyItem, emptyItem, emptyItem)), ct);
        await peer.SendAsync(41, BedrockObservationCodecs.Encode(new BedrockSetEntityLinkPacket(new BedrockEntityLink(10L, 100L, BedrockEntityLinkType.Rider, false, true, 0f))), ct);

        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Player && actor.Position == new BedrockVector3(9f, 65f, 8f)), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Entity && actor.MetadataObserved && actor.Velocity == new BedrockVector3(0f, 1f, 0f)), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Entity && actor.Effects.Length == 1 && actor.Effects[0].EffectId == 10), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Entity && actor.Attributes.Length == 1 && actor.Attributes[0].Current == 14f), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Entity && actor.HeldItem is not null && actor.HeldItem.Item.NetworkId == 7), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Entity && actor.Armor is not null && actor.Armor.Helmet.NetworkId == 8), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.Player && actor.RidingRuntimeEntityId == 3UL), ct);

        BedrockSessionSnapshot snapshot = session.Snapshot;
        Assert.Equal(3, snapshot.ActorCount);
        Assert.True(snapshot.ActorObservationsAvailable);

        BedrockActorSnapshot local = Assert.Single(session.ActorSnapshots, static actor => actor.Kind == BedrockActorKind.LocalPlayer);
        Assert.Equal(456L, local.Handle.UniqueEntityId);
        Assert.Equal(789UL, local.Handle.RuntimeEntityId);

        BedrockActorSnapshot player = Assert.Single(session.ActorSnapshots, static actor => actor.Kind == BedrockActorKind.Player);
        Assert.Equal("SecondBot", player.DisplayName);
        Assert.Equal(s_secondBotId, player.Uuid);
        Assert.Equal(100L, player.Handle.UniqueEntityId);
        Assert.Equal(new BedrockVector3(9f, 65f, 8f), player.Position);
        Assert.Equal(12UL, player.LastServerTick);
        Assert.Equal(3UL, player.RidingRuntimeEntityId);

        BedrockActorSnapshot entity = Assert.Single(session.ActorSnapshots, static actor => actor.Kind == BedrockActorKind.Entity);
        Assert.Equal("minecraft:pig", entity.EntityType);
        Assert.Equal(new BedrockVector3(0f, 1f, 0f), entity.Velocity);
        Assert.Equal(10UL, entity.LastServerTick);
        Assert.True(entity.MetadataObserved);
        BedrockActorEffect effect = Assert.Single(entity.Effects);
        Assert.Equal(10, effect.EffectId);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(14f, Assert.Single(entity.Attributes).Current);
        Assert.NotNull(entity.HeldItem);
        Assert.Equal(7, entity.HeldItem.Item.NetworkId);
        Assert.Equal(1, entity.HeldItem.InventorySlot);
        Assert.NotNull(entity.Armor);
        Assert.Equal(8, entity.Armor.Helmet.NetworkId);

        Assert.Contains(session.ActorEvents, static evt => evt.Kind == BedrockActorEventKind.Added);
        Assert.Contains(session.ActorEvents, static evt => evt.Kind == BedrockActorEventKind.Updated);
        Assert.Contains(session.ActorWireWitnesses, static witness => witness.PacketId == 19 && witness.TotalLength > 0);
    }

    [Fact]
    public async Task ActorBeforeEntityRegistryIsCountedWithoutStateEffects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);

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
        await peer.SendAsync(162, ItemRegistry(), ct);
        await peer.SendAsync(13, AddEntityPayload(), ct);
        await peer.SendBatchAsync(
        [
            (119, EntityIdentifiers()),
            (122, Join(helpers => helpers.BiomeList())),
            (11, Join(helpers => helpers.StartGame())),
        ], ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(58, LevelChunk(), ct);
        BedrockSessionSnapshot snapshot = await session.WaitUntilReadyAsync(ct);

        Assert.True(snapshot.IsReady);
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 13);
        Assert.Single(session.ActorSnapshots);
        Assert.Equal(BedrockActorKind.LocalPlayer, session.ActorSnapshots[0].Kind);
    }

    [Fact]
    public async Task AddPlayerForLocalRuntimeUpdatesLocalShellWithoutDuplicating()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(12, AddPlayerPayload(runtimeEntityId: 789UL, uniqueEntityId: 456L, username: "TestPlayer"), ct);

        await WaitUntilAsync(() => session.ActorSnapshots.Any(static actor =>
            actor.Kind == BedrockActorKind.LocalPlayer && actor.DisplayName == "TestPlayer"), ct);
        await Task.Delay(200, ct);

        Assert.Single(session.ActorSnapshots);
        BedrockActorSnapshot local = session.ActorSnapshots[0];
        Assert.Equal(BedrockActorKind.LocalPlayer, local.Kind);
        Assert.Equal("TestPlayer", local.DisplayName);
    }

    [Fact]
    public async Task RemovalByUniqueIdCannotLeaveStaleActors()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(13, AddEntityPayload(), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Count == 2, ct);
        await peer.SendAsync(14, BedrockActorCodecs.Encode(new BedrockRemoveEntityPacket(10L)), ct);
        await WaitUntilAsync(() => session.ActorSnapshots.Count == 1, ct);

        Assert.Single(session.ActorSnapshots);
        Assert.Contains(session.ActorEvents, static evt => evt.Kind == BedrockActorEventKind.Removed);
    }

    [Fact]
    public async Task RemoveBeforeAddKeepsSessionAlive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(14, BedrockActorCodecs.Encode(new BedrockRemoveEntityPacket(4242L)), ct);

        await WaitUntilAsync(() => session.StaleActorRemovals == 1, ct);
        Assert.False(session.Completion.IsCompleted);
        Assert.Single(session.ActorSnapshots);
    }

    [Fact]
    public async Task MalformedActorPayloadTerminatesSessionClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(19, [0x01], ct);

        BedrockTermination termination = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(BedrockSessionEndReason.ProtocolFailure, termination.Reason);
        Assert.Equal(BedrockSessionScope.Session, termination.Scope);
    }

    [Fact]
    public async Task LocatorBarIsCountedWithoutInterpretation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        await peer.SendAsync(341, [0x00], ct);

        await WaitUntilAsync(() => session.PostJoinPacketIds.Contains(341), ct);
        Assert.False(session.Completion.IsCompleted);
        Assert.DoesNotContain(session.ActorWireWitnesses, static witness => witness.PacketId == 341);
    }

    private static async ValueTask WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    private static byte[] AddPlayerPayload(ulong runtimeEntityId = 2UL, long uniqueEntityId = 100L, string username = "SecondBot")
    {
        var packet = new BedrockAddPlayerPacket(
            s_secondBotId,
            username,
            runtimeEntityId,
            string.Empty,
            new BedrockVector3(8f, 65f, 8f),
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            0,
            [],
            BedrockEntityProperties.Empty,
            uniqueEntityId,
            0,
            0,
            [],
            [],
            string.Empty,
            null);
        return BedrockActorCodecs.Encode(packet);
    }

    private static byte[] AddEntityPayload() =>
        BedrockActorCodecs.Encode(new BedrockAddEntityPacket(
            10L,
            3UL,
            "minecraft:pig",
            new BedrockVector3(1f, 2f, 3f),
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            0f,
            [],
            [],
            BedrockEntityProperties.Empty,
            []));

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

internal static class ActorSessionByteExtensions
{
    internal static byte[] Concat(this byte[] first, params byte[][] rest)
    {
        int length = first.Length + rest.Sum(static part => part.Length);
        var combined = new byte[length];
        int offset = 0;
        first.CopyTo(combined, offset);
        offset += first.Length;
        foreach (byte[] part in rest)
        {
            part.CopyTo(combined, offset);
            offset += part.Length;
        }

        return combined;
    }
}
