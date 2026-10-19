using Umpk.Client.Internal;
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

public sealed class FlattenedAdventureMiningRuntimeTests
{
    public static IEnumerable<object[]> Selectors()
    {
        foreach (int protocol in JavaVersions.All.Where(row => row.Version.Protocol is >= 393 and < 766).Select(row => row.Version.Protocol))
            yield return [protocol, "stone", "minecraft:stone", true];
        foreach (int protocol in new[] { 393, 756, 757, 765 })
        {
            foreach (string selector in new[] { "stone", ":stone", "stone trailing", "stone [bad=bad]", "stone {foo:1b}", "stone[]trailing", "stone!", "stoneUPPER" })
                yield return [protocol, "stone", selector, true];
            foreach (string selector in new[] { "Stone", "1", "x:stone", " stone", "minecraft:STONE", "stone[bad=bad]", "stone[", "dirt", "minecraft:stone:extra" })
                yield return [protocol, "stone", selector, false];
            foreach (string selector in new[] { "oak_log[axis=y]", "oak_log[ axis = y ]", "oak_log[\"axis\"=\"y\"]", "oak_log[axis=y,]", "oak_log[axis=y]ignored" })
                yield return [protocol, "oak_log", selector, true];
            foreach (string selector in new[] { "oak_log[axis=x]", "oak_log[axis=q]", "oak_log[bad=y]", "oak_log[axis=y,axis=y]", "oak_log[axis=y", "oak_log[axis=y, ]" })
                yield return [protocol, "oak_log", selector, false];
        }
        yield return [404, "oak_log", "oak_log['axis'='y']", false];
        yield return [477, "oak_log", "oak_log['axis'='y']", true];
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public async Task WireSelectorsUseNativePrefixAndPropertyGrammar(int protocol, string block, string selector, bool allowed)
    {
        await using var fixture = new Fixture(protocol, block);
        await AdventureAsync(fixture);
        await HoldAsync(fixture, selector);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Equal(allowed ? new[] { 0 } : [], fixture.ActionsSent.Select(packet => packet.Action));
            Assert.False(mining.IsCompleted);
            Assert.Equal(allowed ? 2 : 1, Swings(fixture));
            if (allowed && block == "stone")
            {
                for (int tick = 1; tick < 6; tick++)
                    await fixture.TickAsync();
                Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
                await mining.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(393)]
    [InlineData(765)]
    public async Task MissingCanDestroyIsKnownDenialAndRetainsTheHeldRequest(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await AdventureAsync(fixture);
        await fixture.HoldAsync("diamond_pickaxe", 0);
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
    [InlineData(393, false)]
    [InlineData(393, true)]
    [InlineData(756, false)]
    [InlineData(756, true)]
    [InlineData(757, false)]
    [InlineData(757, true)]
    [InlineData(765, false)]
    [InlineData(765, true)]
    public async Task TagReloadRetainsTheSameStackCacheButAnEqualWireReplacementRecomputes(int protocol, bool initiallyAllowed)
    {
        await using var fixture = new Fixture(protocol);
        await AdventureAsync(fixture);
        await TagsAsync(fixture, initiallyAllowed);
        await HoldAsync(fixture, "#minecraft:permitted");
        await AssertStartAsync(fixture, initiallyAllowed);
        ItemStack first = fixture.State.Inventory.PlayerSlots[36];
        await TagsAsync(fixture, !initiallyAllowed);
        await AssertStartAsync(fixture, initiallyAllowed);
        await fixture.SetSlotAsync(36, first);
        Assert.NotSame(first, fixture.State.Inventory.PlayerSlots[36]);
        Assert.Equal(first.Components, fixture.State.Inventory.PlayerSlots[36].Components);
        await AssertStartAsync(fixture, !initiallyAllowed);
    }

    [Theory]
    [InlineData(393, 0)]
    [InlineData(393, 1)]
    [InlineData(393, 2)]
    [InlineData(765, 0)]
    [InlineData(765, 1)]
    [InlineData(765, 2)]
    public async Task CacheIsClearedWhenItsControllerPlayerOrWorldEnds(int protocol, int boundary)
    {
        await using var fixture = new Fixture(protocol);
        await AdventureAsync(fixture);
        await TagsAsync(fixture, false);
        await HoldAsync(fixture, "#minecraft:permitted");
        await AssertStartAsync(fixture, false);
        await TagsAsync(fixture, true);
        if (boundary == 0)
            fixture.State.AdvanceInteractionLifetime();
        else if (boundary == 1)
            fixture.State.AdvancePlayerLifetime();
        else
        {
            var old = fixture.State.World;
            var world = new Umpk.Game.World.World(old.Dimension, old.BlockData, WorldFactory.EmptyBiomes());
            world.SetBlockStateId(Fixture.Target, old.GetBlock(Fixture.Target).StateId);
            fixture.State.InstallWorld(world);
        }
        await AssertStartAsync(fixture, true);
    }

    [Theory]
    [InlineData(393, "#minecraft:permitted[axis=y]", true)]
    [InlineData(765, "#minecraft:permitted[axis=y]", true)]
    [InlineData(765, "#minecraft:permitted[axis=x]", false)]
    [InlineData(765, "#minecraft:permitted[unknown=y]", false)]
    [InlineData(765, "#minecraft:permitted[axis=y,axis=y]", false)]
    [InlineData(765, "#minecraft:missing", false)]
    public async Task SynchronizedTagsAndVaguePropertiesConstrainTheTarget(int protocol, string selector, bool allowed)
    {
        await using var fixture = new Fixture(protocol, "oak_log");
        await AdventureAsync(fixture);
        await TagsAsync(fixture, true);
        await HoldAsync(fixture, selector);
        await AssertStartAsync(fixture, allowed);
    }

    [Theory]
    [InlineData(393, "stone{}")]
    [InlineData(765, "stone{foo:1b}")]
    [InlineData(765, "#minecraft:unknown")]
    public async Task MissingTagsAndBlockEntityPredicateFactsFailExplicitly(int protocol, string selector)
    {
        await using var fixture = new Fixture(protocol);
        await AdventureAsync(fixture);
        await HoldAsync(fixture, selector);
        if (selector.StartsWith('#'))
            fixture.State.RegistryTags.Clear();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(fixture.ActionsSent);
    }

    [Theory]
    [InlineData(393, false)]
    [InlineData(756, false)]
    [InlineData(757, true)]
    [InlineData(765, true)]
    public async Task BlockEntityCacheBoundaryDoesNotInferAbsenceFromMissingPacketNbt(int protocol, bool supported)
    {
        await using var fixture = new Fixture(protocol, "chest");
        await AdventureAsync(fixture);
        await HoldAsync(fixture, "chest");
        Assert.Null(fixture.State.World.GetBlockEntityNbt(Fixture.Target));
        if (supported)
            await AssertStartAsync(fixture, true);
        else
        {
            Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
            await fixture.TickAsync();
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(fixture.ActionsSent);
        }
    }

    [Theory]
    [InlineData(393)]
    [InlineData(765)]
    public async Task InvalidAndUnknownTagAlternativesDoNotOverrideALaterKnownGrant(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await AdventureAsync(fixture);
        await HoldAsync(fixture, "stone[invalid=yes]", "#minecraft:unknown", "stone");
        fixture.State.RegistryTags.Clear();
        await AssertStartAsync(fixture, true);
    }

    [Theory]
    [InlineData(757, "wheat[age=+01]")]
    [InlineData(757, "wheat[age=01]")]
    [InlineData(757, "wheat[age=\"١\"]")]
    [InlineData(765, "wheat[age=+01]")]
    [InlineData(765, "wheat[age=01]")]
    [InlineData(765, "wheat[age=\"١\"]")]
    public async Task NoncanonicalIntegerSpellingRemainsUnknownUntilPropertyKindIsOwned(int protocol, string selector)
    {
        await using var fixture = new Fixture(protocol, "wheat");
        Assert.True(fixture.State.Registries!.Blocks.TryGetValue(Identifier.Minecraft("wheat"), out BlockDefinition? wheat));
        int state = Enumerable.Range(wheat.MinStateId, wheat.StateCount).Single(id => wheat.TryGetPropertyValue(id, "age", out string age) && age == "1");
        fixture.State.World.SetBlockStateId(Fixture.Target, state);
        await AdventureAsync(fixture);
        await HoldAsync(fixture, selector);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("The Adventure permission requires unavailable tag, property-kind or block-entity facts.", error.Message);
            Assert.Empty(fixture.ActionsSent);
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    private static Task AdventureAsync(Fixture fixture)
        => fixture.ApplyAsync("game_event", new ClientboundGameEventPacket(3, (float)GameMode.Adventure));

    private static Task HoldAsync(Fixture fixture, params string[] selectors)
    {
        Assert.True(fixture.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_pickaxe"), out RegistryEntry<ItemDefinition> item));
        var list = new NbtList(NbtTagType.String);
        foreach (string selector in selectors)
            list.Add(new NbtString(selector));
        var tag = new NbtCompound();
        tag.Put("CanDestroy", list);
        return fixture.SetSlotAsync(36, new ItemStack(item, 1).With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));
    }

    private static Task TagsAsync(Fixture fixture, bool member)
    {
        int id = fixture.State.World.GetBlock(Fixture.Target).Block.NetworkId;
        return fixture.ApplyAsync("update_tags", new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block,
            [new TagEntry(Identifier.Minecraft("permitted"), member ? [id] : []), new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [id])])]));
    }

    private static async Task AssertStartAsync(Fixture fixture, bool allowed)
    {
        fixture.Sent.Clear();
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Equal(allowed ? new[] { 0 } : [], fixture.ActionsSent.Select(packet => packet.Action));
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
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
