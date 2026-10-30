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

public sealed class Legacy315QueryCallbackSessionTests
{
    private const int SquidId = 2829;
    private static readonly Vec3d Position = new(18.5, 61, 169.5);

    [Theory]
    [InlineData("waterlily")]
    [InlineData("tallgrass")]
    [InlineData("vine")]
    [InlineData("web")]
    [InlineData("soul_sand")]
    public async Task PaddedCollisionScanDoesNotInvokeAnUnvisitedBlockCallback(string block)
    {
        var cell = new BlockPos(18, 63, 169);
        await using BlockUseSession session = await Prepare(block, cell);
        await session.Client.InvokeAsync(client =>
        {
            var state = client.State.World.GetBlock(cell);
            Assert.Equal(Identifier.Minecraft(block), state.Block.Id);
            Aabb original = VanillaEntityBounds.Create(Position, .8F, .8F).Box;
            Aabb query = original.ExpandTowards(0, .6F, 0);
            Assert.True(query.MaxY < cell.Y);
            foreach (Aabb shape in JavaGameData.BlockShapes(315).GetCollisionShapes(state))
                Assert.False(shape.Move(cell.X, cell.Y, cell.Z).Intersects(query));
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertOwned(session, Position);
        await AssertPlacementSwing(session);
    }

    [Theory]
    [InlineData("tallgrass")]
    [InlineData("vine")]
    public async Task NativeEmptyCollisionAndBaseInsideCallbackRetainOwnedBody(string block)
    {
        await using BlockUseSession session = await Prepare(block, BlockPos.Containing(Position));
        await session.Client.InvokeAsync(client =>
        {
            Assert.Empty(JavaGameData.BlockShapes(315).GetCollisionShapes(client.State.World.GetBlock(BlockPos.Containing(Position))).ToArray());
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertOwned(session, Position);
        await AssertPlacementSwing(session);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("soul_sand")]
    public async Task NativeCustomInsideCallbackRemainsUnknownWhenFinalBodyVisitsItsCell(string block)
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

    [Theory]
    [InlineData("tnt")]
    [InlineData("slime")]
    [InlineData("hay_block")]
    public async Task QueryRelevantUnitBoxDoesNotInvokeAnUnvisitedInsideOrLandingCallback(string block)
    {
        var cell = new BlockPos(18, 62, 169);
        await using BlockUseSession session = await Prepare(block, cell);
        await session.Client.InvokeAsync(client =>
        {
            Aabb shape = Assert.Single(JavaGameData.BlockShapes(315).GetCollisionShapes(client.State.World.GetBlock(cell)).ToArray()).Move(cell.X, cell.Y, cell.Z);
            Aabb original = VanillaEntityBounds.Create(Position, .8F, .8F).Box;
            Assert.False(shape.Intersects(original));
            Assert.True(shape.Intersects(original.ExpandTowards(0, .6F, 0)));
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertOwned(session, Position);
        await AssertPlacementSwing(session);
    }

    [Fact]
    public async Task NativeInterpolationPrecedesQueryAndEmptyVineDoesNotBecomeAnObstacle()
    {
        var initial = new Vec3d(99.36, 61.93, 234.207);
        var target = new Vec3d(99.6, 61.91, 234.156);
        await using BlockUseSession session = await Prepare("vine", new(100, 63, 235), initial, 1493, -119, -574);
        var packet = new ClientboundTeleportEntityPacket(SquidId, target.X, target.Y, target.Z, 0, 0, false, null, 0);
        var decoded = Assert.IsType<ClientboundTeleportEntityPacket>(BoundDescriptorCodec.RoundTrip(315, "teleport_entity", packet));
        Assert.Equal(SquidId, decoded.EntityId);
        Assert.Equal(target, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());

        await session.Tick();
        Assert.Empty(await session.Barrier());
        Vec3d interpolated = new(initial.X + (target.X - initial.X) / 3, initial.Y + (target.Y - initial.Y) / 3, initial.Z + (target.Z - initial.Z) / 3);
        Aabb expectedBounds = VanillaEntityBounds.Create(interpolated, .8F, .8F).Box.Move(1493 / 8000.0F, -119 / 8000.0F, -574 / 8000.0F);
        Vec3d expected = new((expectedBounds.MinX + expectedBounds.MaxX) / 2, expectedBounds.MinY, (expectedBounds.MinZ + expectedBounds.MaxZ) / 2);
        await AssertOwned(session, expected, expectedBounds);
        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All);
            Assert.True(client.State.EntityMotion.TryGetLegacyAmbientInterpolation(squid, out var motion));
            Assert.Equal(2, motion.RemainingSteps);
            Assert.Equal(target, motion.Target);
            return true;
        }, session.Token);
        await AssertPlacementSwing(session);
    }

    [Fact]
    public async Task NativeInsideAreaAllowsAnAbsentHorizontalNeighborChunk()
    {
        var initial = new Vec3d(15.75, 61, 169.5);
        await using BlockUseSession session = await Prepare("air", new(15, 63, 169), initial);
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMembership.HasColumn(new(15, 61, 169)));
            Assert.False(client.State.EntityMembership.HasColumn(new(16, 61, 169)));
            Assert.Null(client.State.World.GetColumn(new BlockPos(16, 61, 169)));
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertOwned(session, initial);
        await AssertPlacementSwing(session);
    }

    [Theory]
    [InlineData("stone", false)]
    [InlineData("slime", true)]
    [InlineData("hay_block", true)]
    public async Task FinalFloorCallbackEligibilityFollowsYClippingAndGroundedFall(string block, bool unknown)
    {
        var initial = new Vec3d(18.5, 62.5, 169.5);
        var floor = new BlockPos(18, 61, 169);
        await using BlockUseSession session = await Prepare("air", floor, initial, 0, -2000, 0);
        Aabb expected = VanillaEntityBounds.Create(initial, .8F, .8F).Box;
        double velocityY = -.25;
        for (int tick = 0; tick < 2; tick++)
        {
            velocityY *= .98;
            expected = expected.Move(0, velocityY, 0);
            await session.Tick();
            Assert.Empty(await session.Barrier());
            await AssertOwned(session, new(18.5, expected.MinY, 169.5), expected);
            Assert.False(Assert.Single(session.Client.State.Entities.All).OnGround);
        }
        Assert.True(expected.MinY > 62);
        Assert.True(expected.MinY + velocityY * .98 < 62);
        await session.Block(floor, block);
        Assert.Empty(await session.Barrier());

        await session.Tick();
        Assert.Empty(await session.Barrier());
        if (unknown)
        {
            await session.Client.InvokeAsync(client =>
            {
                Entity squid = Assert.Single(client.State.Entities.All);
                Assert.False(client.State.EntityGeometry.IsPositionKnown(squid));
                Assert.Null(client.State.EntityMembership.IsWorldQueryMember(squid));
                return true;
            }, session.Token);
            Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
            return;
        }
        expected = expected.Move(0, 62 - expected.MinY, 0);
        await AssertOwned(session, new(18.5, 62, 169.5), expected);
        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All);
            Assert.True(squid.OnGround);
            Assert.Equal(Vec3d.Zero, squid.Velocity);
            return true;
        }, session.Token);
        await AssertPlacementSwing(session);
    }

    private static async Task AssertOwned(BlockUseSession session, Vec3d expected, Aabb? expectedBounds = null)
    {
        await session.Client.InvokeAsync(client =>
        {
            Entity squid = Assert.Single(client.State.Entities.All);
            Assert.True(client.State.EntityGeometry.IsPositionKnown(squid));
            Assert.Equal(expected, squid.Position);
            Assert.True(client.State.EntityGeometry.TryGet(squid, JavaGameData.EntityGeometryData(315), out Aabb bounds, out bool blocks, 315));
            Assert.Equal(expectedBounds ?? VanillaEntityBounds.Create(expected, .8F, .8F).Box, bounds);
            Assert.True(blocks);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(squid));
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(squid, out var membership));
            Assert.True(membership.RegistrationKnown);
            Assert.Equal(((int)Math.Floor(expected.X) >> 4, (int)Math.Floor(expected.Y) >> 4, (int)Math.Floor(expected.Z) >> 4), (membership.X, membership.Y, membership.Z));
            return true;
        }, session.Token);
    }

    private static async Task AssertPlacementSwing(BlockUseSession session)
    {
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

    private static async Task<BlockUseSession> Prepare(string block, BlockPos cell, Vec3d? initial = null, short velocityX = 0, short velocityY = 0, short velocityZ = 0)
    {
        Vec3d position = initial ?? Position;
        BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        try
        {
            await session.Block(BlockUseSession.Target.Above(), "iron_door", upper: true);
            await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
            await session.Hold("stone");
            await session.Terrain(cell.X >> 4, cell.Z >> 4);
            await session.Block(cell, block);
            var packet = new ClientboundAddMobPacket(SquidId, 94, position.X, position.Y, position.Z, 0, 0, 0, velocityX, velocityY, velocityZ, new EntityMetadataList([], []), Guid.Empty);
            var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(315, "add_mob", packet));
            Assert.Equal(SquidId, decoded.EntityId);
            Assert.Equal(94, decoded.TypeId);
            Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
            Assert.Equal((velocityX, velocityY, velocityZ), (decoded.VelocityX, decoded.VelocityY, decoded.VelocityZ));
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
