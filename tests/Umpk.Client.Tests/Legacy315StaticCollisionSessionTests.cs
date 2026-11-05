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

public sealed class Legacy315StaticCollisionSessionTests
{
    private const int SquidId = 2535;
    private static readonly Vec3d Position = new(79.5, 62.125, 145.5);
    private static readonly BlockPos RemoteCell = new(79, 61, 145);

    [Theory]
    [InlineData("stone")]
    [InlineData("dirt")]
    [InlineData("grass")]
    [InlineData("cobblestone")]
    [InlineData("planks")]
    [InlineData("bedrock")]
    [InlineData("sand")]
    [InlineData("gravel")]
    [InlineData("glass")]
    public async Task StaticNativeCollisionDataRetainsDistantSquidAndPlacementSwing(string block)
    {
        await using BlockUseSession session = await Prepare(block, RemoteCell);
        await session.Client.InvokeAsync(client =>
        {
            var state = client.State.World.GetBlock(RemoteCell);
            Assert.Equal(Identifier.Minecraft(block), state.Block.Id);
            Assert.Equal(Aabb.BlockAt(0, 0, 0), Assert.Single(JavaGameData.BlockShapes(315).GetCollisionShapes(state).ToArray()));
            return true;
        }, session.Token);

        for (int tick = 0; tick < 3; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
        }

        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All);
            Assert.Equal(Position, squid.Position);
            Assert.Equal(Vec3d.Zero, squid.Velocity);
            Assert.True(client.State.EntityGeometry.IsPositionKnown(squid));
            Assert.True(client.State.EntityGeometry.TryGet(squid, JavaGameData.EntityGeometryData(315), out Aabb bounds, out bool blocks, 315));
            Assert.Equal(VanillaEntityBounds.Create(Position, .8F, .8F).Box, bounds);
            Assert.True(blocks);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(squid));
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(squid, out var membership));
            Assert.True(membership.RegistrationKnown);
            Assert.Equal((4, 3, 9), (membership.X, membership.Y, membership.Z));
            return true;
        }, session.Token);

        Assert.Collection(await UseStone(session),
            packet =>
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
                Assert.Equal(BlockUseSession.Target, use.Position);
                Assert.Equal((int)Direction.West, use.Face);
                Assert.Equal((int)Hand.Main, use.Hand);
                Assert.Equal((0F, .5F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            },
            packet => Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packet).Hand));
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("soul_sand")]
    public async Task UnownedInsideBodyCallbacksCannotBecomeClearFromAnEmptyOrSimpleShape(string block)
    {
        await using BlockUseSession session = await Prepare(block, BlockPos.Containing(Position));

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All);
            Assert.False(client.State.EntityGeometry.IsPositionKnown(squid));
            Assert.Null(client.State.EntityMembership.IsWorldQueryMember(squid));
            return true;
        }, session.Token);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    private static async Task<BlockUseSession> Prepare(string block, BlockPos cell)
    {
        BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        try
        {
            await session.Block(BlockUseSession.Target.Above(), "iron_door", upper: true);
            await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
            await session.Hold("stone");
            await session.Terrain(4, 9);
            await session.Block(cell, block);
            var packet = new ClientboundAddMobPacket(SquidId, 94, Position.X, Position.Y, Position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []), Guid.Empty);
            var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(315, "add_mob", packet));
            Assert.Equal(SquidId, decoded.EntityId);
            Assert.Equal(94, decoded.TypeId);
            Assert.Equal(Position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
            Assert.Equal((0, 0, 0), (decoded.VelocityX, decoded.VelocityY, decoded.VelocityZ));
            await session.Send(decoded);
            Assert.Empty(await session.Barrier());
            await session.Client.InvokeAsync(client =>
            {
                Entity squid = Assert.Single(client.State.Entities.All);
                Assert.Equal(Identifier.Minecraft("squid"), squid.Type.Id);
                Assert.True(client.State.EntityMembership.HasColumn(cell));
                Assert.True(client.State.EntityGeometry.IsPositionKnown(squid));
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

    private static async Task<List<IPacket>> UseStone(BlockUseSession session)
    {
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(BlockUseSession.Target, Direction.West, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> sent = await session.Barrier();
        await action.WaitAsync(session.Token);
        return sent;
    }
}
