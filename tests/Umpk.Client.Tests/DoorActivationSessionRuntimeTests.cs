using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
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
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Umpk.Client.Tests.Support.BlockUseSession;

public sealed class DoorActivationSessionRuntimeTests
{
    private static IEnumerable<int> Protocols => JavaVersions.All.Select(version => version.Version.Protocol).Distinct();
    private static readonly string[] DoorNames = ["wooden_door", "oak_door", "iron_door", "spruce_door", "birch_door", "jungle_door", "acacia_door", "dark_oak_door", "crimson_door", "warped_door", "mangrove_door", "bamboo_door", "cherry_door", "pale_oak_door", "poplar_door", "copper_door", "exposed_copper_door", "weathered_copper_door", "oxidized_copper_door", "waxed_copper_door", "waxed_exposed_copper_door", "waxed_weathered_copper_door", "waxed_oxidized_copper_door"];
    private static string Wood(int protocol) => protocol < 393 ? "wooden_door" : "oak_door";
    public static IEnumerable<object[]> EraHands() => Protocols.SelectMany(protocol => (protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off }).Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(EraHands))]
    public async Task EveryRegisteredDoorUsesTheNativeActivationAndHandRule(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol));
        int checkedDoors = 0;
        foreach (string name in DoorNames.Where(name => session.Client.State.Registries!.Blocks.TryGet(Identifier.Minecraft(name), out _)))
        {
            checkedDoors++;
            foreach (bool upper in new[] { false, true })
                foreach (bool open in new[] { false, true })
                {
                    await session.Block(Session.Target.Below(), name);
                    await session.Block(Session.Target, name, upper, open);
                    Assert.Empty(await session.Barrier());
                    int clicked = session.Client.State.World.GetBlock(Session.Target).StateId;
                    int lower = session.Client.State.World.GetBlock(Session.Target.Below()).StateId;
                    bool handles = (name != "iron_door" || protocol <= 210) && (hand == Hand.Main || protocol < 766);
                    List<IPacket> sent = await session.Use(hand);
                    AssertUse(sent, protocol, hand, handles && protocol != 777, ItemStack.Empty);
                    Assert.Equal(clicked, session.Client.State.World.GetBlock(Session.Target).StateId);
                    Assert.Equal(lower, session.Client.State.World.GetBlock(Session.Target.Below()).StateId);
                    Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
                }
        }
        Assert.Equal(session.Client.State.Registries!.Blocks.Count(block => block.Id.Path.EndsWith("_door", StringComparison.Ordinal)), checkedDoors);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
    }

    public static IEnumerable<object[]> ContextCases()
    {
        foreach (int protocol in new[] { 47, 107, 210, 315, 340, 393, 498, 573, 761, 765, 766, 768, 776, 777 })
            foreach (int scenario in Enumerable.Range(0, 10))
                if (protocol != 47 || scenario is not (3 or 5 or 6 or 9))
                    yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task NativeDoorActivationPrecedesItemPermissionsAndKeepsSecondaryUseDistinct(int protocol, int scenario)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol));
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
            await session.Block(Session.Target, Wood(protocol));
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

    public static IEnumerable<object[]> LegacyLowerCases() => Protocols.Where(protocol => protocol < 393).SelectMany(protocol => Enumerable.Range(0, 6).Select(scenario => new object[] { protocol, scenario }));

    [Theory]
    [MemberData(nameof(LegacyLowerCases))]
    public async Task LegacyUpperDoorsRequireTheOriginalMatchingLowerBlockOnly(int protocol, int scenario)
    {
        await using Session session = await Session.Create(protocol, Wood(protocol));
        await session.Block(Session.Target, Wood(protocol), upper: true, metadata: scenario == 5 ? 15 : 8);
        await session.Block(Session.Target.Below(), scenario switch { 0 => "air", 1 => "stone", 2 => "spruce_door", _ => Wood(protocol) }, upper: scenario == 4);
        Assert.Empty(await session.Barrier());
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, scenario >= 3, ItemStack.Empty);
    }

    public static IEnumerable<object[]> AvailabilityCases() => new[] { 47, 315, 340, 393, 498, 765, 766, 777 }.SelectMany(protocol => new[] { Wood(protocol), "iron_door" }.Select(name => new object[] { protocol, name }));

    [Theory]
    [MemberData(nameof(AvailabilityCases))]
    public async Task NativeTerrainArrivalUnloadAndReloadAreDistinctFromCacheUpdates(int protocol, string name)
    {
        await using Session session = await Session.Create(protocol, name, terrain: false);
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, false, ItemStack.Empty);
        bool swing = (name != "iron_door" || protocol <= 210) && protocol != 777;
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

    [Theory]
    [InlineData(765)]
    [InlineData(766)]
    public async Task DisabledCopperTargetFailsAfterSendingWithoutBorrowingAxeSwing(int protocol)
    {
        await using Session session = await Session.Create(protocol, "waxed_weathered_copper_door", featureFlags: false);
        await session.Hold("diamond_axe");
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, false, session.Client.State.Inventory.GetSlot(36));
    }

    [Theory]
    [InlineData(315)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task NativeDoorPassContinuesIntoOrdinaryBlockItemUse(int protocol)
    {
        await using Session session = await Session.Create(protocol, "iron_door", physics: true);
        await session.Hold("stone");
        AssertUse(await session.Use(Hand.Main), protocol, Hand.Main, protocol != 777, session.Client.State.Inventory.GetSlot(36));
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Fact]
    public async Task LegacyOffhandRejectsBeforeMainHandFallback()
    {
        await using Session session = await Session.Create(47, "wooden_door");
        await session.Hold("stone");
        await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Use(Hand.Off));
        Assert.Empty(await session.Barrier());
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
        }
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
    }

}
