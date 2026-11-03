using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class PillarPlacementSessionRuntimeTests
{
    private static readonly (int[] Protocols, string Names)[] NativeCatalog =
    [
        ([47], "hay_block log log2"),
        ([107, 108, 109, 110], "hay_block log log2 purpur_pillar"),
        ([210, 315, 316, 335, 338, 340], "bone_block hay_block log log2 purpur_pillar"),
        ([393, 401, 404, 477, 480, 485, 490, 498, 573, 575, 578], "acacia_log acacia_wood birch_log birch_wood bone_block dark_oak_log dark_oak_wood hay_block jungle_log jungle_wood oak_log oak_wood purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_birch_log stripped_birch_wood stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood"),
        ([735, 736, 751, 753, 754], "acacia_log acacia_wood basalt birch_log birch_wood bone_block crimson_hyphae crimson_stem dark_oak_log dark_oak_wood hay_block jungle_log jungle_wood oak_log oak_wood polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_birch_log stripped_birch_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem warped_hyphae warped_stem"),
        ([755, 756, 757, 758], "acacia_log acacia_wood basalt birch_log birch_wood bone_block crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood oak_log oak_wood polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_birch_log stripped_birch_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem warped_hyphae warped_stem"),
        ([759, 760], "acacia_log acacia_wood basalt birch_log birch_wood bone_block crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood mangrove_log mangrove_wood muddy_mangrove_roots oak_log oak_wood ochre_froglight pearlescent_froglight polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_birch_log stripped_birch_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_mangrove_log stripped_mangrove_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem verdant_froglight warped_hyphae warped_stem"),
        ([761], "acacia_log acacia_wood bamboo_block basalt birch_log birch_wood bone_block crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood mangrove_log mangrove_wood muddy_mangrove_roots oak_log oak_wood ochre_froglight pearlescent_froglight polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_bamboo_block stripped_birch_log stripped_birch_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_mangrove_log stripped_mangrove_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem verdant_froglight warped_hyphae warped_stem"),
        ([762, 763, 764, 765, 766, 767], "acacia_log acacia_wood bamboo_block basalt birch_log birch_wood bone_block cherry_log cherry_wood crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood mangrove_log mangrove_wood muddy_mangrove_roots oak_log oak_wood ochre_froglight pearlescent_froglight polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_bamboo_block stripped_birch_log stripped_birch_wood stripped_cherry_log stripped_cherry_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_mangrove_log stripped_mangrove_wood stripped_oak_log stripped_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem verdant_froglight warped_hyphae warped_stem"),
        ([768, 769, 770, 771, 772, 773, 774, 775, 776], "acacia_log acacia_wood bamboo_block basalt birch_log birch_wood bone_block cherry_log cherry_wood crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood mangrove_log mangrove_wood muddy_mangrove_roots oak_log oak_wood ochre_froglight pale_oak_log pale_oak_wood pearlescent_froglight polished_basalt purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_bamboo_block stripped_birch_log stripped_birch_wood stripped_cherry_log stripped_cherry_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_mangrove_log stripped_mangrove_wood stripped_oak_log stripped_oak_wood stripped_pale_oak_log stripped_pale_oak_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem verdant_froglight warped_hyphae warped_stem"),
        ([777], "acacia_log acacia_wood bamboo_block basalt birch_log birch_wood bone_block cherry_log cherry_wood crimson_hyphae crimson_stem dark_oak_log dark_oak_wood deepslate hay_block infested_deepslate jungle_log jungle_wood mangrove_log mangrove_wood muddy_mangrove_roots oak_log oak_wood ochre_froglight pale_oak_log pale_oak_wood pearlescent_froglight polished_basalt poplar_log poplar_wood purpur_pillar quartz_pillar spruce_log spruce_wood stripped_acacia_log stripped_acacia_wood stripped_bamboo_block stripped_birch_log stripped_birch_wood stripped_cherry_log stripped_cherry_wood stripped_crimson_hyphae stripped_crimson_stem stripped_dark_oak_log stripped_dark_oak_wood stripped_jungle_log stripped_jungle_wood stripped_mangrove_log stripped_mangrove_wood stripped_oak_log stripped_oak_wood stripped_pale_oak_log stripped_pale_oak_wood stripped_poplar_log stripped_poplar_wood stripped_spruce_log stripped_spruce_wood stripped_warped_hyphae stripped_warped_stem verdant_froglight warped_hyphae warped_stem"),
    ];
    private static readonly int[] Boundaries = [47, 107, 340, 393, 404, 477, 735, 761, 766, 768, 770, 777];
    private static string DefaultLog(int protocol) => protocol < 393 ? "log" : "oak_log";
    public static IEnumerable<object[]> CatalogCases() => NativeCatalog.SelectMany(group => group.Protocols.SelectMany(protocol => group.Names.Split(' ').Select(name => new object[] { protocol, name })));
    public static IEnumerable<object[]> ProtocolCases() => NativeCatalog.SelectMany(group => group.Protocols).Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> MaterialCases() => CatalogCases().GroupBy(row => (string)row[1], StringComparer.Ordinal).Select(group => group.First());

    [Fact]
    public void SourceBoundInventoryHasOnlyTheRegisteredFullCubeAxisFamilies()
    {
        object[][] catalog = CatalogCases().ToArray();
        Assert.Equal(1761, catalog.Length);
        Assert.Equal(50, catalog.Select(row => (int)row[0]).Distinct().Count());
        Assert.Equal(64, catalog.Select(row => (string)row[1]).Distinct().Count());
        foreach (var profile in catalog.GroupBy(row => (int)row[0]).Where(group => group.Key >= 393))
        {
            RegistryAccess registry = JavaGameData.Registries(profile.Key);
            string[] current = registry.Items.Where(item => registry.Blocks.TryGet(item.Id, out RegistryEntry<BlockDefinition> block)
                    && block.Value.StateCount == 3 && block.Value.Properties.Count == 1 && block.Value.Properties[0].Name == "axis")
                .Select(item => item.Id.Path).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(profile.Select(row => (string)row[1]).Order(StringComparer.Ordinal), current);
        }
        foreach (object[] row in catalog)
        {
            int protocol = (int)row[0];
            Identifier item = Identifier.Minecraft((string)row[1]);
            Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(item, out ItemUseOnInfo contract));
            Assert.Equal(ItemUseOnKind.Unknown, contract.Kind);
            Assert.Equal(item, contract.PlacedBlock);
            if (protocol < 393)
                continue;
            IBlockDataSource blocks = new RegistryBlockDataSource(JavaGameData.Registries(protocol).Blocks, isLegacy: false);
            BlockDefinition block = blocks.Blocks[item].Value;
            Assert.Equal(3, block.StateCount);
            Assert.Equal(new[] { "axis" }, block.Properties.Select(property => property.Name));
            Assert.Equal(new[] { "x", "y", "z" }, block.Properties[0].Values);
            IBlockCollisionGridSource grids = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(protocol));
            for (int state = block.MinStateId; state <= block.MaxStateId; state++)
            {
                BlockCollisionGrid grid = Assert.IsType<BlockCollisionGrid>(grids.GetCollisionGrid(new BlockState(blocks, state)));
                Assert.True(grid.CanonicalFullBlock);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task DefaultLogUsesTheExistingPlacementOwnerOnEveryProtocol(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(DefaultLog(protocol));
        await AssertUse(session, BlockUsePrediction.Success(protocol == 777));
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(MaterialCases))]
    public async Task EverySourceBoundMaterialHasAnObservablePublicPlacementCaller(int protocol, string item)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(item);
        Assert.Equal(Identifier.Minecraft(item), session.Client.State.Inventory.GetSlot(36).Item.Id);
        await AssertUse(session, BlockUsePrediction.Success(protocol == 777));
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> FaceCases() => Boundaries.SelectMany(protocol => Enum.GetValues<Direction>().Select(face => new object[] { protocol, face }));

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task AllSixNativeFacesKeepTheirOrientationInputAndFullBoxDecision(int protocol, Direction face)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold(DefaultLog(protocol));
        BlockPos destination = Session.Target.Offset(face);
        await session.Block(destination, "air");
        Assert.Empty(await session.Barrier());
        // Native pillar placement selects the clicked face's axis; every selected axis has the same full obstruction box. The public action preserves that face for the native server write.
        await AssertUse(session, BlockUsePrediction.Success(protocol == 777), face: face);
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> ContextCases() => Boundaries.SelectMany(protocol => new[] { "neighbor-solid", "self", "synthesized-terrain", "no-physics", "custom-stack", "clicked-air", "floating-side", "world-height" }.Concat(protocol >= 107 ? new[] { "cooldown-clear", "cooldown-blocked" } : Array.Empty<string>()).Select(scene => new object[] { protocol, scene }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task NativePreSendFailureAndMissingContextRemainDistinct(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", terrain: scene != "synthesized-terrain", physics: scene != "no-physics");
        await session.Hold(DefaultLog(protocol), custom: scene == "custom-stack");
        BlockPos target = Session.Target;
        Direction face = Direction.Up;
        BlockUsePrediction expected = BlockUsePrediction.Success(protocol == 777);
        if (scene == "neighbor-solid")
        {
            await session.Block(target.Above(), "stone");
            expected = protocol <= 340 ? BlockUsePrediction.Rejected : BlockUsePrediction.Fail;
        }
        if (scene is "self" or "cooldown-blocked")
        {
            target = new(2, 64, 2);
            expected = protocol <= 340 ? BlockUsePrediction.Rejected : BlockUsePrediction.Fail;
        }
        if (scene is "synthesized-terrain" or "no-physics" or "custom-stack")
            expected = BlockUsePrediction.Unknown;
        if (scene == "clicked-air")
            await session.Block(target, "air");
        if (scene == "floating-side")
        {
            face = Direction.East;
            await session.Block(target.Offset(face).Below(), "air");
        }
        if (scene == "world-height")
        {
            target = new(1, session.Client.State.World.Dimension.MaxY - 1, 1);
            await session.Block(target, "stone");
            expected = protocol <= 340 ? BlockUsePrediction.Unknown : BlockUsePrediction.Fail;
        }
        if (scene.StartsWith("cooldown", StringComparison.Ordinal))
        {
            await session.Cooldown();
            expected = scene == "cooldown-blocked" && protocol <= 340 ? BlockUsePrediction.Rejected : BlockUsePrediction.Pass;
        }
        Assert.Empty(await session.Barrier());
        int clickedState = session.Client.State.World.GetBlock(target).StateId;
        int stackCount = session.Client.State.Inventory.GetSlot(36).Count;
        await AssertUse(session, expected, target, face);
        Assert.Equal(clickedState, session.Client.State.World.GetBlock(target).StateId);
        Assert.Equal(stackCount, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> HandCases() => Boundaries.Where(protocol => protocol >= 107).Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(HandCases))]
    public async Task OffhandKeepsNativePlacementAndSwingOwnership(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Offhand(DefaultLog(protocol));
        Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        await AssertUse(session, BlockUsePrediction.Success(protocol == 777), hand: Hand.Off);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(45).Count);
    }

    public static IEnumerable<object[]> AdventureCases() => new[] { 770, 773, 774, 777 }.SelectMany(protocol => new[] { "allow", "deny", "missing", "extra-patch" }.Select(scene => new object[] { protocol, scene }));

    [Theory]
    [MemberData(nameof(AdventureCases))]
    public async Task ExistingAdventurePermissionControlsTheSameDefaultFamily(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("oak_log")], 1);
        if (scene != "missing")
        {
            int block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(scene == "deny" ? "dirt" : "stone")].NetworkId;
            stack = stack.With(DataComponents.CanPlaceOn, new AdventureModePredicateComponent([
                new BlockPredicateEntry(new HolderSetRef(null, [block]), null, null) { ComponentMatchers = new([], []) },
            ]));
        }
        if (scene == "extra-patch")
            stack = stack.With(DataComponents.Unbreakable, new UnbreakableComponent());
        await session.Send(new ClientboundContainerSetSlotPacket(0, 4, 36, stack));
        Assert.Empty(await session.Barrier());
        Assert.Equal(stack.Item.Id, session.Client.State.Inventory.GetSlot(36).Item.Id);
        BlockUsePrediction expected = scene == "allow" ? BlockUsePrediction.Success(protocol == 777)
            : scene == "extra-patch" ? BlockUsePrediction.Unknown : BlockUsePrediction.Pass;
        await AssertUse(session, expected);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(766)]
    [InlineData(769)]
    public async Task EarlierUnownedAdventureContextsRemainUnknown(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Hold(DefaultLog(protocol));
        await AssertUse(session, BlockUsePrediction.Unknown);
    }

    [Theory]
    [InlineData(761, "bamboo_block")]
    [InlineData(762, "bamboo_block")]
    [InlineData(762, "cherry_log")]
    [InlineData(768, "pale_oak_log")]
    public async Task NativeDisabledFeatureStillRejectsBeforeUse(int protocol, string item)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true, featureFlags: false);
        await session.Hold(item);
        Assert.False(session.Client.State.EnabledFeatures.IsItemEnabled(Identifier.Minecraft(item), protocol));
        await AssertUse(session, BlockUsePrediction.Rejected);
    }

    private static async Task AssertUse(Session session, BlockUsePrediction expected, BlockPos? position = null, Direction face = Direction.Up, Hand hand = Hand.Main)
    {
        Assert.Empty(await session.Barrier());
        BlockPos target = position ?? Session.Target;
        Vec3d cursor = face switch
        {
            Direction.Down => new(.5, 0, .5),
            Direction.Up => new(.5, 1, .5),
            Direction.North => new(.5, .5, 0),
            Direction.South => new(.5, .5, 1),
            Direction.West => new(0, .5, .5),
            _ => new(1, .5, .5),
        };
        BlockUsePrediction observed = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(target, face, cursor, hand), session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        // Complete the actual packet group's acknowledgment even in the retained red run, before comparing its predicted result or packet count.
        if (session.Protocol >= 759 && packets.FirstOrDefault() is ServerboundUseItemOnPacket actualUse)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(actualUse.Sequence));
        }
        await action.WaitAsync(session.Token);
        Assert.Equal(expected, observed);
        Assert.Equal(!expected.SendPacket ? 0 : expected.Swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (expected.SendPacket)
        {
            if (session.Protocol == 47)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
                Assert.Equal((target, (int)face), (use.Position, use.Face));
                Assert.Equal(((byte)(cursor.X * 16), (byte)(cursor.Y * 16), (byte)(cursor.Z * 16)), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.Equal(1, use.HeldItem.Count);
            }
            else
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
                Assert.Equal((target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside);
                Assert.False(use.WorldBorderHit);
            }
            if (expected.Swing == BlockUseSwing.Packet)
            {
                var swing = Assert.IsType<ServerboundSwingPacket>(packets[1]);
                Assert.Equal((int)hand, swing.Hand);
                Assert.Equal(session.Protocol >= 107, swing.HasHand);
            }
        }
        Assert.DoesNotContain(packets, packet => packet is ServerboundPlayerActionPacket or ServerboundPunchPacket);
        await action.WaitAsync(session.Token);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }
}
