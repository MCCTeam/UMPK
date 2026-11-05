using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class ChainPlacementSessionRuntimeTests
{
    private static readonly int[] Protocols = [735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly int[] Boundaries = [735, 751, 773, 777];
    private static readonly string[] CopperNames = ["iron_chain", "copper_chain", "exposed_copper_chain", "weathered_copper_chain", "oxidized_copper_chain", "waxed_copper_chain", "waxed_exposed_copper_chain", "waxed_weathered_copper_chain", "waxed_oxidized_copper_chain"];
    private static readonly string[] Scenarios = ["occupied", "custom", "no-physics", "synthesized", "unloaded", "source-water", "flowing-water", "offhand", "cooldown", "adventure-clear", "adventure-occupied", "creative", "floating", "clicked-air", "activation", "spectator"];
    private static string DefaultChain(int protocol) => protocol < 773 ? "chain" : "iron_chain";
    private static IEnumerable<string> Names(int protocol) => protocol < 773 ? ["chain"] : CopperNames;
    public static IEnumerable<object[]> ItemCases() => Protocols.SelectMany(protocol => Names(protocol).Select(name => new object[] { protocol, name }));
    public static IEnumerable<object[]> FaceCases() => Boundaries.SelectMany(protocol => Enum.GetValues<Direction>().Select(face => new object[] { protocol, face }));
    public static IEnumerable<object[]> BodyCases() => Boundaries.SelectMany(protocol => new[] { Direction.Up, Direction.South, Direction.East }.SelectMany(face => new[] { "center", "corner", "above-horizontal", "x-arm", "z-arm" }.Select(scene => new object[] { protocol, face, scene })));
    public static IEnumerable<object[]> ContextCases() => Boundaries.SelectMany(protocol => Scenarios.Select(scene => new object[] { protocol, scene }));

    [Fact]
    public void TheExactRegisteredCatalogRetainsAllFourHundredNativeRodStates()
    {
        Assert.Equal(28, Protocols.Length);
        Assert.Equal(68, ItemCases().Count());
        Assert.Equal(24, FaceCases().Count());
        Assert.Equal(60, BodyCases().Count());
        Assert.Equal(64, ContextCases().Count());
        int states = 0;
        foreach (int protocol in Protocols)
        {
            RegistryAccess registry = JavaGameData.Registries(protocol);
            IBlockDataSource blocks = new RegistryBlockDataSource(registry.Blocks, isLegacy: false);
            IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
            IBlockCollisionGridSource grids = Assert.IsAssignableFrom<IBlockCollisionGridSource>(shapes);
            string[] expectedNames = Names(protocol).Order(StringComparer.Ordinal).ToArray();
            string[] actualNames = registry.Items.Where(item => item.Id.IsMinecraft && (item.Id.Path == "chain" || CopperNames.Contains(item.Id.Path, StringComparer.Ordinal)))
                .Select(item => item.Id.Path).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedNames, actualNames);
            foreach (string name in expectedNames)
            {
                Identifier id = Identifier.Minecraft(name);
                Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(id, out ItemUseOnInfo use));
                Assert.Equal(ItemUseOnKind.Unknown, use.Kind);
                Assert.Equal(id, use.PlacedBlock);
                BlockDefinition block = registry.Blocks[id].Value;
                Assert.Equal(protocol <= 736 ? 2 : 6, block.StateCount);
                Assert.Equal(protocol <= 736 ? new[] { "waterlogged" } : ["axis", "waterlogged"], block.Properties.Select(property => property.Name));
                for (int state = block.MinStateId; state <= block.MaxStateId; state++)
                {
                    var value = new BlockState(blocks, state);
                    string axis = protocol <= 736 ? "y" : value.TryGetProperty("axis", out string selected) ? selected : throw new InvalidOperationException("Native chain axis is unavailable.");
                    Aabb expected = axis switch
                    {
                        "x" => new(0, .40625, .40625, 1, .59375, .59375),
                        "y" => new(.40625, 0, .40625, .59375, 1, .59375),
                        "z" => new(.40625, .40625, 0, .59375, .59375, 1),
                        _ => throw new InvalidOperationException("Unexpected native chain axis."),
                    };
                    BlockCollisionGrid grid = Assert.IsType<BlockCollisionGrid>(grids.GetCollisionGrid(value));
                    Assert.False(grid.CanonicalFullBlock);
                    Assert.Equal(0, grid.MaxHorizontalOffset);
                    Assert.Equal(new[] { expected }, grid.Boxes.ToArray());
                    Assert.Equal(new[] { expected }, shapes.GetOutlineShapes(value).ToArray());
                    Assert.True(value.TryGetProperty("waterlogged", out string water) && water is "true" or "false");
                    states++;
                }
            }
        }
        Assert.Equal(400, states);
    }

    [Theory]
    [MemberData(nameof(ItemCases))]
    public async Task EveryRegisteredDefaultChainUsesTheExistingPlacementCaller(int protocol, string item)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(item);
        await Use(session, Session.Target, Direction.Up, BlockUseResult.Success);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task AllSixFacesRetainTheirNativeInputAndSwingPolicy(int protocol, Direction face)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(DefaultChain(protocol));
        await session.Block(Session.Target.Offset(face), "air");
        await Use(session, Session.Target, face, BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(BodyCases))]
    public async Task ExactRodGeometryDistinguishesAxisAndAUnitCellCorner(int protocol, Direction face, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(DefaultChain(protocol));
        BlockPos destination = Session.Target.Offset(face);
        await session.Block(destination, "air");
        Vec3d position = new(destination.X + (scene is "corner" or "x-arm" ? .95 : .5), destination.Y + (scene == "above-horizontal" ? .75 : 0), destination.Z + (scene is "corner" or "z-arm" ? .95 : .5));
        await Villager(session, position);
        Aabb body = await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out bool building, protocol));
            Assert.True(building);
            return bounds;
        }, session.Token);
        Assert.True(Aabb.BlockAt(destination.X, destination.Y, destination.Z).Intersects(body));
        Axis axis = protocol <= 736 ? Axis.Y : face.GetAxis();
        bool blocked = scene == "center" || scene == "above-horizontal" && axis == Axis.Y || scene == "x-arm" && axis == Axis.X || scene == "z-arm" && axis == Axis.Z;
        await Use(session, Session.Target, face, blocked ? BlockUseResult.Fail : BlockUseResult.Success);
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        Assert.Empty(await session.Barrier());
        Assert.Null(session.Client.State.Entities.Get(42));
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ExistingContextAndPermissionOwnersKeepTheirNativeOrder(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, scene == "activation" ? "crafting_table" : "stone", terrain: scene != "synthesized", physics: scene != "no-physics");
        await session.Hold(DefaultChain(protocol), custom: scene == "custom");
        BlockPos clicked = Session.Target;
        Direction face = scene == "floating" ? Direction.East : Direction.Up;
        Hand hand = Hand.Main;
        BlockUseResult expected = BlockUseResult.Success;
        if (scene is "occupied" or "adventure-occupied")
        {
            await session.Block(clicked.Above(), "stone");
            expected = BlockUseResult.Fail;
        }
        if (scene == "unloaded")
        {
            await session.Forget();
            await session.Block(clicked, "stone");
            await session.Block(clicked.Above(), "air");
        }
        if (scene is "synthesized" or "unloaded" or "no-physics" or "custom")
            expected = BlockUseResult.Unknown;
        if (scene is "source-water" or "flowing-water")
        {
            BlockDefinition water = session.Client.State.Registries!.Blocks[Identifier.Minecraft("water")].Value;
            int state = Enumerable.Range(water.MinStateId, water.StateCount).First(value => water.TryGetPropertyValue(value, "level", out string level) && level == (scene == "source-water" ? "0" : "1"));
            await session.Send(new ClientboundBlockUpdatePacket(clicked.Above(), state));
        }
        if (scene == "offhand") { await session.Hold(null); await session.Offhand(DefaultChain(protocol)); hand = Hand.Off; }
        if (scene == "cooldown") { await session.Cooldown(); expected = BlockUseResult.Pass; }
        if (scene.StartsWith("adventure", StringComparison.Ordinal)) { await session.Mode(GameMode.Adventure, false); expected = BlockUseResult.Pass; }
        if (scene == "creative") await session.Mode(GameMode.Creative, true);
        if (scene == "spectator") { await session.Mode(GameMode.Spectator, false); expected = protocol < 768 ? BlockUseResult.Success : BlockUseResult.Consume; }
        if (scene == "floating") await session.Block(clicked.Offset(face).Below(), "air");
        if (scene == "clicked-air") await session.Block(clicked, "air");
        Assert.Empty(await session.Barrier());
        int originalState = session.Client.State.World.GetBlock(clicked).StateId;
        await Use(session, clicked, face, expected, hand);
        Assert.Equal(originalState, session.Client.State.World.GetBlock(clicked).StateId);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count);
    }

    private static async Task Villager(Session session, Vec3d position)
    {
        RegistryEntry<EntityTypeDefinition> type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("villager")];
        Assert.True(JavaGameData.EntityGeometryData(session.Protocol).TryGetRule(type.Id, out EntityGeometryRule rule));
        if (session.Protocol < 759)
            await session.Send(new ClientboundAddMobPacket(42, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        else
            await session.Send(new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        await session.Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(rule.MetadataIndex, MetadataValue.Boolean(false))], [])));
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Entities.Get(42)!.Position);
    }

    private static async Task Use(Session session, BlockPos clicked, Direction face, BlockUseResult expected, Hand hand = Hand.Main)
    {
        Vec3d cursor = face switch
        {
            Direction.Down => new(.5, 0, .5),
            Direction.Up => new(.5, 1, .5),
            Direction.North => new(.5, .5, 0),
            Direction.South => new(.5, .5, 1),
            Direction.West => new(0, .5, .5),
            _ => new(1, .5, .5),
        };
        Assert.Empty(await session.Barrier());
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(clicked, face, cursor, hand), session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, hand, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((clicked, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside || use.WorldBorderHit);
        if (session.Protocol >= 759)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
        }
        await action.WaitAsync(session.Token);
        BlockUseSwing swing = expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        // The complete public action runs before these assertions, so the old producer's packet-only result remains genuine runtime red evidence.
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        Assert.Equal(swing, prediction.Swing);
    }
}
