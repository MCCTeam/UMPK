using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Umpk.Client.Tests.Support.BlockUseSession;

public sealed class TrapdoorGateActivationSessionRuntimeTests
{
    private static IEnumerable<int> Protocols => JavaVersions.All.Select(version => version.Version.Protocol).Distinct();
    // Exact registered native classes/types across all fifty datasets; no suffix is used by production prediction.
    private static readonly string[] Trapdoors = ["trapdoor", "oak_trapdoor", "iron_trapdoor", "spruce_trapdoor", "birch_trapdoor", "jungle_trapdoor", "acacia_trapdoor", "dark_oak_trapdoor", "crimson_trapdoor", "warped_trapdoor", "mangrove_trapdoor", "bamboo_trapdoor", "cherry_trapdoor", "pale_oak_trapdoor", "poplar_trapdoor", "copper_trapdoor", "exposed_copper_trapdoor", "weathered_copper_trapdoor", "oxidized_copper_trapdoor", "waxed_copper_trapdoor", "waxed_exposed_copper_trapdoor", "waxed_weathered_copper_trapdoor", "waxed_oxidized_copper_trapdoor"];
    private static readonly string[] Gates = ["fence_gate", "oak_fence_gate", "spruce_fence_gate", "birch_fence_gate", "jungle_fence_gate", "acacia_fence_gate", "dark_oak_fence_gate", "crimson_fence_gate", "warped_fence_gate", "mangrove_fence_gate", "bamboo_fence_gate", "cherry_fence_gate", "pale_oak_fence_gate", "poplar_fence_gate"];
    private static string Wood(int protocol, bool gate) => gate ? protocol < 393 ? "fence_gate" : "oak_fence_gate" : protocol < 393 ? "trapdoor" : "oak_trapdoor";
    public static IEnumerable<object[]> EraHands() => Protocols.SelectMany(protocol => (protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off }).Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(EraHands))]
    public async Task RegisteredTrapdoorAndGateStatesUseNativeResultAndHandRules(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol, gate: false));
        await session.Block(Session.Target.Below(), "air");
        Assert.Empty(await session.Barrier());
        int blocks = 0, states = 0;
        foreach (string name in Trapdoors.Concat(Gates).Where(name => session.Client.State.Registries!.Blocks.TryGet(Identifier.Minecraft(name), out _)))
        {
            blocks++;
            BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
            foreach (int state in Enumerable.Range(block.MinStateId, block.StateCount))
            {
                await SetState(session, protocol, state);
                int before = session.Client.State.World.GetBlock(Session.Target).StateId;
                bool handles = (name != "iron_trapdoor" || protocol <= 210) && (hand == Hand.Main || protocol < 766);
                AssertUse(await session.Use(hand), protocol, hand, handles && protocol != 777, ItemStack.Empty);
                Assert.Equal(before, session.Client.State.World.GetBlock(Session.Target).StateId);
                Assert.True(session.Client.State.World.GetBlock(Session.Target.Below()).IsAir);
                Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
                states++;
            }
        }
        Assert.Equal(session.Client.State.Registries!.Blocks.Count(block => block.Id.Path is "trapdoor" or "fence_gate" || block.Id.Path.EndsWith("_trapdoor", StringComparison.Ordinal) || block.Id.Path.EndsWith("_fence_gate", StringComparison.Ordinal)), blocks);
        Assert.True(states > blocks);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
    }

    public static IEnumerable<object[]> ContextCases()
    {
        foreach (int protocol in new[] { 47, 107, 210, 315, 340, 393, 498, 573, 761, 765, 766, 768, 776, 777 })
            foreach (bool gate in new[] { false, true })
                foreach (int scenario in Enumerable.Range(0, 10))
                    if (protocol != 47 || scenario is not (3 or 5 or 6 or 9))
                        yield return [protocol, gate, scenario];
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ActivationPrecedesPermissionsCooldownAndCustomItemsButRespectsSecondaryUse(int protocol, bool gate, int scenario)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol, gate));
        if (scenario is 1 or 4)
            await session.Hold("stick");
        if (scenario == 5)
        {
            await session.Hold("diamond_axe");
            await session.Cooldown();
        }
        if (scenario == 9)
            await session.Hold("diamond_axe", custom: true);
        if (scenario == 3)
            await session.Offhand("stick");
        if (scenario is 1 or 2 or 3)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.True(session.Client.State.Self.Sneaking);
            Assert.Empty(await session.Barrier());
        }
        if (scenario == 4)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (scenario == 7)
            await session.Mode(GameMode.Spectator, instantBuild: false);
        if (scenario == 8)
        {
            await session.Forget();
            await session.Block(Session.Target, Wood(protocol, gate));
            Assert.Empty(await session.Barrier());
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        Hand hand = scenario == 6 ? Hand.Off : Hand.Main;
        bool handles = scenario is not (1 or 3 or 8) && (scenario != 6 || protocol < 766);
        if (scenario == 7)
            handles = protocol < 768;
        AssertUse(await session.Use(hand), protocol, hand, handles && protocol != 777, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
    }

    public static IEnumerable<object[]> AvailabilityCases() => new[] { 47, 315, 340, 393, 498, 765, 766, 777 }.SelectMany(protocol => new[] { Wood(protocol, false), "iron_trapdoor", Wood(protocol, true) }.Select(name => new object[] { protocol, name }));

    [Theory]
    [MemberData(nameof(AvailabilityCases))]
    public async Task PacketTerrainArrivalUnloadAndReloadStayDistinctFromStorageUpdates(int protocol, string name)
    {
        await using Session session = await Session.Create(protocol, name, terrain: false);
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, false, ItemStack.Empty);
        bool swing = (name != "iron_trapdoor" || protocol <= 210) && protocol != 777;
        await session.Terrain();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, swing, ItemStack.Empty);
        await session.Forget();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, false, ItemStack.Empty);
        await session.Terrain();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, swing, ItemStack.Empty);
    }

    public static IEnumerable<object[]> FeatureTargets()
    {
        foreach ((int protocol, string target) in new[] { (761, "bamboo_trapdoor"), (761, "bamboo_fence_gate"), (762, "cherry_trapdoor"), (762, "cherry_fence_gate"), (765, "waxed_weathered_copper_trapdoor"), (766, "waxed_weathered_copper_trapdoor"), (768, "pale_oak_trapdoor"), (768, "pale_oak_fence_gate") })
            foreach (bool enabled in new[] { false, true })
                yield return [protocol, target, enabled];
    }

    [Theory]
    [MemberData(nameof(FeatureTargets))]
    public async Task DisabledTargetsFailAfterUseBeforeBorrowingAnItemSwing(int protocol, string target, bool enabled)
    {
        await using Session session = await Session.Create(protocol, target, featureFlags: enabled);
        await session.Hold("diamond_axe");
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, enabled, session.Client.State.Inventory.GetSlot(36));
    }

    [Theory]
    [InlineData(761, "bamboo_trapdoor")]
    [InlineData(762, "cherry_fence_gate")]
    [InlineData(765, "copper_trapdoor")]
    [InlineData(766, "copper_trapdoor")]
    [InlineData(768, "pale_oak_fence_gate")]
    public async Task DisabledHeldItemsRejectBeforeTheUsePacket(int protocol, string item)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol, gate: false), featureFlags: false);
        await session.Hold(item);
        Assert.Empty(await session.Use(Hand.Main));
    }

    [Theory]
    [InlineData(315)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(498)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task IronPassContinuesIntoOrdinaryBlockItemUse(int protocol)
    {
        await using Session session = await Session.Create(protocol, "iron_trapdoor", physics: true);
        await session.Hold("stone");
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, protocol != 777, session.Client.State.Inventory.GetSlot(36));
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> ItemPhaseHands() => new[] { 766, 768, 776, 777 }.SelectMany(protocol => new[] { false, true }.Select(gate => new object[] { protocol, gate }));

    [Theory]
    [MemberData(nameof(ItemPhaseHands))]
    public async Task ModernOffhandReachesTheItemPhaseWithoutMainHandActivation(int protocol, bool gate)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol, gate), physics: true);
        await session.Offhand("stone");
        AssertUse(await session.Use(Hand.Off), protocol, Hand.Off, protocol != 777, session.Client.State.Inventory.GetSlot(45));
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(45).Count);
    }

    public static IEnumerable<object[]> SecondaryItemCases() => new[] { 47, 107, 340, 393, 766, 777 }.SelectMany(protocol => new[] { false, true }.Select(gate => new object[] { protocol, gate }));

    [Theory]
    [MemberData(nameof(SecondaryItemCases))]
    public async Task SecondaryUseReachesTheExistingItemPhase(int protocol, bool gate)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol, gate), physics: true);
        await session.Hold("stone");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        Assert.True(session.Client.State.Self.Sneaking);
        Assert.Empty(await session.Barrier());
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, protocol != 777, session.Client.State.Inventory.GetSlot(36));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOffhandRejectsBeforeMainHandFallback(bool gate)
    {
        await using Session session = await Session.Create(47, Wood(47, gate));
        await session.Hold("stone");
        await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Use(Hand.Off));
        Assert.Empty(await session.Barrier());
    }

    [Theory]
    [InlineData(47, false, false)]
    [InlineData(47, true, false)]
    [InlineData(315, false, true)]
    [InlineData(393, false, false)]
    [InlineData(393, true, false)]
    [InlineData(766, false, true)]
    [InlineData(766, true, true)]
    [InlineData(766, false, false)]
    [InlineData(766, true, false)]
    [InlineData(777, false, true)]
    [InlineData(777, true, true)]
    [InlineData(777, false, false)]
    [InlineData(777, true, false)]
    public async Task MissingNativeTerrainCannotBecomeClearDuringItemContinuation(int protocol, bool gate, bool offhandOrIron)
    {
        bool offhand = offhandOrIron && protocol >= 766;
        string target = offhandOrIron && protocol == 315 ? "iron_trapdoor" : Wood(protocol, gate);
        await using Session session = await Session.Create(protocol, target, terrain: false, physics: true);
        if (offhand)
            await session.Offhand("stone");
        else
            await session.Hold("stone");
        if (!offhandOrIron)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.True(session.Client.State.Self.Sneaking);
            Assert.Empty(await session.Barrier());
        }
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
        Hand hand = offhand ? Hand.Off : Hand.Main;
        AssertUse(await session.Use(hand), protocol, hand, false, session.Client.State.Inventory.GetSlot(offhand ? 45 : 36));
    }

    private static async Task SetState(Session session, int protocol, int state)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("block_update"));
        Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var expected = new ClientboundBlockUpdatePacket(Session.Target, state);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, expected, context);
        byte[] payload = buffer.WrittenSpan.ToArray();
        var decoded = Assert.IsType<ClientboundBlockUpdatePacket>(codec.Decode(payload, context));
        Assert.Equal(expected, decoded);
        buffer = new ArrayBufferWriter<byte>();
        writer = new PacketWriter(buffer);
        codec.Encode(ref writer, decoded, context);
        Assert.Equal(payload, buffer.WrittenSpan.ToArray());
        await session.Server.SendFrameAsync(pair.WireId, payload, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    private static void AssertUse(List<IPacket> sent, int protocol, Hand hand, bool packetSwing, ItemStack held)
    {
        Assert.Equal(packetSwing ? 2 : 1, sent.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(sent[0]);
            Assert.Equal(Session.Target, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal(held.Item.Id, use.HeldItem.Item.Id);
            Assert.Equal(held.Count, use.HeldItem.Count);
            Assert.Equal((byte)8, use.CursorX);
            Assert.Equal((byte)16, use.CursorY);
            Assert.Equal((byte)8, use.CursorZ);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            Assert.Equal(Session.Target, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal((int)hand, use.Hand);
            Assert.Equal(.5F, use.CursorX);
            Assert.Equal(1F, use.CursorY);
            Assert.Equal(.5F, use.CursorZ);
            Assert.False(use.Inside);
            Assert.False(use.WorldBorderHit);
            Assert.True(protocol < 759 || use.Sequence > 0);
        }
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
    }
}
