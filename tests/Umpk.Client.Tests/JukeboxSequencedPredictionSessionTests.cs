using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

// Private preparation only. These tests have not been compiled or executed.
public sealed partial class JukeboxSequencedPredictionSessionTests
{
    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    public async Task ReceivedFilledUpdateBeforeAckPreservesLocalEmptyBeforeObservers(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
        Assert.Empty(await session.Barrier());
        (Task action, int sequence) = await StartWithoutAck(session);
        try
        {
            Assert.Equal(BlockUseResult.Pass, await Predict(session));
            BlockUseResult? duringEvent = null;
            using IDisposable observer = session.Client.Events.Subscribe<BlockChanged>(change =>
            {
                if (change.Position == Session.Target)
                    duringEvent = Prediction(session).Result;
            });
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
            Assert.Empty(await session.Barrier());
            Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(BlockUseResult.Pass, duringEvent);
            Assert.Equal(BlockUseResult.Pass, await Predict(session));
            Assert.False(action.IsCompleted);
            await SendAck(session, protocol, sequence);
            await action.WaitAsync(session.Token);
            Assert.Equal(BlockUseResult.Success, await Predict(session));
        }
        finally
        {
            await FinishOwnedAction(session, protocol, sequence, action);
        }
    }

    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    public async Task AckWithoutServerUpdateRestoresCapturedFilledState(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, filled));
        Assert.Empty(await session.Barrier());
        (Task action, int sequence) = await StartWithoutAck(session);
        try
        {
            Assert.Equal(BlockUseResult.Pass, await Predict(session));
            await SendAck(session, protocol, sequence);
            await action.WaitAsync(session.Token);
            Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(BlockUseResult.Success, await Predict(session));
            Assert.Empty(await session.Barrier());
        }
        finally
        {
            await FinishOwnedAction(session, protocol, sequence, action);
        }
    }

    private static int Filled(Session session)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        return Enumerable.Range(block.MinStateId, block.StateCount).First(state =>
            block.TryGetPropertyValue(state, "has_record", out string hasRecord) && hasRecord == "true");
    }

    private static BlockUsePrediction Prediction(Session session)
        => session.Client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main);

    private static async Task<BlockUseResult> Predict(Session session)
    {
        BlockUsePrediction prediction = await session.Client.InvokeAsync(_ => Prediction(session), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(prediction.Result == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None, prediction.Swing);
        Assert.Empty(await session.Barrier());
        return prediction.Result;
    }

    private static async Task<(Task Action, int Sequence)> StartWithoutAck(Session session)
    {
        // Session.Use automatically acknowledges 759+; these cases must control the native pending interval explicitly.
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(2, packets.Count);
        ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)Direction.Up, (int)Hand.Main), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        Assert.Equal(1, use.Sequence);
        Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.False(action.IsCompleted);
        return (action, use.Sequence);
    }

    private static async Task SendAck(Session session, int protocol, int sequence)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "block_changed_ack");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = new ClientboundBlockChangedAckPacket(sequence);
        byte[] raw = Encode(codec, context, packet);
        var decoded = Assert.IsType<ClientboundBlockChangedAckPacket>(codec.Decode(raw, context));
        Assert.Equal(sequence, decoded.Sequence);
        Assert.Equal(raw, Encode(codec, context, decoded));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static byte[] Encode(BoundPacketCodec codec, PacketCodecContext context, object packet)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private static async Task FinishOwnedAction(Session session, int protocol, int sequence, Task action)
    {
        // Completing the owned fixture action after a failed assertion does not change the failed assertion's outcome.
        if (!action.IsCompleted)
            await SendAck(session, protocol, sequence);
        await action.WaitAsync(session.Token);
    }
}
