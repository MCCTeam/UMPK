using Umpk.Client.Actions;
using Umpk.Client.Internal;
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
    [InlineData(93, "chicken", false)]
    [InlineData(93, "chicken", true)]
    [InlineData(54, "zombie", false)]
    [InlineData(54, "zombie", true)]
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
    [InlineData(93, "chicken", false)]
    [InlineData(93, "chicken", true)]
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
    public async Task HardCollisionKnowledgeDoesNotInventPeerPlacementGeometry(bool boat, bool overlapsSquid)
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

        if (boat)
            await AssertUnknown(session);
        else
        {
            await AssertKnown(session);
            await AssertPeerPlacementGeometryUnknown(session);
        }
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    public async Task MinecartSubtypeHardBoxDoesNotMasqueradeAsPlacementGeometry(int nativeSubtype, bool overlapsSquid)
    {
        await using BlockUseSession session = await PrepareBase();
        Vec3d position = PeerPosition(overlapsSquid);
        var packet = new ClientboundAddEntityPacket(PeerId, Guid.Empty, 10, position.X, position.Y, position.Z, 0, 0, 0, nativeSubtype, 0, 0, 0, null);
        var decoded = Assert.IsType<ClientboundAddEntityPacket>(BoundDescriptorCodec.RoundTrip(315, "add_entity", packet));
        Assert.Equal(PeerId, decoded.EntityId);
        Assert.Equal(10, decoded.TypeId);
        Assert.Equal(nativeSubtype, decoded.Data);
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity peer = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            // The current object caller retains the subtype data on the frame but resolves its shared type key as minecart. Every original subtype inherits the same null hard getter.
            Assert.Equal(Identifier.Minecraft("minecart"), peer.Type.Id);
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertKnown(session);
        await AssertPeerPlacementGeometryUnknown(session);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    [Theory]
    [InlineData(69, "shulker", false)]
    [InlineData(69, "shulker", true)]
    [InlineData(999, "unknown", false)]
    [InlineData(999, "unknown", true)]
    public async Task NativeHardAndUnresolvedIdentityControlsRetainUnknown(int type, string family, bool overlapsSquid)
    {
        await using BlockUseSession session = await PrepareBase();
        await Spawn(session, PeerId, type, family, PeerPosition(overlapsSquid));

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertUnknown(session);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PacketEstablishedPlayerIdentityCanProveItsNullHardGetter(bool overlapsSquid)
    {
        await using BlockUseSession session = await PrepareBase();
        await SpawnPlayer(session, PeerPosition(overlapsSquid));

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertKnown(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementUnknownTypeDoesNotInheritThePreviousPlayersIdentity(bool overlapsSquid)
    {
        await using BlockUseSession session = await PrepareBase();
        Vec3d position = PeerPosition(overlapsSquid);
        await SpawnPlayer(session, position);
        Entity original = await session.Client.InvokeAsync(client => Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId), session.Token);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertKnown(session);
        await Spawn(session, PeerId, 999, "unknown", position);
        await session.Client.InvokeAsync(client =>
        {
            Entity replacement = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            Assert.NotSame(original, replacement);
            Assert.Equal(original.Type.Id, replacement.Type.Id);
            Assert.False(client.State.EntityMotion.IsTrackedPlayer(original));
            Assert.False(client.State.EntityMotion.IsTrackedPlayer(replacement));
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertUnknown(session);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(await UseStone(session)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PacketEstablishedXpOrbIdentityProvesOnlyItsNativeNullHardGetter(bool overlapsSquid)
    {
        await using BlockUseSession session = await PrepareBase();
        Vec3d position = PeerPosition(overlapsSquid);
        var packet = new ClientboundAddExperienceOrbPacket(PeerId, position.X, position.Y, position.Z, 5);
        var decoded = Assert.IsType<ClientboundAddExperienceOrbPacket>(BoundDescriptorCodec.RoundTrip(315, "add_experience_orb", packet));
        Assert.Equal(packet, decoded);
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity peer = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            Assert.Equal(Identifier.Minecraft("xp_orb"), peer.Type.Id);
            Assert.False(client.State.EntityMotion.IsTrackedPlayer(peer));
            Assert.True(LegacyAmbientMobProfile.HasNoHardPeerBox(peer.Type.Id));
            return true;
        }, session.Token);

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await AssertKnown(session);
    }

    [Theory]
    [InlineData("minecraft:arrow", true)]
    [InlineData("minecraft:fireball", true)]
    [InlineData("minecraft:guardian", true)]
    [InlineData("minecraft:horse", true)]
    [InlineData("minecraft:minecart", true)]
    [InlineData("minecraft:pig", true)]
    [InlineData("minecraft:spider", true)]
    [InlineData("minecraft:zombie", true)]
    [InlineData("minecraft:boat", false)]
    [InlineData("minecraft:shulker", false)]
    [InlineData("minecraft:unknown", false)]
    [InlineData("minecraft:experience_orb", false)]
    [InlineData("example:chicken", false)]
    [InlineData("example:minecart", false)]
    [InlineData("minecraft:chicke", false)]
    [InlineData("minecraft:chicken_x", false)]
    [InlineData("minecraft:x_chicken", false)]
    [InlineData("minecraft:chicken/zombie", false)]
    [InlineData("minecraft:mine", false)]
    public void FiniteHardGetterClassificationRequiresExactNativeNames(string identifier, bool expected)
        => Assert.Equal(expected, LegacyAmbientMobProfile.HasNoHardPeerBox(Identifier.Parse(identifier)));

    [Fact]
    public void DefaultIdentifierCannotProveAHardGetter()
        => Assert.False(LegacyAmbientMobProfile.HasNoHardPeerBox(default));

    private static async Task<BlockUseSession> PrepareBase()
    {
        BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        try
        {
            await PrepareDoor(session);
            await session.Hold("stone");
            await Spawn(session, SquidId, 94, "squid", SquidPosition);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task SpawnPlayer(BlockUseSession session, Vec3d position)
    {
        var packet = new ClientboundAddPlayerPacket(PeerId, new Guid(PeerId, 0, 0, new byte[8]), position.X, position.Y, position.Z, 0, 0, 0, new EntityMetadataList([], []));
        var decoded = Assert.IsType<ClientboundAddPlayerPacket>(BoundDescriptorCodec.RoundTrip(315, "add_player", packet));
        Assert.Equal(packet.EntityId, decoded.EntityId);
        Assert.Equal(packet.Uuid, decoded.Uuid);
        Assert.Equal(position, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        Assert.Equal(0F, decoded.Yaw);
        Assert.Equal(0F, decoded.Pitch);
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity peer = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            Assert.Equal(Identifier.Minecraft("unknown"), peer.Type.Id);
            Assert.True(client.State.EntityMotion.IsTrackedPlayer(peer));
            Assert.False(LegacyAmbientMobProfile.HasNoHardPeerBox(peer.Type.Id));
            return true;
        }, session.Token);
    }

    private static Task AssertKnown(BlockUseSession session)
        => session.Client.InvokeAsync(client =>
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
            return true;
        }, session.Token);

    private static Task AssertPeerPlacementGeometryUnknown(BlockUseSession session)
        => session.Client.InvokeAsync(client =>
        {
            Entity peer = Assert.Single(client.State.Entities.All, entity => entity.Id == PeerId);
            Assert.False(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(peer.Type.Id));
            Assert.False(client.State.EntityGeometry.TryGet(peer, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.False(JavaGameData.BlockInteractionData(315).TryGetFixedEntityDimensions(peer.Type.Id, out _, out _));
            return true;
        }, session.Token);

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
