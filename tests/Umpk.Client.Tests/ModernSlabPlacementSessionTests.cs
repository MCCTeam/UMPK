using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
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

public sealed class ModernSlabPlacementSessionTests
{
    private static readonly int[] ModernProtocols = [393, 401, 404, 477, 480, 485, 490, 498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly int[] BoundaryProtocols = [393, 404, 477, 498, 755, 761, 766, 768, 769, 777];

    public static IEnumerable<object[]> ProtocolCases() => ModernProtocols.Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> CatalogCases() => ModernProtocols.SelectMany(protocol => JavaGameData.Registries(protocol).Items.Where(item => item.Id.Path.EndsWith("_slab", StringComparison.Ordinal)).Select(item => new object[] { protocol, item.Id.Path }));

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task ClearNativeSlabPlacementHasTheRequiredSuccessAnimation(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_slab");

        List<IPacket> packets = await session.Use(Hand.Main);

        Assert.Equal(protocol == 777 ? 1 : 2, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal(Session.Target, use.Position);
        if (protocol != 777)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Fact]
    public void FiniteModernCatalogContainsTheSourceBoundItemRows()
    {
        Assert.Equal(1924, CatalogCases().Count());
        Assert.Equal(101, CatalogCases().Select(row => (string)row[1]).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task EveryRegisteredModernSlabUsesTheNativeDefaultPlacement(int protocol, string item)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(item);
        Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(Identifier.Minecraft(item), out ItemUseOnInfo contract));
        Assert.Equal(ItemUseOnKind.Unknown, contract.Kind);
        Assert.Equal(Identifier.Minecraft(item), contract.PlacedBlock);

        await AssertPredictionAndUse(session, protocol, BlockUseResult.Success);

        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> MergeCases() => BoundaryProtocols.SelectMany(protocol => new[] { "clicked-bottom", "clicked-top", "side-bottom-upper", "side-bottom-lower", "side-top-lower", "adjacent-top", "adjacent-bottom", "different-clicked", "different-adjacent", "double-clicked", "double-adjacent", "waterlogged-merge", "water-destination" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(MergeCases))]
    public async Task MatchingSlabReplacementAndAdjacentFallbackUseTheResultingShape(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_slab");
        Direction face = Direction.Up;
        Vec3d cursor = new(.5, 1, .5);
        BlockUseResult expected = condition is "side-bottom-lower" or "different-adjacent" or "double-adjacent" ? BlockUseResult.Fail : BlockUseResult.Success;
        if (condition is "clicked-bottom" or "side-bottom-upper" or "side-bottom-lower")
            await Slab(session, protocol, Session.Target, "oak_slab", "bottom");
        if (condition is "clicked-top" or "side-top-lower" or "waterlogged-merge")
            await Slab(session, protocol, Session.Target, "oak_slab", "top", waterlogged: condition == "waterlogged-merge");
        if (condition is "clicked-top" or "waterlogged-merge")
        {
            face = Direction.Down;
            cursor = new(.5, 0, .5);
        }
        if (condition.StartsWith("side-", StringComparison.Ordinal))
        {
            face = Direction.East;
            cursor = new(1, condition == "side-bottom-upper" ? .75 : .25, .5);
            await session.Block(Session.Target.Offset(face), "stone");
        }
        if (condition is "adjacent-top" or "adjacent-bottom")
            await Slab(session, protocol, Session.Target.Above(), "oak_slab", condition == "adjacent-top" ? "top" : "bottom");
        if (condition == "different-clicked")
            await Slab(session, protocol, Session.Target, "birch_slab", "bottom");
        if (condition == "different-adjacent")
            await Slab(session, protocol, Session.Target.Above(), "birch_slab", "bottom");
        if (condition == "double-clicked")
            await Slab(session, protocol, Session.Target, "oak_slab", "double");
        if (condition == "double-adjacent")
            await Slab(session, protocol, Session.Target.Above(), "oak_slab", "double");
        if (condition == "water-destination")
            await session.Block(Session.Target.Above(), "water");
        Assert.Empty(await session.Barrier());
        int original = session.Client.State.World.GetBlock(Session.Target).StateId;
        int neighbor = session.Client.State.World.GetBlock(Session.Target.Offset(face)).StateId;

        await AssertPredictionAndUse(session, protocol, expected, face: face, cursor: cursor);

        Assert.Equal(original, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(neighbor, session.Client.State.World.GetBlock(Session.Target.Offset(face)).StateId);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> HalfCases() => BoundaryProtocols.SelectMany(protocol => new[] { "bottom-lower", "bottom-upper", "top-lower", "top-upper", "double-lower", "double-upper" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(HalfCases))]
    public async Task ObstructionUsesOnlyTheSelectedHalfAndTheFullMergeResult(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_slab");
        bool upper = condition.EndsWith("upper", StringComparison.Ordinal);
        bool top = condition.StartsWith("top", StringComparison.Ordinal);
        bool merge = condition.StartsWith("double", StringComparison.Ordinal);
        Direction face = merge ? Direction.Up : Direction.East;
        Vec3d cursor = new(.5, top ? .75 : .25, .5);
        BlockPos destination = merge ? Session.Target : Session.Target.Offset(face);
        if (merge)
            await Slab(session, protocol, Session.Target, "oak_slab", "bottom");
        await Villager(session, protocol, new(destination.X + .5, destination.Y + (upper ? .75 : -1.75), destination.Z + .5));
        Aabb bounds = await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool building, protocol));
            Assert.True(building);
            return box;
        }, session.Token);
        Assert.Equal(upper, bounds.MinY > destination.Y + .5);
        Assert.Equal(!upper, bounds.MaxY < destination.Y + .5);
        BlockUseResult expected = merge || top == upper ? BlockUseResult.Fail : BlockUseResult.Success;

        await AssertPredictionAndUse(session, protocol, expected, face: face, cursor: cursor);
    }

    // The original SlabBlock uses a float hit offset through protocol 404 and an absolute double hit minus clicked Y later. At Target.Y = 65, these source-backed outcomes include addition cancellation and the first float above 0.5.
    public static IEnumerable<object[]> CursorCases()
    {
        (double Y, bool FloatEraTop, bool AbsoluteDoubleEraTop)[] truth =
        [
            (.5, false, false),
            (Math.BitIncrement(.5), false, false),
            (.500000001, false, true),
            ((double)MathF.BitIncrement(.5F), true, true),
            (.50001, true, true),
        ];
        return new[] { 393, 404, 477, 755, 777 }.SelectMany(protocol => truth.Select(point => new object[] { protocol, point.Y, protocol <= 404 ? point.FloatEraTop : point.AbsoluteDoubleEraTop }));
    }

    [Theory]
    [MemberData(nameof(CursorCases))]
    public async Task CursorComparisonUsesItsNativeFloatOrAbsoluteDoubleArithmetic(int protocol, double y, bool top)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_slab");
        BlockPos destination = Session.Target.Offset(Direction.East);
        await Villager(session, protocol, new(destination.X + .5, destination.Y + .75, destination.Z + .5));
        foreach (bool inside in new[] { false, true })
            await AssertPredictionAndUse(session, protocol, top ? BlockUseResult.Fail : BlockUseResult.Success, face: Direction.East, cursor: new(1, y, .5), inside: inside);
    }

