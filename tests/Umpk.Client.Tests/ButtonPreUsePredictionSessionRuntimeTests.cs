using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class ButtonPreUsePredictionSessionRuntimeTests
{
    private sealed record Observation(int Current, int Received, BlockUseResult NextUse);

    public static IEnumerable<object[]> Cases() => new[] { 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777 }.SelectMany(protocol => new[] { "unpowered-main", "powered-main", "unpowered-offhand", "cancel-before" }.Select(scene => new object[] { protocol, scene }));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ButtonStatePrecedesTheActualUseWriteWithoutOwningAnotherTimer(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone_button", observeWrites: true);
        await session.Hold(null);
        BlockDefinition definition = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone_button")].Value;
        int unpowered = definition.DefaultStateId;
        Assert.True(definition.TryGetPropertyValue(unpowered, "powered", out string original));
        Assert.Equal("false", original);
        int powered = Enumerable.Range(definition.MinStateId, definition.StateCount).Single(state =>
            definition.TryGetPropertyValue(state, "powered", out string value) && value == "true"
            && definition.Properties.Where(property => property.Name != "powered").All(property =>
                definition.TryGetPropertyValue(state, property.Name, out string actual)
                && definition.TryGetPropertyValue(unpowered, property.Name, out string expected) && actual == expected));
        int initial = scene == "powered-main" ? powered : unpowered;
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, initial));
        Assert.Empty(await session.Barrier());
        Assert.Equal(initial, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.True(session.Client.State.EntityMembership.HasColumn(Session.Target));
        Hand hand = scene == "unpowered-offhand" ? Hand.Off : Hand.Main;
        BlockUseResult before = scene == "powered-main" ? BlockUseResult.Consume : hand == Hand.Off ? BlockUseResult.Pass : BlockUseResult.Success;
        Assert.Equal(before, await session.Client.InvokeAsync(client => Predict(client.Actions.Interaction, hand).Result, session.Token));
        Assert.Equal(initial, await session.Client.InvokeAsync(client => Current(client.State), session.Token));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        if (scene == "cancel-before")
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new(.5, 1, .5), hand, ct: caller.Token));
            Assert.Empty(await session.Barrier());
            Assert.Equal(initial, await session.Client.InvokeAsync(client => Current(client.State), session.Token));
            Assert.Equal(initial, session.Client.State.World.GetBlock(Session.Target).StateId);
            return;
        }
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound).TryGetOutbound(ItemPackets.Serverbound.UseItemOn, out int useId, out BoundPacketCodec useCodec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerboundUseItemOnPacket? capturedUse = null;
        Observation? atUse = null;
        byte[]? capturedBody = null;
        bool gateNextFlush = false;
        bool acknowledged = false;
        session.BeforeFramedWrite = frame =>
        {
            var reader = new PacketReader(frame.Span);
            int length = reader.ReadVarInt();
            int payloadStart = reader.Position;
            if (reader.ReadVarInt() != useId)
                return;
            Assert.Null(capturedUse);
            Assert.Equal(frame.Length - payloadStart, length);
            capturedBody = reader.ReadBytes(reader.Remaining).ToArray();
            capturedUse = Assert.IsType<ServerboundUseItemOnPacket>(useCodec.Decode(capturedBody, context));
            Assert.Equal(capturedBody, Encode(useCodec, capturedUse, context));
            atUse = new(Current(session.Client.State), session.Client.State.World.GetBlock(Session.Target).StateId, Predict(session.Client.Actions.Interaction, hand).Result);
            gateNextFlush = true;
        };
        session.BeforeClientFlush = async cancellationToken =>
        {
            if (!gateNextFlush)
                return;
            gateNextFlush = false;
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new(.5, 1, .5), hand, ct: caller.Token);
        try
        {
            await entered.Task.WaitAsync(session.Token);
            Assert.False(action.IsCompleted);
            int expectedCurrent = scene == "unpowered-main" ? powered : initial;
            BlockUseResult expectedNext = hand == Hand.Main ? BlockUseResult.Consume : BlockUseResult.Pass;
            // The first causal assertion reads the existing owner while transport of the real encoded USE is held.
            Assert.Equal(new Observation(expectedCurrent, initial, expectedNext), atUse);
            ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(capturedUse);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside || use.WorldBorderHit);
            Assert.Equal(1, use.Sequence);
            release.TrySetResult();
            session.BeforeFramedWrite = null;
            session.BeforeClientFlush = null;
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await session.Barrier();
            Assert.Equal(use, Assert.IsType<ServerboundUseItemOnPacket>(packets[0]));
            bool packetSwing = scene == "unpowered-main" && protocol != 777;
            Assert.Equal(packetSwing ? 2 : 1, packets.Count);
            if (packetSwing)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
            Assert.Equal(initial, session.Client.State.World.GetBlock(Session.Target).StateId);
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            acknowledged = true;
            Assert.Empty(await session.Barrier());
            await action.WaitAsync(session.Token);
            Assert.Equal(initial, await session.Client.InvokeAsync(client => Current(client.State), session.Token));
            Assert.Equal(before, await session.Client.InvokeAsync(client => Predict(client.Actions.Interaction, hand).Result, session.Token));
        }
        finally
        {
            release.TrySetResult();
            session.BeforeFramedWrite = null;
            session.BeforeClientFlush = null;
            if (!acknowledged && capturedUse is { } use)
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                await session.Barrier();
                await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
                await session.Barrier();
            }
            caller.Cancel();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
        }
    }

    private static int Current(ClientState state) => state.JukeboxPrediction.GetBlock(state.World, Session.Target).StateId;
    private static BlockUsePrediction Predict(InteractionActions interaction, Hand hand) => interaction.PredictBlockUse(Session.Target, Direction.Up, new(.5, 1, .5), hand);
    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
