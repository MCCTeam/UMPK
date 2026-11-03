using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class LaterAdventureSessionTests
{
    public static IEnumerable<object[]> IdentityCases()
    {
        foreach (int protocol in new[] { 774, 775, 776, 777 })
            foreach (string scenario in new[] { "simple", "concrete", "presence", "duplicate-presence" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public async Task FramedAdventureMiningUsesNativeFreshPredicateIdentity(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Info(GameMode.Adventure);
        await Hold(session, Permission(session, scenario));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => AssertAction(packet, protocol, 0), packet => AssertSwing(packet, protocol), packet => AssertSwing(packet, protocol));
            AssertSwing(Assert.Single(await session.Tick()), protocol);
            Assert.False(action.IsCompleted);
            AdventureModePredicateComponent before = Assert.IsType<AdventureModePredicateComponent>(session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            await Hold(session, Permission(session, scenario));
            AdventureModePredicateComponent after = Assert.IsType<AdventureModePredicateComponent>(session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            Assert.NotSame(before, after);
            Assert.NotSame(before.Predicates, after.Predicates);
            bool reset = scenario is "presence" or "duplicate-presence";
            List<IPacket> continued = await session.Tick();
            if (reset)
                Assert.Collection(continued, packet => AssertAction(packet, protocol, protocol == 777 ? 2 : 1), packet => AssertAction(packet, protocol, 0), packet => AssertSwing(packet, protocol));
            else
                AssertSwing(Assert.Single(continued), protocol);
            int stopAfter = reset ? 6 : 3;
            for (int tick = 1; tick <= stopAfter; tick++)
            {
                List<IPacket> packets = await session.Tick();
                if (tick < stopAfter)
                {
                    AssertSwing(Assert.Single(packets), protocol);
                    Assert.False(action.IsCompleted);
                }
                else
                {
                    Assert.Collection(packets, packet => AssertAction(packet, protocol, protocol == 777 ? 3 : 2), packet => AssertSwing(packet, protocol));
                    Assert.False(action.IsCompleted);
                    await session.Complete(action, Assert.IsType<ServerboundPlayerActionPacket>(packets[0]));
                    Assert.True(action.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task KnownMissingPermissionRetainsHeldRequestUntilFreshMatchingFrame(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Info(GameMode.Adventure);
        await session.Hold("diamond_pickaxe");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            AssertSwing(Assert.Single(await session.Tick()), protocol);
            Assert.False(action.IsCompleted);
            await Hold(session, Permission(session, "presence"));
            Assert.Collection(await session.Tick(), packet => AssertAction(packet, protocol, 0), packet => AssertSwing(packet, protocol));
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    public static IEnumerable<object[]> UnknownCases()
    {
        foreach (int protocol in new[] { 774, 775, 776, 777 })
            foreach (string missing in new[] { "tag", "range", "block-entity" })
                yield return [protocol, missing];
    }

    [Theory]
    [MemberData(nameof(UnknownCases))]
    public async Task MissingWorldContextRemainsUnknownAfterLaterCodecBinding(int protocol, string missing)
    {
        await using Session session = await Session.Create(protocol, observeTags: missing != "tag");
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Info(GameMode.Adventure);
        await session.Block(Session.Target, missing == "range" ? "oak_log" : missing == "block-entity" ? "chest" : "stone");
        Assert.Empty(await session.Barrier());
        int block = session.Client.State.World.GetBlock(Session.Target).Block.NetworkId;
        var predicate = new BlockPredicateEntry(
            missing == "tag" ? new HolderSetRef(Identifier.Minecraft("unreceived_later_permission"), []) : new HolderSetRef(null, [block]),
            missing == "range" ? [new StatePropertyMatcher("axis", false, MinValue: "x", MaxValue: "z")] : null,
            missing == "block-entity" ? new NbtCompound { ["id"] = new NbtString("minecraft:chest") } : null)
        {
            ComponentMatchers = new([], [new BlockPredicatePartialMatcher(Identifier.Minecraft("damage"), true, new NbtCompound())]),
        };
        await Hold(session, new AdventureModePredicateComponent([predicate]), item: "stone");
        if (missing == "tag")
            Assert.Null(await session.Client.InvokeAsync(client => client.State.RegistryTags.Contains(RegistryIds.Block, Identifier.Minecraft("unreceived_later_permission"), block), session.Token));
        if (missing == "range")
        {
            Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("axis", out string axis));
            Assert.Equal("y", axis);
        }
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Empty(await session.Tick());
        InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => action.WaitAsync(session.Token));
        Assert.Equal("The Adventure permission requires unavailable tag, property-kind or block-entity facts.", error.Message);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    [Theory]
    [InlineData(770, false)]
    [InlineData(770, true)]
    [InlineData(774, false)]
    [InlineData(774, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public async Task KnownEmptyAdventureDataFailsClickBeforePeerFrameWhileKeepingPrediction(int protocol, bool placement)
    {
        await using Session session = await Session.Create(protocol);
        var key = placement ? DataComponents.CanPlaceOn : DataComponents.CanBreak;
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 1);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 19, 9, stack.With(key, new AdventureModePredicateComponent([]))));
        Assert.Empty(await session.Barrier());
        Task<ClickResult> click = await session.Client.InvokeAsync(client => client.Actions.Inventory.ClickAsync(new ClickAction.Pickup(9, MouseButton.Left), session.Token), session.Token);
        await Assert.ThrowsAsync<ArgumentException>(() => click.WaitAsync(session.Token));
        Assert.True(session.Client.State.Inventory.GetSlot(9).IsEmpty);
        Assert.Empty(Assert.IsType<AdventureModePredicateComponent>(session.Client.State.Inventory.Cursor.Components.Get(key)).Predicates);
        Assert.True(session.Client.State.Inventory.HasPendingPrediction);
        const long nonce = 703774;
        await session.Send(new ClientboundPlayKeepAlivePacket(nonce));
        var frame = await session.Server.NextFrameAsync(session.Token);
        PhaseRegistry outbound = BoundPacketApplierHarness.Version(protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(outbound.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
        var echo = Assert.IsType<ServerboundPlayKeepAlivePacket>(codec.Decode(frame.Payload, new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty)));
        Assert.Equal(nonce, echo.Id);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    public static IEnumerable<object[]> PlacementCases()
    {
        foreach (int protocol in new[] { 774, 775, 776, 777 })
            foreach (string outcome in new[] { "allow", "deny", "unknown" })
                yield return [protocol, outcome];
    }

    [Theory]
    [MemberData(nameof(PlacementCases))]
    public async Task PlacementUsesNativePermissionResultAndEraSwing(int protocol, string outcome)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        HolderSetRef blocks = outcome == "unknown"
            ? new HolderSetRef(Identifier.Minecraft("unreceived_later_placement"), [])
            : new HolderSetRef(null, [session.Client.State.Registries!.Blocks[Identifier.Minecraft(outcome == "allow" ? "stone" : "dirt")].NetworkId]);
        var permission = new AdventureModePredicateComponent([new BlockPredicateEntry(blocks, null, null)
        {
            ComponentMatchers = new([], [new BlockPredicatePartialMatcher(Identifier.Minecraft("damage"), true, new NbtCompound())]),
        }]);
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 1);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 7, 36, stack.With(DataComponents.CanPlaceOn, permission)));
        Assert.Empty(await session.Barrier());
        BlockUsePrediction actual = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(BlockUseSession.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        BlockUsePrediction expected = outcome switch
        {
            "allow" => BlockUsePrediction.Success(localSwing: protocol == 777),
            "deny" => BlockUsePrediction.Pass,
            _ => BlockUsePrediction.Unknown,
        };
        Assert.Equal(expected, actual);
        List<IPacket> sent = await session.Use(Hand.Main);
        Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
        if (outcome == "allow" && protocol != 777)
            Assert.IsType<ServerboundSwingPacket>(Assert.Single(sent.Skip(1)));
        else
            Assert.Single(sent);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    private static AdventureModePredicateComponent Permission(Session session, string scenario)
    {
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId;
        BlockPredicatePartialMatcher[] partial = scenario switch
        {
            "simple" => [],
            "concrete" => [new(Identifier.Minecraft("damage"), false, new NbtCompound())],
            "presence" => [new(Identifier.Minecraft("damage"), true, new NbtCompound())],
            "duplicate-presence" => [new(Identifier.Minecraft("damage"), true, new NbtCompound()), new(Identifier.Minecraft("damage"), true, new NbtCompound())],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        return new AdventureModePredicateComponent([new BlockPredicateEntry(new HolderSetRef(null, [stone]), null, null) { ComponentMatchers = new([], partial) }]);
    }

    private static async Task Hold(Session session, AdventureModePredicateComponent component, string item = "diamond_pickaxe")
    {
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft(item)], 1);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 17, 36, stack.With(DataComponents.CanBreak, component)));
        Assert.Empty(await session.Barrier());
        var actual = Assert.IsType<AdventureModePredicateComponent>(session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
        Assert.NotSame(component, actual);
        Assert.Equal(component.Predicates.Count, actual.Predicates.Count);
        Assert.Equal(component.Predicates.SelectMany(predicate => predicate.ComponentMatchers!.Partial).Select(value => (value.Type, value.IsAnyValue)), actual.Predicates.SelectMany(predicate => predicate.ComponentMatchers!.Partial).Select(value => (value.Type, value.IsAnyValue)));
    }

    private static void AssertAction(IPacket packet, int protocol, int expected)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(packet);
        Assert.Equal(expected, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.North, action.Direction);
        if (expected == (protocol == 777 ? 2 : 1))
            Assert.Equal(0, action.Sequence);
        else
            Assert.True(action.Sequence > 0);
    }

    private static void AssertSwing(IPacket packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
        {
            var swing = Assert.IsType<ServerboundSwingPacket>(packet);
            Assert.True(swing.HasHand);
            Assert.Equal(0, swing.Hand);
        }
    }
}
