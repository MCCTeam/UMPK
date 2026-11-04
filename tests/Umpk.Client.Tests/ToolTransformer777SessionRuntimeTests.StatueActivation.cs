using Umpk.Client.Actions;
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

public sealed partial class ToolTransformer777SessionRuntimeTests
{
    private static readonly int[] StatueActivationProtocols = [773, 774, 775, 776, 777];
    private static readonly (string Input, string Target, int Index, int TargetIndex)[] StatueActivationMappings =
    [
        ("exposed_copper_golem_statue", "copper_golem_statue", 1, 0),
        ("weathered_copper_golem_statue", "exposed_copper_golem_statue", 2, 1),
        ("oxidized_copper_golem_statue", "weathered_copper_golem_statue", 3, 2),
        ("waxed_copper_golem_statue", "copper_golem_statue", 4, 0),
        ("waxed_exposed_copper_golem_statue", "exposed_copper_golem_statue", 5, 1),
        ("waxed_weathered_copper_golem_statue", "weathered_copper_golem_statue", 6, 2),
        ("waxed_oxidized_copper_golem_statue", "oxidized_copper_golem_statue", 7, 3),
    ];

    public static IEnumerable<object[]> StatueActivationCases()
    {
        foreach (int protocol in StatueActivationProtocols)
        {
            // Original registry observations bind all 32 states of every family; default is min+1.
            int first = protocol switch { 773 or 774 => 27085, 775 => 27287, 776 => 29760, _ => 33117 };
            foreach (var mapping in StatueActivationMappings)
                foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                    yield return [protocol, mapping.Input, mapping.Target, first + mapping.Index * 32 + 1, first + mapping.TargetIndex * 32 + 1, hand];
        }
    }

