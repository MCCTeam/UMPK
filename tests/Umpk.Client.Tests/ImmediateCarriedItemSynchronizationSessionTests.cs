using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

/// <summary>Immediate controller entry after framed hand changes. These prove send/cache ownership, not native client execution or777 entity-interaction wire parity.</summary>
public sealed class ImmediateCarriedItemSynchronizationSessionTests
{
    private const int Protocol = 770;
    private const int Selected = 5;
    private const int EntityId = 37;
    private static readonly Vec3d Cursor = new(.5, 1, .5);

    [Fact]
    public async Task ImmediateBlockUseSynchronizesBeforeUseAndSwingWithoutATick()
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        try
        {
            Assert.Equal(BlockUsePrediction.Success(localSwing: false), await Preview(session));
            int start = session.ObservedOutbound.Count;
            Task action = StartAction(session, "place", session.Token);
            await FinishAction(session, action, start);
            AssertActionGroup(session, start, "place", Selected);
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, start).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Fact]
    public async Task ControllerBorderRejectionStillSynchronizesWithoutUseOrSwing()
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        try
        {
            await Known(session, new ClientboundSetBorderSizePacket(1), packet => Assert.Equal(1, packet.Size));
            Assert.False(session.Client.State.World.Border.IsWithinBounds(Session.Target.X, Session.Target.Z,
                Umpk.Game.World.WorldBorderState.ContainmentEraForProtocol(Protocol)));
            Assert.Equal(BlockUsePrediction.Rejected, await Preview(session));
            int start = session.ObservedOutbound.Count;
            await session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, Cursor, ct: session.Token);
            await session.Barrier();
            AssertOnlyCarried(session, start, Selected);
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, start).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Fact]
    public Task DisabledHeldInputRejectsBeforeSynchronization()
        => DisabledInput(GameMode.Survival);

    [Fact]
    public async Task LegacyPlacementPrecheckRejectsAfterSynchronizing()
    {
        Session session = await Prepared(47, Selected, "stone");
        Exception? primaryFailure = null;
        try
        {
            await session.Block(Session.Target.Above(), "stone");
            await session.Barrier();
            Assert.Equal(BlockUsePrediction.Rejected, await Preview(session));
            int start = session.ObservedOutbound.Count;
            await session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, Cursor, ct: session.Token);
            await session.Barrier();
            AssertOnlyCarried(session, start, Selected);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Fact]
    public async Task SpectatorAirUseReturnsBeforeSynchronization()
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        try
        {
            await session.Mode(GameMode.Spectator, instantBuild: false);
            int start = session.ObservedOutbound.Count;
            await session.Client.Actions.Interaction.UseItemAsync(Hand.Main, session.Token);
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, start).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Fact]
    public async Task ExplicitAttackSynchronizesBeforeItsExistingControllerPacket()
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        try
        {
            int start = session.ObservedOutbound.Count;
            Task action = StartAction(session, "attack", session.Token);
            await FinishAction(session, action, start);
            AssertActionGroup(session, start, "attack", Selected);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Theory]
    [InlineData("place")]
    [InlineData("air")]
    [InlineData("attack")]
    public async Task UnchangedSelectionPreservesTheActionWithoutAnExtraCarriedFrame(string kind)
    {
        Session session = await Prepared(Protocol, 0);
        Exception? primaryFailure = null;
        try
        {
            int start = session.ObservedOutbound.Count;
            Task action = StartAction(session, kind, session.Token);
            await FinishAction(session, action, start);
            AssertActionGroup(session, start, kind, carried: null);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Fact]
    public Task DisabledHeldInputStillPrecedesTheSpectatorBlockController()
        => DisabledInput(GameMode.Spectator);

    [Fact]
    public async Task ExplicitSelectionAndImmediateUseShareOneCache()
    {
        Session session = await Prepared(Protocol, 0);
        Exception? primaryFailure = null;
        try
        {
            await Slot(session, Selected, "stone");
            await session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token);
            await session.Barrier();
            int start = session.ObservedOutbound.Count;
            Task action = StartAction(session, "place", session.Token);
            await FinishAction(session, action, start);
            AssertActionGroup(session, start, "place", carried: null);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Theory]
    [InlineData("place")]
    [InlineData("air")]
    public async Task UnsupportedLegacyHandRejectsBeforeAnySynchronization(string kind)
    {
        Session session = await Prepared(47, Selected);
        Exception? primaryFailure = null;
        try
        {
            int start = session.ObservedOutbound.Count;
            await Assert.ThrowsAsync<ActionNotSupportedException>(() => kind == "place"
                ? session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, Cursor, Hand.Off, ct: session.Token)
                : session.Client.Actions.Interaction.UseItemAsync(Hand.Off, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Theory]
    [InlineData("place")]
    [InlineData("air")]
    [InlineData("attack")]
    public async Task CallerCancellationBeforeAdmissionDoesNotWriteOrAdvanceTheCache(string kind)
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        using var caller = new CancellationTokenSource();
        try
        {
            await caller.CancelAsync();
            int start = session.ObservedOutbound.Count;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartAction(session, kind, caller.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, start).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    [Theory]
    [InlineData("place")]
    [InlineData("air")]
    [InlineData("attack")]
    public async Task CallerCancellationAtTheCarriedWriteFinishesTheCapturedPacketGroup(string kind)
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        using var caller = new CancellationTokenSource();
        byte[]? firstFrame = null;
        try
        {
            int start = session.ObservedOutbound.Count;
            session.BeforeFramedWrite = frame =>
            {
                if (firstFrame is not null)
                    return;
                firstFrame = frame.ToArray();
                caller.Cancel();
            };
            Task action = StartAction(session, kind, caller.Token);
            await FenceAndAcknowledge(session, start);
            session.BeforeFramedWrite = null;
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
            AssertCarriedFrame(session, Assert.IsType<byte[]>(firstFrame), Selected);
            AssertActionGroup(session, start, kind, Selected);
            int tickStart = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            Assert.Empty(Packets(session, tickStart).OfType<ServerboundSetCarriedItemPacket>());
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            session.BeforeFramedWrite = null;
            await DisconnectAndObserveEof(session, primaryFailure);
        }
    }

    [Theory]
    [InlineData("place", false)]
    [InlineData("place", true)]
    [InlineData("air", false)]
    [InlineData("air", true)]
    [InlineData("attack", false)]
    [InlineData("attack", true)]
    public async Task FailedCarriedWriteStopsTheActionWithTheSameValidPositiveControl(string kind, bool fault)
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        byte[]? firstFrame = null;
        var failure = new IOException("controlled immediate carried write before Advance");
        try
        {
            int start = session.ObservedOutbound.Count;
            session.BeforeFramedWrite = frame =>
            {
                if (firstFrame is not null)
                    return;
                firstFrame = frame.ToArray();
                if (fault)
                    throw failure;
            };
            Task action = StartAction(session, kind, session.Token);
            if (fault)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => action));
                session.BeforeFramedWrite = null;
                await session.Barrier();
                Assert.Empty(Packets(session, start));
                AssertCarriedFrame(session, Assert.IsType<byte[]>(firstFrame), Selected);
                await session.Tick();
                await session.Barrier();
                Assert.Equal(new[] { Selected }, Packets(session, start).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
            }
            else
            {
                await FinishAction(session, action, start);
                session.BeforeFramedWrite = null;
                AssertCarriedFrame(session, Assert.IsType<byte[]>(firstFrame), Selected);
                AssertActionGroup(session, start, kind, Selected);
            }
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            session.BeforeFramedWrite = null;
            await DisconnectAndObserveEof(session, primaryFailure);
        }
    }

    [Fact]
    public async Task QueuedOldSessionAttackCannotWriteThroughAReconnectedPeer()
    {
        var scheduler = new ChannelSessionScheduler();
        Session session = await Prepared(Protocol, Selected, scheduler: scheduler);
        Exception? primaryFailure = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? blocker = null;
        Task? disconnected = null;
        Task? action = null;
        try
        {
            blocker = scheduler.InvokeAsync((Func<ValueTask>)(async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(session.Token);
            }), session.Token);
            await entered.Task.WaitAsync(session.Token);
            action = StartAction(session, "attack", session.Token);
            FakeJavaServer oldPeer = session.Server;
            disconnected = session.Client.DisconnectAsync(session.Token);
            await PeerEof(oldPeer, session.Token);
            release.TrySetResult();
            await blocker.WaitAsync(session.Token);
            await disconnected.WaitAsync(session.Token);
            Exception? ended = await Record.ExceptionAsync(() => action.WaitAsync(session.Token));
            Assert.True(ended is OperationCanceledException or ConnectionClosedException, ended?.ToString());
            await session.Restart("lever");
            int start = session.ObservedOutbound.Count;
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            Assert.True(action.IsCompleted);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            release.TrySetResult();
            await DisconnectAndObserveEof(session, primaryFailure, [blocker, disconnected], scheduler);
        }
    }

    [Theory]
    [InlineData(754, false, false)]
    [InlineData(755, true, false)]
    [InlineData(766, true, false)]
    [InlineData(767, false, true)]
    public async Task AirUseFollowsTheNativePositionAndLookBoundary(int protocol, bool publishesPosition, bool carriesLook)
    {
        Session session = await Prepared(protocol, 0);
        Exception? primaryFailure = null;
        try
        {
            const float yaw = 37.25F;
            const float pitch = -18.5F;
            // This is the public local-look API on the existing owner; no tick, input packet, or cadence reset is introduced.
            await session.Client.PostAsync(client => client.Actions.Movement.SetRotation(yaw, pitch), session.Token);
            var pose = await session.Client.InvokeAsync(client =>
                (client.State.Self.Position, client.State.Self.Yaw, client.State.Self.Pitch, client.State.Self.OnGround), session.Token);
            Assert.Equal((new Vec3d(2.5, 65, 2.5), yaw, pitch), (pose.Position, pose.Yaw, pose.Pitch));
            int start = session.ObservedOutbound.Count;
            Task action = session.Client.Actions.Interaction.UseItemAsync(Hand.Main, session.Token);
            await FinishAction(session, action, start);
            IPacket[] packets = Packets(session, start);
            Assert.Empty(packets.OfType<ServerboundSetCarriedItemPacket>());
            Assert.Equal(publishesPosition ? 2 : 1, packets.Length);
            if (publishesPosition)
            {
                var position = Assert.IsType<ServerboundMovePlayerPosRotPacket>(packets[0]);
                Assert.Equal((pose.Position.X, pose.Position.Y, pose.Position.Z, yaw, pitch, pose.OnGround, false),
                    (position.X, position.Y, position.Z, position.Yaw, position.Pitch, position.OnGround, position.HorizontalCollision));
                byte[] expectedPosition =
                [
                    0x40, 0x04, 0, 0, 0, 0, 0, 0,
                    0x40, 0x50, 0x40, 0, 0, 0, 0, 0,
                    0x40, 0x04, 0, 0, 0, 0, 0, 0,
                    0x42, 0x15, 0, 0, 0xC1, 0x94, 0, 0,
                    (byte)(pose.OnGround ? 1 : 0),
                ];
                Assert.Equal(expectedPosition, BoundDescriptorCodec.EncodeServerbound(protocol, position));
            }
            var use = Assert.IsType<ServerboundUseItemPacket>(packets[^1]);
            int sequence = protocol >= 759 ? 1 : 0;
            Assert.Equal((0, sequence, carriesLook ? yaw : 0F, carriesLook ? pitch : 0F),
                (use.Hand, use.Sequence, use.YRot, use.XRot));
            byte[] expectedUse = protocol switch
            {
                754 or 755 => [0],
                766 => [0, 1],
                767 => [0, 1, 0x42, 0x15, 0, 0, 0xC1, 0x94, 0, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
            };
            Assert.Equal(expectedUse, BoundDescriptorCodec.EncodeServerbound(protocol, use));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    private static async Task DisabledInput(GameMode mode)
    {
        Session session = await Prepared(Protocol, Selected);
        Exception? primaryFailure = null;
        try
        {
            await session.Reconfigure([]);
            await Slot(session, Selected, "stone");
            var held = await session.Client.InvokeAsync(client =>
                (Slot: client.State.Self.HeldSlot, Available: client.State.Inventory.IsPlayerSlotAvailable(36 + Selected),
                    Stack: client.State.Inventory.GetSlot(36 + Selected), HasWorld: client.State.HasWorld), session.Token);
            Assert.Equal(Selected, held.Slot);
            Assert.True(held.Available);
            Assert.False(held.Stack.IsEmpty);
            Assert.Equal(Identifier.Minecraft("stone"), held.Stack.Item.Id);
            Assert.False(held.HasWorld);
            await session.Mode(mode, instantBuild: false);
            Assert.False(session.Client.State.EnabledFeatures.IsItemEnabled(Identifier.Minecraft("stone"), Protocol));
            int start = session.ObservedOutbound.Count;
            Assert.Equal(BlockUsePrediction.Rejected, await Preview(session));
            await session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, Cursor, ct: session.Token);
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            long beforeTick = session.Client.State.SessionTick;
            await session.Tick();
            await session.Barrier();
            Assert.Equal(beforeTick + 1, session.Client.State.SessionTick);
            Assert.Empty(Packets(session, start));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { await DisconnectAndObserveEof(session, primaryFailure); }
    }

    private static async Task<Session> Prepared(int protocol, int selected, string block = "lever", ISessionScheduler? scheduler = null)
    {
        Session? session = null;
        try
        {
            session = await Session.Create(protocol, block, observeWrites: true, scheduler: scheduler);
            await session.Mode(GameMode.Survival, instantBuild: false);
            await Slot(session, selected, "stone");
            if (selected != 0)
                await Known(session, new ClientboundSetHeldSlotPacket(selected), packet => Assert.Equal(selected, packet.Slot));
            Assert.Equal(selected, session.Client.State.Self.HeldSlot);
            return session;
        }
        catch (Exception error)
        {
            if (session is not null)
                await DisconnectAndObserveEof(session, error, externalScheduler: scheduler as IAsyncDisposable);
            else if (scheduler is IAsyncDisposable owned)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await owned.DisposeAsync().AsTask().WaitAsync(cleanup.Token); }
                catch (Exception disposal) { throw new AggregateException("Session setup and scheduler cleanup both failed.", error, disposal); }
            }
            throw;
        }
    }

    private static Task Slot(Session session, int selected, string name)
    {
        var stack = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
        short slot = checked((short)(36 + selected));
        return Known(session, new ClientboundContainerSetSlotPacket(0, 0, slot, stack, IsLegacy: session.Protocol == 47), packet =>
        {
            Assert.Equal(slot, packet.Slot);
            Assert.Equal(session.Protocol == 47, packet.IsLegacy);
            Assert.Equal(stack.Item.Id, packet.Item.Item.Id);
            Assert.Equal(1, packet.Item.Count);
            Assert.True(packet.Item.Components.HasNoPatch);
        });
    }

    private static Task StartAction(Session session, string kind, CancellationToken token)
        => kind switch
        {
            "place" => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, Cursor, ct: token),
            "air" => session.Client.Actions.Interaction.UseItemAsync(Hand.Main, token),
            "attack" => session.Client.Actions.Interaction.AttackEntityAsync(EntityId, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static async Task FinishAction(Session session, Task action, int start)
    {
        await FenceAndAcknowledge(session, start);
        await action.WaitAsync(session.Token);
    }

    private static async Task FenceAndAcknowledge(Session session, int start)
    {
        await session.Client.InvokeAsync(_ => true, session.Token);
        await session.Barrier();
        ServerboundUseItemOnPacket? use = Packets(session, start).OfType<ServerboundUseItemOnPacket>().SingleOrDefault();
        if (use is not null)
            await Known(session, new ClientboundBlockChangedAckPacket(use.Sequence), packet => Assert.Equal(use.Sequence, packet.Sequence));
    }

    private static Task<BlockUsePrediction> Preview(Session session)
        => session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, Cursor, Hand.Main), session.Token);

    private static IPacket[] Packets(Session session, int start)
        => session.ObservedOutbound.Skip(start).Where(packet => packet is not ServerboundPlayKeepAlivePacket).ToArray();

    private static void AssertOnlyCarried(Session session, int start, int slot)
    {
        var packet = Assert.IsType<ServerboundSetCarriedItemPacket>(Assert.Single(Packets(session, start)));
        Assert.Equal(slot, packet.Slot);
        Assert.Equal(new byte[] { 0, (byte)slot }, BoundDescriptorCodec.EncodeServerbound(session.Protocol, packet));
    }

    private static void AssertActionGroup(Session session, int start, string kind, int? carried)
    {
        IPacket[] packets = Packets(session, start);
        int offset = 0;
        if (carried is int slot)
        {
            Assert.Equal(slot, Assert.IsType<ServerboundSetCarriedItemPacket>(packets[offset++]).Slot);
            Assert.Equal(new byte[] { 0, (byte)slot }, BoundDescriptorCodec.EncodeServerbound(session.Protocol, packets[0]));
        }
        if (kind == "place")
        {
            Assert.Equal(2 + offset, packets.Length);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[offset++]);
            Assert.Equal((0, Session.Target, 1, .5F, 1F, .5F, false, false, 1),
                (use.Hand, use.Position, use.Face, use.CursorX, use.CursorY, use.CursorZ, use.Inside, use.WorldBorderHit, use.Sequence));
            var swing = Assert.IsType<ServerboundSwingPacket>(packets[offset]);
            Assert.Equal(0, swing.Hand);
            Assert.True(swing.HasHand);
        }
        else if (kind == "air")
        {
            Assert.Equal(1 + offset, packets.Length);
            var use = Assert.IsType<ServerboundUseItemPacket>(packets[offset]);
            Assert.Equal((0, 1, 0F, 0F), (use.Hand, use.Sequence, use.YRot, use.XRot));
        }
        else
        {
            Assert.Equal("attack", kind);
            Assert.Equal(1 + offset, packets.Length);
            var attack = Assert.IsType<ServerboundInteractPacket>(packets[offset]);
            Assert.Equal(EntityId, attack.EntityId);
            Assert.Equal(1, attack.Action);
            Assert.Null(attack.Hand);
            Assert.Null(attack.InteractAt);
            Assert.False(attack.UsingSecondaryAction);
        }
    }

    private static void AssertCarriedFrame(Session session, byte[] bytes, int slot)
    {
        Assert.True(JavaVersions.TryGetByProtocol(session.Protocol, out JavaVersion version));
        Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound)
            .TryGetOutbound(EntityPackets.Serverbound.SetCarriedItem, out int wireId, out BoundPacketCodec codec));
        Assert.InRange(wireId, 0, 127);
        Assert.Equal(new byte[] { 3, (byte)wireId, 0, (byte)slot }, bytes);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var decoded = Assert.IsType<ServerboundSetCarriedItemPacket>(codec.Decode(bytes.AsSpan(2), context));
        Assert.Equal(slot, decoded.Slot);
        Assert.Equal(bytes.AsSpan(2).ToArray(), Encode(codec, decoded, context));
    }

    private static async Task Known<TPacket>(Session session, TPacket packet, Action<TPacket> decodedAssertion)
        where TPacket : class, IPacket
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<TPacket>(codec.Decode(bytes, context));
        decodedAssertion(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
        await session.Barrier();
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static async Task PeerEof(FakeJavaServer server, CancellationToken token)
    {
        _ = await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(token);
        });
    }

    private static async Task DisconnectAndObserveEof(Session session, Exception? primaryFailure,
        Task?[]? preceding = null, IAsyncDisposable? externalScheduler = null)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        List<Exception> failures = [];
        foreach (Task? pending in preceding ?? [])
        {
            if (pending is null)
                continue;
            try { await pending.WaitAsync(cleanup.Token); }
            catch (Exception error) { failures.Add(error); }
        }
        try
        {
            await session.Client.DisconnectAsync(cleanup.Token);
            await PeerEof(session.Server, cleanup.Token);
        }
        catch (Exception error) { failures.Add(error); }
        try { await session.DisposeAsync().AsTask().WaitAsync(cleanup.Token); }
        catch (Exception error) { failures.Add(error); }
        if (externalScheduler is not null)
        {
            try { await externalScheduler.DisposeAsync().AsTask().WaitAsync(cleanup.Token); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 0)
            return;
        if (primaryFailure is not null)
            failures.Insert(0, primaryFailure);
        throw new AggregateException(primaryFailure is null ? "Test cleanup failed." : "Test and cleanup both failed.", failures);
    }
}
