using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class ComponentCanBreak770ActivationSessionTests
{
    private const int Protocol = 770;
    private const string UnavailableGate = "This game mode requires an allowed block interaction before mining.";
    private const string UnavailableContext = "The Adventure permission requires unavailable tag, property-kind or block-entity facts.";

    [Fact]
    public async Task Matching770BlockIgnoresNonmatchingComponentMatchers()
    {
        await using Session session = await Adventure();
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId;
        var damageBounds = new NbtCompound
        {
            ["damage"] = new NbtCompound { ["min"] = new NbtInt(99), ["max"] = new NbtInt(100) },
        };
        var predicate = new BlockPredicateEntry(new HolderSetRef(null, [stone]), null, null)
        {
            ComponentMatchers = new(
                [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(999))],
                [new(Identifier.Minecraft("damage"), false, damageBounds)]),
        };
        var permission = new AdventureModePredicateComponent([predicate]);
        ItemStack stack = new(session.Client.State.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 7, 36, stack.With(DataComponents.CanBreak, permission)));
        Assert.Empty(await session.Barrier());
        ItemStack applied = session.Client.State.Inventory.GetSlot(36);
        Assert.Equal("minecraft:diamond_pickaxe", applied.Item.Id.ToString());
        Assert.Equal(1, applied.Count);
        AdventureModePredicateComponent actual = applied.Components.Get(DataComponents.CanBreak)!;
        Assert.NotSame(permission, actual);
        BlockPredicateEntry observed = Assert.Single(actual.Predicates);
        Assert.Equal(new[] { stone }, observed.Blocks!.Ids);
        Assert.Null(observed.Blocks.Tag);
        Assert.Null(observed.Properties);
        Assert.Null(observed.Nbt);
        BlockPredicateComponentMatchers matchers = Assert.IsType<BlockPredicateComponentMatchers>(observed.ComponentMatchers);
        DataComponentEntry exact = Assert.Single(matchers.Exact);
        Assert.Equal(DataComponents.Damage.Id, exact.Type.Id);
        Assert.False(exact.IsRemoval);
        Assert.Equal(999, Assert.IsType<DamageComponent>(exact.Value).Value);
        BlockPredicatePartialMatcher partial = Assert.Single(matchers.Partial);
        Assert.Equal(Identifier.Minecraft("damage"), partial.Type);
        Assert.False(partial.IsAnyValue);
        NbtCompound decodedDamage = Assert.IsType<NbtCompound>(partial.Value);
        Assert.NotSame(damageBounds, decodedDamage);
        Assert.Equal(damageBounds, decodedDamage);
        NbtCompound bounds = Assert.IsType<NbtCompound>(decodedDamage["damage"]);
        Assert.Equal(99, Assert.IsType<NbtInt>(bounds["min"]).Value);
        Assert.Equal(100, Assert.IsType<NbtInt>(bounds["max"]).Value);
        Assert.Equal("minecraft:stone", session.Client.State.World.GetBlock(Session.Target).Block.Id.ToString());
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> initial = await session.Tick();
            await RequirePending(action, session.Token);
            Assert.Collection(initial, packet => AssertAction(packet, 0), AssertSwing, AssertSwing);
            await FinishAfter(session, action, 5);
            Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        }
        finally
        {
            await Release(session, caller, action);
        }
    }

    [Fact]
    public async Task MatchingFramed770PermissionCompletesOnlyAfterStopAcknowledgment()
    {
        await using Session session = await Adventure();
        await session.HoldStonePermission();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> initial = await session.Tick();
            await RequirePending(action, session.Token);
            Assert.Collection(initial, packet => AssertAction(packet, 0), AssertSwing, AssertSwing);
            await FinishAfter(session, action, 5);
            Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        }
        finally
        {
            await Release(session, caller, action);
        }
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("nonmatch")]
    public async Task Denied770RequestStartsOnlyAfterFreshMatchingWireComponent(string kind)
    {
        await using Session session = await Adventure();
        await session.HoldPermission(kind);
        AdventureModePredicateComponent? before = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> denied = await session.Tick();
            await RequirePending(action, session.Token);
            Assert.Collection(denied, AssertSwing);
            await session.HoldStonePermission();
            Assert.NotSame(before, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            List<IPacket> allowed = await session.Tick();
            await RequirePending(action, session.Token);
            Assert.Collection(allowed, packet => AssertAction(packet, 0), AssertSwing);
            await FinishAfter(session, action, 6);
        }
        finally
        {
            await Release(session, caller, action);
        }
    }

    [Fact]
    public async Task Denied770ReplacementChecksPermissionBeforeAbortOrStart()
    {
        await using Session session = await Adventure();
        await session.HoldStonePermission();
        AdventureModePredicateComponent original = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak)!;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> initial = await session.Tick();
            await RequirePending(action, session.Token);
            Assert.Collection(initial, packet => AssertAction(packet, 0), AssertSwing, AssertSwing);
            Assert.Collection(await session.Tick(), AssertSwing);
            await session.HoldPermission("nonmatch");
            Assert.Empty(await session.Tick());
            await RequirePending(action, session.Token);
            await session.HoldStonePermission();
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            Assert.Collection(await session.Tick(), AssertSwing);
            await RequirePending(action, session.Token);
            await FinishAfter(session, action, 3);
        }
        finally
        {
            await Release(session, caller, action);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TagReloadRetains770ComponentCacheAndFreshWirePredicateRecomputes(bool initiallyAllowed)
    {
        await using Session session = await Adventure();
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        await session.Tags(initiallyAllowed);
        await session.HoldPermission("tag");
        AdventureModePredicateComponent original = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak)!;
        await PermissionProbe(session, initiallyAllowed);
        await session.Tags(!initiallyAllowed);
        Assert.Same(original, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
        await PermissionProbe(session, initiallyAllowed);
        await session.HoldPermission("tag");
        Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
        await PermissionProbe(session, !initiallyAllowed, sameRetainedTarget: initiallyAllowed);
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("nbt")]
    public async Task Unowned770ContextRemainsUnavailableAtThePermissionOwner(string kind)
    {
        await using Session session = await Adventure();
        await session.Block(Session.Target, kind == "range" ? "oak_log" : "chest");
        await session.HoldPermission(kind);
        AdventureModePredicateComponent component = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak)!;
        BlockPredicateEntry predicate = Assert.Single(component.Predicates);
        if (kind == "range")
        {
            StatePropertyMatcher property = Assert.Single(predicate.Properties!);
            Assert.Equal("axis", property.Name);
            Assert.False(property.Exact);
            Assert.Equal("x", property.MinValue);
            Assert.Equal("z", property.MaxValue);
        }
        else
            Assert.Equal("unavailable full native save", predicate.Nbt!.GetString("CustomName"));
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Empty(await session.Tick());
        InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
        Assert.Equal("Umpk.Client.Actions.InteractionActions+MiningUnavailableException", error.GetType().FullName);
        Assert.Equal(UnavailableContext, error.Message);
        Assert.Empty(await session.Tick());
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    [Fact]
    public async Task Protocol771RemainsOutsideThePermissionSlice()
    {
        await using Session session = await Session.Create(771);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Hold("diamond_pickaxe");
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Empty(await session.Tick());
        InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
        Assert.Equal(UnavailableGate, error.Message);
    }

    private static async Task<Session> Adventure()
    {
        Session session = await Session.Create(Protocol);
        try
        {
            await session.Mode(GameMode.Adventure, instantBuild: false);
            await session.Info(GameMode.Adventure);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task PermissionProbe(Session session, bool allowed, bool sameRetainedTarget = false)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            await RequirePending(action, session.Token);
            if (allowed)
                Assert.Collection(sent, packet => AssertAction(packet, 0), AssertSwing, AssertSwing);
            else if (sameRetainedTarget)
                Assert.Collection(sent, AssertSwing, AssertSwing);
            else
                Assert.Collection(sent, AssertSwing);
        }
        finally
        {
            await Release(session, caller, action);
        }
    }

    private static async Task FinishAfter(Session session, Task action, int ticks)
    {
        for (int tick = 1; tick <= ticks; tick++)
        {
            List<IPacket> sent = await session.Tick();
            await RequirePending(action, session.Token);
            if (tick < ticks)
                Assert.Collection(sent, AssertSwing);
            else
            {
                Assert.Collection(sent, packet => AssertAction(packet, 2), AssertSwing);
                await session.Complete(action, Assert.IsType<ServerboundPlayerActionPacket>(sent[0]));
                Assert.True(action.IsCompletedSuccessfully);
            }
        }
    }

    private static async Task RequirePending(Task action, CancellationToken ct)
    {
        if (action.IsFaulted)
            await action.WaitAsync(ct);
        Assert.False(action.IsCompleted);
    }

    private static async Task Release(Session session, CancellationTokenSource caller, Task action)
    {
        await caller.CancelAsync();
        await session.Tick();
        if (!action.IsFaulted && !action.IsCompletedSuccessfully)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
    }

    private static void AssertAction(IPacket packet, int expected)
        => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), Protocol, expected);

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }
}
