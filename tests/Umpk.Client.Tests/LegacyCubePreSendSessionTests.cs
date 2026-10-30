using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class LegacyCubePreSendSessionTests
{
    private static readonly int[] LegacyProtocols = [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];
    private static IEnumerable<int> Protocols => LegacyProtocols.Append(393);

    public static IEnumerable<object[]> ObstructionCases() => Protocols.SelectMany(protocol => new[] { "occupied", "self", "entity" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(ObstructionCases))]
    public async Task KnownCubeObstructionIsRejectedBeforeTheLegacyUsePacket(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        if (condition == "occupied")
            await session.Block(Session.Target.Above(), "stone");
        else if (condition == "self")
            await Position(session, protocol, new(1.5, 66, 1.5));
        else
            await Villager(session, protocol, new(1.5, 66, 1.5));
        Assert.Empty(await session.Barrier());

        List<IPacket> packets = await session.Use(Hand.Main);

        if (protocol < 393)
            Assert.Empty(packets);
        else
            AssertUse(packets, protocol, swings: false, session);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> ProtocolCases() => Protocols.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task ClearCubePlacementStillSendsUseThenTheNativeSwing(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");

        AssertUse(await session.Use(Hand.Main), protocol, swings: true, session);

        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> OrderingCases()
    {
        foreach (int protocol in new[] { 47, 107, 340, 393 })
            foreach (bool blocked in new[] { false, true })
                foreach (string mode in protocol == 47 ? new[] { "adventure" } : new[] { "adventure", "cooldown" })
                    yield return [protocol, blocked, mode];
    }

    [Theory]
    [MemberData(nameof(OrderingCases))]
    public async Task NativePrecheckPrecedesTheItemCooldownAndAdventureUsePermission(int protocol, bool blocked, string mode)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        if (blocked)
            await session.Block(Session.Target.Above(), "stone");
        if (mode == "adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        else
            await session.Cooldown();
        Assert.Empty(await session.Barrier());

        List<IPacket> packets = await session.Use(Hand.Main);

        if (blocked && protocol < 393)
            Assert.Empty(packets);
        else
            AssertUse(packets, protocol, swings: false, session);
    }

    public static IEnumerable<object[]> ActivationCases() => Protocols.SelectMany(protocol => new[] { "activation", "secondary", "spectator", "empty" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(ActivationCases))]
    public async Task ActivationAndSpectatorBypassTheItemPrecheckWhileSecondaryUseDoesNot(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "crafting_table", physics: true);
        if (condition != "empty")
            await session.Hold("stone");
        await session.Block(Session.Target.Above(), "stone");
        if (condition == "secondary")
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (condition == "spectator")
            await session.Mode(GameMode.Spectator, instantBuild: false);
        Assert.Empty(await session.Barrier());

        List<IPacket> packets = await session.Use(Hand.Main);

        if (condition == "secondary" && protocol < 393)
            Assert.Empty(packets);
        else
            AssertUse(packets, protocol, swings: condition != "secondary", session);
    }

    public static IEnumerable<object[]> UnknownCases()
    {
        foreach (int protocol in Protocols)
            foreach (string condition in new[] { "physics", "body", "terrain", "custom" })
                if (protocol < 393 || condition != "terrain")
                    yield return [protocol, condition];
    }

    [Theory]
    [MemberData(nameof(UnknownCases))]
    public async Task UnclassifiedContextRetainsThePacketWithoutInventingClearance(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "stone", terrain: condition != "terrain", physics: condition != "physics");
        await session.Hold("stone", custom: condition == "custom");
        if (condition == "body")
        {
            await Send(session, protocol, new ClientboundAddEntityPacket(42, Guid.Empty, 1, 1.5, 66, 1.5, 0, 0, 0, 0, 0, 0, 0, null));
            Assert.Empty(await session.Barrier());
            await session.Client.InvokeAsync(client =>
            {
                Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
                Assert.Equal(Identifier.Minecraft("boat"), actor.Type.Id);
                Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
                return true;
            }, session.Token);
        }
        if (condition == "terrain")
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }

        AssertUse(await session.Use(Hand.Main), protocol, swings: false, session);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(393)]
    public async Task EmptyHandDoesNotRunTheBlockItemPrecheck(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Block(Session.Target.Above(), "stone");

        AssertUse(await session.Use(Hand.Main), protocol, swings: false, session);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(393)]
    public async Task SnowPrecheckUsesItsNativeCellBeforeTheOriginalFacePlacement(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        await session.Block(Session.Target.Below(), "stone");
        BlockDefinition snow = session.Client.State.Registries!.Blocks[Identifier.Minecraft(protocol < 393 ? "snow_layer" : "snow")].Value;
        int state = protocol < 393 ? snow.DefaultStateId | 1 : Enumerable.Range(snow.MinStateId, snow.StateCount).First(id => snow.TryGetPropertyValue(id, "layers", out string layers) && layers == "2");
        await Send(session, protocol, new ClientboundBlockUpdatePacket(Session.Target, state));
        await Villager(session, protocol, new(1.5, 64, 1.5), intersectsDestination: false);
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);

        List<IPacket> packets = await session.Use(Hand.Main);

        if (protocol < 393)
            Assert.Empty(packets);
        else
            AssertUse(packets, protocol, swings: true, session);
    }

    public static IEnumerable<object[]> HeightCases() => new[] { 47, 107, 340, 393 }.SelectMany(protocol => new[] { false, true }.Select(below => new object[] { protocol, below }));

    [Theory]
    [MemberData(nameof(HeightCases))]
    public async Task OutOfHeightDestinationDoesNotBecomeAProvenPreSendRejection(int protocol, bool below)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        var clicked = new BlockPos(1, below ? 0 : 255, 1);
        Direction face = below ? Direction.Down : Direction.Up;
        await session.Block(clicked, "stone");
        Assert.Empty(await session.Barrier());

        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, new(.5, .5, .5), ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);

        Assert.Single(packets);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(packets, packet => packet is ServerboundLegacyBlockPlacePacket));
            Assert.Equal((clicked, (int)face, (byte)8, (byte)8, (byte)8), (use.Position, use.Face, use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(packets, packet => packet is ServerboundUseItemOnPacket));
            Assert.Equal((clicked, (int)face, .5f, .5f, .5f), (use.Position, use.Face, use.CursorX, use.CursorY, use.CursorZ));
        }
    }

    private static void AssertUse(List<IPacket> packets, int protocol, bool swings, Session session)
    {
        Assert.Equal(swings ? 2 : 1, packets.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal(Session.Target, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
            ItemStack held = session.Client.State.Inventory.GetSlot(36);
            Assert.Equal(held.IsEmpty, use.HeldItem.IsEmpty);
            if (!held.IsEmpty)
                Assert.Equal((held.Item.Id, held.Count, held.Damage), (use.HeldItem.Item.Id, use.HeldItem.Count, use.HeldItem.Damage));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((0, Session.Target, (int)Direction.Up), (use.Hand, use.Position, use.Face));
            Assert.Equal((.5f, 1f, .5f), (use.CursorX, use.CursorY, use.CursorZ));
        }
        if (swings)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
    }

    private static async Task Position(Session session, int protocol, Vec3d position)
    {
        await Send(session, protocol, new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Self.Position);
    }

    private static async Task Villager(Session session, int protocol, Vec3d position, bool intersectsDestination = true)
    {
        RegistryEntry<EntityTypeDefinition> type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("villager")];
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(type.Id, out EntityGeometryRule rule));
        MetadataValue age = protocol == 47 ? MetadataValue.Byte(0) : MetadataValue.Boolean(false);
        var metadata = new EntityMetadataList([new(rule.MetadataIndex, age)], []);
        await Send(session, protocol, new ClientboundAddMobPacket(42, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, metadata));
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.Equal(type.Id, actor.Type.Id);
            Assert.Equal(position, actor.Position);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool building, protocol));
            Assert.True(building);
            Assert.Equal(intersectsDestination, Aabb.BlockAt(Session.Target.X, Session.Target.Y + 1, Session.Target.Z).Intersects(box));
            if (!intersectsDestination)
                Assert.True(Aabb.BlockAt(Session.Target.X, Session.Target.Y, Session.Target.Z).Intersects(box));
            return true;
        }, session.Token);
    }

    private static async Task Send(Session session, int protocol, IPacket packet)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var pair = Assert.Single(registry.Packets, entry => entry.Type.Id == packet.Type.Id);
        Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        if (packet is ClientboundPlayerPositionPacket position)
        {
            var observed = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
            Assert.Equal((position.X, position.Y, position.Z), (observed.X, observed.Y, observed.Z));
        }
        else if (packet is ClientboundAddMobPacket mob)
        {
            var observed = Assert.IsType<ClientboundAddMobPacket>(decoded);
            Assert.Equal((mob.EntityId, mob.TypeId, mob.X, mob.Y, mob.Z), (observed.EntityId, observed.TypeId, observed.X, observed.Y, observed.Z));
            Assert.Equal(mob.Metadata.Entries.Count, observed.Metadata.Entries.Count);
            for (int index = 0; index < mob.Metadata.Entries.Count; index++)
            {
                Assert.Equal(mob.Metadata.Entries[index].Index, observed.Metadata.Entries[index].Index);
                Assert.Equal(mob.Metadata.Entries[index].Value, observed.Metadata.Entries[index].Value);
                Assert.True(observed.Metadata.Entries[index].WireSerializerId >= 0);
            }
        }
        else if (packet is ClientboundBlockUpdatePacket block)
            Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
        else if (packet is ClientboundAddEntityPacket actor)
        {
            var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
            Assert.Equal((actor.EntityId, actor.TypeId, actor.X, actor.Y, actor.Z), (observed.EntityId, observed.TypeId, observed.X, observed.Y, observed.Z));
        }
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(pair.WireId, bytes, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
