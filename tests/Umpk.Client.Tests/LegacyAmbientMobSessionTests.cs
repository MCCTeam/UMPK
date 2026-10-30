using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyAmbientMobSessionTests
{
    [Theory]
    [InlineData(65, true)]
    [InlineData(94, true)]
    [InlineData(65, false)]
    [InlineData(94, false)]
    public async Task FramedNativeNearAndFarGeometryDriveTheRealPlacementCaller(int type, bool near)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Hold("stone");
        await Spawn(session, type, near ? new Vec3d(1.5, 66.125, 1.5) : new Vec3d(10.5, 65.125, 10.5));
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.Single(client.State.Entities.All);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            return true;
        }, session.Token);

        List<IPacket> sent = await session.Use(Hand.Main);

        if (near)
            Assert.Empty(sent);
        else
            Assert.Collection(sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).IsAir);
    }

    [Fact]
    public async Task TickedSquidKeepsUnclassifiedDoorMovementUnknownThroughTheRealPlacementCaller()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await session.Hold("stone");
        await Spawn(session, 94, new Vec3d(1.5, 66.125, 1.5));
        Assert.Empty(await session.Barrier());

        await session.Tick();

        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.Single(client.State.Entities.All);
            Assert.Null(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            return true;
        }, session.Token);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await session.Use(Hand.Main)));
    }

    [Fact]
    public async Task RelevantUnclassifiedSquidCollisionRetainsUnknownThroughThePlacementCaller()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await session.Hold("stone");
        await session.Block(new BlockPos(2, 66, 1), "slime");
        await Spawn(session, 94, new Vec3d(1.5, 66.125, 1.5));
        await Send(session, new ClientboundSetEntityMotionPacket(42, 4000, 0, 0));
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.Single(client.State.Entities.All);
            Assert.Null(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.Equal(new Vec3d(1.5, 66.125, 1.5), actor.Position);
            Assert.Equal(new Vec3d(.5, 0, 0), actor.Velocity);
            return true;
        }, session.Token);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await session.Use(Hand.Main)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownCrossColumnTravelSurvivesOldColumnUnloadAndCannotBecomeClearPlacement(bool ambient)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Hold("stone");
        await session.Terrain(chunkX: 1);
        await session.Block(new BlockPos(17, 65, 1), "web");
        if (ambient)
        {
            await Spawn(session, 94, new Vec3d(15.5, 65.125, 1.5));
            await Send(session, new ClientboundTeleportEntityPacket(42, 19.5, 65.125, 1.5, 0, 0, false, null, 0));
        }
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        if (ambient)
            await session.Client.InvokeAsync(client =>
            {
                Entity actor = Assert.Single(client.State.Entities.All);
                Assert.Equal(15.5, actor.Position.X);
                Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
                return true;
            }, session.Token);

        await Send(session, new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Terrain();
        await session.Block(BlockUseSession.Target, "stone");
        await session.Block(BlockUseSession.Target.Above(), "air");
        await session.Block(new BlockPos(2, 64, 2), "stone");

        List<IPacket> sent = await session.Use(Hand.Main);

        if (ambient)
        {
            Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(sent));
            Entity unresolved = Assert.Single(session.Client.State.Entities.All);
            await session.Client.InvokeAsync(client =>
            {
                Entity actor = Assert.Single(client.State.Entities.All);
                Assert.Null(client.State.EntityMembership.IsWorldQueryMember(actor));
                Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
                Assert.True(client.State.EntityMembership.TryGetLegacyMembership(actor, out var member));
                Assert.False(member.RegistrationKnown);
                Assert.True(member.InEntityList);
                Assert.True(member.Added);
                Assert.Equal((0, 4, 0), (member.X, member.Y, member.Z));
                Assert.Equal(4, member.ActualSection);
                return true;
            }, session.Token);
            await Send(session, new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(7, MetadataValue.Float(0))], [])));
            await Send(session, new ClientboundEntityEventPacket(42, 3));
            await Send(session, new ClientboundSetEntityMotionPacket(42, 1000, 0, 0));
            await Send(session, new ClientboundTeleportEntityPacket(42, 28.5, 65.125, 1.5, 0, 0, false, null, 0));
            Assert.Empty(await session.Barrier());
            for (int tick = 0; tick < 20; tick++)
            {
                await session.Tick();
                Assert.Empty(await session.Barrier());
            }
            await session.Client.InvokeAsync(client =>
            {
                Entity actor = Assert.Single(client.State.Entities.All);
                Assert.Null(client.State.EntityMembership.IsWorldQueryMember(actor));
                Assert.True(client.State.EntityMembership.TryGetLegacyMembership(actor, out var member));
                Assert.False(member.RegistrationKnown);
                Assert.False(member.Removed);
                Assert.Equal(0, member.DeathTime);
                Assert.Equal(new Vec3d(28.5, 65.125, 1.5), client.State.PacketBases.Get(actor));
                return true;
            }, session.Token);
            Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await session.Use(Hand.Main)));

            await Send(session, new ClientboundRemoveEntitiesPacket([42]));
            Assert.Empty(await session.Barrier());
            Assert.Empty(session.Client.State.Entities.All);
            Assert.False(session.Client.State.EntityMotion.TryGetLegacyAmbientInterpolation(unresolved, out _));
            Assert.False(session.Client.State.EntityMembership.TryGetLegacyMembership(unresolved, out _));
            Assert.Collection(await session.Use(Hand.Main), packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));

            await Spawn(session, 94, new Vec3d(10.5, 65.125, 10.5));
            Assert.Empty(await session.Barrier());
            await session.Tick();
            Assert.Empty(await session.Barrier());
            await session.Client.InvokeAsync(client =>
            {
                Entity replacement = Assert.Single(client.State.Entities.All);
                Assert.NotSame(unresolved, replacement);
                Assert.True(client.State.EntityMembership.IsWorldQueryMember(replacement));
                Assert.True(client.State.EntityMembership.TryGetLegacyMembership(replacement, out var member));
                Assert.True(member.RegistrationKnown);
                Assert.Equal(10, member.Health);
                Assert.Equal(0, member.DeathTime);
                Assert.True(client.State.EntityGeometry.TryGet(replacement, JavaGameData.EntityGeometryData(315), out _, out _, 315));
                return true;
            }, session.Token);
            Assert.Collection(await session.Use(Hand.Main), packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));
        }
        else
            Assert.Collection(sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(94)]
    public async Task RespawnAndIdReplacementClearTargetsVelocityBoundsAndLifetime(int type)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await Spawn(session, type, new Vec3d(10.5, 65.125, 10.5));
        Assert.Empty(await session.Barrier());
        Entity old = Assert.Single(session.Client.State.Entities.All);
        await Send(session, new ClientboundTeleportEntityPacket(42, 100, 100, 100, 90, 45, false, null, 0));
        await Send(session, new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(7, MetadataValue.Float(0))], [])));
        Assert.Empty(await session.Barrier());
        await Send(session, new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(-1, 2, 0, "flat")));
        Assert.Empty(await session.Barrier());
        Assert.Empty(session.Client.State.Entities.All);
        await session.Terrain();
        await Spawn(session, type, new Vec3d(10.5, 65.125, 10.5));
        Assert.Empty(await session.Barrier());
        Entity replacement = Assert.Single(session.Client.State.Entities.All);
        Assert.NotSame(old, replacement);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(10.5, replacement.Position.X);
        Assert.Equal(Vec3d.Zero, replacement.Velocity);
        Assert.True(session.Client.State.EntityMembership.IsWorldQueryMember(replacement));
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
    }

    private static Task Spawn(BlockUseSession session, int type, Vec3d position)
        => Send(session, new ClientboundAddMobPacket(42, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []), Guid.Empty));

    private static async Task Send(BlockUseSession session, IPacket packet)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(315, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        byte[] body = output.WrittenSpan.ToArray();
        object decoded = codec.Decode(body, context);
        var readback = new ArrayBufferWriter<byte>();
        var readbackWriter = new PacketWriter(readback);
        codec.Encode(ref readbackWriter, decoded, context);
        Assert.Equal(output.WrittenSpan.ToArray(), readback.WrittenSpan.ToArray());
        Assert.True(JavaVersions.TryGetByProtocol(315, out JavaVersion? version));
        var pair = Assert.Single(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).Packets, pair => pair.Type.Id == packet.Type.Id);
        await session.Server.SendFrameAsync(pair.WireId, body, session.Token);
    }
}
