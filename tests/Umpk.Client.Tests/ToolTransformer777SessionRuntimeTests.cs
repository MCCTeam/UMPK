using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class ToolTransformer777SessionRuntimeTests
{
    private static readonly Identifier Transformers = Identifier.Minecraft("block_transformer");
    private static readonly string[] Families = ["hoe", "shovel", "axe"];
    private static readonly string[] Materials = ["wooden", "stone", "copper", "iron", "golden", "diamond", "netherite"];
    private static readonly string[] NativeAxeInputs = ["oak_wood", "oak_log", "dark_oak_wood", "dark_oak_log", "pale_oak_wood", "pale_oak_log", "poplar_wood", "poplar_log", "acacia_wood", "acacia_log", "cherry_wood", "cherry_log", "birch_wood", "birch_log", "jungle_wood", "jungle_log", "spruce_wood", "spruce_log", "warped_stem", "warped_hyphae", "crimson_stem", "crimson_hyphae", "mangrove_wood", "mangrove_log", "bamboo_block", "exposed_copper", "weathered_copper", "oxidized_copper", "exposed_cut_copper", "weathered_cut_copper", "oxidized_cut_copper", "exposed_chiseled_copper", "weathered_chiseled_copper", "oxidized_chiseled_copper", "exposed_cut_copper_slab", "weathered_cut_copper_slab", "oxidized_cut_copper_slab", "exposed_cut_copper_stairs", "weathered_cut_copper_stairs", "oxidized_cut_copper_stairs", "exposed_copper_trapdoor", "weathered_copper_trapdoor", "oxidized_copper_trapdoor", "exposed_copper_bars", "weathered_copper_bars", "oxidized_copper_bars", "exposed_copper_grate", "weathered_copper_grate", "oxidized_copper_grate", "exposed_copper_bulb", "weathered_copper_bulb", "oxidized_copper_bulb", "exposed_copper_lantern", "weathered_copper_lantern", "oxidized_copper_lantern", "exposed_copper_golem_statue", "weathered_copper_golem_statue", "oxidized_copper_golem_statue", "exposed_lightning_rod", "weathered_lightning_rod", "oxidized_lightning_rod", "exposed_copper_chain", "weathered_copper_chain", "oxidized_copper_chain", "exposed_copper_chest", "weathered_copper_chest", "oxidized_copper_chest", "exposed_copper_door", "weathered_copper_door", "oxidized_copper_door", "waxed_copper_block", "waxed_exposed_copper", "waxed_weathered_copper", "waxed_oxidized_copper", "waxed_cut_copper", "waxed_exposed_cut_copper", "waxed_weathered_cut_copper", "waxed_oxidized_cut_copper", "waxed_cut_copper_slab", "waxed_exposed_cut_copper_slab", "waxed_weathered_cut_copper_slab", "waxed_oxidized_cut_copper_slab", "waxed_cut_copper_stairs", "waxed_exposed_cut_copper_stairs", "waxed_weathered_cut_copper_stairs", "waxed_oxidized_cut_copper_stairs", "waxed_chiseled_copper", "waxed_exposed_chiseled_copper", "waxed_weathered_chiseled_copper", "waxed_oxidized_chiseled_copper", "waxed_copper_trapdoor", "waxed_exposed_copper_trapdoor", "waxed_weathered_copper_trapdoor", "waxed_oxidized_copper_trapdoor", "waxed_copper_bars", "waxed_exposed_copper_bars", "waxed_weathered_copper_bars", "waxed_oxidized_copper_bars", "waxed_copper_grate", "waxed_exposed_copper_grate", "waxed_weathered_copper_grate", "waxed_oxidized_copper_grate", "waxed_copper_bulb", "waxed_exposed_copper_bulb", "waxed_weathered_copper_bulb", "waxed_oxidized_copper_bulb", "waxed_copper_golem_statue", "waxed_exposed_copper_golem_statue", "waxed_weathered_copper_golem_statue", "waxed_oxidized_copper_golem_statue", "waxed_lightning_rod", "waxed_exposed_lightning_rod", "waxed_weathered_lightning_rod", "waxed_oxidized_lightning_rod", "waxed_copper_lantern", "waxed_exposed_copper_lantern", "waxed_weathered_copper_lantern", "waxed_oxidized_copper_lantern", "waxed_copper_chain", "waxed_exposed_copper_chain", "waxed_weathered_copper_chain", "waxed_oxidized_copper_chain", "waxed_copper_chest", "waxed_exposed_copper_chest", "waxed_weathered_copper_chest", "waxed_oxidized_copper_chest", "waxed_copper_door", "waxed_exposed_copper_door", "waxed_weathered_copper_door", "waxed_oxidized_copper_door"];
    public static IEnumerable<object[]> AxeCases() => NativeAxeInputs.Select((block, index) => new object[] { block, (Direction)(index % 6) });

    [Fact]
    public void ActualNativeRegistryFrameRetainsAllHolderIdsDefinitionsAndBytes()
    {
        Assert.Equal("3b878a8e38f042468d9f7fa85f258097c1eb6aace880763f75068a78b62934b1", Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(NativeRegistryBody))));
        ClientboundConfigRegistryDataPacket packet = NativePacket();
        Assert.Equal(Transformers, packet.Registry);
        Assert.Equal(new[] { "axe", "hoe", "shovel" }, packet.Entries.Select(entry => entry.Id.Path));
        Assert.Equal(new[] { 7, 2, 1 }, packet.Entries.Select(entry => Assert.IsType<NbtList>(entry.Data).Count));
        Assert.Equal(130, NativeAxeInputs.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("poplar_log", NativeAxeInputs);
        Assert.Contains("poplar_wood", NativeAxeInputs);
        var blocks = JavaGameData.Registries(777).Blocks;
        // Baseline Test Csc binds the existing type without requiring the new member. This fact executes after the Definitions stage; its fixed method name is not a production reflection contract.
        var lookup = typeof(Umpk.Client.Internal.VanillaToolTransformerDefinitions).GetMethod("TryGetAxeTarget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(lookup);
        HashSet<Identifier> mappedInputs = [];
        string[] statueInputs =
        [
            "exposed_copper_golem_statue", "weathered_copper_golem_statue", "oxidized_copper_golem_statue",
            "waxed_copper_golem_statue", "waxed_exposed_copper_golem_statue", "waxed_weathered_copper_golem_statue", "waxed_oxidized_copper_golem_statue"
        ];
        HashSet<string> resolvedStatueInputs = [];
        int resolvedDefaultRows = 0;
        foreach (NbtTag transform in Assert.IsType<NbtList>(packet.Entries[0].Data))
            foreach (NbtTag raw in (NbtList)((NbtCompound)((NbtCompound)transform)["block_state_provider"])["rules"])
            {
                var rule = (NbtCompound)raw;
                Identifier input = Identifier.Parse(((NbtCompound)rule["if_true"]).GetString("blocks")!);
                var provider = (NbtCompound)rule["then"];
                Assert.Equal("minecraft:copy_properties", provider.GetString("type"));
                var source = (NbtCompound)provider["source"];
                Identifier target = Identifier.Parse(source.GetString("id")!);
                Assert.True(mappedInputs.Add(input));
                object?[] arguments = [input, default(Identifier)];
                Assert.True(Assert.IsType<bool>(lookup.Invoke(null, arguments)));
                Assert.Equal(target, Assert.IsType<Identifier>(arguments[1]));
                BlockDefinition before = blocks[input].Value;
                BlockDefinition after = blocks[target].Value;
                Assert.Equal(before.Properties.Select(property => property.Name), after.Properties.Select(property => property.Name));
                foreach (BlockPropertyDefinition property in before.Properties)
                    Assert.Equal(property.Values, Assert.Single(after.Properties, next => next.Name == property.Name).Values);
                if (statueInputs.Contains(input.Path, StringComparer.Ordinal))
                {
                    Assert.True(resolvedStatueInputs.Add(input.Path));
                    var properties = Assert.IsType<NbtCompound>(source["properties"]);
                    Assert.Equal(new[] { "copper_golem_pose", "facing", "waterlogged" }, properties.Select(pair => pair.Key).Order(StringComparer.Ordinal));
                    Assert.Equal("standing", properties.GetString("copper_golem_pose"));
                    Assert.Equal("north", properties.GetString("facing"));
                    Assert.Equal("false", properties.GetString("waterlogged"));
                    Assert.Equal(new[] { "copper_golem_pose", "facing", "waterlogged" }, after.Properties.Select(property => property.Name));
                    Assert.Equal(new[] { "standing", "sitting", "running", "star" }, after.Properties[0].Values);
                    Assert.Equal(new[] { "north", "south", "west", "east" }, after.Properties[1].Values);
                    Assert.Equal(new[] { "true", "false" }, after.Properties[2].Values);
                }
                {
                    resolvedDefaultRows++;
                    if (source.TryGet("properties", out NbtTag? properties))
                        foreach (var pair in (NbtCompound)properties)
                        {
                            Assert.True(after.TryGetPropertyValue(after.DefaultStateId, pair.Key, out string value));
                            Assert.Equal(((NbtCompound)properties).GetString(pair.Key), value);
                        }
                    else
                    {
                        Assert.Empty(after.Properties);
                        Assert.Equal(1, after.StateCount);
                        Assert.Equal(after.MinStateId, after.DefaultStateId);
                    }
                }
            }
        Assert.Equal(130, mappedInputs.Count);
        Assert.Equal(130, resolvedDefaultRows);
        Assert.Equal(statueInputs.Order(StringComparer.Ordinal), resolvedStatueInputs.Order(StringComparer.Ordinal));

    }

    [Theory]
    [MemberData(nameof(AxeCases))]
    public async Task EveryExactNativeStrippingScrapingAndWaxTargetReachesPublicCaller(string block, Direction face)
    {
        await using Session session = await Ready("axe", block);
        await Secondary(session);
        await AssertUse(session, BlockUsePrediction.Success(true), face);
    }

    public static IEnumerable<object[]> SoilCases()
    {
        foreach (string block in new[] { "grass_block", "dirt_path", "dirt", "coarse_dirt", "rooted_dirt" })
            yield return ["hoe", block, Direction.Up, "air", true];
        yield return ["hoe", "dirt", Direction.Down, "air", false];
        yield return ["hoe", "dirt", Direction.Up, "stone", false];
        yield return ["hoe", "rooted_dirt", Direction.Down, "stone", true];
        foreach (string block in new[] { "grass_block", "dirt", "podzol", "coarse_dirt", "mycelium", "rooted_dirt" })
            yield return ["shovel", block, Direction.Up, "air", true];
        yield return ["shovel", "grass_block", Direction.Down, "air", false];
        yield return ["shovel", "grass_block", Direction.Up, "stone", false];
        yield return ["shovel", "campfire", Direction.Up, "air", false];
    }

    [Theory]
    [MemberData(nameof(SoilCases))]
    public async Task NativeRuleOrderFaceAndAirTagsDetermineSuccessOrPass(string family, string block, Direction face, string above, bool success)
    {
        await using Session session = await Ready(family, block);
        await session.Block(Session.Target.Above(), above);
        Assert.Empty(await session.Barrier());
        await Secondary(session);
        await AssertUse(session, success ? BlockUsePrediction.Success(true) : BlockUsePrediction.Pass, face);
    }

    public static IEnumerable<object[]> MaterialCases() => Families.SelectMany(family => Materials.Select(material => new object[] { family, material }));

    [Theory]
    [MemberData(nameof(MaterialCases))]
    public async Task EveryNativeToolMaterialUsesItsVerifiedDefaultHolder(string family, string material)
    {
        await using Session session = await Ready(family);
        await session.Hold(material + "_" + family);
        await AssertUse(session, BlockUsePrediction.Success(true));
        if (material == "diamond")
        {
            ItemStack worn = session.Client.State.Inventory.GetSlot(36).With(DataComponents.Damage, new DamageComponent(2));
            await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, worn));
            Assert.Empty(await session.Barrier());
            Assert.Equal(2, session.Client.State.Inventory.GetSlot(36).Damage);
            await AssertUse(session, BlockUsePrediction.Success(true));
        }
    }

    public static IEnumerable<object[]> DefinitionCases() => Families.SelectMany(family => new[] { "absent", "native", "null", "changed", "reordered-holders", "reordered-fields", "wrong-key", "omitted" }.Select(context => new object[] { family, context }));

    [Theory]
    [MemberData(nameof(DefinitionCases))]
    public async Task EffectiveDefinitionKnowledgeDoesNotFollowAnItemNameOrAssumedHolderId(string family, string context)
    {
        ClientboundConfigRegistryDataPacket original = NativePacket();
        var entries = original.Entries.ToArray();
        int index = Array.FindIndex(entries, entry => entry.Id == Identifier.Minecraft(family));
        if (context == "null")
            entries[index] = entries[index] with { Data = null };
        if (context == "changed")
            Assert.IsType<NbtCompound>(Assert.IsType<NbtList>(entries[index].Data)[0]).PutString("sound", "minecraft:entity.experience_orb.pickup");
        if (context == "reordered-holders")
            Array.Reverse(entries);
        if (context == "reordered-fields")
            entries[index] = entries[index] with { Data = Reorder(entries[index].Data!) };
        if (context == "wrong-key")
            entries[index] = entries[index] with { Id = Identifier.Parse("umpk_test:" + family) };
        if (context == "omitted")
            entries = entries.Where(entry => entry.Id != Identifier.Minecraft(family)).ToArray();
        IPacket[] packets = context == "absent" ? [] : [original with { Entries = entries }];
        await using Session session = await Ready(family, configuration: packets);
        await AssertUse(session, context is "native" or "reordered-holders" or "reordered-fields" ? BlockUsePrediction.Success(true) : BlockUsePrediction.Unknown);
    }

    public static IEnumerable<object[]> ContextCases() => Families.SelectMany(family => new[] { "shield", "shield-secondary", "offhand", "cooldown", "patched", "air-tag-empty", "custom-target-tag", "upper-void-air" }.Select(context => new object[] { family, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task SharedBlockingIntentCooldownAndReceivedTagsKeepNativeOrder(string family, string context)
    {
        await using Session session = await Ready(family, context == "custom-target-tag" ? "stone" : null);
        Hand hand = Hand.Main;
        if (context is "shield" or "shield-secondary")
            await session.Offhand("shield");
        if (context == "shield-secondary")
            await Secondary(session);
        if (context == "offhand")
        {
            await session.Hold("shield");
            await session.Offhand("diamond_" + family);
            hand = Hand.Off;
        }
        if (context == "cooldown")
            await session.Cooldown();
        if (context == "patched")
            await session.Hold("diamond_" + family, custom: true);
        if (context == "air-tag-empty")
            await Tags(session, emptyAir: true);
        if (context == "custom-target-tag")
            await Tags(session, customTarget: true);
        if (context == "upper-void-air")
        {
            await session.Block(Session.Target.Above(), "void_air");
            Assert.Empty(await session.Barrier());
        }
        BlockUsePrediction expected = context is "shield" or "cooldown" || context == "air-tag-empty" && family != "axe" || context == "custom-target-tag" && family == "axe"
            ? BlockUsePrediction.Pass : context == "patched" ? BlockUsePrediction.Unknown : BlockUsePrediction.Success(true);
        if (context == "patched")
        {
            await AssertUse(session, expected, hand: hand);
            // An unmodeled removal has no payload and already round-trips through the existing codec. It must not disappear into the no-patch/default admission.
            DataComponentType<UnmodeledComponent> removed = DataComponents.Unmodeled(Identifier.Minecraft("block_transformer"));
            var item = session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_" + family)];
            Dictionary<DataComponentType, object> prototype = new(item.Value.DefaultComponents)
            {
                [removed] = new UnmodeledComponent(removed.Id, []),
            };
            var stack = new ItemStack(item, 1, DataComponentMap.Create(prototype, [DataComponentEntry.Remove(removed)]));
            await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
            Assert.Empty(await session.Barrier());
            DataComponentEntry removal = Assert.Single(session.Client.State.Inventory.GetSlot(36).Components.Patch);
            Assert.True(removal.IsRemoval);
            Assert.Equal(removed.Id, removal.Type.Id);
        }
        await AssertUse(session, expected, hand: hand);
        if (context == "upper-void-air")
        {
            await session.Forget();
            await session.Block(Session.Target, Target(family));
            Assert.Empty(await session.Barrier());
            await AssertUse(session, BlockUsePrediction.Unknown);
        }
    }

    public static IEnumerable<object[]> AdventureCases() => Families.SelectMany(family => new[] { "allow", "deny", "unknown-tag" }.Select(context => new object[] { family, context }));

    [Theory]
    [MemberData(nameof(AdventureCases))]
    public async Task AdventureChecksTheClickedBlockBeforeTheTransformer(string family, string context)
    {
        await using Session session = await Ready(family, tags: context != "unknown-tag");
        await session.Mode(GameMode.Adventure, instantBuild: false);
        int block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(context == "deny" ? "bedrock" : Target(family))].NetworkId;
        HolderSetRef blocks = context == "unknown-tag" ? new(Identifier.Parse("umpk_test:unreceived"), []) : new(null, [block]);
        ItemStack stack = new(session.Client.State.Registries.Items[Identifier.Minecraft("diamond_" + family)], 1);
        stack = stack.With(DataComponents.CanPlaceOn, new AdventureModePredicateComponent([
            new BlockPredicateEntry(blocks, null, null) { ComponentMatchers = new([], []) },
        ]));
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
        Assert.Empty(await session.Barrier());
        await AssertUse(session, context == "allow" ? BlockUsePrediction.Success(true) : context == "deny" ? BlockUsePrediction.Pass : BlockUsePrediction.Unknown);
    }

    [Theory]
    [InlineData("retain")]
    [InlineData("replace-other-registry")]
    [InlineData("split-payload")]
    [InlineData("disconnect")]
    public async Task ConfigurationReentryAndConnectionTeardownOwnTheVerifiedFacts(string context)
    {
        await using Session session = await Ready("hoe");
        await AssertUse(session, BlockUsePrediction.Success(true));
        if (context == "disconnect")
        {
            await session.Client.DisconnectAsync(session.Token);
            await using Session fresh = await Ready("hoe", configuration: []);
            await AssertUse(fresh, BlockUsePrediction.Unknown);
            return;
        }
        ClientboundConfigRegistryDataPacket packet = NativePacket();
        IPacket[] packets = context switch
        {
            "replace-other-registry" => [new ClientboundConfigRegistryDataPacket(Identifier.Parse("umpk_test:other_registry"), [])],
            "split-payload" => [packet with { Entries = packet.Entries.Take(1).ToArray() }, packet with { Entries = packet.Entries.Skip(1).ToArray() }],
            _ => [],
        };
        await session.Reconfigure(null, packets);
        await session.RejoinWorld("dirt");
        await session.Hold("diamond_hoe");
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 45, ItemStack.Empty));
        Assert.Empty(await session.Barrier());
        await AssertUse(session, context == "replace-other-registry" ? BlockUsePrediction.Unknown : BlockUsePrediction.Success(true));
    }

    [Theory]
    [InlineData("hoe", false)]
    [InlineData("hoe", true)]
    [InlineData("shovel", false)]
    [InlineData("shovel", true)]
    public async Task NativeUpperHeightUsesVoidAirInsteadOfStoredAir(string family, bool tagContainsVoidAir)
    {
        await using Session session = await Ready(family);
        int maxY = session.Client.State.World.Dimension.MaxY;
        var target = new BlockPos(Session.Target.X, maxY - 1, Session.Target.Z);
        await session.Block(target, "dirt");
        Vec3d position = new(2.5, target.Y, 2.5);
        await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 20, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
        int Id(string name) => session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId;
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [
            new TagEntry(Identifier.Minecraft("air"), [Id(tagContainsVoidAir ? "void_air" : "air")]),
            new TagEntry(Identifier.Minecraft("turns_into_farmland"), [Id("dirt")]),
            new TagEntry(Identifier.Minecraft("turns_into_dirt_path"), [Id("dirt")]),
        ])]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Self.Position);
        Assert.True(session.Client.State.EntityMembership.HasColumn(target));
        Assert.Equal(maxY, target.Above().Y);
        // Original Level.getBlockState353–359 returns VOID_AIR outside height; World.GetBlock currently returns stored state0 AIR. The received tag deliberately distinguishes those blocks.
        Assert.Equal(Identifier.Minecraft("air"), session.Client.State.World.GetBlock(target.Above()).Block.Id);
        Assert.Equal(Identifier.Minecraft("dirt"), session.Client.State.World.GetBlock(target).Block.Id);
        await AssertUse(session, tagContainsVoidAir ? BlockUsePrediction.Success(true) : BlockUsePrediction.Pass, position: target);
    }

    private static string Target(string family) => family == "axe" ? "oak_log" : "dirt";

    private static async Task<Session> Ready(string family, string? block = null, IReadOnlyList<IPacket>? configuration = null, bool tags = true)
    {
        Session session = await Session.Create(777, block ?? Target(family), configurationPackets: configuration ?? [NativePacket()]);
        try
        {
            if (tags)
                await Tags(session);
            await session.Hold("diamond_" + family);
            await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 45, ItemStack.Empty));
            Assert.Empty(await session.Barrier());
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task Tags(Session session, bool emptyAir = false, bool customTarget = false)
    {
        int[] Ids(params string[] names) => names.Select(name => session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId).ToArray();
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [
            new TagEntry(Identifier.Minecraft("air"), emptyAir ? [] : Ids("air", "void_air", "cave_air")),
            new TagEntry(Identifier.Minecraft("turns_into_farmland"), customTarget ? Ids("stone") : Ids("grass_block", "dirt_path", "dirt")),
            new TagEntry(Identifier.Minecraft("turns_into_dirt_path"), customTarget ? Ids("stone") : Ids("grass_block", "dirt", "podzol", "coarse_dirt", "mycelium", "rooted_dirt")),
        ])]));
        Assert.Empty(await session.Barrier());
    }

    private static async Task Secondary(Session session)
    {
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.Self.Sneaking);
    }

    private static NbtTag Reorder(NbtTag value)
    {
        if (value is NbtCompound compound)
        {
            var result = new NbtCompound();
            foreach (var pair in compound.Reverse())
                result.Put(pair.Key, Reorder(pair.Value));
            return result;
        }
        if (value is NbtList list)
        {
            var result = new NbtList(list.ElementType);
            foreach (NbtTag element in list)
                result.Add(Reorder(element));
            return result;
        }
        return value.Copy();
    }

    private static ClientboundConfigRegistryDataPacket NativePacket()
    {
        byte[] bytes = Convert.FromBase64String(NativeRegistryBody);
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound);
        Assert.True(registry.TryGetInbound(7, out BoundPacketCodec codec));
        Assert.Equal(Identifier.Minecraft("registry_data"), codec.Type.Id);
        var context = new PacketCodecContext(JavaGameData.Registries(777), IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundConfigRegistryDataPacket>(codec.Decode(bytes, context));
        Assert.Equal(Transformers, packet.Registry);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(bytes, output.WrittenSpan.ToArray());
        return packet;
    }

    private static async Task AssertUse(Session session, BlockUsePrediction expected, Direction face = Direction.Up, Hand hand = Hand.Main, BlockPos? position = null)
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
        int state = session.Client.State.World.GetBlock(target).StateId;
        ItemStack before = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        BlockUsePrediction observed = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(target, face, cursor, hand), session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        if (packets.FirstOrDefault() is ServerboundUseItemOnPacket sent)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(sent.Sequence));
        }
        await action.WaitAsync(session.Token);
        Assert.Equal(expected, observed);
        Assert.True(expected.SendPacket);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(packets));
        Assert.Equal((target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside || use.WorldBorderHit);
        Assert.DoesNotContain(packets, packet => packet is ServerboundSwingPacket or ServerboundPunchPacket or ServerboundPlayerActionPacket);
        // This slice checks send/result/swing against received terrain. Native local block prediction has separate connected cases; default tool damage and loot require server context.
        Assert.Equal(state, session.Client.State.World.GetBlock(target).StateId);
        Assert.Equal(before, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    // Actual Config registry_data body, wire7, retained native26.3 session. The source receipt binds all3 inline definitions and holder ordering to the official jar JSON.
    private const string NativeRegistryBody = "G21pbmVjcmFmdDpibG9ja190cmFuc2Zvcm1lcgMNbWluZWNyYWZ0OmF4ZQEJCgAAAAcIAAVzb3VuZAAYbWluZWNyYWZ0Oml0ZW0uYXhlLnN0cmlwCgAUYmxvY2tfc3RhdGVfcHJvdmlkZXIJAAVydWxlcwoAAAAZCgAEdGhlbgoABnNvdXJjZQgAAmlkABttaW5lY3JhZnQ6c3RyaXBwZWRfb2FrX3dvb2QKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABJtaW5lY3JhZnQ6b2FrX3dvb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAGm1pbmVjcmFmdDpzdHJpcHBlZF9vYWtfbG9nCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwARbWluZWNyYWZ0Om9ha19sb2cIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDpzdHJpcHBlZF9kYXJrX29ha193b29kCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAXbWluZWNyYWZ0OmRhcmtfb2FrX3dvb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpzdHJpcHBlZF9kYXJrX29ha19sb2cKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABZtaW5lY3JhZnQ6ZGFya19vYWtfbG9nCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACBtaW5lY3JhZnQ6c3RyaXBwZWRfcGFsZV9vYWtfd29vZAoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAF21pbmVjcmFmdDpwYWxlX29ha193b29kCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB9taW5lY3JhZnQ6c3RyaXBwZWRfcGFsZV9vYWtfbG9nCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAWbWluZWNyYWZ0OnBhbGVfb2FrX2xvZwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAebWluZWNyYWZ0OnN0cmlwcGVkX3BvcGxhcl93b29kCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAVbWluZWNyYWZ0OnBvcGxhcl93b29kCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB1taW5lY3JhZnQ6c3RyaXBwZWRfcG9wbGFyX2xvZwoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAFG1pbmVjcmFmdDpwb3BsYXJfbG9nCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6c3RyaXBwZWRfYWNhY2lhX3dvb2QKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABVtaW5lY3JhZnQ6YWNhY2lhX3dvb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHW1pbmVjcmFmdDpzdHJpcHBlZF9hY2FjaWFfbG9nCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAUbWluZWNyYWZ0OmFjYWNpYV9sb2cIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpzdHJpcHBlZF9jaGVycnlfd29vZAoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAFW1pbmVjcmFmdDpjaGVycnlfd29vZAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OnN0cmlwcGVkX2NoZXJyeV9sb2cKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABRtaW5lY3JhZnQ6Y2hlcnJ5X2xvZwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OnN0cmlwcGVkX2JpcmNoX3dvb2QKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABRtaW5lY3JhZnQ6YmlyY2hfd29vZAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAcbWluZWNyYWZ0OnN0cmlwcGVkX2JpcmNoX2xvZwoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAE21pbmVjcmFmdDpiaXJjaF9sb2cIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpzdHJpcHBlZF9qdW5nbGVfd29vZAoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAFW1pbmVjcmFmdDpqdW5nbGVfd29vZAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OnN0cmlwcGVkX2p1bmdsZV9sb2cKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABRtaW5lY3JhZnQ6anVuZ2xlX2xvZwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAebWluZWNyYWZ0OnN0cmlwcGVkX3NwcnVjZV93b29kCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAVbWluZWNyYWZ0OnNwcnVjZV93b29kCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB1taW5lY3JhZnQ6c3RyaXBwZWRfc3BydWNlX2xvZwoACnByb3BlcnRpZXMIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAFG1pbmVjcmFmdDpzcHJ1Y2VfbG9nCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6c3RyaXBwZWRfd2FycGVkX3N0ZW0KAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABVtaW5lY3JhZnQ6d2FycGVkX3N0ZW0IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDpzdHJpcHBlZF93YXJwZWRfaHlwaGFlCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAXbWluZWNyYWZ0OndhcnBlZF9oeXBoYWUIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpzdHJpcHBlZF9jcmltc29uX3N0ZW0KAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABZtaW5lY3JhZnQ6Y3JpbXNvbl9zdGVtCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACFtaW5lY3JhZnQ6c3RyaXBwZWRfY3JpbXNvbl9oeXBoYWUKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABhtaW5lY3JhZnQ6Y3JpbXNvbl9oeXBoYWUIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDpzdHJpcHBlZF9tYW5ncm92ZV93b29kCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAXbWluZWNyYWZ0Om1hbmdyb3ZlX3dvb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpzdHJpcHBlZF9tYW5ncm92ZV9sb2cKAApwcm9wZXJ0aWVzCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABZtaW5lY3JhZnQ6bWFuZ3JvdmVfbG9nCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB9taW5lY3JhZnQ6c3RyaXBwZWRfYmFtYm9vX2Jsb2NrCgAKcHJvcGVydGllcwgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAWbWluZWNyYWZ0OmJhbWJvb19ibG9jawgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAgABHR5cGUAFG1pbmVjcmFmdDpydWxlX2Jhc2VkAAMAE2l0ZW1fZGFtYWdlX3Blcl91c2UAAAABAAgABXNvdW5kABltaW5lY3JhZnQ6aXRlbS5heGUuc2NyYXBlCAAIcGFydGljbGUABnNjcmFwZQoAFGJsb2NrX3N0YXRlX3Byb3ZpZGVyCQAFcnVsZXMKAAAAJwoABHRoZW4KAAZzb3VyY2UIAAJpZAAWbWluZWNyYWZ0OmNvcHBlcl9ibG9jawAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAGG1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAYbWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAabWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAGm1pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAZbWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAUbWluZWNyYWZ0OmN1dF9jb3BwZXIACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABxtaW5lY3JhZnQ6ZXhwb3NlZF9jdXRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABxtaW5lY3JhZnQ6ZXhwb3NlZF9jdXRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAebWluZWNyYWZ0OndlYXRoZXJlZF9jdXRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6d2VhdGhlcmVkX2N1dF9jb3BwZXIACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB1taW5lY3JhZnQ6b3hpZGl6ZWRfY3V0X2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAZbWluZWNyYWZ0OmNoaXNlbGVkX2NvcHBlcgAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIW1pbmVjcmFmdDpleHBvc2VkX2NoaXNlbGVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAhbWluZWNyYWZ0OmV4cG9zZWRfY2hpc2VsZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAjbWluZWNyYWZ0OndlYXRoZXJlZF9jaGlzZWxlZF9jb3BwZXIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAI21pbmVjcmFmdDp3ZWF0aGVyZWRfY2hpc2VsZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAibWluZWNyYWZ0Om94aWRpemVkX2NoaXNlbGVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAZbWluZWNyYWZ0OmN1dF9jb3BwZXJfc2xhYgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAR0eXBlAAZib3R0b20AAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAhbWluZWNyYWZ0OmV4cG9zZWRfY3V0X2NvcHBlcl9zbGFiCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACFtaW5lY3JhZnQ6ZXhwb3NlZF9jdXRfY29wcGVyX3NsYWIKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEdHlwZQAGYm90dG9tAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAI21pbmVjcmFmdDp3ZWF0aGVyZWRfY3V0X2NvcHBlcl9zbGFiCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACNtaW5lY3JhZnQ6d2VhdGhlcmVkX2N1dF9jb3BwZXJfc2xhYgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAR0eXBlAAZib3R0b20AAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAibWluZWNyYWZ0Om94aWRpemVkX2N1dF9jb3BwZXJfc2xhYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAbbWluZWNyYWZ0OmN1dF9jb3BwZXJfc3RhaXJzCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGhhbGYABmJvdHRvbQgABXNoYXBlAAhzdHJhaWdodAgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAjbWluZWNyYWZ0OmV4cG9zZWRfY3V0X2NvcHBlcl9zdGFpcnMIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAI21pbmVjcmFmdDpleHBvc2VkX2N1dF9jb3BwZXJfc3RhaXJzCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGhhbGYABmJvdHRvbQgABXNoYXBlAAhzdHJhaWdodAgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAlbWluZWNyYWZ0OndlYXRoZXJlZF9jdXRfY29wcGVyX3N0YWlycwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAlbWluZWNyYWZ0OndlYXRoZXJlZF9jdXRfY29wcGVyX3N0YWlycwoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAVzaGFwZQAIc3RyYWlnaHQIAAZmYWNpbmcABW5vcnRoAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJG1pbmVjcmFmdDpveGlkaXplZF9jdXRfY29wcGVyX3N0YWlycwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAZbWluZWNyYWZ0OmNvcHBlcl90cmFwZG9vcgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACFtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfdHJhcGRvb3IIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIW1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl90cmFwZG9vcgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACNtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl90cmFwZG9vcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAjbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfdHJhcGRvb3IKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEaGFsZgAGYm90dG9tCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEb3BlbgAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAibWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcl90cmFwZG9vcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAVbWluZWNyYWZ0OmNvcHBlcl9iYXJzCgAKcHJvcGVydGllcwgABGVhc3QABWZhbHNlCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAFc291dGgABWZhbHNlCAAFbm9ydGgABWZhbHNlCAAEd2VzdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAdbWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2JhcnMIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHW1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9iYXJzCgAKcHJvcGVydGllcwgABGVhc3QABWZhbHNlCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAFc291dGgABWZhbHNlCAAFbm9ydGgABWZhbHNlCAAEd2VzdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfYmFycwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAfbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfYmFycwoACnByb3BlcnRpZXMIAARlYXN0AAVmYWxzZQgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABXNvdXRoAAVmYWxzZQgABW5vcnRoAAVmYWxzZQgABHdlc3QABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHm1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfYmFycwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAWbWluZWNyYWZ0OmNvcHBlcl9ncmF0ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAebWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2dyYXRlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZ3JhdGUKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIG1pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2dyYXRlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACBtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9ncmF0ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcl9ncmF0ZQgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAVbWluZWNyYWZ0OmNvcHBlcl9idWxiCgAKcHJvcGVydGllcwgAB3Bvd2VyZWQABWZhbHNlCAADbGl0AAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB1taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfYnVsYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2J1bGIKAApwcm9wZXJ0aWVzCAAHcG93ZXJlZAAFZmFsc2UIAANsaXQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2J1bGIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2J1bGIKAApwcm9wZXJ0aWVzCAAHcG93ZXJlZAAFZmFsc2UIAANsaXQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHm1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfYnVsYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAYbWluZWNyYWZ0OmNvcHBlcl9sYW50ZXJuCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAB2hhbmdpbmcABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIG1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9sYW50ZXJuCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACBtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfbGFudGVybgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdoYW5naW5nAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACJtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9sYW50ZXJuCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACJtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9sYW50ZXJuCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAB2hhbmdpbmcABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIW1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfbGFudGVybggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OmNvcHBlcl9nb2xlbV9zdGF0dWUKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAARY29wcGVyX2dvbGVtX3Bvc2UACHN0YW5kaW5nCAAGZmFjaW5nAAVub3J0aAAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACVtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACVtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAEWNvcHBlcl9nb2xlbV9wb3NlAAhzdGFuZGluZwgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAnbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACdtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9nb2xlbV9zdGF0dWUKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAARY29wcGVyX2dvbGVtX3Bvc2UACHN0YW5kaW5nCAAGZmFjaW5nAAVub3J0aAAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACZtaW5lY3JhZnQ6b3hpZGl6ZWRfY29wcGVyX2dvbGVtX3N0YXR1ZQgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAXbWluZWNyYWZ0OmxpZ2h0bmluZ19yb2QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcAAnVwAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAH21pbmVjcmFmdDpleHBvc2VkX2xpZ2h0bmluZ19yb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpleHBvc2VkX2xpZ2h0bmluZ19yb2QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcAAnVwAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIW1pbmVjcmFmdDp3ZWF0aGVyZWRfbGlnaHRuaW5nX3JvZAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAhbWluZWNyYWZ0OndlYXRoZXJlZF9saWdodG5pbmdfcm9kCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAB3Bvd2VyZWQABWZhbHNlCAAGZmFjaW5nAAJ1cAAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACBtaW5lY3JhZnQ6b3hpZGl6ZWRfbGlnaHRuaW5nX3JvZAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAWbWluZWNyYWZ0OmNvcHBlcl9jaGFpbgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARheGlzAAF5AAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHm1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9jaGFpbggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAebWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2NoYWluCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAgbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfY2hhaW4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2NoYWluCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcl9jaGFpbggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAgABHR5cGUAFG1pbmVjcmFmdDpydWxlX2Jhc2VkAAMAE2l0ZW1fZGFtYWdlX3Blcl91c2UAAAABAAEAFXVwZGF0ZV9mcm9tX25laWdoYm9ycwAIAA50cmFuc2Zvcm1fdHlwZQAMY29wcGVyX2NoZXN0CAAFc291bmQAGW1pbmVjcmFmdDppdGVtLmF4ZS5zY3JhcGUIAAhwYXJ0aWNsZQAGc2NyYXBlCgAUYmxvY2tfc3RhdGVfcHJvdmlkZXIJAAVydWxlcwoAAAADCgAEdGhlbgoABnNvdXJjZQgAAmlkABZtaW5lY3JhZnQ6Y29wcGVyX2NoZXN0CgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAAR0eXBlAAZzaW5nbGUAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAebWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2NoZXN0CAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfY2hlc3QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABHR5cGUABnNpbmdsZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACBtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9jaGVzdAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAgbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfY2hlc3QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABHR5cGUABnNpbmdsZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB9taW5lY3JhZnQ6b3hpZGl6ZWRfY29wcGVyX2NoZXN0CAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACAAEdHlwZQAUbWluZWNyYWZ0OnJ1bGVfYmFzZWQAAwATaXRlbV9kYW1hZ2VfcGVyX3VzZQAAAAEAAQAVdXBkYXRlX2Zyb21fbmVpZ2hib3JzAAgABXNvdW5kABltaW5lY3JhZnQ6aXRlbS5heGUuc2NyYXBlCAAIcGFydGljbGUABnNjcmFwZQoAFGJsb2NrX3N0YXRlX3Byb3ZpZGVyCQAFcnVsZXMKAAAAAwoABHRoZW4KAAZzb3VyY2UIAAJpZAAVbWluZWNyYWZ0OmNvcHBlcl9kb29yCgAKcHJvcGVydGllcwgABWhpbmdlAARsZWZ0CAAEaGFsZgAFbG93ZXIIAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB1taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZG9vcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyX2Rvb3IKAApwcm9wZXJ0aWVzCAAFaGluZ2UABGxlZnQIAARoYWxmAAVsb3dlcggAB3Bvd2VyZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABG9wZW4ABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2Rvb3IIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2Rvb3IKAApwcm9wZXJ0aWVzCAAFaGluZ2UABGxlZnQIAARoYWxmAAVsb3dlcggAB3Bvd2VyZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABG9wZW4ABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHm1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfZG9vcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAgABHR5cGUAFG1pbmVjcmFmdDpydWxlX2Jhc2VkAAMAE2l0ZW1fZGFtYWdlX3Blcl91c2UAAAABAAgABXNvdW5kABptaW5lY3JhZnQ6aXRlbS5heGUud2F4X29mZggACHBhcnRpY2xlAAd3YXhfb2ZmCgAUYmxvY2tfc3RhdGVfcHJvdmlkZXIJAAVydWxlcwoAAAA0CgAEdGhlbgoABnNvdXJjZQgAAmlkABZtaW5lY3JhZnQ6Y29wcGVyX2Jsb2NrAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAcbWluZWNyYWZ0OndheGVkX2NvcHBlcl9ibG9jawgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAYbWluZWNyYWZ0OmV4cG9zZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAebWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABptaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcgAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIG1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABltaW5lY3JhZnQ6b3hpZGl6ZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0OndheGVkX294aWRpemVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAUbWluZWNyYWZ0OmN1dF9jb3BwZXIACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABptaW5lY3JhZnQ6d2F4ZWRfY3V0X2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAcbWluZWNyYWZ0OmV4cG9zZWRfY3V0X2NvcHBlcgAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAIm1pbmVjcmFmdDp3YXhlZF9leHBvc2VkX2N1dF9jb3BwZXIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDp3ZWF0aGVyZWRfY3V0X2NvcHBlcgAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJG1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY3V0X2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAdbWluZWNyYWZ0Om94aWRpemVkX2N1dF9jb3BwZXIACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACNtaW5lY3JhZnQ6d2F4ZWRfb3hpZGl6ZWRfY3V0X2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAZbWluZWNyYWZ0OmN1dF9jb3BwZXJfc2xhYgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAR0eXBlAAZib3R0b20AAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0OndheGVkX2N1dF9jb3BwZXJfc2xhYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAhbWluZWNyYWZ0OmV4cG9zZWRfY3V0X2NvcHBlcl9zbGFiCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABHR5cGUABmJvdHRvbQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACdtaW5lY3JhZnQ6d2F4ZWRfZXhwb3NlZF9jdXRfY29wcGVyX3NsYWIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAI21pbmVjcmFmdDp3ZWF0aGVyZWRfY3V0X2NvcHBlcl9zbGFiCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABHR5cGUABmJvdHRvbQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACltaW5lY3JhZnQ6d2F4ZWRfd2VhdGhlcmVkX2N1dF9jb3BwZXJfc2xhYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAibWluZWNyYWZ0Om94aWRpemVkX2N1dF9jb3BwZXJfc2xhYgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAR0eXBlAAZib3R0b20AAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAobWluZWNyYWZ0OndheGVkX294aWRpemVkX2N1dF9jb3BwZXJfc2xhYggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAbbWluZWNyYWZ0OmN1dF9jb3BwZXJfc3RhaXJzCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGhhbGYABmJvdHRvbQgABXNoYXBlAAhzdHJhaWdodAgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAhbWluZWNyYWZ0OndheGVkX2N1dF9jb3BwZXJfc3RhaXJzCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACNtaW5lY3JhZnQ6ZXhwb3NlZF9jdXRfY29wcGVyX3N0YWlycwoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAVzaGFwZQAIc3RyYWlnaHQIAAZmYWNpbmcABW5vcnRoAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAKW1pbmVjcmFmdDp3YXhlZF9leHBvc2VkX2N1dF9jb3BwZXJfc3RhaXJzCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACVtaW5lY3JhZnQ6d2VhdGhlcmVkX2N1dF9jb3BwZXJfc3RhaXJzCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGhhbGYABmJvdHRvbQgABXNoYXBlAAhzdHJhaWdodAgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwArbWluZWNyYWZ0OndheGVkX3dlYXRoZXJlZF9jdXRfY29wcGVyX3N0YWlycwgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAkbWluZWNyYWZ0Om94aWRpemVkX2N1dF9jb3BwZXJfc3RhaXJzCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGhhbGYABmJvdHRvbQgABXNoYXBlAAhzdHJhaWdodAgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAqbWluZWNyYWZ0OndheGVkX294aWRpemVkX2N1dF9jb3BwZXJfc3RhaXJzCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABltaW5lY3JhZnQ6Y2hpc2VsZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAfbWluZWNyYWZ0OndheGVkX2NoaXNlbGVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAhbWluZWNyYWZ0OmV4cG9zZWRfY2hpc2VsZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAnbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY2hpc2VsZWRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACNtaW5lY3JhZnQ6d2VhdGhlcmVkX2NoaXNlbGVkX2NvcHBlcgAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAKW1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY2hpc2VsZWRfY29wcGVyCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACJtaW5lY3JhZnQ6b3hpZGl6ZWRfY2hpc2VsZWRfY29wcGVyAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAobWluZWNyYWZ0OndheGVkX294aWRpemVkX2NoaXNlbGVkX2NvcHBlcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAZbWluZWNyYWZ0OmNvcHBlcl90cmFwZG9vcgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB9taW5lY3JhZnQ6d2F4ZWRfY29wcGVyX3RyYXBkb29yCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACFtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfdHJhcGRvb3IKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEaGFsZgAGYm90dG9tCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEb3BlbgAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAnbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX3RyYXBkb29yCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACNtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl90cmFwZG9vcgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACltaW5lY3JhZnQ6d2F4ZWRfd2VhdGhlcmVkX2NvcHBlcl90cmFwZG9vcggABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAibWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcl90cmFwZG9vcgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAARoYWxmAAZib3R0b20IAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwAFbm9ydGgIAARvcGVuAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAChtaW5lY3JhZnQ6d2F4ZWRfb3hpZGl6ZWRfY29wcGVyX3RyYXBkb29yCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABVtaW5lY3JhZnQ6Y29wcGVyX2JhcnMKAApwcm9wZXJ0aWVzCAAEZWFzdAAFZmFsc2UIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAVzb3V0aAAFZmFsc2UIAAVub3J0aAAFZmFsc2UIAAR3ZXN0AAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABttaW5lY3JhZnQ6d2F4ZWRfY29wcGVyX2JhcnMIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHW1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9iYXJzCgAKcHJvcGVydGllcwgABGVhc3QABWZhbHNlCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAFc291dGgABWZhbHNlCAAFbm9ydGgABWZhbHNlCAAEd2VzdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAjbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX2JhcnMIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2JhcnMKAApwcm9wZXJ0aWVzCAAEZWFzdAAFZmFsc2UIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAVzb3V0aAAFZmFsc2UIAAVub3J0aAAFZmFsc2UIAAR3ZXN0AAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACVtaW5lY3JhZnQ6d2F4ZWRfd2VhdGhlcmVkX2NvcHBlcl9iYXJzCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6b3hpZGl6ZWRfY29wcGVyX2JhcnMKAApwcm9wZXJ0aWVzCAAEZWFzdAAFZmFsc2UIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAVzb3V0aAAFZmFsc2UIAAVub3J0aAAFZmFsc2UIAAR3ZXN0AAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACRtaW5lY3JhZnQ6d2F4ZWRfb3hpZGl6ZWRfY29wcGVyX2JhcnMIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAFm1pbmVjcmFmdDpjb3BwZXJfZ3JhdGUKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHG1pbmVjcmFmdDp3YXhlZF9jb3BwZXJfZ3JhdGUIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9ncmF0ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAkbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX2dyYXRlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACBtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9ncmF0ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAmbWluZWNyYWZ0OndheGVkX3dlYXRoZXJlZF9jb3BwZXJfZ3JhdGUIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfZ3JhdGUKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJW1pbmVjcmFmdDp3YXhlZF9veGlkaXplZF9jb3BwZXJfZ3JhdGUIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAFW1pbmVjcmFmdDpjb3BwZXJfYnVsYgoACnByb3BlcnRpZXMIAAdwb3dlcmVkAAVmYWxzZQgAA2xpdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAbbWluZWNyYWZ0OndheGVkX2NvcHBlcl9idWxiCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB1taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfYnVsYgoACnByb3BlcnRpZXMIAAdwb3dlcmVkAAVmYWxzZQgAA2xpdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAjbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX2J1bGIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2J1bGIKAApwcm9wZXJ0aWVzCAAHcG93ZXJlZAAFZmFsc2UIAANsaXQABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJW1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY29wcGVyX2J1bGIIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfYnVsYgoACnByb3BlcnRpZXMIAAdwb3dlcmVkAAVmYWxzZQgAA2xpdAAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAkbWluZWNyYWZ0OndheGVkX294aWRpemVkX2NvcHBlcl9idWxiCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB1taW5lY3JhZnQ6Y29wcGVyX2dvbGVtX3N0YXR1ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIABFjb3BwZXJfZ29sZW1fcG9zZQAIc3RhbmRpbmcIAAZmYWNpbmcABW5vcnRoAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAI21pbmVjcmFmdDp3YXhlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACVtaW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAEWNvcHBlcl9nb2xlbV9wb3NlAAhzdGFuZGluZwgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwArbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX2dvbGVtX3N0YXR1ZQgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAnbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAEWNvcHBlcl9nb2xlbV9wb3NlAAhzdGFuZGluZwgABmZhY2luZwAFbm9ydGgAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAtbWluZWNyYWZ0OndheGVkX3dlYXRoZXJlZF9jb3BwZXJfZ29sZW1fc3RhdHVlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACZtaW5lY3JhZnQ6b3hpZGl6ZWRfY29wcGVyX2dvbGVtX3N0YXR1ZQoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIABFjb3BwZXJfZ29sZW1fcG9zZQAIc3RhbmRpbmcIAAZmYWNpbmcABW5vcnRoAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MALG1pbmVjcmFmdDp3YXhlZF9veGlkaXplZF9jb3BwZXJfZ29sZW1fc3RhdHVlCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkABdtaW5lY3JhZnQ6bGlnaHRuaW5nX3JvZAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwACdXAAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAdbWluZWNyYWZ0OndheGVkX2xpZ2h0bmluZ19yb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpleHBvc2VkX2xpZ2h0bmluZ19yb2QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcAAnVwAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJW1pbmVjcmFmdDp3YXhlZF9leHBvc2VkX2xpZ2h0bmluZ19yb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIW1pbmVjcmFmdDp3ZWF0aGVyZWRfbGlnaHRuaW5nX3JvZAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwACdXAAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAnbWluZWNyYWZ0OndheGVkX3dlYXRoZXJlZF9saWdodG5pbmdfcm9kCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACBtaW5lY3JhZnQ6b3hpZGl6ZWRfbGlnaHRuaW5nX3JvZAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdwb3dlcmVkAAVmYWxzZQgABmZhY2luZwACdXAAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAmbWluZWNyYWZ0OndheGVkX294aWRpemVkX2xpZ2h0bmluZ19yb2QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAGG1pbmVjcmFmdDpjb3BwZXJfbGFudGVybgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdoYW5naW5nAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzAB5taW5lY3JhZnQ6d2F4ZWRfY29wcGVyX2xhbnRlcm4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9sYW50ZXJuCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAB2hhbmdpbmcABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJm1pbmVjcmFmdDp3YXhlZF9leHBvc2VkX2NvcHBlcl9sYW50ZXJuCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkACJtaW5lY3JhZnQ6d2VhdGhlcmVkX2NvcHBlcl9sYW50ZXJuCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgAB2hhbmdpbmcABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAKG1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY29wcGVyX2xhbnRlcm4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIW1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfbGFudGVybgoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAdoYW5naW5nAAVmYWxzZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACdtaW5lY3JhZnQ6d2F4ZWRfb3hpZGl6ZWRfY29wcGVyX2xhbnRlcm4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAFm1pbmVjcmFmdDpjb3BwZXJfY2hhaW4KAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzABxtaW5lY3JhZnQ6d2F4ZWRfY29wcGVyX2NoYWluCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB5taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfY2hhaW4KAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACRtaW5lY3JhZnQ6d2F4ZWRfZXhwb3NlZF9jb3BwZXJfY2hhaW4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAIG1pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2NoYWluCgAKcHJvcGVydGllcwgAC3dhdGVybG9nZ2VkAAVmYWxzZQgABGF4aXMAAXkAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAmbWluZWNyYWZ0OndheGVkX3dlYXRoZXJlZF9jb3BwZXJfY2hhaW4IAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfY2hhaW4KAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAEYXhpcwABeQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACVtaW5lY3JhZnQ6d2F4ZWRfb3hpZGl6ZWRfY29wcGVyX2NoYWluCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACAAEdHlwZQAUbWluZWNyYWZ0OnJ1bGVfYmFzZWQAAwATaXRlbV9kYW1hZ2VfcGVyX3VzZQAAAAEAAQAVdXBkYXRlX2Zyb21fbmVpZ2hib3JzAAgADnRyYW5zZm9ybV90eXBlAAxjb3BwZXJfY2hlc3QIAAVzb3VuZAAabWluZWNyYWZ0Oml0ZW0uYXhlLndheF9vZmYIAAhwYXJ0aWNsZQAHd2F4X29mZgoAFGJsb2NrX3N0YXRlX3Byb3ZpZGVyCQAFcnVsZXMKAAAABAoABHRoZW4KAAZzb3VyY2UIAAJpZAAWbWluZWNyYWZ0OmNvcHBlcl9jaGVzdAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEdHlwZQAGc2luZ2xlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAHG1pbmVjcmFmdDp3YXhlZF9jb3BwZXJfY2hlc3QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpleHBvc2VkX2NvcHBlcl9jaGVzdAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEdHlwZQAGc2luZ2xlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJG1pbmVjcmFmdDp3YXhlZF9leHBvc2VkX2NvcHBlcl9jaGVzdAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAgbWluZWNyYWZ0OndlYXRoZXJlZF9jb3BwZXJfY2hlc3QKAApwcm9wZXJ0aWVzCAALd2F0ZXJsb2dnZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABHR5cGUABnNpbmdsZQAACAAEdHlwZQAZbWluZWNyYWZ0OmNvcHlfcHJvcGVydGllcwAKAAdpZl90cnVlCAAGYmxvY2tzACZtaW5lY3JhZnQ6d2F4ZWRfd2VhdGhlcmVkX2NvcHBlcl9jaGVzdAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAoABHRoZW4KAAZzb3VyY2UIAAJpZAAfbWluZWNyYWZ0Om94aWRpemVkX2NvcHBlcl9jaGVzdAoACnByb3BlcnRpZXMIAAt3YXRlcmxvZ2dlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEdHlwZQAGc2luZ2xlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJW1pbmVjcmFmdDp3YXhlZF9veGlkaXplZF9jb3BwZXJfY2hlc3QIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAIAAR0eXBlABRtaW5lY3JhZnQ6cnVsZV9iYXNlZAADABNpdGVtX2RhbWFnZV9wZXJfdXNlAAAAAQABABV1cGRhdGVfZnJvbV9uZWlnaGJvcnMACAAFc291bmQAGm1pbmVjcmFmdDppdGVtLmF4ZS53YXhfb2ZmCAAIcGFydGljbGUAB3dheF9vZmYKABRibG9ja19zdGF0ZV9wcm92aWRlcgkABXJ1bGVzCgAAAAQKAAR0aGVuCgAGc291cmNlCAACaWQAFW1pbmVjcmFmdDpjb3BwZXJfZG9vcgoACnByb3BlcnRpZXMIAAVoaW5nZQAEbGVmdAgABGhhbGYABWxvd2VyCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEb3BlbgAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAbbWluZWNyYWZ0OndheGVkX2NvcHBlcl9kb29yCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACgAEdGhlbgoABnNvdXJjZQgAAmlkAB1taW5lY3JhZnQ6ZXhwb3NlZF9jb3BwZXJfZG9vcgoACnByb3BlcnRpZXMIAAVoaW5nZQAEbGVmdAgABGhhbGYABWxvd2VyCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEb3BlbgAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAjbWluZWNyYWZ0OndheGVkX2V4cG9zZWRfY29wcGVyX2Rvb3IIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAH21pbmVjcmFmdDp3ZWF0aGVyZWRfY29wcGVyX2Rvb3IKAApwcm9wZXJ0aWVzCAAFaGluZ2UABGxlZnQIAARoYWxmAAVsb3dlcggAB3Bvd2VyZWQABWZhbHNlCAAGZmFjaW5nAAVub3J0aAgABG9wZW4ABWZhbHNlAAAIAAR0eXBlABltaW5lY3JhZnQ6Y29weV9wcm9wZXJ0aWVzAAoAB2lmX3RydWUIAAZibG9ja3MAJW1pbmVjcmFmdDp3YXhlZF93ZWF0aGVyZWRfY29wcGVyX2Rvb3IIAAR0eXBlABltaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tzAAAKAAR0aGVuCgAGc291cmNlCAACaWQAHm1pbmVjcmFmdDpveGlkaXplZF9jb3BwZXJfZG9vcgoACnByb3BlcnRpZXMIAAVoaW5nZQAEbGVmdAgABGhhbGYABWxvd2VyCAAHcG93ZXJlZAAFZmFsc2UIAAZmYWNpbmcABW5vcnRoCAAEb3BlbgAFZmFsc2UAAAgABHR5cGUAGW1pbmVjcmFmdDpjb3B5X3Byb3BlcnRpZXMACgAHaWZfdHJ1ZQgABmJsb2NrcwAkbWluZWNyYWZ0OndheGVkX294aWRpemVkX2NvcHBlcl9kb29yCAAEdHlwZQAZbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrcwAACAAEdHlwZQAUbWluZWNyYWZ0OnJ1bGVfYmFzZWQAAwATaXRlbV9kYW1hZ2VfcGVyX3VzZQAAAAEADW1pbmVjcmFmdDpob2UBCQoAAAACCQAQZGlzYWxsb3dlZF9mYWNlcwgAAAABAARkb3duCAAFc291bmQAF21pbmVjcmFmdDppdGVtLmhvZS50aWxsCgAUYmxvY2tfc3RhdGVfcHJvdmlkZXIJAAVydWxlcwoAAAACCgAEdGhlbggAAmlkABJtaW5lY3JhZnQ6ZmFybWxhbmQKAApwcm9wZXJ0aWVzCAAIbW9pc3R1cmUAATAAAAoAB2lmX3RydWUJAApwcmVkaWNhdGVzCgAAAAIIAAN0YWcAHW1pbmVjcmFmdDp0dXJuc19pbnRvX2Zhcm1sYW5kCAAEdHlwZQAcbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrX3RhZwALAAZvZmZzZXQAAAADAAAAAAAAAAEAAAAACAADdGFnAA1taW5lY3JhZnQ6YWlyCAAEdHlwZQAcbWluZWNyYWZ0Om1hdGNoaW5nX2Jsb2NrX3RhZwAIAAR0eXBlABBtaW5lY3JhZnQ6YWxsX29mAAAKAAR0aGVuCAACaWQADm1pbmVjcmFmdDpkaXJ0AAoAB2lmX3RydWUJAApwcmVkaWNhdGVzCgAAAAIIAAZibG9ja3MAFW1pbmVjcmFmdDpjb2Fyc2VfZGlydAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MACwAGb2Zmc2V0AAAAAwAAAAAAAAABAAAAAAgAA3RhZwANbWluZWNyYWZ0OmFpcggABHR5cGUAHG1pbmVjcmFmdDptYXRjaGluZ19ibG9ja190YWcACAAEdHlwZQAQbWluZWNyYWZ0OmFsbF9vZgAACAAEdHlwZQAUbWluZWNyYWZ0OnJ1bGVfYmFzZWQAAwATaXRlbV9kYW1hZ2VfcGVyX3VzZQAAAAEACAAFc291bmQAF21pbmVjcmFmdDppdGVtLmhvZS50aWxsCAANZHJvcF9zdHJhdGVneQAMY2xpY2tlZF9mYWNlCAAEbG9vdAAabWluZWNyYWZ0OnRpbGwvcm9vdGVkX2RpcnQKABRibG9ja19zdGF0ZV9wcm92aWRlcgkABXJ1bGVzCgAAAAEKAAR0aGVuCAACaWQADm1pbmVjcmFmdDpkaXJ0AAoAB2lmX3RydWUIAAZibG9ja3MAFW1pbmVjcmFmdDpyb290ZWRfZGlydAgABHR5cGUAGW1pbmVjcmFmdDptYXRjaGluZ19ibG9ja3MAAAgABHR5cGUAFG1pbmVjcmFmdDpydWxlX2Jhc2VkAAMAE2l0ZW1fZGFtYWdlX3Blcl91c2UAAAABABBtaW5lY3JhZnQ6c2hvdmVsAQkKAAAAAQkAEGRpc2FsbG93ZWRfZmFjZXMIAAAAAQAEZG93bggABXNvdW5kAB1taW5lY3JhZnQ6aXRlbS5zaG92ZWwuZmxhdHRlbgoAFGJsb2NrX3N0YXRlX3Byb3ZpZGVyCQAFcnVsZXMKAAAAAQoABHRoZW4IAAJpZAATbWluZWNyYWZ0OmRpcnRfcGF0aAAKAAdpZl90cnVlCQAKcHJlZGljYXRlcwoAAAACCAADdGFnAB5taW5lY3JhZnQ6dHVybnNfaW50b19kaXJ0X3BhdGgIAAR0eXBlABxtaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tfdGFnAAsABm9mZnNldAAAAAMAAAAAAAAAAQAAAAAIAAN0YWcADW1pbmVjcmFmdDphaXIIAAR0eXBlABxtaW5lY3JhZnQ6bWF0Y2hpbmdfYmxvY2tfdGFnAAgABHR5cGUAEG1pbmVjcmFmdDphbGxfb2YAAAgABHR5cGUAFG1pbmVjcmFmdDpydWxlX2Jhc2VkAAMAE2l0ZW1fZGFtYWdlX3Blcl91c2UAAAABAA==";
}
