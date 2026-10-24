using Umpk.Client.Actions;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class JukeboxRepeatedUseSessionRuntimeTests
{
    public static IEnumerable<object[]> Cases() => new[] { 47, 107, 393, 498, 573, 761 }
        .SelectMany(protocol => (protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off })
            .Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task LocalEjectionStopsRepeatedSuccessAndIdenticalServerStateRearmsIt(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        int filled = protocol <= 340 ? block.MinStateId | 1
            : Enumerable.Range(block.MinStateId, block.StateCount).First(state =>
                block.TryGetPropertyValue(state, "has_record", out string hasRecord) && hasRecord == "true");
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
        Assert.Empty(await session.Barrier());
        await AssertPrediction(session, hand, BlockUseResult.Success);
        await AssertPrediction(session, hand, BlockUseResult.Success);
        await AssertPackets(session, protocol, hand, swing: true);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));

        await AssertPrediction(session, hand, BlockUseResult.Pass);
        await AssertPrediction(session, hand, BlockUseResult.Pass);
        await AssertPackets(session, protocol, hand, swing: false);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);

        await session.Send(new ClientboundBlockUpdatePacket(Session.Target.Offset(Direction.East), block.DefaultStateId));
        Assert.Empty(await session.Barrier());
        await AssertPrediction(session, hand, BlockUseResult.Pass);

        BlockUseResult? resultDuringServerEvent = null;
        using IDisposable observed = session.Client.Events.Subscribe<Umpk.Client.Events.BlockChanged>(change =>
        {
            if (change.Position == Session.Target)
                resultDuringServerEvent = session.Client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand).Result;
        });
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
        Assert.Empty(await session.Barrier());
        Assert.Equal(BlockUseResult.Success, resultDuringServerEvent);
        await AssertPrediction(session, hand, BlockUseResult.Success);
        await AssertPackets(session, protocol, hand, swing: true);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
    }

    private static async Task AssertPrediction(Session session, Hand hand, BlockUseResult expected)
    {
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client =>
            client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        Assert.Equal(expected == BlockUseResult.Success ? BlockUseSwing.Packet : BlockUseSwing.None, prediction.Swing);
        Assert.Empty(await session.Barrier());
    }

    private static async Task AssertPackets(Session session, int protocol, Hand hand, bool swing)
    {
        List<IPacket> packets = await session.Use(hand);
        Assert.Equal(swing ? 2 : 1, packets.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up), (use.Position, use.Face));
            Assert.True(use.HeldItem.IsEmpty);
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
        }
        if (swing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.Empty(await session.Barrier());
    }
}
