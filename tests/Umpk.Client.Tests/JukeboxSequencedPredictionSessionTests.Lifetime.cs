using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class JukeboxSequencedPredictionSessionTests
{
    [Theory]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    public async Task NoPendingMutationAppliesAuthoritativeUpdatesImmediately(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
        await session.Block(Session.Target, "stone");
        Assert.Empty(await session.Barrier());
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        if (protocol >= 759)
            await SendAck(session, protocol, 0);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
    }

    [Theory]
    [InlineData(758)]
    public async Task AdjacentPreSequenceJukeboxStillRearmsOnReceivedState(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse use = await BeginUse(session, protocol, 0, BlockUseResult.Success);
        await use.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SetState(session, Session.Target, filled);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(762)]
    [InlineData(766)]
    [InlineData(777)]
    public async Task ModernInstanceDependentJukeboxRetainsUnknown(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        await SetState(session, Session.Target, Filled(session));
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Unknown);
        Assert.Equal(BlockUseResult.Unknown, await Predict(session));
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Unknown, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task SectionUpdateUpdatesShadowWhileUntouchedCellRetainsCurrentState(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        var change = new ClientboundSectionBlocksUpdatePacket(4, 0, 0, [new((short)((1 << 8) | (1 << 4) | 1), filled)], IsLegacy: false);
        await RoundTripSend(session, protocol, change, "section_blocks_update", decoded =>
        {
            var actual = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(decoded);
            Assert.Equal(change.SectionPos, actual.SectionPos);
            Assert.Equal(change.Changes, actual.Changes);
            Assert.False(actual.IsLegacy);
            Assert.False(actual.SuppressLightUpdates);
        });
        Assert.Empty(await session.Barrier());
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task FullOverwriteChangesCurrentButRetainsShadowForLaterAck(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await FullColumn(session, protocol, state: 0);
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759, false)]
    [InlineData(759, true)]
    [InlineData(761, false)]
    [InlineData(761, true)]
    public async Task UnloadKeepsPendingShadowAndAckTimingControlsReload(int protocol, bool ackWhileAbsent)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await session.Forget();
        Assert.Equal(BlockUseResult.Unknown, await Predict(session));
        if (ackWhileAbsent)
        {
            await SendAck(session, protocol, use.Sequence);
            await use.Action.WaitAsync(session.Token);
        }
        await FullColumn(session, protocol, state: 0);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        if (!ackWhileAbsent)
        {
            await SendAck(session, protocol, use.Sequence);
            await use.Action.WaitAsync(session.Token);
        }
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(ackWhileAbsent ? BlockUseResult.Pass : BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task SameCellMutationKeepsOriginalShadowAndIgnoresEarlierAck(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse first = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await FullColumn(session, protocol, filled);
        await using PendingUse second = await BeginUse(session, protocol, 2, BlockUseResult.Success);
        await SendAck(session, protocol, first.Sequence);
        await first.Action.WaitAsync(session.Token);
        Assert.False(second.Action.IsCompleted);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SendAck(session, protocol, second.Sequence);
        await second.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task CumulativeThenOutOfOrderAcksRespectEachLatestMutation(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        BlockPos other = Session.Target.Offset(Direction.East);
        await SetState(session, Session.Target, filled);
        await SetState(session, other, filled);
        await using PendingUse first = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await using PendingUse second = await BeginUse(session, protocol, 2, BlockUseResult.Success, other);
        await SendAck(session, protocol, second.Sequence);
        await Task.WhenAll(first.Action, second.Action).WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, other));
        await using PendingUse third = await BeginUse(session, protocol, 3, BlockUseResult.Success);
        await SendAck(session, protocol, first.Sequence);
        Assert.False(third.Action.IsCompleted);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SendAck(session, protocol, third.Sequence);
        await third.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task SameWorldRespawnRetainsPendingCurrentAndShadow(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        World original = session.Client.State.World;
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await RespawnWorld(session, protocol, "minecraft:overworld");
        Assert.Same(original, session.Client.State.World);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task NewWorldSequenceCannotAcknowledgeOldWorldAction(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        await SetState(session, Session.Target, Filled(session));
        World original = session.Client.State.World;
        await using PendingUse old = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await RespawnWorld(session, protocol, "minecraft:the_nether");
        Assert.NotSame(original, session.Client.State.World);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        await FullColumn(session, protocol, Filled(session));
        await using PendingUse current = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        old.CancelWait();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        Assert.False(current.Action.IsCompleted);
        await SendAck(session, protocol, current.Sequence);
        await current.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task DisconnectClearsPopulatedPredictionAndNewSessionStartsFreshSequence(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        await SetState(session, Session.Target, Filled(session));
        var originalClient = session.Client;
        World retained = session.Client.State.World;
        await using PendingUse old = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.Same(retained, session.Client.State.World);
        Assert.False(session.Client.State.JukeboxPrediction.TryProject(retained, Session.Target, retained.GetBlock(Session.Target), out _));
        await session.Restart("jukebox");
        Assert.Same(originalClient, session.Client);
        Assert.NotSame(retained, session.Client.State.World);
        await SetState(session, Session.Target, Filled(session));
        await using PendingUse current = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await SendAck(session, protocol, current.Sequence);
        await current.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(573)]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(766)]
    [InlineData(775)]
    [InlineData(777)]
    public async Task NativeButtonPressRetainsPoweredCurrentStateWithoutLocalReleaseTimer(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone_button");
        int unpowered = session.Client.State.World.GetBlock(Session.Target).StateId;
        Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("powered", out string initial));
        Assert.Equal("false", initial);
        await using PendingUse use = await BeginUse(session, protocol, protocol >= 759 ? 1 : 0, BlockUseResult.Success);
        if (protocol < 759)
            await use.Action.WaitAsync(session.Token);
        Assert.Equal(unpowered, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Consume, await Predict(session));
        await using PendingUse consume = await BeginUse(session, protocol, protocol >= 759 ? 2 : 0, BlockUseResult.Consume);
        if (protocol < 759)
            await consume.Action.WaitAsync(session.Token);
        for (int tick = 0; tick < 35; tick++)
            await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(BlockUseResult.Consume, await Predict(session));
        if (protocol >= 759)
        {
            await SendAck(session, protocol, use.Sequence);
            await use.Action.WaitAsync(session.Token);
            Assert.False(consume.Action.IsCompleted);
        }
        Assert.Equal(protocol >= 759 ? BlockUseResult.Success : BlockUseResult.Consume, await Predict(session));
        if (protocol >= 759)
        {
            await SendAck(session, protocol, consume.Sequence);
            await consume.Action.WaitAsync(session.Token);
            Assert.Equal(BlockUseResult.Success, await Predict(session));
        }
    }

    [Theory]
    [InlineData(764)]
    [InlineData(775)]
    public async Task ReconfigurationDropsButtonProjectionAndCannotReuseOldWaiter(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone_button");
        World original = session.Client.State.World;
        await using PendingUse old = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        Assert.Equal(BlockUseResult.Consume, await Predict(session));
        await session.Reconfigure(null);
        Assert.False(session.Client.State.HasWorld);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        await session.RejoinWorld("stone_button");
        Assert.NotSame(original, session.Client.State.World);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
        await using PendingUse current = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        old.CancelWait();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        Assert.False(current.Action.IsCompleted);
        await SendAck(session, protocol, current.Sequence);
        await current.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task PhysicsUsesCurrentJukeboxUntilAckWhileReceivedWorldShowsAir(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        await session.Block(new BlockPos(1, 64, 1), "stone");
        await SetState(session, Session.Target, Filled(session));
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await session.Block(Session.Target, "air");
        await MoveSelf(session, new Vec3d(1.5, 68, 1.5));
        for (int tick = 0; tick < 35; tick++)
            await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(66, session.Client.State.Self.Position.Y, precision: 7);
        Assert.True(session.Client.State.Self.OnGround);
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        for (int tick = 0; tick < 20; tick++)
            await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(65, session.Client.State.Self.Position.Y, precision: 7);
    }

    [Theory]
    [InlineData(759, false, false)]
    [InlineData(759, true, false)]
    [InlineData(759, true, true)]
    [InlineData(761, false, false)]
    [InlineData(761, true, false)]
    [InlineData(761, true, true)]
    public async Task AckRestoresCapturedSelfPositionOnlyForChangedStateAndNativeBodyCollision(int protocol, bool colliding, bool equalState)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        await SetState(session, Session.Target, Filled(session));
        Vec3d captured = session.Client.State.Self.Position;
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        if (equalState)
            await SetState(session, Session.Target, Empty(session));
        Vec3d moved = colliding ? new(1.5, 65, 1.5) : new(4.5, 65, 4.5);
        await MoveSelf(session, moved);
        Assert.Equal(moved, session.Client.State.Self.Position);
        await session.Send(new ClientboundSetEntityMotionPacket(session.Client.State.Self.EntityId, 1000, -2000, 3000));
        Assert.Empty(await session.Barrier());
        PhysicsState before = await ReadBody(session);
        Assert.Equal(new Vec3d(.125, -.25, .375), before.Velocity);
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Vec3d expectedPosition = colliding && !equalState ? captured : moved;
        Assert.Equal(expectedPosition, session.Client.State.Self.Position);
        PhysicsState after = await ReadBody(session);
        Assert.Equal(expectedPosition, after.Position);
        Assert.Equal(before.BoundingBox.Move(expectedPosition.Subtract(moved)), after.BoundingBox);
        Assert.Equal((before.Velocity, before.Yaw, before.Pitch, before.Pose, before.FallDistance, before.OnGround, before.HorizontalCollision, before.VerticalCollision),
            (after.Velocity, after.Yaw, after.Pitch, after.Pose, after.FallDistance, after.OnGround, after.HorizontalCollision, after.VerticalCollision));
        Assert.Equal(before.Velocity, session.Client.State.Self.Velocity);
        Assert.Empty(await session.Barrier());
        if (!equalState)
            Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task InternalViewMiningUsesCurrentJukeboxWhilePublicRaycastKeepsReceivedWorld(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        await SetState(session, Session.Target, Filled(session));
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await session.Client.Actions.Movement.LookAtLocalAsync(Session.Target.Center, session.Token);
        BlockHitResult originalHit = Assert.IsType<BlockHitResult>(await session.Client.Snapshots.RaycastViewAsync(ct: session.Token));
        Assert.True(originalHit.Hit);
        Assert.Equal(Session.Target, originalHit.BlockPos);
        await session.Block(Session.Target, "air");
        Assert.Empty(await session.Barrier());
        BlockHitResult? receivedHit = await session.Client.Snapshots.RaycastViewAsync(ct: session.Token);
        Assert.True(receivedHit is null || receivedHit.Value.BlockPos != Session.Target);
        using var caller = new CancellationTokenSource();
        Task mining = session.Client.Actions.Interaction.DigBlockInViewAsync(Session.Target, ct: caller.Token);
        bool started = false;
        try
        {
            int before = session.ObservedOutbound.Count;
            await session.Tick();
            List<IPacket> animations = await session.Barrier();
            IPacket[] group = session.ObservedOutbound.Skip(before).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket).ToArray();
            Assert.Equal(3, group.Length);
            var start = Assert.IsType<ServerboundPlayerActionPacket>(group[0]);
            Assert.Equal((0, Session.Target, (byte)originalHit.Face, 2), (start.Action, start.Position, start.Direction, start.Sequence));
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(group[1]).Hand);
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(group[2]).Hand);
            Assert.Equal(2, animations.Count);
            started = true;
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            int beforeRelease = session.ObservedOutbound.Count;
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            await session.Tick();
            Assert.Empty(await session.Barrier());
            ServerboundPlayerActionPacket[] released = session.ObservedOutbound.Skip(beforeRelease).OfType<ServerboundPlayerActionPacket>().ToArray();
            if (started)
                Assert.Equal((1, Session.Target, (byte)Direction.Down, 0), (Assert.Single(released).Action, released[0].Position, released[0].Direction, released[0].Sequence));
            else
                Assert.Empty(released);
        }
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
    }

    private static int Empty(Session session)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
        return Enumerable.Range(block.MinStateId, block.StateCount).First(state => block.TryGetPropertyValue(state, "has_record", out string record) && record == "false");
    }

    private static async Task<BlockUseResult> PredictAt(Session session, BlockPos position)
    {
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(position, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Empty(await session.Barrier());
        return prediction.Result;
    }

    private static async Task SetState(Session session, BlockPos position, int state)
    {
        await session.Send(new ClientboundBlockUpdatePacket(position, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(position).StateId);
    }

    [Theory]
    [InlineData(761, false)]
    [InlineData(761, true)]
    public async Task SynthesizedStorageAfterUnloadCannotRestoreGhostStateButStillChecksSuppliedBody(int protocol, bool shadowAir)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        await SetState(session, Session.Target, Filled(session));
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        await session.Send(new ClientboundForgetLevelChunkPacket(ChunkPos.Containing(Session.Target)));
        Assert.Empty(await session.Barrier());
        Assert.Null(session.Client.State.World.GetColumn(Session.Target));
        BlockPos unrelated = Session.Target.Offset(Direction.East);
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.MinStateId;
        await SetState(session, unrelated, stone);
        if (shadowAir)
            await SetState(session, Session.Target, 0);
        Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Equal(BlockUseResult.Unknown, await Predict(session));
        Vec3d captured = session.Client.State.Self.Position;
        Vec3d moved = new(1.5, 65, 1.5);
        await MoveSelf(session, moved);
        await session.Send(new ClientboundSetEntityMotionPacket(session.Client.State.Self.EntityId, 1000, -2000, 3000));
        Assert.Empty(await session.Barrier());
        PhysicsState beforeAck = await ReadBody(session);
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Vec3d expected = shadowAir ? moved : captured;
        PhysicsState afterAck = await ReadBody(session);
        Assert.Equal(expected, session.Client.State.Self.Position);
        Assert.Equal(expected, afterAck.Position);
        Assert.Equal(beforeAck.BoundingBox.Move(expected.Subtract(moved)), afterAck.BoundingBox);
        Assert.Equal(beforeAck.Velocity, afterAck.Velocity);
        Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.False(session.Client.State.JukeboxPrediction.TryProject(session.Client.State.World, Session.Target, session.Client.State.World.GetBlock(Session.Target), out _));
        await FullColumn(session, protocol, stone);
        Assert.True(session.Client.State.EntityMembership.HasColumn(Session.Target));
        Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        Assert.Equal(expected, session.Client.State.Self.Position);
    }

    [Theory]
    [InlineData(775, false, false)]
    [InlineData(775, true, false)]
    [InlineData(775, true, true)]
    [InlineData(777, false, false)]
    [InlineData(777, true, false)]
    [InlineData(777, true, true)]
    public async Task ModernButtonShadowRestorationUsesNativeTeleportAndCumulativeAckBoundary(int protocol, bool teleport, bool cumulative)
    {
        await using Session session = await Session.Create(protocol, "stone_button", physics: true);
        BlockPos cell = BlockPos.Containing(session.Client.State.Self.Position);
        int initial = session.Client.State.World.GetBlock(Session.Target).StateId;
        await SetState(session, cell, initial);
        Vec3d captured = session.Client.State.Self.Position;
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success, cell);
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.MinStateId;
        await SetState(session, cell, stone);
        Assert.Equal(stone, session.Client.State.World.GetBlock(cell).StateId);
        Assert.Equal(BlockUseResult.Consume, await PredictAt(session, cell));
        using var walkCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task walk = session.Client.Actions.Movement.MoveToAsync(new Vec3d(cell.X + .8, cell.Y, cell.Z + .8), walkCancellation.Token);
        try
        {
            for (int tick = 0; tick < 90 && !walk.IsCompleted; tick++)
            {
                await session.Tick();
                Assert.Empty(await session.Barrier());
            }
            await walk.WaitAsync(session.Token);
        }
        finally
        {
            walkCancellation.Cancel();
            if (!walk.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => walk.WaitAsync(session.Token));
        }
        PhysicsState moved = await ReadBody(session);
        Assert.True(moved.Position.Subtract(captured).LengthSqr() > .01);
        Assert.True(Aabb.BlockAt(cell.X, cell.Y, cell.Z).Intersects(moved.BoundingBox));
        if (teleport)
            await MoveSelf(session, moved.Position);
        int acknowledged = use.Sequence;
        if (cumulative)
        {
            int before = session.ObservedOutbound.Count;
            await session.Client.Actions.Interaction.UseItemAsync(ct: session.Token);
            Assert.Empty(await session.Barrier());
            ServerboundUseItemPacket airUse = Assert.Single(session.ObservedOutbound.Skip(before).OfType<ServerboundUseItemPacket>());
            Assert.Equal(2, airUse.Sequence);
            Assert.Equal(0, airUse.Hand);
            acknowledged = airUse.Sequence;
        }
        PhysicsState beforeAck = await ReadBody(session);
        await SendAck(session, protocol, acknowledged);
        await use.Action.WaitAsync(session.Token);
        Vec3d expected = !teleport || cumulative ? captured : beforeAck.Position;
        PhysicsState afterAck = await ReadBody(session);
        Assert.Equal(expected, session.Client.State.Self.Position);
        Assert.Equal(expected, afterAck.Position);
        Assert.Equal(beforeAck.BoundingBox.Move(expected.Subtract(beforeAck.Position)), afterAck.BoundingBox);
        Assert.Equal((beforeAck.Velocity, beforeAck.Yaw, beforeAck.Pitch, beforeAck.Pose, beforeAck.FallDistance),
            (afterAck.Velocity, afterAck.Yaw, afterAck.Pitch, afterAck.Pose, afterAck.FallDistance));
        Assert.Equal(stone, session.Client.State.World.GetBlock(cell).StateId);
        Assert.Equal(BlockUseResult.Pass, await PredictAt(session, cell));
        Assert.Empty(await session.Barrier());
    }

    [Theory]
    [InlineData(759, false, false)]
    [InlineData(759, false, true)]
    [InlineData(759, true, false)]
    [InlineData(759, true, true)]
    [InlineData(761, false, false)]
    [InlineData(761, false, true)]
    [InlineData(761, true, false)]
    [InlineData(761, true, true)]
    public async Task FullReplacementCurrentSurvivesLaterVerifiedUpdateUntilAck(int protocol, bool reload, bool sectionUpdate)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        int filled = Filled(session);
        await SetState(session, Session.Target, filled);
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        if (reload)
            await session.Forget();
        int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.MinStateId;
        await FullColumn(session, protocol, stone);
        Assert.True(session.Client.State.EntityMembership.HasColumn(Session.Target));
        Assert.Equal(stone, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        BlockUseResult? observed = null;
        using IDisposable observer = session.Client.Events.Subscribe<BlockChanged>(change =>
        {
            if (change.Position == Session.Target)
                observed = Prediction(session).Result;
        });
        if (sectionUpdate)
        {
            var update = new ClientboundSectionBlocksUpdatePacket(4, 0, 0, [new((short)((1 << 8) | (1 << 4) | 1), filled)], IsLegacy: false);
            await RoundTripSend(session, protocol, update, "section_blocks_update", decoded =>
            {
                var actual = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(decoded);
                Assert.Equal(update.SectionPos, actual.SectionPos);
                Assert.Equal(update.Changes, actual.Changes);
                Assert.False(actual.IsLegacy);
                Assert.False(actual.SuppressLightUpdates);
            });
            Assert.Empty(await session.Barrier());
        }
        else
            await SetState(session, Session.Target, filled);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Pass, observed);
        Assert.Equal(BlockUseResult.Pass, await Predict(session));
        Assert.False(use.Action.IsCompleted);
        await SendAck(session, protocol, use.Sequence);
        await use.Action.WaitAsync(session.Token);
        Assert.Equal(filled, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(BlockUseResult.Success, await Predict(session));
    }

    private static Task<PhysicsState> ReadBody(Session session)
        => session.Client.InvokeAsync(client =>
        {
            var owner = Assert.IsType<PhysicsEngineHolder>(typeof(UmpkClient).GetField("_physics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
            return Assert.IsType<PhysicsState>(owner.EngineState);
        }, session.Token);

    private static async Task MoveSelf(Session session, Vec3d position)
    {
        var packet = new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new PositionMoveRotation(position, Vec3d.Zero, 0, 0));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Self.Position);
    }

    private static async Task FullColumn(Session session, int protocol, int state)
    {
        int minSection = session.Client.State.World.Dimension.MinY >> 4;
        int count = (session.Client.State.World.Dimension.MaxY - session.Client.State.World.Dimension.MinY) / 16;
        var air = new ChunkPalettedContainerWire(0, 0, null, []);
        ChunkSectionWire[] sections = Enumerable.Range(minSection, count).Select(y => new ChunkSectionWire(y == 4 && state != 0 ? (short)4096 : (short)0, null, y == 4 ? new(0, state, null, []) : air, air)).ToArray();
        byte[] raw = ChunkCodecs.EncodeStructural(new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[8], NbtWireFormat.JavaNamedRoot));
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "level_chunk_with_light");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(raw, context));
        Assert.Equal((0, 0, true), (packet.ChunkX, packet.ChunkZ, packet.FullChunk));
        Assert.Equal(state, packet.Column.GetBlockStateId(1, (4 - minSection) * 16 + 1, 1));
        Assert.Equal(raw, Encode(codec, context, packet));
        Assert.Equal(raw, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    private static async Task RespawnWorld(Session session, int protocol, string dimension)
    {
        var spawn = new CommonPlayerSpawnInfo(0, dimension, 0, 0, -1, false, false, null, 0, 0) { DimensionTypeName = dimension };
        var packet = new ClientboundRespawnPacket(spawn, 0, null);
        await RoundTripSend(session, protocol, packet, "respawn", decoded =>
        {
            var actual = Assert.IsType<ClientboundRespawnPacket>(decoded);
            var value = Assert.IsType<CommonPlayerSpawnInfo>(actual.SpawnInfo);
            Assert.Equal(dimension, value.Dimension);
            Assert.Equal(dimension, value.DimensionTypeName);
            Assert.Equal(0, value.DimensionTypeId);
            Assert.Equal(0, actual.DataToKeep);
            Assert.Null(actual.Legacy);
        });
        Assert.Empty(await session.Barrier());
    }

    private static async Task RoundTripSend(Session session, int protocol, IPacket packet, string name, Action<object> inspect)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, name);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] raw = Encode(codec, context, packet);
        object decoded = codec.Decode(raw, context);
        inspect(decoded);
        Assert.Equal(raw, Encode(codec, context, decoded));
        await session.Send(Assert.IsAssignableFrom<IPacket>(decoded));
    }

    private static async Task<PendingUse> BeginUse(Session session, int protocol, int expectedSequence, BlockUseResult expected, BlockPos? target = null)
    {
        BlockPos position = target ?? Session.Target;
        Assert.Equal(expected, await PredictAt(session, position));
        var wait = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(position, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main, ct: wait.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await session.Barrier();
            bool swingPacket = expected == BlockUseResult.Success && protocol != 777;
            Assert.Equal(swingPacket ? 2 : 1, packets.Count);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((position, (int)Direction.Up, (int)Hand.Main), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
            Assert.Equal(expectedSequence, use.Sequence);
            if (swingPacket)
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
            if (protocol >= 759)
                Assert.False(action.IsCompleted);
            return new PendingUse(action, use.Sequence, wait);
        }
        catch
        {
            wait.Cancel();
            try
            {
                await action.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                // Observe this owned action if a fixture assertion fails before ownership can be returned.
            }
            wait.Dispose();
            throw;
        }
    }

    private sealed class PendingUse(Task action, int sequence, CancellationTokenSource wait) : IAsyncDisposable
    {
        internal Task Action { get; } = action;
        internal int Sequence { get; } = sequence;
        internal void CancelWait() => wait.Cancel();

        public async ValueTask DisposeAsync()
        {
            wait.Cancel();
            try
            {
                await Action.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                // Observe only this fixture's canceled wait; assertion failures and unrelated action failures remain failures.
            }
            wait.Dispose();
        }
    }
}
