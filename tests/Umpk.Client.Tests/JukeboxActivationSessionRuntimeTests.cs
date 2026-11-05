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

public sealed class JukeboxActivationSessionRuntimeTests
{
    public static IEnumerable<object[]> FilledCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct()
        .SelectMany(protocol => (protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off })
            .Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(FilledCases))]
    public async Task FilledStateUsesProvenResultOrRetainsModernNativeEntityUncertainty(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        int[] states = protocol <= 340 ? Enumerable.Range(1, 15).Select(metadata => block.MinStateId | metadata).ToArray()
            : Enumerable.Range(block.MinStateId, block.StateCount)
                .Where(state => block.TryGetPropertyValue(state, "has_record", out string hasRecord) && hasRecord == "true").ToArray();
        Assert.Equal(protocol <= 340 ? 15 : 1, states.Length);
        foreach (int state in states)
        {
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
            Assert.Empty(await session.Barrier());
            BlockState observed = session.Client.State.World.GetBlock(Session.Target);
            Assert.True(observed.IsValid);
            if (protocol <= 340)
            {
                Assert.Equal(state & 15, observed.LegacyMeta);
                Assert.False(observed.TryGetProperty("has_record", out _));
            }
            else
            {
                Assert.True(observed.TryGetProperty("has_record", out string hasRecord));
                Assert.Equal("true", hasRecord);
            }
            Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
            await AssertUse(session, protocol, hand, protocol <= 761 ? BlockUseResult.Success : BlockUseResult.Unknown);
        }
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }

    public static IEnumerable<object[]> ContextCases() => new[] { 47, 107, 393, 498, 573, 761, 762, 766, 777 }
        .SelectMany(protocol => Enumerable.Range(0, 9).Where(context => protocol != 47 || context != 2)
            .Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task FilledUseKeepsSecondaryUsePermissionTerrainAndInsertionBoundaries(int protocol, int context)
    {
        await using Session session = await Session.Create(protocol, "jukebox", terrain: context != 5);
        if (context is 1 or 2 or 3 or 4)
            await session.Hold("stick");
        if (context == 8)
            await session.Hold(protocol <= 340 ? "record_13" : "music_disc_13");
        if (context is 0 or 4)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (context == 2)
            await session.Cooldown();
        if (context == 3)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == 6)
            await session.Forget();
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        bool filled = context is not (7 or 8);
        int state = protocol <= 340 ? block.MinStateId | (filled ? 1 : 0)
            : Enumerable.Range(block.MinStateId, block.StateCount).First(id =>
                block.TryGetPropertyValue(id, "has_record", out string hasRecord) && hasRecord == (filled ? "true" : "false"));
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        if (context is 5 or 6)
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        BlockUseResult expected = context == 4 ? BlockUseResult.Pass
            : context >= 5 || protocol >= 762 ? BlockUseResult.Unknown
            : BlockUseResult.Success;
        await AssertUse(session, protocol, Hand.Main, expected);
    }

    private static async Task AssertUse(Session session, int protocol, Hand hand, BlockUseResult expected)
    {
        ItemStack main = session.Client.State.Inventory.GetSlot(36);
        ItemStack off = session.Client.State.Inventory.GetSlot(45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client =>
            client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
        List<IPacket> packets = await session.Use(hand);
        bool packetSwing = expected == BlockUseResult.Success;
        Assert.Equal(packetSwing ? 2 : 1, packets.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal(Session.Target, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal(main.IsEmpty, use.HeldItem.IsEmpty);
            if (!main.IsEmpty)
            {
                Assert.Equal(main.Item.Id, use.HeldItem.Item.Id);
                Assert.Equal(main.Count, use.HeldItem.Count);
            }
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
        }
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        Assert.Equal(packetSwing ? BlockUseSwing.Packet : BlockUseSwing.None, prediction.Swing);
        Assert.Empty(await session.Barrier());
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
    }
}
