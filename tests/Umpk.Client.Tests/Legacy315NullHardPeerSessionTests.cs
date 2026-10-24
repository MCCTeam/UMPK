using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Legacy315NullHardPeerSessionTests
{
    private const int SquidId = 42;
    private const int PeerId = 43;
    private static readonly Vec3d SquidPosition = new(10.5, 65.125, 10.5);

    [Theory]
    [InlineData(91, "sheep", false)]
    [InlineData(91, "sheep", true)]
    [InlineData(120, "villager", false)]
    [InlineData(120, "villager", true)]
    public async Task NativeNullHardPeerRetainsSquidMotionGeometryAndMembership(int type, string family, bool overlapsSquid)
    {
        await using BlockUseSession session = await Prepare(type, family, overlapsSquid);

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All, entity => entity.Id == SquidId);
            Assert.Equal(SquidPosition, squid.Position);
            Assert.Equal(Vec3d.Zero, squid.Velocity);
            Assert.True(client.State.EntityGeometry.TryGet(squid, JavaGameData.EntityGeometryData(315), out Aabb bounds, out bool blocksBuilding, 315));
            Assert.Equal(VanillaEntityBounds.Create(SquidPosition, .8F, .8F).Box, bounds);
            Assert.True(blocksBuilding);
            Assert.True(client.State.EntityGeometry.IsPositionKnown(squid));
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(squid));
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(squid, out var member));
            Assert.True(member.RegistrationKnown);
            Assert.True(member.InEntityList);
            Assert.True(member.Added);
            Assert.False(member.Removed);
            Assert.Equal((0, 4, 0), (member.X, member.Y, member.Z));
            Assert.Equal(4, member.ActualSection);
            Assert.Equal(10, member.Health);
            Assert.Equal(0, member.DeathTime);
            Assert.Equal(2, client.State.Entities.Count);
            return true;
        }, session.Token);
    }

    [Theory]
    [InlineData(91, "sheep", false)]
    [InlineData(91, "sheep", true)]
    [InlineData(120, "villager", false)]
    [InlineData(120, "villager", true)]
    public async Task NativeNullHardPeerCannotRemoveTheRealHeldStoneUseSwing(int type, string family, bool overlapsSquid)
    {
        await using BlockUseSession session = await Prepare(type, family, overlapsSquid);
        for (int tick = 0; tick < 3; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
        }

        List<IPacket> sent = await UseStone(session);

        Assert.Collection(sent,
            packet =>
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
                Assert.Equal(BlockUseSession.Target, use.Position);
                Assert.Equal((int)Direction.West, use.Face);
                Assert.Equal((int)Hand.Main, use.Hand);
                Assert.Equal(0F, use.CursorX);
                Assert.Equal(.5F, use.CursorY);
                Assert.Equal(.5F, use.CursorZ);
            },
            packet => Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packet).Hand));
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.Equal(Identifier.Minecraft("stone"), session.Client.State.Inventory.GetSlot(36).Item.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task NativeHardAndUnauditedPeersKeepSquidAndPlacementUnknown(bool boat, bool overlapsSquid)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await PrepareDoor(session);
        await session.Hold("stone");
        await Spawn(session, SquidId, 94, "squid", SquidPosition);
        Vec3d position = PeerPosition(overlapsSquid);
        if (boat)
            await SpawnBoat(session, position);
        else
            await Spawn(session, PeerId, 54, "zombie", position);
        Assert.Empty(await session.Barrier());

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertUnknown(session);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    [Theory]
    [InlineData(91, "sheep")]
    [InlineData(120, "villager")]
    public async Task ReplacingAnUnknownPeerWithAProvenFamilyCannotRepairLostSquidPosition(int type, string family)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await PrepareDoor(session);
        await session.Hold("stone");
        await Spawn(session, SquidId, 94, "squid", SquidPosition);
        Vec3d position = PeerPosition(false);
        await SpawnBoat(session, position);
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertUnknown(session);
        await session.Send(new ClientboundRemoveEntitiesPacket([PeerId]));
        await Spawn(session, PeerId, type, family, position);
        Assert.Empty(await session.Barrier());

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertUnknown(session);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    private static async Task<BlockUseSession> Prepare(int type, string family, bool overlapsSquid)
    {
        BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        try
        {
            await PrepareDoor(session);
            await session.Hold("stone");
            await Spawn(session, SquidId, 94, "squid", SquidPosition);
            await Spawn(session, PeerId, type, family, PeerPosition(overlapsSquid));
            Assert.Empty(await session.Barrier());
            await session.Client.InvokeAsync(client =>
            {
                Assert.True(client.State.EntityMembership.HasColumn(BlockUseSession.Target));
                Assert.Equal(Identifier.Minecraft("iron_door"), client.State.World.GetBlock(BlockUseSession.Target).Block.Id);
                Assert.True(client.State.Inventory.GetSlot(36).Components.HasNoPatch);
                Entity peer = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
                Assert.Equal(Identifier.Minecraft(family), peer.Type.Id);
                Assert.Equal(PeerPosition(overlapsSquid), peer.Position);
                return true;
            }, session.Token);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task PrepareDoor(BlockUseSession session)
    {
        await session.Block(BlockUseSession.Target.Above(), "iron_door", upper: true);
        await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
        Assert.Empty(await session.Barrier());
        Assert.Equal(1136, session.Client.State.World.GetBlock(BlockUseSession.Target).StateId);
        Assert.Equal(1144, session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).StateId);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
    }

    private static async Task<List<IPacket>> UseStone(BlockUseSession session)
    {
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(BlockUseSession.Target, Direction.West, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> sent = await session.Barrier();
        await action.WaitAsync(session.Token);
        return sent;
    }

    private static Vec3d PeerPosition(bool overlapsSquid) => overlapsSquid ? SquidPosition : new Vec3d(14.5, 65.125, 14.5);

    private static async Task Spawn(BlockUseSession session, int id, int type, string family, Vec3d position)
    {
        var packet = new ClientboundAddMobPacket(id, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []), Guid.Empty);
        var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(315, "add_mob", packet));
        Assert.Equal(id, decoded.EntityId);
        Assert.Equal(type, decoded.TypeId);
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        Assert.Equal(0, decoded.VelocityX);
        Assert.Equal(0, decoded.VelocityY);
        Assert.Equal(0, decoded.VelocityZ);
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.Entities.TryGet(id, out Entity? entity));
            Assert.Equal(Identifier.Minecraft(family), entity!.Type.Id);
            return true;
        }, session.Token);
    }

    private static async Task SpawnBoat(BlockUseSession session, Vec3d position)
    {
        await session.Send(new ClientboundAddEntityPacket(PeerId, Guid.Empty, 1, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity boat = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            Assert.Equal(Identifier.Minecraft("boat"), boat.Type.Id);
            Assert.Equal(position, boat.Position);
            return true;
        }, session.Token);
    }

    private static Task AssertUnknown(BlockUseSession session)
        => session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All, entity => entity.Id == SquidId);
            Assert.Null(client.State.EntityMembership.IsWorldQueryMember(squid));
            Assert.False(client.State.EntityGeometry.IsPositionKnown(squid));
            Assert.False(client.State.EntityGeometry.TryGet(squid, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.Equal(SquidPosition, squid.Position);
            return true;
        }, session.Token);
}
