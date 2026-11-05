using Umpk.Client.Actions;
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

public sealed class BoneMealWheatSessionRuntimeTests
{
    public static IEnumerable<object[]> WheatCases() => Enumerable.Range(0, 8).SelectMany(age => new[] { Hand.Main, Hand.Off }.SelectMany(hand => new[] { GameMode.Survival, GameMode.Creative }.Select(mode => new object[] { age, hand, mode })));

    public static IEnumerable<object[]> ContextCases() => new[] { 0, 7 }.SelectMany(age => new[] { Hand.Main, Hand.Off }.SelectMany(hand => new[] { "adventure", "cooldown", "cooldown-adventure", "secondary", "spectator", "missing-terrain", "unload", "respawn", "custom", "custom-adventure", "empty", "other-target", "other-hand-empty", "clone" }.Select(context => new object[] { age, hand, context })));

    public static IEnumerable<object[]> BoundaryCases() => new[] { 765 }.SelectMany(protocol => new[] { 0, 7 }.SelectMany(age => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, age, hand })));

    public static IEnumerable<object[]> AdmittedBoundaryCases() => new[] { 767, 777 }.SelectMany(protocol => new[] { 0, 7 }.SelectMany(age => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, age, hand })));

    public static IEnumerable<object[]> ComponentWheatCases() => Enumerable.Range(767, 11).SelectMany(protocol => new[] { 0, 6, 7 }.SelectMany(age => new[] { Hand.Main, Hand.Off }.SelectMany(hand => new[] { GameMode.Survival, GameMode.Creative }.Where(mode => !(protocol is 767 or 777 && age is 0 or 7 && mode == GameMode.Survival)).Select(mode => new object[] { protocol, age, hand, mode }))));

    public static IEnumerable<object[]> ComponentContextCases() => Enumerable.Range(767, 11).SelectMany(protocol => new[] { Hand.Main, Hand.Off }.SelectMany(hand => new[] { "adventure", "cooldown", "spectator", "custom", "custom-adventure", "clone", "missing-terrain", "unload", "respawn" }.Select(context => new object[] { protocol, hand, context })));

    [Fact]
    public async Task CanonicalWheatMatchesTheOriginalEightStateAgeAndEmptySupportSchema()
    {
        await using Session session = await Ready(766, 0);
        RegistryEntry<BlockDefinition> wheat = JavaGameData.Registries(766).Blocks[Identifier.Minecraft("wheat")];
        Assert.Equal((4278, 4285, 4278, 8), (wheat.Value.MinStateId, wheat.Value.MaxStateId, wheat.Value.DefaultStateId, wheat.Value.StateCount));
        BlockPropertyDefinition property = Assert.Single(wheat.Value.Properties);
        Assert.Equal("age", property.Name);
        Assert.Equal(new[] { "0", "1", "2", "3", "4", "5", "6", "7" }, property.Values);
        for (int age = 0; age < 8; age++)
        {
            var block = new BlockState(session.Client.State.World.BlockData, 4278 + age);
            Assert.True(block.TryGetProperty("age", out string actual));
            Assert.Equal(age.ToString(System.Globalization.CultureInfo.InvariantCulture), actual);
            Assert.Same(wheat.Value, block.Block.Value);
            Assert.Empty(JavaGameData.BlockShapes(766).GetCollisionShapes(block).ToArray());
        }
        ItemStack held = Held(session, Hand.Main);
        Assert.True(held.Components.HasNoPatch);
        Assert.Null(held.Components.Get(DataComponents.CanPlaceOn));
    }

    [Theory]
    [MemberData(nameof(WheatCases))]
    public async Task OriginalWheatResultControlsThePublicUseAndSwingWithoutGrowingOrShrinking(int age, Hand hand, GameMode mode)
    {
        await using Session session = await Ready(766, age);
        await session.Mode(mode, instantBuild: mode == GameMode.Creative);
        await Use(session, hand, age == 7 ? BlockUseResult.Pass : BlockUseResult.Success, (Direction)(age % 6));
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task NativePrechecksAndUnownedContextsKeepTheirExistingBoundaries(int age, Hand hand, string context)
    {
        await using Session session = await Ready(766, age, terrain: context != "missing-terrain");
        if (context is "adventure" or "cooldown-adventure" or "custom-adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == "spectator")
            await session.Mode(GameMode.Spectator, instantBuild: false);
        if (context == "secondary")
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        if (context is "cooldown" or "cooldown-adventure")
            await session.Cooldown();
        if (context is "custom" or "custom-adventure")
            await Stack(session, hand, Held(session, hand).With(DataComponents.Unbreakable, new UnbreakableComponent()));
        if (context == "empty")
            await Stack(session, hand, ItemStack.Empty);
        if (context == "other-hand-empty")
            await Stack(session, hand == Hand.Main ? Hand.Off : Hand.Main, ItemStack.Empty);
        if (context == "clone")
        {
            ItemStack original = Held(session, hand);
            var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
            builder.Add(original.Item.NetworkId, original.Item.Id, original.Item.Value with { });
            var modeled = new ItemStack(builder.Build()[original.Item.Id], original.Count);
            await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, Slot(hand), modeled); return true; }, session.Token);
            Assert.NotSame(original.Item.Value, Held(session, hand).Item.Value);
        }
        if (context == "other-target")
            await session.Block(Session.Target, "stone");
        if (context == "unload")
        {
            await session.Forget();
            await Wheat(session, age);
        }
        if (context == "respawn")
        {
            await session.Respawn();
            await Wheat(session, age);
            await Stack(session, Hand.Main, DefaultStack(session));
            await Stack(session, Hand.Off, DefaultStack(session));
        }
        Assert.Empty(await session.Barrier());
        if (context is "missing-terrain" or "unload" or "respawn")
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            Assert.Equal(Identifier.Minecraft("wheat"), session.Client.State.World.GetBlock(Session.Target).Block.Id);
        }
        BlockUseResult expected = context switch
        {
            "adventure" or "cooldown" or "cooldown-adventure" or "empty" => BlockUseResult.Pass,
            "spectator" => BlockUseResult.Success,
            "secondary" or "other-hand-empty" => age == 7 ? BlockUseResult.Pass : BlockUseResult.Success,
            _ => BlockUseResult.Unknown,
        };
        await Use(session, hand, expected);
    }

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public async Task OtherProfilesRetainTheUnclassifiedItemContinuation(int protocol, int age, Hand hand)
    {
        await using Session session = await Ready(protocol, age);
        await Use(session, hand, BlockUseResult.Unknown);
    }

    [Theory]
    [MemberData(nameof(AdmittedBoundaryCases))]
    public async Task AdmittedComponentBoundaryProfilesFollowTheOriginalWheatResult(int protocol, int age, Hand hand)
    {
        await using Session session = await Ready(protocol, age);
        await Use(session, hand, age == 7 ? BlockUseResult.Pass : BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(ComponentWheatCases))]
    public async Task ComponentWheatResultReachesTheFramedPublicCallerWithoutGrowthOrShrink(int protocol, int age, Hand hand, GameMode mode)
    {
        await using Session session = await Ready(protocol, age);
        await session.Mode(mode, instantBuild: mode == GameMode.Creative);
        BlockUseResult expected = age == 7 || protocol == 775 ? BlockUseResult.Pass : BlockUseResult.Success;
        await Use(session, hand, expected, age == 6 ? Direction.West : Direction.Up);
    }

    [Theory]
    [MemberData(nameof(ComponentContextCases))]
    public async Task ComponentNativePrechecksAndUnownedContextsRemainBounded(int protocol, Hand hand, string context)
    {
        await using Session session = await Ready(protocol, 0, terrain: context != "missing-terrain");
        if (context is "adventure" or "custom-adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == "spectator")
            await session.Mode(GameMode.Spectator, instantBuild: false);
        if (context == "cooldown")
            await session.Cooldown();
        if (context is "custom" or "custom-adventure")
            await Stack(session, hand, Held(session, hand).With(DataComponents.Unbreakable, new UnbreakableComponent()));
        if (context == "clone")
        {
            ItemStack original = Held(session, hand);
            var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
            builder.Add(original.Item.NetworkId, original.Item.Id, original.Item.Value with { });
            var modeled = new ItemStack(builder.Build()[original.Item.Id], original.Count);
            await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, Slot(hand), modeled); return true; }, session.Token);
            Assert.NotSame(original.Item.Value, Held(session, hand).Item.Value);
        }
        if (context == "unload")
        {
            await session.Forget();
            await Wheat(session, 0);
        }
        if (context == "respawn")
        {
            await session.Respawn();
            await Wheat(session, 0);
            await Stack(session, Hand.Main, DefaultStack(session));
            await Stack(session, Hand.Off, DefaultStack(session));
        }
        Assert.Empty(await session.Barrier());
        if (context is "missing-terrain" or "unload" or "respawn")
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            Assert.Equal(Identifier.Minecraft("wheat"), session.Client.State.World.GetBlock(Session.Target).Block.Id);
        }
        BlockUseResult expected = context switch
        {
            "adventure" or "cooldown" => BlockUseResult.Pass,
            "spectator" => protocol < 768 ? BlockUseResult.Success : BlockUseResult.Consume,
            _ => BlockUseResult.Unknown,
        };
        await Use(session, hand, expected);
    }

    [Fact]
    public async Task ComponentWheatUsesEachCachedProtocolRegistryAndTheNativeEightAgeSchema()
    {
        foreach (int protocol in Enumerable.Range(766, 12))
        {
            await using Session session = await Ready(protocol, 0);
            RegistryEntry<BlockDefinition> wheat = JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft("wheat")];
            Assert.Equal(8, wheat.Value.StateCount);
            BlockPropertyDefinition property = Assert.Single(wheat.Value.Properties);
            Assert.Equal("age", property.Name);
            Assert.Equal(new[] { "0", "1", "2", "3", "4", "5", "6", "7" }, property.Values);
            Assert.Same(wheat.Value, session.Client.State.World.GetBlock(Session.Target).Block.Value);
            for (int age = 0; age < 8; age++)
            {
                await Wheat(session, age);
                BlockState block = session.Client.State.World.GetBlock(Session.Target);
                Assert.Empty(JavaGameData.BlockShapes(protocol).GetCollisionShapes(block).ToArray());
            }
            ItemStack held = Held(session, Hand.Main);
            Assert.Same(JavaGameData.Registries(protocol).Items[Identifier.Minecraft("bone_meal")].Value, held.Item.Value);
            Assert.True(held.Components.HasNoPatch);
            Assert.Null(held.Components.Get(DataComponents.CanPlaceOn));
        }
    }

    private static async Task<Session> Ready(int protocol, int age, bool terrain = true)
    {
        Session session = await Session.Create(protocol, "wheat", terrain: terrain, observeWrites: true);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await Wheat(session, age);
            await Stack(session, Hand.Main, DefaultStack(session));
            await Stack(session, Hand.Off, DefaultStack(session));
            Assert.Empty(await session.Barrier());
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    private static ItemStack DefaultStack(Session session) => new(session.Client.State.Registries!.Items[Identifier.Minecraft("bone_meal")], 32);
    private static int Slot(Hand hand) => hand == Hand.Main ? 36 : 45;
    private static ItemStack Held(Session session, Hand hand) => session.Client.State.Inventory.GetSlot(Slot(hand));

    private static async Task Stack(Session session, Hand hand, ItemStack stack)
    {
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, Slot(hand), stack));
        Assert.Empty(await session.Barrier());
        Assert.Equal(stack.Count, Held(session, hand).Count);
    }

    private static async Task Wheat(Session session, int age)
    {
        BlockDefinition wheat = session.Client.State.Registries!.Blocks[Identifier.Minecraft("wheat")].Value;
        int state = Enumerable.Range(wheat.MinStateId, wheat.StateCount).Single(id => wheat.TryGetPropertyValue(id, "age", out string actual) && actual == age.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    private static async Task Use(Session session, Hand hand, BlockUseResult expected, Direction face = Direction.Up)
    {
        Assert.Empty(await session.Barrier());
        ItemStack before = Held(session, hand);
        Hand other = hand == Hand.Main ? Hand.Off : Hand.Main;
        ItemStack otherBefore = Held(session, other);
        int beforeState = session.Client.State.World.GetBlock(Session.Target).StateId;
        Vec3d hit = new(.25, .75, .5);
        BlockUsePrediction preview = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, face, hit, hand), session.Token);
        (int State, int Count, int OtherCount, bool Projected)? atWrite = null;
        session.BeforeClientWrite = () => atWrite ??= (session.Client.State.World.GetBlock(Session.Target).StateId, Held(session, hand).Count, Held(session, other).Count,
            session.Client.State.JukeboxPrediction.TryProject(session.Client.State.World, Session.Target, session.Client.State.World.GetBlock(Session.Target), out _));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        long interactionLifetime = session.Client.State.InteractionLifetime;
        long playerLifetime = session.Client.State.PlayerLifetime;
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, face, hit, hand, insideBlock: true, ct: caller.Token);
        int? sequence = null;
        bool acknowledged = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await session.Barrier();
            Assert.NotEmpty(packets);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            sequence = use.Sequence;
            session.BeforeClientWrite = null;
            Assert.Equal((Session.Target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal(((float)hit.X, (float)hit.Y, (float)hit.Z), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.True(use.Inside);
            Assert.False(use.WorldBorderHit);
            Assert.False(action.IsCompleted);
            bool success = expected == BlockUseResult.Success;
            BlockUseSwing expectedSwing = !success ? BlockUseSwing.None : session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet;
            bool swing = expectedSwing == BlockUseSwing.Packet;
            // These assertions run only after the real public caller has emitted and the fake peer has decoded/round-tripped its framed packet group.
            Assert.Equal(swing ? 2 : 1, packets.Count);
            if (swing)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
            Assert.True(preview.SendPacket);
            Assert.Equal(expected, preview.Result);
            Assert.Equal(expectedSwing, preview.Swing);
            Assert.NotNull(atWrite);
            Assert.Equal((beforeState, before.Count, otherBefore.Count, false), atWrite.Value);
            acknowledged = true;
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            await action.WaitAsync(session.Token);
            Assert.Empty(await session.Barrier());
            Assert.Equal(before, Held(session, hand));
            Assert.Equal(otherBefore, Held(session, other));
            Assert.Equal(beforeState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.False(session.Client.State.JukeboxPrediction.TryProject(session.Client.State.World, Session.Target, session.Client.State.World.GetBlock(Session.Target), out _));
        }
        finally
        {
            session.BeforeClientWrite = null;
            if (!action.IsCompleted && !acknowledged && sequence is int knownSequence
                && session.Client.State.InteractionLifetime == interactionLifetime && session.Client.State.PlayerLifetime == playerLifetime)
            {
                acknowledged = true;
                await session.Send(new ClientboundBlockChangedAckPacket(knownSequence));
                Assert.Empty(await session.Barrier());
            }
            if (!action.IsCompleted)
                await caller.CancelAsync();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
        }
    }
}
