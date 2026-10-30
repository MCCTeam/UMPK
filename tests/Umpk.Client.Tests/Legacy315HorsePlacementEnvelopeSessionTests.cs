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

public sealed class Legacy315HorsePlacementEnvelopeSessionTests
{
    private const int HorseId = 37;
    private static readonly Vec3d Far = new(95.91408965582805, 67.76636799395752, 254.23759078250413);
    private static readonly Vec3d Near = new(.5, 65, 1.5);

    [Fact]
    public async Task ActualNativeHorseCannotMakeUnrelatedPlacementUnknown()
    {
        await using BlockUseSession session = await Create();
        await AssertUse(session, clear: true);
        await session.Terrain(5, 15);
        // Exact native 1.11 AddMob frame 6517 from capture ad341874, Horse 37. Keep these bytes and its decoded metadata separate from reconstructed later snapshots.
        byte[] payload = Convert.FromBase64String("JdYQBro3GU/Wqmimkwt/eQtkQFf6gHHmWS1AUPEMLFev6kBvx5pX/BX2jQCMAAX4zP+OAAAAAQGsAgIDAAMGAAQGAAUGAAYAAAcCQegAAAgBAAkGAAoBAAsAAAwGAA0AAA4LAA8BgQQQAQD/");
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(315, "add_mob");
        var context = new PacketCodecContext(JavaGameData.Registries(315), IConnectionCodecState.Empty);
        var spawn = Assert.IsType<ClientboundAddMobPacket>(codec.Decode(payload, context));
        Assert.Equal((HorseId, 100, Guid.Parse("d61006ba-3719-4fd6-aa68-a6930b7f790b")), (spawn.EntityId, spawn.TypeId, spawn.Uuid));
        Assert.Equal(Far, new Vec3d(spawn.X, spawn.Y, spawn.Z));
        Assert.Equal(((short)5, (short)-1844, (short)-114), (spawn.VelocityX, spawn.VelocityY, spawn.VelocityZ));
        Assert.Empty(spawn.Metadata.RawTail);
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 12 && entry.WireSerializerId == 6 && entry.Value.Equals(MetadataValue.Boolean(false)));
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 13 && entry.WireSerializerId == 0 && entry.Value.Equals(MetadataValue.Byte(0)));
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 14 && entry.WireSerializerId == 11 && entry.Value.Equals(MetadataValue.OptionalUuid(null)));
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 15 && entry.WireSerializerId == 1 && entry.Value.Equals(MetadataValue.VarInt(513)));
        Assert.Contains(spawn.Metadata.Entries, entry => entry.Index == 16 && entry.WireSerializerId == 1 && entry.Value.Equals(MetadataValue.VarInt(0)));
        var encoded = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(encoded);
        codec.Encode(ref writer, spawn, context);
        Assert.Equal(payload, encoded.WrittenSpan.ToArray());
        await session.Send(spawn);
        Assert.Empty(await session.Barrier());
        Assert.Equal(true, session.Client.State.EntityMembership.IsWorldQueryMember(session.Client.State.Entities.Get(HorseId)!));
        await AssertActualGeometryUnowned(session);
        Vec3d velocity = session.Client.State.Entities.Get(HorseId)!.Velocity;
        await AssertUse(session, clear: true);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(velocity, session.Client.State.Entities.Get(HorseId)!.Velocity);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28, false)]
    [InlineData(28, true)]
    [InlineData(29, false)]
    [InlineData(29, true)]
    [InlineData(31, false)]
    [InlineData(31, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(103, false)]
    [InlineData(103, true)]
    public async Task DistantAdultAndBabyFamiliesRemainOnlySpatiallyExcluded(int type, bool baby)
    {
        await using BlockUseSession session = await Prepare(Far, type, baby);
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(103)]
    public async Task AgeFlagsCrouchingAndStatusRetainOnlyTheProvenDistantEnvelope(int type)
    {
        await using BlockUseSession session = await Prepare(Far, type, baby: true);
        Vec3d velocity = session.Client.State.Entities.Get(HorseId)!.Velocity;
        await session.Send(new ClientboundSetEntityDataPacket(HorseId, new EntityMetadataList([
            new(0, MetadataValue.Byte(2)), new(12, MetadataValue.Boolean(false)), new(13, MetadataValue.Byte(16)),
        ], [])));
        await session.Send(new ClientboundEntityEventPacket(HorseId, 6));
        await session.Send(new ClientboundEntityEventPacket(HorseId, 7));
        Assert.Empty(await session.Barrier());
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(velocity, session.Client.State.Entities.Get(HorseId)!.Velocity);
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(103)]
    public async Task PendingFarEndpointsDoNotClaimAnExactClientTrajectory(int type)
    {
        await using BlockUseSession session = await Prepare(Far, type);
        await Teleport(session, new(Far.X + 2, Far.Y - 1, Far.Z + 3));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(103)]
    public async Task NativeMaximumHeightCannotBeExcludedByTheOldOneBlockPadding(int type)
    {
        // Destination is Y65..66. Native adult height is 1.6F, or 1.87F for Llama; the actor endpoint is below it but its possible body is relevant.
        await using BlockUseSession session = await Prepare(new(.5, 63.5, 1.5), type);
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(103)]
    public async Task RepeatedAgeGrowthRetainsItsNativeMinimumWithoutClearingARelevantBody(int type)
    {
        double x = type == 103 ? -.67 : -1.02;
        await using BlockUseSession session = await Prepare(new(x, 65, 1.5), type, baby: true);
        for (int step = 0; step < 3; step++)
        {
            await session.Send(new ClientboundSetEntityDataPacket(HorseId, new EntityMetadataList([new(12, MetadataValue.Boolean(false))], [])));
            Assert.Empty(await session.Barrier());
            await AssertUse(session, clear: false);
            await session.Send(new ClientboundSetEntityDataPacket(HorseId, new EntityMetadataList([new(12, MetadataValue.Boolean(true))], [])));
            Assert.Empty(await session.Barrier());
        }
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
    [InlineData(28, 12)]
    [InlineData(28, 13)]
    [InlineData(28, 14)]
    [InlineData(29, 12)]
    [InlineData(29, 13)]
    [InlineData(29, 14)]
    [InlineData(31, 12)]
    [InlineData(31, 13)]
    [InlineData(31, 14)]
    [InlineData(32, 12)]
    [InlineData(32, 13)]
    [InlineData(32, 14)]
    [InlineData(100, 12)]
    [InlineData(100, 13)]
    [InlineData(100, 14)]
    [InlineData(103, 12)]
    [InlineData(103, 13)]
    [InlineData(103, 14)]
    [InlineData(100, 15)]
    [InlineData(100, 16)]
    [InlineData(31, 15)]
    [InlineData(32, 15)]
    [InlineData(103, 15)]
    [InlineData(103, 16)]
    [InlineData(103, 17)]
    [InlineData(103, 18)]
    public async Task MalformedNativeMetadataKindsRemainUnknownAfterAValidReplacement(int type, int index)
    {
        await using BlockUseSession session = await Prepare(Far, type);
        MetadataValue valid = Value(type, index);
        MetadataValue wrong = valid.Kind == MetadataValueKind.VarInt ? MetadataValue.Byte(0) : MetadataValue.VarInt(0);
        await session.Send(new ClientboundSetEntityDataPacket(HorseId, new EntityMetadataList([new(index, wrong)], [])));
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: false);
        await session.Send(new ClientboundSetEntityDataPacket(HorseId, new EntityMetadataList([new(index, valid)], [])));
        Assert.Empty(await session.Barrier());
        await Teleport(session, Far);
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(28, false, false)]
    [InlineData(28, false, true)]
    [InlineData(28, true, false)]
    [InlineData(28, true, true)]
    [InlineData(29, false, false)]
    [InlineData(29, false, true)]
    [InlineData(29, true, false)]
    [InlineData(29, true, true)]
    [InlineData(31, false, false)]
    [InlineData(31, false, true)]
    [InlineData(31, true, false)]
    [InlineData(31, true, true)]
    [InlineData(32, false, false)]
    [InlineData(32, false, true)]
    [InlineData(32, true, false)]
    [InlineData(32, true, true)]
    [InlineData(100, false, false)]
    [InlineData(100, false, true)]
    [InlineData(100, true, false)]
    [InlineData(100, true, true)]
    [InlineData(103, false, false)]
    [InlineData(103, false, true)]
    [InlineData(103, true, false)]
    [InlineData(103, true, true)]
    public async Task MountOrDismountCannotRestoreAnEarlierIndependentEnvelope(int type, bool horseVehicle, bool dismount)
    {
        await using BlockUseSession session = await Prepare(Far, type);
        await Spawn(session, new(20, 65, 20), type: 90, id: 99);
        int vehicle = horseVehicle ? HorseId : 99;
        int passenger = horseVehicle ? 99 : HorseId;
        await session.Send(new ClientboundSetPassengersPacket(vehicle, [passenger]));
        if (dismount)
            await session.Send(new ClientboundSetPassengersPacket(vehicle, []));
        Assert.Empty(await session.Barrier());
        await Teleport(session, Far);
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData("coordinates")]
    [InlineData("piston-event")]
    [InlineData("unowned-displacement")]
    [InlineData("furnace-state")]
    [InlineData("shulker-box-state")]
    [InlineData("moving-piston-state")]
    [InlineData("unowned-shulker")]
    [InlineData("direct-position")]
    [InlineData("transient-state")]
    [InlineData("transient-shulker")]
    public async Task LostWorldOrPositionProofIsStickyForTheLifetime(string context)
    {
        await using BlockUseSession session = await Prepare(context == "coordinates" ? new(2147483649D, 79, 153) : Far);
        var cell = new BlockPos(8, 65, 8);
        if (context == "piston-event")
        {
            await SetBlock(session, cell, "piston");
            await session.Send(new ClientboundBlockEventPacket(cell, 0, 0, 33));
            await SetBlock(session, cell, "air");
        }
        else if (context is "unowned-shulker" or "transient-shulker")
        {
            await Spawn(session, Far, type: 69, id: 100);
            if (context == "transient-shulker")
                await session.Send(new ClientboundRemoveEntitiesPacket([100]));
        }
        else if (context == "direct-position")
            await session.Client.InvokeAsync(client => { client.State.Entities.Get(HorseId)!.Position = Near; return true; }, session.Token);
        else if (context == "unowned-displacement")
            await session.Client.InvokeAsync(client => { client.State.EntityGeometry.InvalidatePosition(client.State.Entities.Get(HorseId)!); return true; }, session.Token);
        else if (context.EndsWith("-state", StringComparison.Ordinal))
            await SetBlock(session, cell, context == "furnace-state" ? "furnace" : context == "shulker-box-state" ? "white_shulker_box" : "piston_extension");
        if (context == "transient-state")
            await SetBlock(session, cell, "air");
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear: false);
        if (context.EndsWith("-state", StringComparison.Ordinal))
            await SetBlock(session, cell, "air");
        if (context == "unowned-shulker")
            await session.Send(new ClientboundRemoveEntitiesPacket([100]));
        await Teleport(session, Far);
        await AssertUse(session, clear: false);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData("chest")]
    [InlineData("mob_spawner")]
    public async Task ExistingBenignBlockEntityProofRemainsUsableWithoutNbt(string block)
    {
        await using BlockUseSession session = await Prepare(Far);
        var cell = new BlockPos(8, 65, 8);
        await SetBlock(session, cell, block);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(cell));
        await AssertUse(session, clear: true);
    }

    [Fact]
    public async Task ReplacingAnIdStartsANewLifetimeEnvelope()
    {
        await using BlockUseSession session = await Prepare(Near);
        await AssertUse(session, clear: false);
        await Spawn(session, Far);
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Fact]
    public async Task RemovingTheUnknownActorClearsItsCandidate()
    {
        await using BlockUseSession session = await Prepare(Near);
        await session.Send(new ClientboundRemoveEntitiesPacket([HorseId]));
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
    public async Task DisconnectRetainsNoOwnedEnvelopeInTheNextWorld()
    {
        await using BlockUseSession session = await Prepare(Near);
        Entity oldHorse = session.Client.State.Entities.Get(HorseId)!;
        await session.Client.DisconnectAsync(session.Token);
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.Same(oldHorse, session.Client.State.Entities.Get(HorseId));
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
    public async Task AdmittedNumericBoundaryProvesOnlySpatialNonparticipation(double x)
    {
        await using BlockUseSession session = await Prepare(new(x, 79, 153));
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    [Theory]
    [InlineData(210)]
    [InlineData(316)]
    public async Task AdjacentProfilesRetainTheirUnclassifiedHorseGeometry(int protocol)
    {
        await using BlockUseSession session = await Create(protocol);
        await session.Terrain(5, 15);
        await Spawn(session, Far, protocol: protocol);
        await AssertUse(session, clear: false);
    }

    [Fact]
    public async Task PartialDestinationGridRemainsUnknownDespiteADistantHorse()
    {
        await using BlockUseSession session = await Create(terrain: false);
        Assert.NotNull(session.Client.State.World.GetColumn(BlockUseSession.Target));
        Assert.False(session.Client.State.EntityMembership.HasColumn(BlockUseSession.Target));
        await Spawn(session, Far);
        await AssertUse(session, clear: false);
    }

    [Fact]
    public async Task DistantPartialActorGridDoesNotPublishBodyOrMembershipOwnership()
    {
        await using BlockUseSession session = await Create();
        await AssertUse(session, clear: true);
        await Spawn(session, Far);
        Assert.False(session.Client.State.EntityMembership.HasColumn(BlockPos.Containing(Far)));
        await AssertUse(session, clear: true);
        await AssertActualGeometryUnowned(session);
    }

    private static async Task<BlockUseSession> Create(int protocol = 315, bool terrain = true)
    {
        BlockUseSession session = await BlockUseSession.Create(protocol, "stone", terrain: terrain, physics: true);
        try
        {
            await SetBlock(session, BlockUseSession.Target.Offset(Direction.West), "air");
            await session.Hold("stone");
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task<BlockUseSession> Prepare(Vec3d position, int type = 100, bool baby = false)
    {
        BlockUseSession session = await Create();
        try
        {
            await AssertUse(session, clear: true);
            await session.Terrain(5, 15);
            await Spawn(session, position, type, baby);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static MetadataValue Value(int type, int index) => index switch
    {
        12 => MetadataValue.Boolean(false),
        13 => MetadataValue.Byte(16),
        14 => MetadataValue.OptionalUuid(null),
        15 when type is 31 or 32 or 103 => MetadataValue.Boolean(false),
        _ => MetadataValue.VarInt(0),
    };

    private static async Task Spawn(BlockUseSession session, Vec3d position, int type = 100, bool baby = false, int id = HorseId, int protocol = 315)
    {
        EntityMetadataList metadata = type == 69 ? new([], [])
            : type == 90 || protocol != 315 ? new([new(12, MetadataValue.Boolean(baby))], [])
            : new([new(12, MetadataValue.Boolean(baby)), new(13, MetadataValue.Byte(16)), new(14, MetadataValue.OptionalUuid(null))], []);
        var packet = new ClientboundAddMobPacket(id, type, position.X, position.Y, position.Z, 0, 0, 0, 5, -1844, -114, metadata, Guid.Empty);
        var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(protocol, "add_mob", packet));
        Assert.Equal((id, type), (decoded.EntityId, decoded.TypeId));
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        Assert.Equal(((short)5, (short)-1844, (short)-114), (decoded.VelocityX, decoded.VelocityY, decoded.VelocityZ));
        Assert.Empty(decoded.Metadata.RawTail);
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static async Task Teleport(BlockUseSession session, Vec3d position)
    {
        var decoded = Assert.IsType<ClientboundTeleportEntityPacket>(BoundDescriptorCodec.RoundTrip(315, "teleport_entity",
            new ClientboundTeleportEntityPacket(HorseId, position.X, position.Y, position.Z, 0, 0, true, null, 0)));
        Assert.Equal(HorseId, decoded.EntityId);
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
            Entity horse = client.State.Entities.Get(HorseId)!;
            Assert.Contains(horse.Type.Id.Path, new[] { "horse", "skeleton_horse", "zombie_horse", "donkey", "mule", "llama" });
            Assert.False(client.State.EntityGeometry.TryGet(horse, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.False(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(horse.Type.Id));
            Assert.False(JavaGameData.BlockInteractionData(315).TryGetFixedEntityDimensions(horse.Type.Id, out _, out _));
            Assert.False(client.State.EntityMotion.IsTrackedOrdinaryLiving(horse));
            Assert.False(client.State.EntityMotion.TryGetLegacyAmbientInterpolation(horse, out _));
            return true;
        }, session.Token);
    }

    private static async Task AssertUse(BlockUseSession session, bool clear)
    {
        BlockPos target = BlockUseSession.Target;
        const Direction face = Direction.West;
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
