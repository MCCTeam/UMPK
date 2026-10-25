using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyExperienceOrbPlacementRuntimeTests
{
    private const int OrbId = 4242;

    public static IEnumerable<object[]> ProvenNonparticipants()
    {
        foreach ((int protocol, string canonical) in new (int, string)[]
        {
            (47, "experience_orb"),
            (107, "xp_orb"),
            (108, "xp_orb"),
            (109, "xp_orb"),
            (110, "xp_orb"),
            (210, "xp_orb"),
            (316, "xp_orb"),
            (335, "xp_orb"),
            (338, "xp_orb"),
            (340, "xp_orb"),
            (393, "experience_orb"),
            (401, "experience_orb"),
            (404, "experience_orb"),
            (477, "experience_orb"),
            (480, "experience_orb"),
            (485, "experience_orb"),
            (490, "experience_orb"),
            (498, "experience_orb"),
        })
        {
            yield return [protocol, canonical, false];
            yield return [protocol, canonical, true];
        }
    }

    [Theory]
    [MemberData(nameof(ProvenNonparticipants))]
    public async Task ProvenXpNonparticipationAllowsNativeUseAndSwingWithoutOwnedMotion(int protocol, string canonical, bool overlapsPlacement)
    {
        await using BlockUseSession session = await Prepare(protocol);
        Vec3d position = overlapsPlacement ? new(.5, 65.125, 1.5) : new(14.5, 65.125, 14.5);
        await Spawn(session, position);
        await session.Client.InvokeAsync(client =>
        {
            Entity orb = Assert.Single(client.State.Entities.All);
            Assert.Equal(Identifier.Minecraft(canonical), orb.Type.Id);
            Assert.Equal(position, orb.Position);
            var source = JavaGameData.EntityGeometryData(protocol);
            Assert.False(source.TryGetRule(orb.Type.Id, out _));
            Assert.False(client.State.EntityGeometry.TryGet(orb, source, out _, out _, protocol));
            return true;
        }, session.Token);

        List<IPacket> sent = await Use(session);

        Assert.Collection(sent,
            packet => AssertUse(packet, session),
            packet => Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packet).Hand));
        Assert.True(JavaGameData.BlockInteractionData(protocol).IsAlwaysNonObstructingEntity(Identifier.Minecraft(canonical)));
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(490)]
    public async Task UnresolvedXpIdentityKeepsPacketOnlyPredictionAndDoesNotBecomeClear(int protocol)
    {
        await using BlockUseSession session = await Prepare(protocol);
        await session.Client.InvokeAsync(client =>
        {
            var types = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType);
            types.Add(2, Identifier.Minecraft("zombie"), new EntityTypeDefinition(.6F, 1.8F));
            client.State.Registries = RegistryAccess.FromSnapshot(client.State.Registries!.Snapshot.With(types.Build()));
            return true;
        }, session.Token);
        await Spawn(session, new(.5, 65.125, 1.5));
        await session.Client.InvokeAsync(client =>
        {
            Entity orb = Assert.Single(client.State.Entities.All);
            Assert.Equal(Identifier.Minecraft("unknown"), orb.Type.Id);
            Assert.False(JavaGameData.BlockInteractionData(protocol).IsAlwaysNonObstructingEntity(orb.Type.Id));
            return true;
        }, session.Token);

        List<IPacket> sent = await Use(session);

        Assert.Collection(sent, packet => AssertUse(packet, session));
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    private static async Task<BlockUseSession> Prepare(int protocol)
    {
        BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        try
        {
            await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
            await session.Hold("stone");
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task Spawn(BlockUseSession session, Vec3d position)
    {
        var packet = new ClientboundAddExperienceOrbPacket(OrbId, position.X, position.Y, position.Z, 7);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "add_experience_orb");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundAddExperienceOrbPacket>(codec.Decode(bytes, context));
        Assert.Equal(packet, decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static async Task<List<IPacket>> Use(BlockUseSession session)
    {
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(BlockUseSession.Target, Direction.West, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> sent = await session.Barrier();
        await action.WaitAsync(session.Token);
        return sent;
    }

    private static void AssertUse(IPacket packet, BlockUseSession session)
    {
        if (session.Protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packet);
            Assert.Equal(BlockUseSession.Target, use.Position);
            Assert.Equal((int)Direction.West, use.Face);
            Assert.Equal(Identifier.Minecraft("stone"), use.HeldItem.Item.Id);
            Assert.Equal(1, use.HeldItem.Count);
            Assert.Equal((byte)0, use.CursorX);
            Assert.Equal((byte)8, use.CursorY);
            Assert.Equal((byte)8, use.CursorZ);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
            Assert.Equal(BlockUseSession.Target, use.Position);
            Assert.Equal((int)Direction.West, use.Face);
            Assert.Equal((int)Hand.Main, use.Hand);
            Assert.Equal(0F, use.CursorX);
            Assert.Equal(.5F, use.CursorY);
            Assert.Equal(.5F, use.CursorZ);
        }
    }
}
