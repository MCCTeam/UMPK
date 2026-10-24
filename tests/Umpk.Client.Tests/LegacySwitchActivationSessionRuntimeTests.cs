using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class LegacySwitchActivationSessionRuntimeTests
{
    public static IEnumerable<object[]> FamilyCases()
    {
        foreach (int protocol in JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol <= 490))
            foreach (string name in Names(protocol))
                foreach (Hand hand in protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off })
                    yield return [protocol, name, hand];
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task NativeOldLeverAndButtonPoweredStatesSendUseThenSwing(int protocol, string name, Hand hand)
    {
        await using Session session = await Session.Create(protocol, name);
        foreach (bool powered in new[] { false, true })
        {
            await State(session, protocol, name, powered);
            await AssertUse(session, protocol, hand, BlockUseResult.Success);
        }
    }

    public static IEnumerable<object[]> ModernCases() => new[] { 498, 573, 766, 777 }
        .SelectMany(protocol => new[] { "lever", "stone_button" }.SelectMany(name => new[] { false, true }
            .SelectMany(powered => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, name, powered, hand }))));

    [Theory]
    [MemberData(nameof(ModernCases))]
    public async Task ModernPoweredConsumeAndOffhandFallbackStayDistinct(int protocol, string name, bool powered, Hand hand)
    {
        await using Session session = await Session.Create(protocol, name);
        await State(session, protocol, name, powered);
        BlockUseResult expected = protocol >= 766 && hand == Hand.Off ? BlockUseResult.Pass
            : name == "stone_button" && powered && protocol >= 573 ? BlockUseResult.Consume : BlockUseResult.Success;
        await AssertUse(session, protocol, hand, expected);
    }

    public static IEnumerable<object[]> ContextCases()
    {
        foreach (int protocol in new[] { 47, 107, 393, 490 })
            foreach (string name in new[] { "lever", "stone_button" })
                foreach (int context in Enumerable.Range(0, protocol == 47 ? 4 : 5))
                    yield return [protocol, name, context];
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task OldActivationKeepsNativeSecondaryUseAdventureAndCooldownOrder(int protocol, string name, int context)
    {
        await using Session session = await Session.Create(protocol, name);
        if (context != 0)
            await session.Hold("stick");
        if (context is 0 or 2)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (context == 3)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == 4)
            await session.Cooldown();
        await AssertUse(session, protocol, Hand.Main, context == 2 ? BlockUseResult.Pass : BlockUseResult.Success);
    }

    public static IEnumerable<object[]> TerrainCases() => new[] { 47, 107, 393, 490, 498, 573, 766, 777 }
        .SelectMany(protocol => new[] { "lever", "stone_button" }.Select(name => new object[] { protocol, name }));

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public async Task SynthesizedOrUnloadedSwitchStateDoesNotInventActivation(int protocol, string name)
    {
        await using Session session = await Session.Create(protocol, name, terrain: false);
        Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        await AssertUse(session, protocol, Hand.Main, BlockUseResult.Unknown);
        await session.Terrain();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        await AssertUse(session, protocol, Hand.Main, BlockUseResult.Success);
        await session.Forget();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        await AssertUse(session, protocol, Hand.Main, BlockUseResult.Unknown);
        await session.Terrain();
        await session.Block(Session.Target, name);
        Assert.Empty(await session.Barrier());
        await AssertUse(session, protocol, Hand.Main, BlockUseResult.Success);
    }

    [Theory]
    [InlineData("lever")]
    [InlineData("stone_button")]
    [InlineData("wooden_button")]
    public async Task Protocol47OffhandCannotFallBackToTheMainHand(string name)
    {
        await using Session session = await Session.Create(47, name);
        await session.Hold("stick");
        await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Use(Hand.Off));
        Assert.Empty(await session.Barrier());
        Assert.Equal("stick", session.Client.State.Inventory.GetSlot(36).Item.Id.Path);
    }

    private static string[] Names(int protocol) => protocol < 393
        ? ["lever", "stone_button", "wooden_button"]
        : ["lever", "stone_button", "oak_button", "spruce_button", "birch_button", "jungle_button", "acacia_button", "dark_oak_button"];

    private static async Task State(Session session, int protocol, string name, bool powered)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        int state = protocol < 393 ? block.DefaultStateId | (powered ? 9 : 1)
            : Enumerable.Range(block.MinStateId, block.StateCount).First(id => block.TryGetPropertyValue(id, "powered", out string value) && value == (powered ? "true" : "false"));
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        if (protocol < 393)
            Assert.Equal(powered ? 9 : 1, session.Client.State.World.GetBlock(Session.Target).LegacyMeta);
        else
        {
            Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("powered", out string value));
            Assert.Equal(powered ? "true" : "false", value);
        }
    }

    private static async Task AssertUse(Session session, int protocol, Hand hand, BlockUseResult expected)
    {
        ItemStack main = session.Client.State.Inventory.GetSlot(36), off = session.Client.State.Inventory.GetSlot(45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
        List<IPacket> sent = await session.Use(hand);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, sent.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(sent[0]);
            Assert.Equal((Session.Target, (int)Direction.Up), (use.Position, use.Face));
            Assert.Equal(main.Item.Id, use.HeldItem.Item.Id);
            Assert.Equal(main.Count, use.HeldItem.Count);
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
        }
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
        Assert.DoesNotContain(sent, packet => packet is ServerboundPunchPacket);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        Assert.Equal(swing, prediction.Swing);
        Assert.Empty(await session.Barrier());
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }
}