    [Theory]
    [MemberData(nameof(StatueActivationCases))]
    public async Task OrdinaryNativeAxesPassStatueActivationBeforeBothHandItemContinuation(int protocol, string input, string target, int original, int transformed, Hand hand)
    {
        await using Session session = await StatueActivationReady(protocol, input);
        await StatueActivationTags(session, ["wooden_axe", "stone_axe", "copper_axe", "iron_axe", "golden_axe", "diamond_axe", "netherite_axe"]);
        await StatueActivationHold(session, hand, "diamond_axe");
        Assert.False(session.Client.State.Self.Sneaking);
        Assert.Equal(original, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(Identifier.Minecraft(target), new BlockState(session.Client.State.World.BlockData, transformed).Block.Id);
        await StatueActivationUse(session, hand, BlockUseResult.Success, protocol == 777 ? transformed : original);
    }

    public static IEnumerable<object[]> StatueActivationContexts() => StatueActivationProtocols.SelectMany(protocol => Enumerable.Range(0, 18).Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(StatueActivationContexts))]
    public async Task OrdinaryStatueActivationKeepsTagIdentityPermissionsAndUnavailableContextSeparate(int protocol, int context)
    {
        string block = context is 0 or 1 or 5 ? "exposed_copper_golem_statue" : context == 7 ? "copper_golem_statue" : "waxed_copper_golem_statue";
        await using Session session = await StatueActivationReady(protocol, block, terrain: context != 13, definitions: context != 16);
        string? item = context is 4 or 5 ? "stick" : context == 6 ? null : context == 8 ? "diamond_pickaxe" : "diamond_axe";
        await StatueActivationHold(session, Hand.Main, item, patched: context == 14);
        if (context is not (0 or 12 or 15))
        {
            if (context == 2)
                await StatueActivationTags(session, [], omitTag: true);
            else if (context == 3)
                await StatueActivationTags(session, ["stone"], wrongRegistry: true);
            else
                await StatueActivationTags(session, context == 1 ? [] : context is 4 or 5 ? ["stick"] : context == 6 ? ["air"] : ["diamond_axe"]);
        }
        if (context == 9)
            await session.Cooldown();
        if (context == 10)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == 11)
            await session.Offhand("shield");
        if (context == 12)
            await Secondary(session);
        if (context == 15)
            await session.Mode(GameMode.Spectator, instantBuild: false);
        bool? membership = session.Client.State.RegistryTags.Contains(RegistryIds.Item, Identifier.Minecraft("axes"), session.Client.State.Registries!.Items[Identifier.Minecraft(item ?? "air")].NetworkId);
        bool? expectedMembership = context is 0 or 3 or 12 or 15 ? null : context is 1 or 2 or 8 ? false : true;
        Assert.Equal(expectedMembership, membership);
        int original = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUseResult expected = context switch
        {
            4 or 5 or 6 or 9 or 10 or 11 => BlockUseResult.Pass,
            12 => BlockUseResult.Success,
            15 => BlockUseResult.Consume,
            16 when protocol != 777 => BlockUseResult.Success,
            17 => BlockUseResult.Success,
            _ => BlockUseResult.Unknown,
        };
        int transformed = session.Client.State.Registries.Blocks[Identifier.Minecraft("copper_golem_statue")].Value.DefaultStateId;
        int expectedBeforeUse = protocol == 777 && expected == BlockUseResult.Success ? transformed : original;
        if (context == 13)
        {
            int expectedStored = protocol switch { 773 or 774 => 27214, 775 => 27416, 776 => 29889, _ => 33246 };
            Assert.Equal(expectedStored, original);
            expectedBeforeUse = protocol switch { 773 or 774 => 15090, 775 or 776 => 15292, _ => 18649 };
        }
        await StatueActivationUse(session, Hand.Main, expected, expectedBeforeUse, unavailableTerrain: context == 13);
        if (context == 17)
        {
            await StatueActivationTags(session, []);
            Assert.False(session.Client.State.RegistryTags.Contains(RegistryIds.Item, Identifier.Minecraft("axes"), session.Client.State.Inventory.GetSlot(36).Item.NetworkId));
            await StatueActivationUse(session, Hand.Main, BlockUseResult.Unknown, original, sequence: 2);
        }
    }

    private static Task<Session> StatueActivationReady(int protocol, string block, bool terrain = true, bool definitions = true)
        => Session.Create(protocol, block, terrain: terrain, observeWrites: true, configurationPackets: protocol == 777 && definitions ? [NativePacket()] : []);

    private static async Task StatueActivationTags(Session session, string[] names, bool omitTag = false, bool wrongRegistry = false)
    {
        Identifier registry = wrongRegistry ? RegistryIds.Block : RegistryIds.Item;
        int[] ids = names.Select(name => wrongRegistry ? session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId : session.Client.State.Registries!.Items[Identifier.Minecraft(name)].NetworkId).ToArray();
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(registry, omitTag ? [] : [new TagEntry(Identifier.Minecraft("axes"), ids)])]));
        Assert.Empty(await session.Barrier());
    }

    private static async Task StatueActivationHold(Session session, Hand hand, string? name, bool patched = false)
    {
        ItemStack stack = name is null ? ItemStack.Empty : new(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 2);
        if (patched)
            stack = stack.With(DataComponents.Unbreakable, new UnbreakableComponent());
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, hand == Hand.Main ? stack : ItemStack.Empty));
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 45, hand == Hand.Off ? stack : ItemStack.Empty));
        Assert.Empty(await session.Barrier());
        Assert.Equal(stack, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
    }

    private static async Task StatueActivationUse(Session session, Hand hand, BlockUseResult expected, int expectedBeforeUse, int sequence = 1, bool unavailableTerrain = false)
    {
        ItemStack main = session.Client.State.Inventory.GetSlot(36);
        ItemStack off = session.Client.State.Inventory.GetSlot(45);
        int received = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction preview = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new(.5, 1, .5), hand), session.Token);
        if (unavailableTerrain)
            AssertUnavailableProjection(expectedBeforeUse);
        BlockState? atWrite = null;
        session.BeforeClientWrite = () => atWrite ??= session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, Session.Target);
        List<IPacket> packets;
        try { packets = await session.Use(hand); }
        finally { session.BeforeClientWrite = null; }
        BlockUseSwing swing = expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(new BlockUsePrediction(true, expected, swing), preview);
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)Direction.Up, (int)hand, sequence), (use.Position, use.Face, use.Hand, use.Sequence));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside || use.WorldBorderHit);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket or ServerboundPlayerActionPacket);
        BlockState captured = Assert.IsType<BlockState>(atWrite);
        Assert.Equal(expectedBeforeUse, captured.StateId);
        Assert.Same(session.Client.State.World.BlockData, captured.Source);
        if (unavailableTerrain)
        {
            Assert.True(captured.IsValid);
            Assert.True(captured.IsAir);
            Assert.Equal(Identifier.Minecraft("void_air"), captured.Block.Id);
            AssertUnavailableProjection(expectedBeforeUse);
        }
        else
            Assert.Equal(received, await Current(session));
        Assert.Equal(received, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        Assert.Empty(await session.Barrier());

        void AssertUnavailableProjection(int nativeVoidAir)
        {
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            BlockState stored = session.Client.State.World.GetBlock(Session.Target);
            Assert.True(stored.IsValid);
            Assert.Equal(received, stored.StateId);
            Assert.Equal(Identifier.Minecraft("waxed_copper_golem_statue"), stored.Block.Id);
            Assert.Same(session.Client.State.World.BlockData, stored.Source);
            var declared = session.Client.State.Registries!.Blocks[Identifier.Minecraft("void_air")];
            Assert.Equal(nativeVoidAir, declared.Value.DefaultStateId);
            BlockState effective = session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, Session.Target);
            Assert.True(effective.IsValid);
            Assert.True(effective.IsAir);
            Assert.Equal(declared.Id, effective.Block.Id);
            Assert.Equal(nativeVoidAir, effective.StateId);
            Assert.Same(session.Client.State.World.BlockData, effective.Source);
        }
    }
}
