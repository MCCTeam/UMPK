using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class JukeboxPredictionLifetimeSessionTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(751)]
    public async Task IdenticalBatchedStateRearmsBeforeBlockChangedObservers(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        BlockUseResult? duringEvent = null;
        using IDisposable subscription = session.Client.Events.Subscribe<BlockChanged>(change =>
        {
            if (change.Position == Session.Target)
                duringEvent = Prediction(session).Result;
        });
        short packed = protocol == 47 ? (short)((1 << 12) | (1 << 8) | 65) : (short)((1 << 8) | (1 << 4) | 1);
        var update = new ClientboundSectionBlocksUpdatePacket(protocol == 47 ? 0 : 4, 0, 0, [new(packed, filled)], IsLegacy: protocol == 47);
        await SendFrame(session, protocol, update, protocol == 47 ? "block_change_multi" : "section_blocks_update", decoded =>
        {
            var actual = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(decoded);
            Assert.Equal(update.IsLegacy, actual.IsLegacy);
            Assert.Equal(update.SectionPos, actual.SectionPos);
            Assert.Equal(update.LegacyChunkX, actual.LegacyChunkX);
            Assert.Equal(update.LegacyChunkZ, actual.LegacyChunkZ);
            Assert.Equal(update.Changes, actual.Changes);
            Assert.False(actual.SuppressLightUpdates);
        });
        Assert.Empty(await session.Barrier());
        Assert.Equal(BlockUseResult.Success, duringEvent);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(498)]
    [InlineData(751)]
    public async Task FullAndTouchedPartialSectionsRearmWhileUntouchedPartialPreserves(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));

        await SendChunk(session, protocol, filled, full: false, section: 3, includeRecord: false);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        await AssertUse(session, protocol, AfterAcknowledgedUse(protocol));

        BlockUseResult? duringEvent = null;
        using IDisposable subscription = session.Client.Events.Subscribe<ChunkLoaded>(_ => duringEvent = Prediction(session).Result);
        await SendChunk(session, protocol, filled, full: false, section: 4, includeRecord: true);
        Assert.Equal(BlockUseResult.Success, duringEvent);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));

        duringEvent = null;
        await SendChunk(session, protocol, filled, full: true, section: 4, includeRecord: true);
        Assert.Equal(BlockUseResult.Success, duringEvent);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(498)]
    [InlineData(761)]
    public async Task UnloadIsUnknownAndReloadedFilledColumnRearms(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        BlockUseResult? duringUnload = null;
        using IDisposable subscription = session.Client.Events.Subscribe<ChunkUnloaded>(_ => duringUnload = Prediction(session).Result);
        await session.Forget();
        Assert.Equal(BlockUseResult.Unknown, duringUnload);
        Assert.Null(session.Client.State.World.GetColumn(Session.Target));
        await AssertPrediction(session, BlockUseResult.Unknown);
        await AssertUse(session, protocol, BlockUseResult.Unknown);
        await SendChunk(session, protocol, filled, full: true, section: 4, includeRecord: true);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(498)]
    [InlineData(761)]
    public async Task RetainedRespawnPreservesAndReplacementWorldDropsLocalClearing(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        var oldWorld = session.Client.State.World;
        await Respawn(session, protocol, "minecraft:overworld");
        Assert.Same(oldWorld, session.Client.State.World);
        Assert.Equal(filled, oldWorld.GetBlock(Session.Target).StateId);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        await AssertUse(session, protocol, AfterAcknowledgedUse(protocol));

        await Respawn(session, protocol, "minecraft:the_nether");
        Assert.NotSame(oldWorld, session.Client.State.World);
        Assert.Equal(Identifier.Minecraft("the_nether"), session.Client.State.World.Dimension.DimensionName);
        Assert.Null(session.Client.State.World.GetColumn(Session.Target));
        await AssertPrediction(session, BlockUseResult.Unknown);
        await SendChunk(session, protocol, filled, full: true, section: 4, includeRecord: true);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(498)]
    [InlineData(761)]
    public async Task CancellationBeforeWriteDoesNotConsumeAndReconnectStartsFresh(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: canceled.Token));
        Assert.Empty(await session.Barrier());
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        await AssertPrediction(session, BlockUseResult.Success);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        var originalClient = session.Client;
        var retainedWorld = session.Client.State.World;
        long priorTick = session.Client.State.SessionTick;
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.Same(retainedWorld, session.Client.State.World);
        Assert.Equal(filled, retainedWorld.GetBlock(Session.Target).StateId);
        Assert.False(session.Client.State.JukeboxPrediction.TryProject(retainedWorld, Session.Target, retainedWorld.GetBlock(Session.Target), out _));
        await session.Restart("jukebox");
        Assert.Same(originalClient, session.Client);
        Assert.NotSame(retainedWorld, session.Client.State.World);
        Assert.Equal(priorTick + 1, session.Client.State.SessionTick);
        await SendChunk(session, protocol, filled, full: true, section: 4, includeRecord: true);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    [Theory]
    [InlineData(47)]
    public async Task BulkChunkReplacementRearmsBeforeChunkLoadedObservers(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session, protocol);
        await Prime(session, filled);
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
        int observations = 0;
        BlockUseResult? duringEvent = null;
        using IDisposable subscription = session.Client.Events.Subscribe<ChunkLoaded>(change =>
        {
            Assert.Equal(new ChunkPos(0, 0), change.Position);
            observations++;
            duringEvent = Prediction(session).Result;
        });
        var column = new LegacyChunkWireBody(0, 0, true, 16, [Enumerable.Repeat(filled, 4096).ToArray()], new byte[4096 + 256]);
        byte[] bytes = ChunkCodecs.EncodeStructural(new MapChunkBulkWireBody(true, [column]));
        PhaseRegistry registry = JavaVersions.All.First(version => version.Version.Protocol == protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("map_chunk_bulk"));
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundMapChunkBulkPacket>(codec.Decode(bytes, context));
        Assert.True(packet.SkyLight);
        var decodedColumn = Assert.Single(packet.Columns);
        Assert.Equal(new ChunkPos(0, 0), decodedColumn.Position);
        Assert.Equal(filled, decodedColumn.GetBlockStateId(Session.Target.X, Session.Target.Y, Session.Target.Z));
        Assert.Equal(bytes, Encode(codec, packet, context));
        var structure = Assert.IsType<MapChunkBulkWireBody>(ChunkCodecs.DecodeStructure(packet));
        Assert.True(structure.SkyLight);
        LegacyChunkWireBody structuralColumn = Assert.Single(structure.Chunks);
        Assert.True(structuralColumn.GroundUp);
        Assert.Equal((0, 0, (ushort)16), (structuralColumn.X, structuralColumn.Z, structuralColumn.PrimaryMask));
        Assert.Equal(filled, Assert.Single(structuralColumn.SectionBlockStates)[(1 << 8) | (1 << 4) | 1]);
        Assert.Equal(bytes, ChunkCodecs.EncodeStructural(structure));
        await session.Server.SendFrameAsync(entry.WireId, bytes, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(1, observations);
        Assert.Equal(BlockUseResult.Success, duringEvent);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        Assert.True(session.Client.State.EntityMembership.HasColumn(Session.Target));
        await AssertUse(session, protocol, BlockUseResult.Success);
        await AssertPrediction(session, AfterAcknowledgedUse(protocol));
    }

    // These historical cases use Session.Use, which has already acknowledged 759+; dedicated sequenced cases exercise pending state through every lifetime transition.
    private static BlockUseResult AfterAcknowledgedUse(int protocol) => protocol >= 759 ? BlockUseResult.Success : BlockUseResult.Pass;

    private static int Filled(Session session, int protocol)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        return protocol <= 340 ? block.MinStateId | 1 : Enumerable.Range(block.MinStateId, block.StateCount).First(state => block.TryGetPropertyValue(state, "has_record", out string record) && record == "true");
    }

    private static async Task Prime(Session session, int filled)
    {
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
        Assert.Empty(await session.Barrier());
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        await AssertPrediction(session, BlockUseResult.Success);
    }

    private static BlockUsePrediction Prediction(Session session) => session.Client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main);

    private static async Task AssertPrediction(Session session, BlockUseResult expected)
    {
        BlockUsePrediction actual = await session.Client.InvokeAsync(_ => Prediction(session), session.Token);
        Assert.True(actual.SendPacket);
        Assert.Equal(expected, actual.Result);
        Assert.Equal(expected == BlockUseResult.Success ? BlockUseSwing.Packet : BlockUseSwing.None, actual.Swing);
        Assert.Empty(await session.Barrier());
    }

    private static async Task AssertUse(Session session, int protocol, BlockUseResult expected)
    {
        await AssertPrediction(session, expected);
        int before = session.Client.State.World.GetBlock(Session.Target).StateId;
        var main = session.Client.State.Inventory.GetSlot(36);
        List<IPacket> packets = await session.Use(Hand.Main);
        bool swing = expected == BlockUseResult.Success;
        Assert.Equal(swing ? 2 : 1, packets.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up), (use.Position, use.Face));
            Assert.True(use.HeldItem.IsEmpty);
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)Hand.Main), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
        }
        if (swing)
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.Equal(before, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
    }

    private static async Task Respawn(Session session, int protocol, string dimension)
    {
        ClientboundRespawnPacket packet;
        if (protocol < 735)
            packet = new(null, 0, new LegacyRespawnFields(dimension == "minecraft:the_nether" ? -1 : 0, protocol <= 404 ? (byte)2 : (byte)0, 0, "flat"));
        else
        {
            // 1.19.3 sends a dimension-type resource key; this fixture has not received a dimension registry or a numeric type id.
            var spawn = new CommonPlayerSpawnInfo(0, dimension, 0, 0, -1, false, false, null, 0, 0) { DimensionTypeName = dimension };
            packet = new(spawn, 0, null);
        }
        await SendFrame(session, protocol, packet, "respawn", decoded =>
        {
            var actual = Assert.IsType<ClientboundRespawnPacket>(decoded);
            Assert.Equal(packet.DataToKeep, actual.DataToKeep);
            if (packet.Legacy is not null)
            {
                Assert.Equal(packet.Legacy, actual.Legacy);
                Assert.Null(actual.SpawnInfo);
            }
            else
            {
                Assert.Null(actual.Legacy);
                CommonPlayerSpawnInfo spawn = Assert.IsType<CommonPlayerSpawnInfo>(actual.SpawnInfo);
                Assert.Equal(dimension, spawn.DimensionTypeName);
                Assert.Equal(dimension, spawn.Dimension);
                Assert.Equal(0, spawn.DimensionTypeId);
                Assert.Equal(0L, spawn.Seed);
                Assert.Equal(0, spawn.GameType);
                Assert.Equal(-1, spawn.PreviousGameType);
                Assert.False(spawn.IsDebug);
                Assert.False(spawn.IsFlat);
                Assert.Null(spawn.LastDeathDimensionAndPos);
                Assert.Null(spawn.LastDeathDimension);
                Assert.Equal(0, spawn.PortalCooldown);
                Assert.Equal(0, spawn.SeaLevel);
            }
        });
        Assert.Empty(await session.Barrier());
    }

    private static async Task SendChunk(Session session, int protocol, int filled, bool full, int section, bool includeRecord)
    {
        int state = includeRecord ? filled : 0;
        byte[] bytes;
        if (protocol == 47)
            bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, full, (ushort)(1 << section), [Enumerable.Repeat(state, 4096).ToArray()], new byte[4096 + (full ? 256 : 0)]));
        else if (protocol == 761)
        {
            Assert.True(full);
            int minSection = session.Client.State.World.Dimension.MinY >> 4;
            int count = (session.Client.State.World.Dimension.MaxY - session.Client.State.World.Dimension.MinY) / 16;
            var air = new ChunkPalettedContainerWire(0, 0, null, []);
            ChunkSectionWire[] sections = Enumerable.Range(minSection, count).Select(y => new ChunkSectionWire(y == section ? (short)4096 : (short)0, null, y == section ? new(0, state, null, []) : air, air)).ToArray();
            bytes = ChunkCodecs.EncodeStructural(new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[8], NbtWireFormat.JavaNamedRoot));
        }
        else
        {
            var body = new ArrayBufferWriter<byte>();
            var blocks = new PacketWriter(body);
            if (protocol >= 477)
                blocks.WriteShort(state == 0 ? (short)0 : (short)4096);
            blocks.WriteByte(4);
            blocks.WriteVarInt(2);
            blocks.WriteVarInt(0);
            blocks.WriteVarInt(filled);
            blocks.WriteVarInt(256);
            for (int i = 0; i < 256; i++)
                blocks.WriteLong(state == 0 ? 0 : 0x1111111111111111L);
            if (protocol == 107)
                blocks.WriteBytes(new byte[4096 + (full ? 256 : 0)]);
            else if (protocol == 498 && full)
                blocks.WriteBytes(new byte[1024]);
            var frame = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(frame);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteBool(full);
            writer.WriteVarInt(1 << section);
            if (protocol >= 477)
                writer.WriteNbt(new NbtCompound(), NbtWireFormat.JavaNamedRoot);
            if (protocol == 751 && full)
            {
                writer.WriteVarInt(1024);
                for (int i = 0; i < 1024; i++)
                    writer.WriteVarInt(0);
            }
            writer.WriteVarInt(body.WrittenCount);
            writer.WriteBytes(body.WrittenSpan);
            if (protocol >= 110)
                writer.WriteVarInt(0);
            bytes = frame.WrittenSpan.ToArray();
        }
        PhaseRegistry registry = JavaVersions.All.First(version => version.Version.Protocol == protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id.Path is "level_chunk" or "level_chunk_with_light");
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
        Assert.Equal((0, 0, full), (packet.ChunkX, packet.ChunkZ, packet.FullChunk));
        int wireSection = protocol == 761 ? section - (session.Client.State.World.Dimension.MinY >> 4) : section;
        Assert.NotNull(packet.Column.GetSectionForY(wireSection << 4));
        Assert.Equal(state, packet.Column.GetBlockStateId(1, wireSection * 16 + 1, 1));
        if (!full)
            Assert.Null(packet.Column.GetSectionForY((section == 4 ? 3 : 4) << 4));
        Assert.Equal(bytes, Encode(codec, packet, context));
        await session.Server.SendFrameAsync(entry.WireId, bytes, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(includeRecord && section == 4 || !full ? filled : 0, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    private static async Task SendFrame(Session session, int protocol, IPacket packet, string alias, Action<object> inspect)
    {
        PhaseRegistry registry = JavaVersions.All.First(version => version.Version.Protocol == protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id || pair.Type.Id == Identifier.Minecraft(alias));
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        inspect(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(entry.WireId, bytes, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
