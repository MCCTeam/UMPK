using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.MiningItemIdentityRuntimeTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class LegacyAdventureMiningRuntimeTests
{
    public static IEnumerable<object[]> Selectors()
    {
        foreach (int protocol in JavaVersions.All.Where(row => row.Version.Protocol < 393).Select(row => row.Version.Protocol))
            foreach (string name in new[] { "stone", "minecraft:stone", "1" })
                yield return [protocol, name, true];
        foreach (int protocol in new[] { 47, 340 })
        {
            foreach (string name in new[] { "+1", "0001", "١", "MINECRAFT:stone", ":stone", "x:stone" })
                yield return [protocol, name, true];
            foreach (string name in new[] { "dirt", "2", "1 ", " 1", "#minecraft:stone", "stone[variant=stone]", "stone{}", "", "2147483648", "minecraft:1" })
                yield return [protocol, name, false];
        }
        yield return [47, "Stone", false];
        yield return [210, "Stone", false];
        yield return [315, "Stone", true];
        yield return [340, "Stone", true];
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public async Task WireCanDestroyUsesTheNativeLegacyNameAndNumericRules(int protocol, string selector, bool allowed)
    {
        await using var fixture = new Fixture(protocol);
        await SetModeAsync(fixture, GameMode.Adventure);
        await HoldAsync(fixture, selector);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            if (allowed)
            {
                for (int tick = 1; tick < 6; tick++)
                    await fixture.TickAsync();
                Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
                await mining.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(7, Swings(fixture));
            }
            else
            {
                await fixture.TickAsync();
                Assert.Empty(fixture.ActionsSent);
                Assert.False(mining.IsCompleted);
                Assert.Equal(1, Swings(fixture));
            }
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(47, 0)]
    [InlineData(47, 1)]
    [InlineData(47, 2)]
    [InlineData(340, 0)]
    [InlineData(340, 1)]
    [InlineData(340, 2)]
    public async Task MissingEmptyAndWrongTypeCanDestroyListsDoNotStart(int protocol, int form)
    {
        await using var fixture = new Fixture(protocol);
        await SetModeAsync(fixture, GameMode.Adventure);
        Assert.True(fixture.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_pickaxe"), out RegistryEntry<ItemDefinition> item));
        ItemStack stack = new(item, 1);
        var tag = new NbtCompound();
        if (form == 1)
            tag.Put("CanDestroy", new NbtList(NbtTagType.String));
        if (form == 2)
            tag.Put("CanDestroy", new NbtList(NbtTagType.Int) { new NbtInt(1) });
        if (form != 0)
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        await fixture.SetSlotAsync(36, stack);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Empty(fixture.ActionsSent);
            Assert.False(mining.IsCompleted);
            Assert.Equal(1, Swings(fixture));
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    public async Task APermissionArrivingWhileHeldStartsWithoutInventingAnotherClick(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await SetModeAsync(fixture, GameMode.Adventure);
        await HoldAsync(fixture, "dirt");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.TickAsync();
            Assert.Empty(fixture.ActionsSent);
            Assert.Equal(1, Swings(fixture));
            await HoldAsync(fixture, "stone");
            await fixture.TickAsync();
            Assert.Equal(new[] { 0 }, fixture.ActionsSent.Select(packet => packet.Action));
            Assert.Equal(2, Swings(fixture));
            for (int tick = 0; tick < 6; tick++)
                await fixture.TickAsync();
            Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(8, Swings(fixture));
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    public async Task DeniedRestartRetainsTheOldTargetAndProgressUntilItsOriginalItemReturns(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await SetModeAsync(fixture, GameMode.Adventure);
        await HoldAsync(fixture, "stone");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await HoldAsync(fixture, "dirt");
            await fixture.TickAsync();
            await fixture.TickAsync();
            Assert.Equal(new[] { 0 }, fixture.ActionsSent.Select(packet => packet.Action));
            Assert.Equal(2, Swings(fixture));
            await HoldAsync(fixture, "stone");
            for (int tick = 0; tick < 5; tick++)
                await fixture.TickAsync();
            Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, Swings(fixture));
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(340)]
    public async Task SwitchingToAdventureDoesNotRecheckAnUnchangedContinuationTarget(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await fixture.HoldAsync("diamond_pickaxe", 0);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await SetModeAsync(fixture, GameMode.Adventure);
            for (int tick = 1; tick < 6; tick++)
                await fixture.TickAsync();
            Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(393)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task UnsupportedAdventurePredicateErasRemainExplicitFailures(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await SetModeAsync(fixture, GameMode.Adventure);
        await fixture.HoldAsync("diamond_pickaxe", 0);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(fixture.Sent);
    }

    private static Task SetModeAsync(Fixture fixture, GameMode mode)
        => fixture.ApplyAsync("game_event", new ClientboundGameEventPacket(3, (float)mode));

    private static Task HoldAsync(Fixture fixture, string selector)
    {
        Assert.True(fixture.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_pickaxe"), out RegistryEntry<ItemDefinition> item));
        var tag = new NbtCompound();
        tag.Put("CanDestroy", new NbtList(NbtTagType.String) { new NbtString(selector) });
        return fixture.SetSlotAsync(36, new ItemStack(item, 1).With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));
    }

    private static int Swings(Fixture fixture) => fixture.Sent.Count(packet => packet is ServerboundSwingPacket);

    private static async Task ReleaseAsync(CancellationTokenSource caller, Task mining)
    {
        caller.Cancel();
        if (mining.IsCompleted)
            return;
        try
        {
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