    public static IEnumerable<object[]> UnknownCases() => BoundaryProtocols.SelectMany(protocol => new[] { "physics", "terrain", "body", "far-body", "custom", "unloaded", "stairs" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(UnknownCases))]
    public async Task MissingContextAndOtherSpecializedItemsRemainPacketOnlyUnknown(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, "stone", terrain: condition != "terrain", physics: condition != "physics");
        await session.Hold(condition == "stairs" ? "oak_stairs" : "oak_slab", custom: condition == "custom");
        if (condition is "body" or "far-body")
            await UnknownBody(session, protocol, condition == "body" ? new(1.5, 66, 1.5) : new(100.5, 66, 100.5));
        if (condition == "unloaded")
        {
            await session.Forget();
            await session.Block(Session.Target, "stone");
            await session.Block(Session.Target.Above(), "air");
        }
        await AssertPredictionAndUse(session, protocol, BlockUseResult.Unknown);
        if (condition is "body" or "far-body")
        {
            await Send(session, protocol, new ClientboundRemoveEntitiesPacket([42]));
            Assert.Empty(await session.Barrier());
            Assert.Null(session.Client.State.Entities.Get(42));
            await AssertPredictionAndUse(session, protocol, BlockUseResult.Success);
        }
    }

    public static IEnumerable<object[]> OrderingCases() => BoundaryProtocols.SelectMany(protocol => new[] { "offhand", "activation", "secondary-blocked", "cooldown", "adventure", "spectator", "empty" }.Select(condition => new object[] { protocol, condition }));

    [Theory]
    [MemberData(nameof(OrderingCases))]
    public async Task HandsAndExistingActivationAndItemGatesKeepTheirOrder(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol, condition is "activation" or "secondary-blocked" ? "crafting_table" : "stone", physics: true);
        await session.Hold(condition == "empty" ? null : "oak_slab", custom: condition == "activation");
        Hand hand = Hand.Main;
        BlockUseResult expected = BlockUseResult.Success;
        if (condition == "offhand")
        {
            await session.Hold(null);
            await session.Offhand("oak_slab");
            hand = Hand.Off;
        }
        if (condition is "activation" or "secondary-blocked")
            await session.Block(Session.Target.Above(), "stone");
        if (condition == "secondary-blocked")
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            expected = BlockUseResult.Fail;
        }
        if (condition == "cooldown")
        {
            await session.Cooldown();
            expected = BlockUseResult.Pass;
        }
        if (condition == "adventure")
        {
            await session.Mode(GameMode.Adventure, instantBuild: false);
            expected = BlockUseResult.Unknown;
        }
        if (condition == "spectator")
        {
            await session.Mode(GameMode.Spectator, instantBuild: false);
            expected = protocol < 768 ? BlockUseResult.Success : BlockUseResult.Consume;
        }
        if (condition == "empty")
            expected = BlockUseResult.Pass;

        await AssertPredictionAndUse(session, protocol, expected, hand: hand);
    }

    public static IEnumerable<object[]> LifetimeCases() => BoundaryProtocols.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(LifetimeCases))]
    public async Task WorldReplacementCannotReuseEarlierSlabClearance(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_slab");
        await AssertPredictionAndUse(session, protocol, BlockUseResult.Success);
        if (protocol < 735)
        {
            await Send(session, protocol, new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(-1, 0, 0, "flat")));
            Assert.Empty(await session.Barrier());
        }
        else
            await session.Respawn();
        await session.Hold("oak_slab");
        await session.Block(Session.Target, "stone");
        await session.Block(Session.Target.Above(), "air");
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));

        await AssertPredictionAndUse(session, protocol, BlockUseResult.Unknown);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("blocked")]
    [InlineData("merge")]
    [InlineData("merge-blocked")]
    [InlineData("missing-physics")]
    [InlineData("custom")]
    [InlineData("half-clear")]
    [InlineData("half-blocked")]
    public async Task Protocol777KeepsTypedResultSendAndLocalSwingDistinct(string condition)
    {
        const int protocol = 777;
        await using Session session = await Session.Create(protocol, "stone", physics: condition != "missing-physics");
        await session.Hold("oak_slab", custom: condition == "custom");
        BlockUseResult expected = condition is "missing-physics" or "custom" ? BlockUseResult.Unknown : condition is "blocked" or "merge-blocked" or "half-blocked" ? BlockUseResult.Fail : BlockUseResult.Success;
        Direction face = condition.StartsWith("half-", StringComparison.Ordinal) ? Direction.East : Direction.Up;
        Vec3d cursor = new(.5, condition == "half-blocked" ? .75 : .25, .5);
        if (condition == "blocked")
            await session.Block(Session.Target.Above(), "stone");
        if (condition is "merge" or "merge-blocked")
            await Slab(session, protocol, Session.Target, "oak_slab", "bottom");
        if (condition == "merge-blocked")
            await Villager(session, protocol, new(1.5, 65.75, 1.5));
        if (condition.StartsWith("half-", StringComparison.Ordinal))
            await Villager(session, protocol, new(2.5, 65.75, 1.5));

        await AssertPredictionAndUse(session, protocol, expected, face: face, cursor: cursor);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    public async Task LegacySlabRulesRemainUnknown(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("wooden_slab");

        await AssertPredictionAndUse(session, protocol, BlockUseResult.Unknown);
    }

    private static async Task AssertPredictionAndUse(Session session, int protocol, BlockUseResult expected, Hand hand = Hand.Main, Direction face = Direction.Up, Vec3d? cursor = null, bool inside = false)
    {
        Assert.Empty(await session.Barrier());
        Vec3d hit = cursor ?? new(.5, 1, .5);
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, face, hit, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, face, hit, hand, insideBlock: inside, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (protocol == 47)
        {
            var legacy = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal(Session.Target, legacy.Position);
            Assert.Equal((int)face, legacy.Face);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal(((float)hit.X, (float)hit.Y, (float)hit.Z), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.Equal(protocol >= 477 && inside, use.Inside);
            if (protocol >= 759)
            {
                Assert.False(action.IsCompleted);
                await Send(session, protocol, new ClientboundBlockChangedAckPacket(use.Sequence));
            }
        }
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        await action.WaitAsync(session.Token);
    }

    private static async Task Slab(Session session, int protocol, BlockPos cell, string name, string type, bool waterlogged = false)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        int state = Enumerable.Range(block.MinStateId, block.StateCount).Single(id => block.TryGetPropertyValue(id, "type", out string half) && half == type && block.TryGetPropertyValue(id, "waterlogged", out string water) && water == (waterlogged ? "true" : "false"));
        await Send(session, protocol, new ClientboundBlockUpdatePacket(cell, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(cell).StateId);
    }

    private static async Task Villager(Session session, int protocol, Vec3d position)
    {
        RegistryEntry<EntityTypeDefinition> type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("villager")];
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(type.Id, out EntityGeometryRule rule));
        var metadata = new EntityMetadataList([new(rule.MetadataIndex, MetadataValue.Boolean(false))], []);
        if (protocol < 759)
            await Send(session, protocol, new ClientboundAddMobPacket(42, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        else
        {
            await Send(session, protocol, new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        }
        await Send(session, protocol, new ClientboundSetEntityDataPacket(42, metadata));
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Entities.Get(42)!.Position);
    }

    private static async Task UnknownBody(Session session, int protocol, Vec3d position)
    {
        int type = protocol < 477 ? 1 : session.Client.State.Registries!.EntityTypes.First(entry => entry.Id.Path is "boat" or "oak_boat").NetworkId;
        await Send(session, protocol, new ClientboundAddEntityPacket(42, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.Contains(actor.Type.Id.Path, new[] { "boat", "oak_boat" });
            Assert.False(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
            return true;
        }, session.Token);
    }

    private static async Task Send(Session session, int protocol, IPacket packet)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        string name = protocol == 47 && packet is ClientboundBlockUpdatePacket ? "block_change" : packet.Type.Id.Path;
        var pair = Assert.Single(registry.Packets, entry => entry.Type.Id.Path == name);
        Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        switch (packet)
        {
            case ClientboundRespawnPacket respawn:
                Assert.Equal(respawn.Legacy, Assert.IsType<ClientboundRespawnPacket>(decoded).Legacy);
                break;
            case ClientboundBlockUpdatePacket block:
                Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                break;
            case ClientboundBlockChangedAckPacket ack:
                Assert.Equal(ack, Assert.IsType<ClientboundBlockChangedAckPacket>(decoded));
                break;
            case ClientboundAddMobPacket mob:
                var readMob = Assert.IsType<ClientboundAddMobPacket>(decoded);
                Assert.Equal((mob.EntityId, mob.TypeId, mob.X, mob.Y, mob.Z), (readMob.EntityId, readMob.TypeId, readMob.X, readMob.Y, readMob.Z));
                Assert.Equal(mob.Metadata.Entries.Select(entry => (entry.Index, entry.Value)), readMob.Metadata.Entries.Select(entry => (entry.Index, entry.Value)));
                break;
            case ClientboundAddEntityPacket actor:
                var readActor = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal((actor.EntityId, actor.TypeId, actor.X, actor.Y, actor.Z), (readActor.EntityId, readActor.TypeId, readActor.X, readActor.Y, readActor.Z));
                break;
            case ClientboundSetEntityDataPacket metadata:
                Assert.Equal(metadata.Metadata.Entries.Select(entry => (entry.Index, entry.Value)), Assert.IsType<ClientboundSetEntityDataPacket>(decoded).Metadata.Entries.Select(entry => (entry.Index, entry.Value)));
                break;
            case ClientboundRemoveEntitiesPacket remove:
                Assert.Equal(remove.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                break;
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
