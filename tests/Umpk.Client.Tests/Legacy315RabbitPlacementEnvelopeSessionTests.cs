using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Legacy315RabbitPlacementEnvelopeSessionTests
{
    private const int RabbitId = 215;
    private static readonly Vec3d Far = new(108.5, 64, 158.5);
    private static readonly Vec3d Near = new(.5, 65, 1.5);

    [Fact]
    public async Task ActualNativeBabyRabbitCannotMakeUnrelatedPlacementUnknown()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await SetBlock(session, BlockUseSession.Target.Offset(Direction.West), "air");
        await session.Hold("stone");
        await AssertUse(session, clear: true);
        await session.Terrain(6, 9);
        // Exact native1.11 AddMob frame988 from capture08bd9785, Rabbit215. This row proves its typed identity and byte roundtrip before the framed public-session placement regression.
        byte[] payload = Convert.FromBase64String("1wGPHRPykf5LNrYdF5cfq8rlZUBbIAAAAAAAQFAAAAAAAABAY9AAAAAAAGcAAAAA/Y0AAAAAAAEBrAICAwADBgAEBgAFBgAGAAAHAkBAAAAIAQAJBgAKAQALAAAMBgENAQL/");
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(315, "add_mob");
        var context = new PacketCodecContext(JavaGameData.Registries(315), IConnectionCodecState.Empty);
        var spawn = Assert.IsType<ClientboundAddMobPacket>(codec.Decode(payload, context));
        Assert.Equal((RabbitId, 101, Guid.Parse("8f1d13f2-91fe-4b36-b61d-17971fabcae5")), (spawn.EntityId, spawn.TypeId, spawn.Uuid));
        Assert.Equal(Far, new Vec3d(spawn.X, spawn.Y, spawn.Z));
        Assert.Empty(spawn.Metadata.RawTail);
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 12 && entry.WireSerializerId == 6 && entry.Value.Equals(MetadataValue.Boolean(true)));
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 13 && entry.WireSerializerId == 1 && entry.Value.Equals(MetadataValue.VarInt(2)));
        var encoded = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(encoded);
        codec.Encode(ref writer, spawn, context);
        Assert.Equal(payload, encoded.WrittenSpan.ToArray());
        await session.Send(spawn);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity rabbit = Assert.IsType<Entity>(client.State.Entities.Get(RabbitId));
            Assert.Equal(true, client.State.EntityMembership.IsWorldQueryMember(rabbit));
            return true;
        }, session.Token);
        await AssertActualGeometryUnowned(session);
        Vec3d observedVelocity = session.Client.State.Entities.Get(RabbitId)!.Velocity;
        await AssertUse(session, clear: true);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(observedVelocity, session.Client.State.Entities.Get(RabbitId)!.Velocity);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FarRabbitCannotMakeUnrelatedPlacementUnknown(bool baby)
    {
        await using BlockUseSession session = await Prepare(Far, baby);
        await AssertActualGeometryUnowned(session);
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(99)]
    public async Task AgeAndRabbitTypeCannotMoveTheSourceBoundEnvelope(int rabbitType)
    {
        await using BlockUseSession session = await Prepare(Far, baby: true);
        await session.Send(new ClientboundSetEntityDataPacket(RabbitId, new EntityMetadataList([
            new(12, MetadataValue.Boolean(false)), new(13, MetadataValue.VarInt(rabbitType)),
        ], [])));
        await session.Send(new ClientboundEntityEventPacket(RabbitId, 1));
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Fact]
    public async Task PendingFarEndpointsRemainIrrelevantWithoutInventingAnExactTrajectory()
    {
        await using BlockUseSession session = await Prepare(Far);
        await Teleport(session, new(Far.X + 2, Far.Y - 1, Far.Z + 3));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData("near")]
    [InlineData("prior-near")]
    [InlineData("straddling")]
    public async Task ARelevantLifetimeEnvelopeRetainsUnknown(string context)
    {
        await using BlockUseSession session = await Prepare(context == "straddling" ? new(-8, 65, 1.5) : Near);
        if (context == "prior-near")
            await Teleport(session, Far);
        else if (context == "straddling")
            await Teleport(session, new(8, 65, 1.5));
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData("age-kind")]
    [InlineData("type-kind")]
    [InlineData("coordinates")]
    [InlineData("piston-event")]
    [InlineData("mounted")]
    [InlineData("dismounted")]
    [InlineData("unowned-displacement")]
    [InlineData("furnace-state")]
    [InlineData("shulker-box-state")]
    [InlineData("moving-piston-state")]
    [InlineData("unowned-shulker")]
    [InlineData("direct-position")]
    [InlineData("transient-state")]
    [InlineData("transient-shulker")]
    public async Task AnUnprovenLifetimeCannotBecomeClearAfterItsCurrentTriggerDisappears(string context)
    {
        await using BlockUseSession session = await Prepare(context == "coordinates" ? new(2147483649D, 79, 153) : Far);
        var cell = new BlockPos(8, 65, 8);
        if (context == "age-kind")
            await session.Send(new ClientboundSetEntityDataPacket(RabbitId, new EntityMetadataList([new(12, MetadataValue.VarInt(1))], [])));
        else if (context == "type-kind")
            await session.Send(new ClientboundSetEntityDataPacket(RabbitId, new EntityMetadataList([new(13, MetadataValue.Byte(2))], [])));
        else if (context is "mounted" or "dismounted")
        {
            await Spawn(session, new(20, 65, 20), id: 99, type: 90);
            await session.Send(new ClientboundSetPassengersPacket(99, [RabbitId]));
            if (context == "dismounted")
                await session.Send(new ClientboundSetPassengersPacket(99, []));
        }
        else if (context == "piston-event")
        {
            await SetBlock(session, cell, "piston");
            await session.Send(new ClientboundBlockEventPacket(cell, 0, 0, 33));
            await SetBlock(session, cell, "air");
        }
        else if (context is "unowned-shulker" or "transient-shulker")
        {
            await Spawn(session, Far, id: 100, type: 69);
            if (context == "transient-shulker")
                await session.Send(new ClientboundRemoveEntitiesPacket([100]));
        }
        else if (context == "direct-position")
            await session.Client.InvokeAsync(client => { client.State.Entities.Get(RabbitId)!.Position = Near; return true; }, session.Token);
        else if (context == "unowned-displacement")
            await session.Client.InvokeAsync(client => { client.State.EntityGeometry.InvalidatePosition(client.State.Entities.Get(RabbitId)!); return true; }, session.Token);
        else if (context.EndsWith("-state", StringComparison.Ordinal))
            await SetBlock(session, cell, context == "furnace-state" ? "furnace" : context == "shulker-box-state" ? "white_shulker_box" : "piston_extension");
        if (context == "transient-state")
            await SetBlock(session, cell, "air");
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: false);
        if (context.EndsWith("-state", StringComparison.Ordinal))
            await SetBlock(session, cell, "air");
        if (context == "age-kind")
            await session.Send(new ClientboundSetEntityDataPacket(RabbitId, new EntityMetadataList([new(12, MetadataValue.Boolean(false))], [])));
        if (context == "type-kind")
            await session.Send(new ClientboundSetEntityDataPacket(RabbitId, new EntityMetadataList([new(13, MetadataValue.VarInt(2))], [])));
        if (context == "unowned-shulker")
            await session.Send(new ClientboundRemoveEntitiesPacket([100]));
        await Teleport(session, Far);
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData("chest")]
    [InlineData("mob_spawner")]
    public async Task NativeBenignBlockEntityStateNeedsNoNbtToRemainNonDisplacing(string block)
    {
        await using BlockUseSession session = await Prepare(Far);
        var cell = new BlockPos(8, 65, 8);
        await SetBlock(session, cell, block);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client => { Assert.Null(client.State.World.GetBlockEntityNbt(cell)); return true; }, session.Token);
        await AssertUse(session, clear: true);
    }

    [Fact]
    public async Task ReplacingAnIdStartsANewEnvelopeInsteadOfReusingItsNearHistory()
    {
        await using BlockUseSession session = await Prepare(Near);
        await AssertUse(session, clear: false);
        await Spawn(session, Far);
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Fact]
    public async Task RemovingTheUnknownActorClearsItsPlacementCandidate()
    {
        await using BlockUseSession session = await Prepare(Near);
        await session.Send(new ClientboundRemoveEntitiesPacket([RabbitId]));
        Assert.Empty(await session.Barrier());
        Assert.Empty(session.Client.State.Entities.All);
        await AssertUse(session, clear: true);
    }

    [Fact]
    public async Task RespawnClearsTheOldActorAndItsLifetime()
    {
        await using BlockUseSession session = await Prepare(Near);
        await session.Send(new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(-1, 2, 0, "flat")));
        Assert.Empty(await session.Barrier());
        Assert.Empty(session.Client.State.Entities.All);
    }

    [Fact]
    public async Task DisconnectClearsOwnedGeometryBeforeTheNextWorld()
    {
        await using BlockUseSession session = await Prepare(Near);
        Entity oldRabbit = session.Client.State.Entities.Get(RabbitId)!;
        await Spawn(session, Far, id: 99, type: 90);
        Entity pig = session.Client.State.Entities.Get(99)!;
        Assert.True(session.Client.State.EntityGeometry.TryGet(pig, JavaGameData.EntityGeometryData(315), out _, out _, 315));
        Assert.True(session.Client.State.EntityGeometry.TryGetDimensions(pig, out _));
        await session.Client.DisconnectAsync(session.Token);
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.Same(oldRabbit, session.Client.State.Entities.Get(RabbitId));
        Assert.Same(pig, session.Client.State.Entities.Get(99));
        Assert.False(session.Client.State.EntityGeometry.TryGetDimensions(pig, out _));
        Assert.False(session.Client.State.EntityMembership.HasColumn(BlockUseSession.Target));
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
        await session.Restart("stone");
        Assert.Empty(session.Client.State.Entities.All);
        await SetBlock(session, BlockUseSession.Target.Offset(Direction.West), "air");
        await session.Hold("stone");
        await AssertUse(session, clear: true);
    }

    [Theory]
    [InlineData(2147483648D)]
    [InlineData(-2147483648D)]
    public async Task AdmittedNumericBoundaryStillOnlyProvesSpatialNonparticipation(double x)
    {
        await using BlockUseSession session = await Prepare(new(x, 79, 153));
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Fact]
    public async Task OutOfBorderQueryRejectsBeforeEitherRawOrClampedRabbitContext()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Hold("stone");
        var clicked = new BlockPos(29999998, 65, 1);
        await session.Terrain(clicked.X >> 4, clicked.Z >> 4);
        await SetBlock(session, clicked, "stone");
        await SetBlock(session, clicked.Offset(Direction.East), "air");
        Assert.False(session.Client.State.World.Border.IsWithinBounds(clicked.X, clicked.Z, WorldBorderState.ContainmentEraForProtocol(315)));
        await Reject();
        await Spawn(session, new(2147483648D, 65, 1.5));
        await Reject();
        await AssertActualGeometryUnowned(session);

        async Task Reject()
        {
            await session.Client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.East, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.World.GetBlock(clicked.Offset(Direction.East)).IsAir);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        }
    }

    [Theory]
    [InlineData(210)]
    [InlineData(316)]
    public async Task AdjacentNativeProfilesRetainTheirUnclassifiedRabbitGeometry(int protocol)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await SetBlock(session, BlockUseSession.Target.Offset(Direction.West), "air");
        await session.Hold("stone");
        await session.Terrain(6, 9);
        await Spawn(session, Far, protocol: protocol);
        await AssertUse(session, clear: false);
    }

    private static async Task<BlockUseSession> Prepare(Vec3d position, bool baby = false)
    {
        BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        try
        {
            await SetBlock(session, BlockUseSession.Target.Offset(Direction.West), "air");
            await session.Hold("stone");
            await AssertUse(session, clear: true);
            await session.Terrain(6, 9);
            await Spawn(session, position, baby);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task Spawn(BlockUseSession session, Vec3d position, bool baby = false, int id = RabbitId, int type = 101, int protocol = 315)
    {
        EntityMetadataList metadata = type == 69 ? new([], [])
            : type == 101 ? new([new(12, MetadataValue.Boolean(baby)), new(13, MetadataValue.VarInt(2))], [])
            : new([new(12, MetadataValue.Boolean(baby))], []);
        var packet = new ClientboundAddMobPacket(id, type, position.X, position.Y, position.Z, 0, 0, 0, 48, -627, 129, metadata, Guid.Empty);
        var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(protocol, "add_mob", packet));
        Assert.Equal((id, type), (decoded.EntityId, decoded.TypeId));
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        Assert.Equal(((short)48, (short)-627, (short)129), (decoded.VelocityX, decoded.VelocityY, decoded.VelocityZ));
        Assert.Empty(decoded.Metadata.RawTail);
        if (type == 69)
            Assert.Empty(decoded.Metadata.Entries);
        else
        {
            Assert.Equal(type == 101 ? 2 : 1, decoded.Metadata.Entries.Count);
            EntityDataEntry age = Assert.Single(decoded.Metadata.Entries, entry => entry.Index == 12);
            Assert.Equal(12, age.Index);
            Assert.Equal(6, age.WireSerializerId);
            Assert.Equal(MetadataValueKind.Boolean, age.Value.Kind);
            Assert.Equal(baby, age.Value.AsBoolean());
            if (type == 101)
            {
                EntityDataEntry rabbitType = Assert.Single(decoded.Metadata.Entries, entry => entry.Index == 13);
                Assert.Equal(1, rabbitType.WireSerializerId);
                Assert.Equal(MetadataValueKind.VarInt, rabbitType.Value.Kind);
                Assert.Equal(2, rabbitType.Value.AsVarInt());
            }
        }
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static async Task Teleport(BlockUseSession session, Vec3d position)
    {
        var decoded = Assert.IsType<ClientboundTeleportEntityPacket>(BoundDescriptorCodec.RoundTrip(315, "teleport_entity",
            new ClientboundTeleportEntityPacket(RabbitId, position.X, position.Y, position.Z, 0, 0, true, null, 0)));
        Assert.Equal(RabbitId, decoded.EntityId);
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static async Task SetBlock(BlockUseSession session, BlockPos position, string name)
    {
        await session.Block(position, name);
        Assert.Empty(await session.Barrier());
        Assert.Equal(Identifier.Minecraft(name), session.Client.State.World.GetBlock(position).Block.Id);
    }

    private static async Task AssertActualGeometryUnowned(BlockUseSession session)
    {
        await session.Client.InvokeAsync(client =>
        {
            Entity rabbit = client.State.Entities.Get(RabbitId)!;
            Assert.Equal(Identifier.Minecraft("rabbit"), rabbit.Type.Id);
            Assert.False(client.State.EntityGeometry.TryGet(rabbit, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.False(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(rabbit.Type.Id));
            Assert.False(JavaGameData.BlockInteractionData(315).TryGetFixedEntityDimensions(rabbit.Type.Id, out _, out _));
            Assert.False(client.State.EntityMotion.IsTrackedOrdinaryLiving(rabbit));
            Assert.False(client.State.EntityMotion.TryGetLegacyAmbientInterpolation(rabbit, out _));
            return true;
        }, session.Token);
    }

    private static async Task AssertUse(BlockUseSession session, bool clear, BlockPos? clicked = null, Direction face = Direction.West)
    {
        BlockPos target = clicked ?? BlockUseSession.Target;
        Assert.Empty(await session.Barrier());
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(target, face, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);
        Assert.Equal(clear ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal(target, use.Position);
        Assert.Equal(((int)face, (int)Hand.Main, 0F, .5F, .5F), (use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ));
        if (clear)
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.True(session.Client.State.World.GetBlock(target.Offset(face)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }
}
