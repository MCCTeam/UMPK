using Umpk.Client.Actions;
using Umpk.Client.Internal;
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

public sealed class LadderStonePlacementSessionRuntimeTests
{
    private static readonly int[] Protocols = [735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly int[] Boundaries = [735, 755, 766, 777];
    private static readonly Direction[] Faces = [Direction.North, Direction.South, Direction.West, Direction.East];
    private static readonly string[] Contexts = ["occupied", "custom", "no-physics", "synthesized", "unloaded", "source-water", "flowing-water", "offhand", "cooldown", "adventure-clear", "adventure-occupied", "creative", "other-support", "vertical", "clicked-air", "activation", "destination-unavailable", "destination-loaded"];
    public static IEnumerable<object[]> FaceCases() => Protocols.SelectMany(protocol => Faces.Select(face => new object[] { protocol, face }));
    public static IEnumerable<object[]> BodyCases() => Boundaries.SelectMany(protocol => Faces.SelectMany(face => new[] { "plate", "clear", "above" }.Select(scene => new object[] { protocol, face, scene })));
    public static IEnumerable<object[]> ContextCases() => Boundaries.SelectMany(protocol => Contexts.Select(scene => new object[] { protocol, scene }));
    public static IEnumerable<object[]> ViewCases() => Boundaries.SelectMany(protocol => new[] { -180F, -.1F, 0F, 179.999F }.Select(yaw => new object[] { protocol, yaw }));
    public static IEnumerable<object[]> SelfCases() => Boundaries.Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> DefinitionCases() => new[] { 735, 777 }.SelectMany(protocol => new[] { "clone-survival", "override-adventure" }.Select(scene => new object[] { protocol, scene }));

    [Fact]
    public void ExactAdmittedCatalogRetainsTwoHundredTwentyFourThinWallStates()
    {
        Assert.Equal(28, Protocols.Length);
        Assert.Equal(112, FaceCases().Count());
        Assert.Equal(48, BodyCases().Count());
        Assert.Equal(72, ContextCases().Count());
        Assert.Equal(16, ViewCases().Count());
        Assert.Equal(4, SelfCases().Count());
        Assert.Equal(4, DefinitionCases().Count());
        int count = 0;
        foreach (int protocol in Protocols)
        {
            RegistryAccess registry = JavaGameData.Registries(protocol);
            IBlockDataSource blocks = new RegistryBlockDataSource(registry.Blocks, isLegacy: false);
            IBlockCollisionGridSource grids = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(protocol));
            BlockDefinition ladder = registry.Blocks[Identifier.Minecraft("ladder")].Value;
            Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(Identifier.Minecraft("ladder"), out ItemUseOnInfo use));
            Assert.Equal(ItemUseOnKind.Unknown, use.Kind);
            Assert.Equal(Identifier.Minecraft("ladder"), use.PlacedBlock);
            Assert.Equal(8, ladder.StateCount);
            Assert.Equal(new[] { "facing", "waterlogged" }, ladder.Properties.Select(property => property.Name));
            foreach (int state in Enumerable.Range(ladder.MinStateId, ladder.StateCount))
            {
                var selected = new BlockState(blocks, state);
                Assert.True(selected.TryGetProperty("facing", out string facing));
                Assert.True(selected.TryGetProperty("waterlogged", out string water) && water is "true" or "false");
                Aabb expected = facing switch
                {
                    "north" => new(0, 0, .8125, 1, 1, 1),
                    "south" => new(0, 0, 0, 1, 1, .1875),
                    "west" => new(.8125, 0, 0, 1, 1, 1),
                    "east" => new(0, 0, 0, .1875, 1, 1),
                    _ => throw new InvalidOperationException("The native ladder direction is unavailable."),
                };
                BlockCollisionGrid grid = Assert.IsType<BlockCollisionGrid>(grids.GetCollisionGrid(selected));
                Assert.False(grid.CanonicalFullBlock);
                Assert.Equal(0, grid.MaxHorizontalOffset);
                Assert.Equal(new[] { expected }, grid.Boxes.ToArray());
                count++;
            }
        }
        Assert.Equal(224, count);
    }

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task EveryModernProfilePlacesTheDefaultLadderOnEachStoneWallFace(int protocol, Direction face)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        await session.Hold("ladder");
        BlockPos destination = Session.Target.Offset(face);
        await session.Block(destination, "air");
        await Use(session, Session.Target, face, BlockUseResult.Success);
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(BodyCases))]
    public async Task NativeThreeSixteenthsPlateDistinguishesAnIntersectingBodyFromTheUnusedCell(int protocol, Direction face, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        await session.Hold("ladder");
        BlockPos destination = Session.Target.Offset(face);
        await session.Block(destination, "air");
        bool low = face is Direction.East or Direction.South;
        double normal = scene == "clear" ? low ? .75 : .25 : low ? .1 : .9;
        var position = new Vec3d(destination.X + (face.GetAxis() == Axis.X ? normal : .5), destination.Y + (scene == "above" ? 1.1 : 0), destination.Z + (face.GetAxis() == Axis.Z ? normal : .5));
        await Villager(session, position);
        Aabb body = await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out bool building, protocol));
            Assert.True(building);
            return bounds;
        }, session.Token);
        Assert.Equal(scene != "above", Aabb.BlockAt(destination.X, destination.Y, destination.Z).Intersects(body));
        await Use(session, Session.Target, face, scene == "plate" ? BlockUseResult.Fail : BlockUseResult.Success);
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        Assert.Empty(await session.Barrier());
        await Use(session, Session.Target, face, BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public async Task HorizontalClickedFaceHasNativePriorityOverObservedViewDirection(int protocol, float yaw)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        await session.Hold("ladder");
        int before = session.ObservedOutbound.Count;
        Task rotation = await session.Client.InvokeAsync(client => client.Actions.Movement.SetRotationAsync(yaw, 0, session.Token), session.Token);
        await rotation.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        var reported = Assert.Single(session.ObservedOutbound.Skip(before).OfType<ServerboundMovePlayerRotPacket>());
        Assert.Equal(BitConverter.SingleToInt32Bits(yaw), BitConverter.SingleToInt32Bits(reported.Yaw));
        Assert.Equal(yaw, session.Client.State.Self.Yaw);
        await Use(session, Session.Target, Direction.East, BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(SelfCases))]
    public async Task FramedSelfPositionUsesTheSameThinCollisionPlate(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        await session.Hold("ladder");
        BlockPos destination = Session.Target.Offset(Direction.East);
        var position = new Vec3d(destination.X + .1, destination.Y, destination.Z + .5);
        await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
        await session.Barrier();
        Assert.Equal(position, session.Client.State.Self.Position);
        await Use(session, Session.Target, Direction.East, BlockUseResult.Fail);
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ExistingPlacementPermissionAndUnavailableContextRemainExplicit(int protocol, string scene)
    {
        string clickedBlock = scene == "activation" ? "crafting_table" : scene == "other-support" ? "cobblestone" : "stone";
        await using Session session = await Session.Create(protocol, clickedBlock, terrain: scene != "synthesized", physics: scene != "no-physics", observeWrites: true);
        await session.Hold("ladder", custom: scene == "custom");
        BlockPos clicked = scene.StartsWith("destination-", StringComparison.Ordinal) ? new BlockPos(15, Session.Target.Y, 1) : Session.Target;
        if (scene.StartsWith("destination-", StringComparison.Ordinal))
            await session.Block(clicked, "stone");
        Direction face = scene == "vertical" ? Direction.Up : Direction.East;
        BlockPos destination = clicked.Offset(face);
        await session.Block(destination, "air");
        Assert.Empty(await session.Barrier());
        Hand hand = Hand.Main;
        BlockUseResult expected = BlockUseResult.Success;
        if (scene.StartsWith("destination-", StringComparison.Ordinal))
        {
            Assert.True(session.Client.State.EntityMembership.HasColumn(clicked));
            Assert.NotNull(session.Client.State.World.GetColumn(destination));
            Assert.False(session.Client.State.EntityMembership.HasColumn(destination));
            if (scene == "destination-loaded")
            {
                await session.Terrain(1, 0);
                Assert.Empty(await session.Barrier());
                Assert.True(session.Client.State.EntityMembership.HasColumn(destination));
            }
            else
                expected = BlockUseResult.Unknown;
        }
        if (scene is "occupied" or "adventure-occupied")
        {
            await session.Block(destination, "stone");
            expected = BlockUseResult.Fail;
        }
        if (scene == "unloaded")
        {
            await session.Forget();
            await session.Block(clicked, "stone");
            await session.Block(destination, "air");
        }
        if (scene is "synthesized" or "unloaded" or "no-physics" or "custom" or "other-support" or "vertical" or "clicked-air")
            expected = BlockUseResult.Unknown;
        if (scene is "source-water" or "flowing-water")
        {
            BlockDefinition water = session.Client.State.Registries!.Blocks[Identifier.Minecraft("water")].Value;
            int state = Enumerable.Range(water.MinStateId, water.StateCount).First(value => water.TryGetPropertyValue(value, "level", out string level) && level == (scene == "source-water" ? "0" : "1"));
            await session.Send(new ClientboundBlockUpdatePacket(destination, state));
        }
        if (scene == "offhand") { await session.Hold(null); await session.Offhand("ladder"); hand = Hand.Off; }
        if (scene == "cooldown") { await session.Cooldown(); expected = BlockUseResult.Pass; }
        if (scene.StartsWith("adventure", StringComparison.Ordinal)) { await session.Mode(GameMode.Adventure, false); expected = BlockUseResult.Pass; }
        if (scene == "creative") await session.Mode(GameMode.Creative, true);
        if (scene == "clicked-air") await session.Block(clicked, "air");
        Assert.Empty(await session.Barrier());
        int before = session.Client.State.World.GetBlock(clicked).StateId;
        int stackCount = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count;
        await Use(session, clicked, face, expected, hand);
        Assert.Equal(before, session.Client.State.World.GetBlock(clicked).StateId);
        Assert.Equal(stackCount, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count);
    }

    [Theory]
    [MemberData(nameof(DefinitionCases))]
    public async Task UntrustedClonedAndOverriddenDefinitionsCannotSupplyVanillaDefaultPlacement(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        await session.Hold("ladder");
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        ItemDefinition definition = original.Item.Value with { };
        if (scene == "override-adventure")
            definition = definition with { MaxStackSize = original.Item.Value.MaxStackSize + 1 };
        var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
        builder.Add(original.Item.NetworkId, original.Item.Id, definition);
        RegistryEntry<ItemDefinition> supplied = builder.Build()[original.Item.Id];
        Assert.Equal(original.Item.NetworkId, supplied.NetworkId);
        Assert.Equal(original.Item.Id, supplied.Id);
        Assert.NotSame(original.Item.Value, supplied.Value);
        ItemStack replacement = new(supplied, 1);
        Assert.True(replacement.Components.HasNoPatch);
        await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, replacement); return true; }, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Same(replacement, session.Client.State.Inventory.GetSlot(36));
        if (scene == "override-adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        await Use(session, Session.Target, Direction.North, BlockUseResult.Unknown);
        Assert.Same(replacement, session.Client.State.Inventory.GetSlot(36));
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
