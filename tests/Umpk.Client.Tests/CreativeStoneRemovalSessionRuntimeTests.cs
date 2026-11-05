using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class CreativeStoneRemovalSessionRuntimeTests
{
    private sealed record Observation(int Current, int Received, bool Projected, bool EmptyHand, long Tick);

    public static IEnumerable<object[]> Cases() => new[] { 766, 769 }.SelectMany(protocol => new[]
    {
        "ack-rollback", "authority-air", "repeat-before-ack", "world-replacement", "session-replacement",
        "cancel-before", "missing-terrain", "survival-first-tick",
    }.Select(scene => new object[] { protocol, scene }));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalCreativeRemovalPrecedesStartWhileReceivedTerrainAndLifetimeRemainOwned(int protocol, string scene)
    {
        await using Session session = await Ready(protocol, terrain: scene != "missing-terrain");
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
        int air = session.Client.State.Registries!.Blocks[Identifier.Minecraft("air")].Value.DefaultStateId;
        int beforePackets = session.ObservedOutbound.Count;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        if (scene == "cancel-before")
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token));
            await session.Tick();
            await session.Barrier();
            Assert.Empty(ActionPackets(session, beforePackets));
            Assert.Equal(new Observation(stone, stone, false, true, session.Client.State.SessionTick), await Observe(session));
            return;
        }
        if (scene == "survival-first-tick")
            await session.Mode(GameMode.Survival, instantBuild: false);
        if (scene is "missing-terrain" or "survival-first-tick")
        {
            Task held = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
            try
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                await session.Tick();
                await session.Barrier();
                IPacket[] packets = ActionPackets(session, beforePackets);
                if (scene == "missing-terrain")
                {
                    Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
                    Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
                    AssertMainSwing(Assert.Single(packets));
                    await held.WaitAsync(session.Token);
                    BlockState unavailable = await session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target), session.Token);
                    Assert.Equal(Identifier.Minecraft("void_air"), unavailable.Block.Id);
                    Assert.NotEqual(air, unavailable.StateId);
                }
                else
                {
                    Assert.Collection(packets, packet => AssertStart(packet, sequence: 1), AssertMainSwing, AssertMainSwing);
                    Assert.False(held.IsCompleted);
                    Assert.Equal(stone, (await Observe(session)).Current);
                }
                Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
                Assert.False((await Observe(session)).Projected);
            }
            finally
            {
                await caller.CancelAsync();
                try { await held.WaitAsync(session.Token); }
                catch (OperationCanceledException) when (caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
            }
            return;
        }

        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound).TryGetOutbound(EntityPackets.Serverbound.PlayerAction, out int actionId, out BoundPacketCodec actionCodec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Observation? atStart = null;
        ServerboundPlayerActionPacket? captured = null;
        bool holdNextFlush = false;
        long interactionLifetime = session.Client.State.InteractionLifetime;
        long playerLifetime = session.Client.State.PlayerLifetime;
        long beforeTick = session.Client.State.SessionTick;
        var oldWorld = session.Client.State.World;
        session.BeforeFramedWrite = frame =>
        {
            var reader = new PacketReader(frame.Span);
            int length = reader.ReadVarInt();
            int payloadStart = reader.Position;
            if (reader.ReadVarInt() != actionId)
                return;
            Assert.Null(captured);
            Assert.Equal(frame.Length - payloadStart, length);
            byte[] body = reader.ReadBytes(reader.Remaining).ToArray();
            captured = Assert.IsType<ServerboundPlayerActionPacket>(actionCodec.Decode(body, context));
            Assert.Equal(body, Encode(actionCodec, captured, context));
            atStart = ReadObservation(session);
            holdNextFlush = true;
        };
        session.BeforeClientFlush = async cancellationToken =>
        {
            if (!holdNextFlush)
                return;
            holdNextFlush = false;
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        Task? tick = null;
        bool lifetimeEnded = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            tick = session.Tick();
            await entered.Task.WaitAsync(session.Token);
            Assert.False(action.IsCompleted);
            AssertStart(Assert.IsType<ServerboundPlayerActionPacket>(captured), sequence: 1);
            // This causal assertion observes the real owner's state during the real encoded START write, before flush reaches the peer.
            Assert.Equal(new Observation(air, stone, true, true, beforeTick + 1), atStart);
            release.TrySetResult();
            session.BeforeFramedWrite = null;
            session.BeforeClientFlush = null;
            await tick.WaitAsync(session.Token);
            await session.Barrier();
            Assert.Collection(ActionPackets(session, beforePackets), packet => AssertStart(packet, sequence: 1), AssertMainSwing);
            Assert.False(action.IsCompleted);
            Assert.Equal(air, (await Observe(session)).Current);
            Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
            if (scene == "authority-air")
            {
                await session.Block(Session.Target, "air");
                await session.Barrier();
                Assert.Equal(air, session.Client.State.World.GetBlock(Session.Target).StateId);
                Assert.Equal(air, (await Observe(session)).Current);
            }
            if (scene == "repeat-before-ack")
            {
                int repeatedFrom = session.ObservedOutbound.Count;
                Task repeated = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
                try
                {
                    await session.Client.InvokeAsync(_ => true, session.Token);
                    await session.Tick();
                    await session.Barrier();
                    await repeated.WaitAsync(session.Token);
                    AssertMainSwing(Assert.Single(ActionPackets(session, repeatedFrom)));
                    Assert.False(action.IsCompleted);
                    Assert.Equal(air, (await Observe(session)).Current);
                    Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
                }
                finally
                {
                    if (!repeated.IsCompleted)
                        await caller.CancelAsync();
                    try { await repeated.WaitAsync(session.Token); }
                    catch (OperationCanceledException) when (caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
                }
            }
            if (scene is "world-replacement" or "session-replacement")
            {
                if (scene == "world-replacement")
                {
                    await session.Respawn();
                    await session.Terrain();
                    await session.Block(Session.Target, "stone");
                    await session.Barrier();
                }
                else
                {
                    await session.Client.DisconnectAsync(session.Token);
                    await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
                    {
                        while (true)
                            await session.Server.NextFrameAsync(session.Token);
                    });
                    await session.Restart("stone");
                }
                lifetimeEnded = true;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(session.Token));
                Assert.NotSame(oldWorld, session.Client.State.World);
                Assert.Equal(stone, (await Observe(session)).Current);
                Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
                Assert.False((await Observe(session)).Projected);
                return;
            }
            await session.Send(new ClientboundBlockChangedAckPacket(captured!.Sequence!.Value));
            await session.Barrier();
            await action.WaitAsync(session.Token);
            int expected = scene == "authority-air" ? air : stone;
            Assert.Equal(expected, (await Observe(session)).Current);
            Assert.Equal(expected, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.False((await Observe(session)).Projected);
            Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        }
        finally
        {
            release.TrySetResult();
            session.BeforeFramedWrite = null;
            session.BeforeClientFlush = null;
            if (tick is not null)
                await tick.WaitAsync(session.Token);
            if (session.Client.Status == ClientStatus.Playing)
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                await session.Barrier();
                if (!action.IsCompleted && captured?.Sequence is int sequence
                    && session.Client.State.InteractionLifetime == interactionLifetime && session.Client.State.PlayerLifetime == playerLifetime)
                {
                    await session.Send(new ClientboundBlockChangedAckPacket(sequence));
                    await session.Barrier();
                }
            }
            if (!action.IsCompleted)
                await caller.CancelAsync();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (lifetimeEnded || caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
        }
    }

    private static async Task<Session> Ready(int protocol, bool terrain)
    {
        Session session = await Session.Create(protocol, "stone", terrain: terrain, observeWrites: true);
        try
        {
            await session.Mode(GameMode.Creative, instantBuild: true);
            await session.Hold(null);
            foreach (Direction direction in new[] { Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East })
                await session.Block(Session.Target.Offset(direction), direction == Direction.Down ? "stone" : "air");
            await session.Barrier();
            Assert.Equal(terrain, session.Client.State.EntityMembership.HasColumn(Session.Target));
            Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
            Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
            Assert.Equal(session.Client.State.PlayerLifetime, session.Client.State.Self.AbilitiesObservationLifetime);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static Observation ReadObservation(Session session)
    {
        var state = session.Client.State;
        BlockState received = state.World.GetBlock(Session.Target);
        bool projected = state.JukeboxPrediction.TryProject(state.World, Session.Target, received, out _);
        return new(state.JukeboxPrediction.GetBlock(state.World, Session.Target).StateId, received.StateId, projected, state.Inventory.GetSlot(36).IsEmpty, state.SessionTick);
    }

    private static Task<Observation> Observe(Session session) => session.Client.InvokeAsync(_ => ReadObservation(session), session.Token);
    private static IPacket[] ActionPackets(Session session, int start) => session.ObservedOutbound.Skip(start).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket).ToArray();
    private static void AssertStart(IPacket packet, int sequence)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(packet);
        Assert.Equal((0, Session.Target, (byte)Direction.North, (int?)sequence), (action.Action, action.Position, action.Direction, action.Sequence));
    }
    private static void AssertMainSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }
    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
