using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class LecternActivationSessionRuntimeTests
{
    public static IEnumerable<object[]> FilledCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct()
        .Where(protocol => protocol >= 477).SelectMany(protocol => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(FilledCases))]
    public async Task EveryFilledLecternStateUsesNativeHandAndSwingRulesWithoutBlockEntityData(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "lectern");
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("lectern")].Value;
        int[] states = Enumerable.Range(block.MinStateId, block.StateCount)
            .Where(state => block.TryGetPropertyValue(state, "has_book", out string hasBook) && hasBook == "true").ToArray();
        Assert.Equal(8, states.Length);
        foreach (int state in states)
        {
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("has_book", out string observedBook));
            Assert.Equal("true", observedBook);
            Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
            await AssertUse(session, protocol, hand, hand == Hand.Off && protocol >= 766 ? BlockUseResult.Pass : BlockUseResult.Success);
        }
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }

    public static IEnumerable<object[]> ContextCases() => new[] { 477, 498, 573, 735, 765, 766, 768, 777 }
        .SelectMany(protocol => Enumerable.Range(0, 10).Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task FilledLecternRetainsSecondaryUseCooldownAdventureTerrainAndEmptyUnknownRules(int protocol, int context)
    {
        await using Session session = await Session.Create(protocol, "lectern", terrain: context != 6);
        if (context is 1 or 2 or 3 or 4)
            await session.Hold("stick");
        if (context == 5)
            await session.Offhand("stick");
        if (context == 9)
            await session.Offhand("written_book");
        if (context is 0 or 4 or 5)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (context == 2)
            await session.Cooldown();
        if (context == 3)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == 7)
            await session.Forget();
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("lectern")].Value;
        string requiredBook = context is 8 or 9 ? "false" : "true";
        int state = Enumerable.Range(block.MinStateId, block.StateCount)
            .First(id => block.TryGetPropertyValue(id, "has_book", out string hasBook) && hasBook == requiredBook);
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("has_book", out string observedBook));
        Assert.Equal(requiredBook, observedBook);
        if (context is 6 or 7)
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        BlockUseResult expected = context is 6 or 7 or 8 or 9 ? BlockUseResult.Unknown
            : context is 4 or 5 ? BlockUseResult.Pass
            : BlockUseResult.Success;
        await AssertUse(session, protocol, context == 9 ? Hand.Off : Hand.Main, expected);
    }

    private static async Task AssertUse(Session session, int protocol, Hand hand, BlockUseResult expected)
    {
        ItemStack main = session.Client.State.Inventory.GetSlot(36);
        ItemStack off = session.Client.State.Inventory.GetSlot(45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
        List<IPacket> packets = await session.Use(hand);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        Assert.Equal(swing, prediction.Swing);
        Assert.Empty(await session.Barrier());
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
    }
}
