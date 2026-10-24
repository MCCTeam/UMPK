using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class AxeUsePredictionRuntimeTests
{
    private static IEnumerable<int> Protocols => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol <= 776);
    private static IEnumerable<Hand> Hands(int protocol) => protocol == 47 ? [Hand.Main] : [Hand.Main, Hand.Off];
    private static readonly string[] Strippables = ["oak_log", "oak_wood", "spruce_log", "spruce_wood", "birch_log", "birch_wood", "jungle_log", "jungle_wood", "acacia_log", "acacia_wood", "dark_oak_log", "dark_oak_wood", "crimson_stem", "crimson_hyphae", "warped_stem", "warped_hyphae", "mangrove_log", "mangrove_wood", "bamboo_block", "cherry_log", "cherry_wood", "pale_oak_log", "pale_oak_wood"];
    private static readonly string[] CopperFamilies = ["copper_block", "cut_copper", "cut_copper_slab", "cut_copper_stairs", "chiseled_copper", "copper_door", "copper_trapdoor", "copper_grate", "copper_bulb", "copper_bars", "copper_lantern", "copper_chain", "copper_chest", "copper_golem_statue", "lightning_rod"];
    public static IEnumerable<object[]> EraHands() => Protocols.SelectMany(protocol => Hands(protocol).Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(EraHands))]
    public async Task RegisteredAxesAndStrippingMapsReachTheOwnedPacketGroup(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        if (protocol >= 761)
            await harness.Sneak();
        var items = harness.State.Registries!.Items.Where(item => item.Id.Path.EndsWith("_axe", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(items);
        string[] targets = protocol < 393 ? ["log"] : Strippables.Where(name => harness.State.Registries.Blocks.TryGet(Identifier.Minecraft(name), out _)).ToArray();
        int index = 0;
        foreach (string target in targets.Concat(Enumerable.Repeat(targets[0], items.Length)).Concat(["stone"]).Concat(protocol >= 393 ? ["stripped_oak_log"] : Array.Empty<string>()))
        {
            await harness.Block(harness.Target, target);
            await harness.Block(harness.Target.Above(), index % 2 == 0 ? "stone" : "air");
            var item = items[index++ % items.Length];
            await harness.Slot(hand, new ItemStack(item, 1));
            ItemStack beforeStack = harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
            int beforeState = harness.State.World.GetBlock(harness.Target).StateId;
            int mark = harness.Sent.Count;
            Direction face = (Direction)(index % 6);
            bool success = protocol >= 393 && target is not ("stone" or "stripped_oak_log");
            await harness.Use(face, hand);
            Assert.Equal(mark + (success ? 2 : 1), harness.Sent.Count);
            harness.AssertUse(harness.Sent[mark], face, hand, item.Id);
            if (success)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[mark + 1]).Hand);
            Assert.Equal(beforeState, harness.State.World.GetBlock(harness.Target).StateId);
            Assert.Equal(beforeStack, harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
        }
    }

    public static IEnumerable<object[]> CopperHands() => EraHands().Where(row => (int)row[0] >= 755);
    [Theory]
    [MemberData(nameof(CopperHands))]
    public async Task CopperMapsAndNativeNonparticipationRemainDistinct(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Sneak();
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1));
        int observed = 0;
        foreach (string family in CopperFamilies)
            foreach (string prefix in new[] { "", "exposed_", "weathered_", "oxidized_", "waxed_", "waxed_exposed_", "waxed_weathered_", "waxed_oxidized_" })
            {
                string name = prefix + (family == "copper_block" && prefix is not ("" or "waxed_") ? "copper" : family);
                if (!harness.State.Registries.Blocks.TryGet(Identifier.Minecraft(name), out _))
                    continue;
                await harness.Block(harness.Target, name);
                int mark = harness.Sent.Count;
                int state = harness.State.World.GetBlock(harness.Target).StateId;
                Direction face = (Direction)(observed++ % 6);
                await harness.Use(face, hand);
                Assert.Equal(mark + (prefix.Length == 0 ? 1 : 2), harness.Sent.Count);
                harness.AssertUse(harness.Sent[mark], face, hand, item.Id);
                if (prefix.Length != 0)
                    Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[mark + 1]).Hand);
                Assert.Equal(state, harness.State.World.GetBlock(harness.Target).StateId);
            }
        Assert.True(observed >= 32);
        foreach (string name in new[] { "raw_copper_block", "copper_ore", "iron_block" })
        {
            await harness.Block(harness.Target, name);
            int mark = harness.Sent.Count;
            await harness.Use(Direction.Up, hand);
            harness.AssertUse(Assert.Single(harness.Sent.Skip(mark)), Direction.Up, hand, item.Id);
        }
    }

    public static IEnumerable<object[]> BlockingIntentCases()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769, 770, 776 })
            foreach (Hand hand in Hands(protocol))
                foreach (bool secondary in new[] { false, true })
                    foreach (string off in new[] { "air", "shield", "stone" })
                        if (hand == Hand.Main || off == "air")
                            yield return [protocol, hand, secondary, off];
    }
    [Theory]
    [MemberData(nameof(BlockingIntentCases))]
    public async Task BlockingIntentFollowsHandAndSecondaryUse(int protocol, Hand hand, bool secondary, string off)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        if (off != "air")
        {
            Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft(off), out var offItem));
            await harness.Slot(Hand.Off, new ItemStack(offItem, 1));
        }
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1));
        if (secondary)
            await harness.Sneak();
        bool succeeds = protocol < 767 || hand == Hand.Off || secondary || off != "shield";
        await harness.Use(Direction.Up, hand);
        Assert.Equal(succeeds ? 2 : 1, harness.Sent.Count);
        harness.AssertUse(harness.Sent[0], Direction.Up, hand, item.Id);
    }

    public static IEnumerable<object[]> TerrainCases()
    {
        foreach (int protocol in new[] { 393, 755, 770, 776 })
            foreach (bool entities in new[] { false, true })
                foreach (int scene in Enumerable.Range(0, 4))
                    yield return [protocol, entities, scene];
    }
    [Theory]
    [MemberData(nameof(TerrainCases))]
    public async Task NativeTerrainReceiptRemainsRequired(int protocol, bool entities, int scene)
    {
        await using var harness = new Harness(protocol, entities);
        if (scene != 0)
            await harness.ReceiveTerrain();
        if (scene is 2 or 3)
            await harness.ForgetTerrain();
        if (scene == 3)
            await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        Assert.NotNull(harness.State.World.GetColumn(harness.Target));
        Assert.Equal(scene is 1 or 3, harness.State.EntityMembership.HasColumn(harness.Target));
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(scene is 1 or 3 ? 2 : 1, harness.Sent.Count);
    }

    public static IEnumerable<object[]> FeatureTargets()
    {
        foreach ((int protocol, string target, string flag) in new[] { (761, "bamboo_block", "update_1_20"), (762, "cherry_log", "update_1_20"), (765, "waxed_weathered_copper_bulb", "update_1_21"), (766, "waxed_weathered_copper_bulb", "update_1_21"), (768, "pale_oak_log", "winter_drop") })
            foreach (int state in Enumerable.Range(0, 4))
                foreach (bool secondary in new[] { false, true })
                    yield return [protocol, target, flag, state, secondary];
    }
    [Theory]
    [MemberData(nameof(FeatureTargets))]
    public async Task FeatureTargetPermissionRunsBeforeActivationAndToolResult(int protocol, string target, string flag, int state, bool secondary)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, target);
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        if (state != 0)
            await harness.Features(state == 1 ? ["vanilla", flag] : state == 2 ? ["vanilla"] : [flag]);
        if (secondary)
            await harness.Sneak();
        await harness.Use(Direction.Up, Hand.Main);
        int expected = state == 3 ? 0 : secondary || state == 1 ? 2 : 1;
        Assert.Equal(expected, harness.Sent.Count);
        if (expected > 0)
            harness.AssertUse(harness.Sent[0], Direction.Up, Hand.Main, item.Id);
        if (expected == 2)
            Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]);
    }

    [Theory]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(776)]
    public async Task FeatureUpdatesReplaceRatherThanUnionAndIgnoreUnknownNames(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        await harness.Features(["vanilla", "unregistered_future_feature", "vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(2, harness.Sent.Count);
        await harness.Features([]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(2, harness.Sent.Count);
        await harness.Features(["vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(4, harness.Sent.Count);
    }

    [Theory]
    [InlineData(762, "cherry_log", "update_1_20")]
    [InlineData(765, "waxed_weathered_copper_bulb", "update_1_21")]
    [InlineData(768, "pale_oak_log", "winter_drop")]
    public async Task FeatureSetSurvivesWorldAndConfigurationButResetsAtSessionEnd(int protocol, string target, string flag)
    {
        await using var harness = new Harness(protocol);
        await harness.Features(["vanilla", flag]);
        foreach (int transition in Enumerable.Range(0, 4))
        {
            if (transition == 1)
                harness.State.ResetForRespawn(harness.State.World);
            if (transition == 2 && protocol >= 764)
                harness.State.ResetForConfigurationReentry();
            if (transition == 3)
                harness.State.ResetForSessionEnd();
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            await harness.ReceiveTerrain();
            await harness.Block(harness.Target, target);
            Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
            await harness.Slot(Hand.Main, new ItemStack(item, 1));
            int before = harness.Sent.Count;
            await harness.Use(Direction.Up, Hand.Main);
            Assert.Equal(before + (transition == 3 ? 1 : 2), harness.Sent.Count);
        }
    }

    public static IEnumerable<object[]> WornCases()
    {
        foreach (int protocol in Protocols.Where(protocol => protocol >= 393))
            foreach (Hand hand in Hands(protocol))
                yield return [protocol, hand];
    }

    [Theory]
    [MemberData(nameof(WornCases))]
    public async Task TheNativeDamageRepresentationDoesNotSuppressStripping(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, protocol < 393 ? "log" : "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        ItemStack stack = new(item, 1);
        if (protocol >= 766)
            stack = stack.With(DataComponents.Damage, new DamageComponent(7));
        else
        {
            var tag = new NbtCompound();
            tag.PutInt("Damage", 7);
            tag.PutString("unrelated", "retained native data");
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
            if (protocol < 393)
                stack = stack.With(DataComponents.Damage, new DamageComponent(7));
        }
        await harness.Slot(hand, stack);
        Assert.Equal(7, harness.Durability(hand));

        await harness.Use(Direction.Up, hand);

        Assert.Equal(2, harness.Sent.Count);
        harness.AssertUse(harness.Sent[0], Direction.Up, hand, item.Id);
        Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
        Assert.Equal(7, harness.Durability(hand));
    }

    public static IEnumerable<object[]> PermissionCases()
    {
        foreach (int protocol in Protocols)
            foreach (bool cooldown in protocol == 47 ? new[] { false } : new[] { false, true })
                yield return [protocol, cooldown];
    }

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public async Task AdventureAndCooldownOrderingStayWithTheirExistingOwners(int protocol, bool cooldown)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, protocol < 393 ? "log" : "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        if (cooldown)
            await harness.Cooldown(item.NetworkId);
        else
            await harness.GameMode(GameMode.Adventure);

        await harness.Use(Direction.Up, Hand.Main);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, Hand.Main, item.Id);
    }

    [Theory]
    [InlineData(766, Hand.Main)]
    [InlineData(766, Hand.Off)]
    [InlineData(776, Hand.Main)]
    [InlineData(776, Hand.Off)]
    public async Task UnclassifiedModernComponentPatchesRemainPacketOnly(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1).With(DataComponents.Unbreakable, new UnbreakableComponent()));

        await harness.Use(Direction.Up, hand);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, hand, item.Id);
    }

    [Theory]
    [InlineData(777, Hand.Main)]
    [InlineData(777, Hand.Off)]
    public async Task TheDataDrivenTransformerEraIsNotInferredFromTheOldClass(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1));

        await harness.Use(Direction.Up, hand);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, hand, item.Id);
    }

    [Theory]
    [InlineData(761, "bamboo_button", "bamboo_planks", "update_1_20")]
    [InlineData(762, "cherry_button", "cherry_planks", "update_1_20")]
    [InlineData(768, "pale_oak_button", "pale_oak_planks", "winter_drop")]
    public async Task FeaturePermissionPrecedesActivationAndHeldPermissionPrecedesSend(int protocol, string button, string held, string flag)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, button);
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var axe));
        await harness.Slot(Hand.Main, new ItemStack(axe, 1));
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Single(harness.Sent);
        await harness.Features(["vanilla", flag]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(3, harness.Sent.Count);
        await harness.Block(harness.Target, "lever");
        Assert.True(harness.State.Registries.Items.TryGet(Identifier.Minecraft(held), out var blockItem));
        await harness.Slot(Hand.Main, new ItemStack(blockItem, 1));
        await harness.Features(["vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(3, harness.Sent.Count);
        await harness.Features([flag]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(4, harness.Sent.Count);
        await harness.Features(["vanilla", flag]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(6, harness.Sent.Count);
    }

    [Fact]
    public async Task EnabledDoorActivationConsumesBeforeTheAxePhase()
    {
        await using var harness = new Harness(766);
        await harness.ReceiveTerrain();
        await harness.Features(["vanilla", "update_1_21"]);
        await harness.Block(harness.Target, "waxed_weathered_copper_door");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        BlockState beforeBlock = harness.State.World.GetBlock(harness.Target);
        ItemStack beforeStack = harness.State.Inventory.GetSlot(36);
        Assert.Equal(Identifier.Minecraft("waxed_weathered_copper_door"), beforeBlock.Block.Id);
        Assert.True(beforeStack.Components.HasNoPatch);

        await harness.Use(Direction.Up, Hand.Main);

        Assert.Equal(2, harness.Sent.Count);
        harness.AssertUse(harness.Sent[0], Direction.Up, Hand.Main, item.Id);
        Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
        Assert.Equal(beforeBlock.StateId, harness.State.World.GetBlock(harness.Target).StateId);
        ItemStack afterStack = harness.State.Inventory.GetSlot(36);
        Assert.Equal(beforeStack, afterStack);
        Assert.Equal(item.Id, afterStack.Item.Id);
        Assert.Equal(1, afterStack.Count);
        Assert.Equal(beforeStack.Damage, afterStack.Damage);
        Assert.Same(beforeStack.Components, afterStack.Components);
        Assert.True(afterStack.Components.HasNoPatch);
    }

    [Theory]
    [InlineData(761)]
    [InlineData(766)]
    [InlineData(768)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task AdvertisedUnknownNamesDoNotSupplyUnknownRequirements(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Features(["vanilla", "unregistered_future_feature"]);
        Assert.Null(harness.State.EnabledFeatures.IsBlockEnabled(new Identifier("example", "custom_block"), protocol));
        Assert.Null(harness.State.EnabledFeatures.IsItemEnabled(Identifier.Minecraft("unregistered_custom_item"), protocol));
    }

    [Theory]
    [InlineData(761, "bamboo_button", "update_1_20")]
    [InlineData(762, "cherry_button", "update_1_20")]
    [InlineData(768, "pale_oak_button", "winter_drop")]
    public async Task NativeEmptyStackBypassesHeldFeaturePermission(int protocol, string target, string flag)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, target);
        await harness.Slot(Hand.Main, ItemStack.Empty);
        await harness.Features([flag]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(2, harness.Sent.Count);
        await harness.Features(["vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(3, harness.Sent.Count);
        await harness.Block(harness.Target, "lever");
        await harness.Features([]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(4, harness.Sent.Count);
        await harness.Features(["vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(6, harness.Sent.Count);
    }

    [Theory]
    [InlineData(761, "update_1_20")]
    [InlineData(762, "update_1_20")]
    [InlineData(765, "update_1_21")]
    [InlineData(768, "winter_drop")]
    [InlineData(767, "")]
    public async Task MissingInventoryRequiresEveryNativeItemRequirementToBeEnabled(int protocol, string flag)
    {
        await using var harness = new Harness(protocol, inventory: false);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "lever");
        await harness.Features(flag.Length == 0 ? ["vanilla"] : ["vanilla", flag]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(2, harness.Sent.Count);
        await harness.Features([]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(3, harness.Sent.Count);
    }

    [Theory]
    [InlineData(765, false)]
    [InlineData(765, true)]
    [InlineData(767, false)]
    [InlineData(767, true)]
    public async Task MissingInventoryDoesNotClassifyCustomRegistryRequirements(int protocol, bool custom)
    {
        await using var harness = new Harness(protocol, inventory: false);
        var items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
        foreach (var item in harness.State.Registries!.Items)
            items.Add(item.NetworkId, item.Id, item.Value);
        if (custom)
            items.Add(100000, new Identifier("example", "custom_item"), new ItemDefinition());
        harness.State.Registries = RegistryAccess.FromSnapshot(harness.State.Registries.Snapshot.With(items.Build()));
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "lever");
        await harness.Features(protocol == 765 ? ["vanilla", "update_1_21"] : ["vanilla"]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(custom ? 1 : 2, harness.Sent.Count);
    }

    public static IEnumerable<object[]> DisabledItemTerrainCases()
    {
        foreach (int protocol in new[] { 761, 765, 768, 776 })
            foreach (int terrain in Enumerable.Range(0, 3))
                yield return [protocol, terrain];
    }

    [Theory]
    [MemberData(nameof(DisabledItemTerrainCases))]
    public async Task DisabledHeldItemRejectsBeforeMissingTerrainPrediction(int protocol, int terrain)
    {
        await using var harness = new Harness(protocol);
        if (terrain == 2)
            await harness.ReceiveTerrain();
        if (terrain != 0)
            await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        await harness.Features([]);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Empty(harness.Sent);
    }

    [Theory]
    [InlineData(Hand.Main)]
    [InlineData(Hand.Off)]
    public async Task DataDrivenUnknownDoesNotEraseKnownFeatureRejection(Hand hand)
    {
        await using var harness = new Harness(777);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "oak_log");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_axe"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1));
        await harness.Use(Direction.Up, hand);
        Assert.Single(harness.Sent);
        await harness.Features([]);
        await harness.Use(Direction.Up, hand);
        Assert.Single(harness.Sent);
        await harness.Features(["vanilla"]);
        await harness.Use(Direction.Up, hand);
        Assert.Equal(2, harness.Sent.Count);
        Assert.All(harness.Sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet));
    }

    [Fact]
    public async Task DataDrivenFeaturePermissionStillDistinguishesUnobservedAndCustomRegistries()
    {
        await using var harness = new Harness(777, inventory: false);
        await harness.Features(["vanilla"]);
        Assert.True(harness.State.EnabledFeatures.IsUnobservedItemEnabled(harness.State.Registries, 777));
        await harness.Features([]);
        Assert.Null(harness.State.EnabledFeatures.IsUnobservedItemEnabled(harness.State.Registries, 777));
        var items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
        foreach (var item in harness.State.Registries!.Items)
            items.Add(item.NetworkId, item.Id, item.Value);
        items.Add(100000, new Identifier("example", "custom_item"), new ItemDefinition());
        harness.State.Registries = RegistryAccess.FromSnapshot(harness.State.Registries.Snapshot.With(items.Build()));
        await harness.Features(["vanilla"]);
        Assert.Null(harness.State.EnabledFeatures.IsUnobservedItemEnabled(harness.State.Registries, 777));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly int _protocol;
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _session = new(TimeSpan.FromSeconds(30));
        private readonly InteractionActions _actions;
        private readonly MovementActions _movement;
        public BlockPos Target { get; } = new(1, 64, 1);
        public ClientState State => _applier.State;
        public List<object> Sent { get; } = [];

        public Harness(int protocol, bool entities = true, bool inventory = true)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            _applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = entities, Inventory = inventory, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            var services = new ClientSessionServices { Version = version, Options = new(), Policies = new(), State = State, Wire = new(version), Logger = NullLogger.Instance, Scheduler = _scheduler, CurrentSessionCancellation = () => _session.Token };
            var sink = new Sink(Sent, _applier.Sequences, protocol);
            _actions = new InteractionActions(sink, services, _applier.Sequences);
            _movement = new MovementActions(sink, services, () => null);
        }

        public async Task ReceiveTerrain()
        {
            byte[] bytes;
            if (_protocol == 47)
                bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
            else if (_protocol >= 757)
            {
                var empty = new ChunkPalettedContainerWire(0, 0, null, []);
                var sections = Enumerable.Range(0, (State.World.Dimension.MaxY - State.World.Dimension.MinY) / 16)
                    .Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
                byte[] tail = new byte[_protocol <= 762 ? 8 : 7];
                ChunkWireBody body = _protocol >= 770
                    ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], tail)
                    : _protocol >= 768 ? new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], tail)
                    : new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], tail, _protocol >= 764 ? NbtWireFormat.JavaUnnamedRoot : NbtWireFormat.JavaNamedRoot);
                bytes = ChunkCodecs.EncodeStructural(body);
            }
            else if (_protocol >= 735)
            {
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                writer.WriteInt(0);
                writer.WriteInt(0);
                if (_protocol < 755)
                    writer.WriteBool(true);
                if (_protocol <= 736)
                    writer.WriteBool(false);
                writer.WriteVarInt(0);
                writer.WriteNbt(new NbtCompound(), NbtWireFormat.JavaNamedRoot);
                if (_protocol <= 736)
                    writer.WriteBytes(new byte[4096]);
                else
                {
                    writer.WriteVarInt(1024);
                    writer.WriteBytes(new byte[1024]);
                }
                writer.WriteVarInt(0);
                writer.WriteVarInt(0);
                bytes = output.WrittenSpan.ToArray();
            }
            else if (_protocol >= 477)
                bytes = ChunkCodecs.EncodeStructural(new Pre1_18ChunkWireBody(0, 0, true, 0, new NbtCompound(), [], _protocol < 573 ? new byte[1024] : [], [0], _protocol >= 573 ? new byte[4096] : []));
            else
            {
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteBool(true);
                writer.WriteVarInt(0);
                int biomes = _protocol >= 393 ? 1024 : 256;
                writer.WriteVarInt(biomes);
                writer.WriteBytes(new byte[biomes]);
                if (_protocol >= 110)
                    writer.WriteVarInt(0);
                bytes = output.WrittenSpan.ToArray();
            }
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Reencode(codec, packet, context));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _session.Token);
            Assert.True(State.EntityMembership.HasColumn(Target));
        }

        public Task Features(string[] names)
        {
            Assert.True(JavaVersions.TryGetByProtocol(_protocol, out var version));
            ProtocolPhase phase = _protocol < 764 ? ProtocolPhase.Play : ProtocolPhase.Configuration;
            Assert.True(version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out var registry));
            var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("update_enabled_features"));
            Assert.True(registry.TryGetInbound(entry.WireId, out var codec));
            Assert.True(codec.IsImplemented, $"Feature packet is a marker at {_protocol}/{phase}.");
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            writer.WriteVarInt(names.Length);
            foreach (string name in names)
                writer.WriteString("minecraft:" + name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var packet = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(codec.Decode(output.WrittenSpan, context));
            Assert.Equal(names.Select(Identifier.Minecraft), packet.Features);
            Assert.Equal(phase, packet.Type.Phase);
            Assert.Equal(output.WrittenSpan.ToArray(), Reencode(codec, packet, context));
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _session.Token);
        }

        public Task Block(BlockPos position, string name, int metadata = 0, string? axis = null)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out var block));
            int state = _protocol < 393 ? block.DefaultStateId | metadata : block.DefaultStateId;
            if (axis is { } expected)
                state = Enumerable.Range(block.MinStateId, block.StateCount).First(id => block.TryGetPropertyValue(id, "axis", out string value) && value == expected);
            return Apply("block_update", new ClientboundBlockUpdatePacket(position, state));
        }

        public Task Slot(Hand hand, ItemStack item) => Apply(_protocol == 47 ? "set_slot" : "container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, item));
        public Task GameMode(GameMode mode) => Apply("game_event", new ClientboundGameEventPacket(3, (float)mode));
        public Task Cooldown(int item) => Apply("cooldown", new ClientboundCooldownPacket(_protocol >= 768 ? Identifier.Minecraft("diamond_axe") : default, 100, _protocol < 768 ? item : null));
        public Task ForgetTerrain() => Apply("forget_level_chunk", new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        public async Task Sneak()
        {
            await _scheduler.InvokeAsync(() => new ValueTask(_movement.SetSneakingAsync(true, _session.Token)), _session.Token);
            Assert.True(State.Self.Sneaking);
            Assert.Single(Sent);
            Assert.True(Sent[0] is ServerboundPlayerCommandPacket or ServerboundPlayerInputPacket);
            Sent.Clear();
        }
        private Task Apply(string name, object packet)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var original = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(original);
            codec.Encode(ref writer, packet, context);
            object decoded = codec.Decode(original.WrittenSpan, context);
            if (packet is ClientboundContainerSetSlotPacket expected)
            {
                var observed = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded);
                Assert.Equal(expected.Slot, observed.Slot);
                Assert.Equal(expected.Item.Item.Id, observed.Item.Item.Id);
                Assert.Equal(expected.Item.Count, observed.Item.Count);
                Assert.Equal(expected.Item.Damage, observed.Item.Damage);
            }
            else
                Assert.Equal(packet, decoded);
            Assert.Equal(original.WrittenSpan.ToArray(), Reencode(codec, decoded, context));
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _session.Token);
        }
        private static byte[] Reencode(BoundPacketCodec codec, object packet, PacketCodecContext context)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, context);
            return output.WrittenSpan.ToArray();
        }

        public int Durability(Hand hand)
        {
            ItemStack item = State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
            return _protocol < 393 || _protocol >= 766 ? item.Damage : item.Components.Get(DataComponents.LegacyNbt)!.Nbt.GetInt("Damage");
        }

        public Task Use(Direction face, Hand hand) => _actions.PlaceBlockAsync(Target, face, new Vec3d(.5, 1, .5), hand, ct: _session.Token).WaitAsync(TimeSpan.FromSeconds(5));
        public void AssertUse(object packet, Direction face, Hand hand, Identifier item)
        {
            if (_protocol == 47)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packet);
                Assert.Equal(Target, use.Position);
                Assert.Equal((int)face, use.Face);
                Assert.Equal(item, use.HeldItem.Item.Id);
                Assert.Equal((byte)8, use.CursorX);
                Assert.Equal((byte)16, use.CursorY);
                Assert.Equal((byte)8, use.CursorZ);
            }
            else
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
                Assert.Equal(Target, use.Position);
                Assert.Equal((int)face, use.Face);
                Assert.Equal((int)hand, use.Hand);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await _scheduler.DisposeAsync();
            _session.Dispose();
        }
    }

    private sealed class Sink(List<object> packets, SequenceTracker sequences, int protocol) : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[] body = BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<IPacket>(packet));
            if (protocol == 47 && packet is ServerboundSwingPacket)
                Assert.Empty(body);
            else
                Assert.NotEmpty(body);
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            Assert.True(version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out var registry));
            var typed = Assert.IsAssignableFrom<IPacket>(packet);
            Assert.True(registry.TryGetOutbound(typed.Type, out _, out var codec));
            var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
            object decoded = codec.Decode(body, context);
            if (packet is ServerboundUseItemOnPacket usePacket)
            {
                var observed = Assert.IsType<ServerboundUseItemOnPacket>(decoded);
                Assert.Equal(usePacket.Position, observed.Position);
                Assert.Equal(usePacket.Face, observed.Face);
                Assert.Equal(usePacket.Hand, observed.Hand);
                Assert.Equal(usePacket.CursorX, observed.CursorX);
                Assert.Equal(usePacket.CursorY, observed.CursorY);
                Assert.Equal(usePacket.CursorZ, observed.CursorZ);
                Assert.Equal(protocol >= 477 && usePacket.Inside, observed.Inside);
                Assert.Equal(protocol >= 759 ? usePacket.Sequence : 0, observed.Sequence);
            }
            else if (packet is ServerboundLegacyBlockPlacePacket legacy)
            {
                var observed = Assert.IsType<ServerboundLegacyBlockPlacePacket>(decoded);
                Assert.Equal(legacy.Position, observed.Position);
                Assert.Equal(legacy.Face, observed.Face);
                Assert.Equal(legacy.HeldItem.Item.Id, observed.HeldItem.Item.Id);
                Assert.Equal(legacy.CursorX, observed.CursorX);
                Assert.Equal(legacy.CursorY, observed.CursorY);
                Assert.Equal(legacy.CursorZ, observed.CursorZ);
            }
            else if (packet is ServerboundSwingPacket swing)
                Assert.Equal(protocol == 47 ? 0 : swing.Hand, Assert.IsType<ServerboundSwingPacket>(decoded).Hand);
            var reencoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(reencoded);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(body, reencoded.WrittenSpan.ToArray());
            packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                sequences.Acknowledge(use.Sequence);
            return ValueTask.CompletedTask;
        }
        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
