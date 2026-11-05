using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

/// <summary>Framed session witnesses for the native game-mode carried-index cache. These do not prove immediate use/attack entry-point synchronization or execute a native client.</summary>
public sealed class CarriedItemSynchronizationSessionTests
{
    private const int Selected = 5;
    private static readonly CommonPlayerSpawnInfo Spawn = new(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);

    [Theory]
    [InlineData(47)]
    [InlineData(315)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(765)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task ServerSelectionSynchronizesOnTheGameModeTickOnlyOnce(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone");
        try
        {
            int start = session.ObservedOutbound.Count;
            await SelectedByServer(session, Selected);
            Assert.Empty(SelectionPackets(session, start));
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task ExplicitSelectionDoesNotSendADuplicateOnTheNextTick()
    {
        await using Session session = await Session.Create(770, "stone");
        try
        {
            int start = session.ObservedOutbound.Count;
            await session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token);
            await session.Barrier();
            AssertSlots(session, start, Selected);
            Assert.Equal(Selected, session.Client.State.Self.HeldSlot);
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task RespawnRetainsTheControllerIndexAndSynchronizesTheNewPlayersZero()
    {
        await using Session session = await Session.Create(770, "stone");
        try
        {
            await session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token);
            await session.Barrier();
            await SendKnown(session, new ClientboundRespawnPacket(Spawn, 3, null), actual => Assert.Equal(new ClientboundRespawnPacket(Spawn, 3, null), actual));
            Assert.Equal(0, session.Client.State.Self.HeldSlot);
            int start = session.ObservedOutbound.Count;
            // The new local player has no position correction or player-loaded readiness yet. Game-mode tick still runs.
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, 0);
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, 0);
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task RepeatedJoinCreatesANewControllerEvenWhenThePlayerIsRetained()
    {
        await using Session session = await Session.Create(770, "stone");
        try
        {
            await session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token);
            await session.Barrier();
            int start = session.ObservedOutbound.Count;
            // RejoinWorld sends a real Join and fires exactly one session tick after its position and terrain frames.
            await session.RejoinWorld("stone");
            Assert.Equal(Selected, session.Client.State.Self.HeldSlot);
            AssertSlots(session, start, Selected);
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task ReconnectStartsANewZeroControllerWithoutSendingTheOldSelection()
    {
        await using Session session = await Session.Create(770, "stone");
        bool disconnected = false;
        try
        {
            await session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token);
            await session.Barrier();
            await DisconnectAndObserveEof(session);
            disconnected = true;
            int start = session.ObservedOutbound.Count;
            disconnected = false;
            await session.Restart("stone");
            Assert.Equal(0, session.Client.State.Self.HeldSlot);
            Assert.Empty(SelectionPackets(session, start));
            await session.Tick();
            await session.Barrier();
            Assert.Empty(SelectionPackets(session, start));
        }
        finally
        {
            if (!disconnected)
                await DisconnectAndObserveEof(session);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GameModeSelectionDoesNotRequireTerrainPhysicsOrRunningWorldTicks(bool frozen)
    {
        await using Session session = await Session.Create(770, "stone", terrain: false, physics: false);
        try
        {
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            Assert.False(session.Client.State.Features.Physics);
            await SendKnown(session, new ClientboundTickingStatePacket(5f, frozen), actual => Assert.Equal(new ClientboundTickingStatePacket(5f, frozen), actual));
            await SelectedByServer(session, Selected);
            int start = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task CanceledExplicitSelectionDoesNotChangeTheIndexOrTheWire()
    {
        await using Session session = await Session.Create(770, "stone");
        try
        {
            int start = session.ObservedOutbound.Count;
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, canceled.Token));
            Assert.Equal(0, session.Client.State.Self.HeldSlot);
            await session.Tick();
            await session.Barrier();
            Assert.Empty(SelectionPackets(session, start));
        }
        finally
        {
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task CancellationAtAnAdmittedWriteFinishesItsCapturedSelection()
    {
        await using Session session = await Session.Create(770, "stone", observeWrites: true);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        try
        {
            int start = session.ObservedOutbound.Count;
            bool enteredWrite = false;
            session.BeforeClientWrite = () =>
            {
                if (enteredWrite)
                    return;
                enteredWrite = true;
                caller.Cancel();
            };
            Task action = session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            session.BeforeClientWrite = null;
            // Queue cancellation and completed-write notification can race. The wire and owner state must agree in either ordering.
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
            Assert.True(enteredWrite);
            await session.Barrier();
            AssertSlots(session, start, Selected);
            Assert.Equal(Selected, session.Client.State.Self.HeldSlot);
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            session.BeforeClientWrite = null;
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task FailedExplicitWriteDoesNotClaimTheSelectionWasSent()
    {
        await using Session session = await Session.Create(770, "stone", observeWrites: true);
        try
        {
            int start = session.ObservedOutbound.Count;
            var failure = new IOException("controlled carried-item pre-Advance write failure");
            session.BeforeClientWrite = () => throw failure;
            IOException observed = await Assert.ThrowsAsync<IOException>(() => session.Client.Actions.Inventory.SelectHeldSlotAsync(Selected, session.Token));
            session.BeforeClientWrite = null;
            Assert.Same(failure, observed);
            Assert.Equal(0, session.Client.State.Self.HeldSlot);
            await SelectedByServer(session, Selected);
            Assert.Empty(SelectionPackets(session, start));
            await session.Tick();
            await session.Barrier();
            AssertSlots(session, start, Selected);
        }
        finally
        {
            session.BeforeClientWrite = null;
            await DisconnectAndObserveEof(session);
        }
    }

    [Fact]
    public async Task GameModeSelectionPrecedesHeldMiningAndTheTickEndMarker()
    {
        await using Session session = await Session.Create(770, "stone");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? action = null;
        try
        {
            await SelectedByServer(session, Selected);
            int start = session.ObservedOutbound.Count;
            action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.Up, caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            await session.Tick();
            await session.Barrier();
            IPacket[] packets = session.ObservedOutbound.Skip(start).Where(packet => packet is not ServerboundPlayKeepAlivePacket).ToArray();
            Assert.Collection(packets,
                packet => Assert.Equal(Selected, Assert.IsType<ServerboundSetCarriedItemPacket>(packet).Slot),
                packet =>
                {
                    var mining = Assert.IsType<ServerboundPlayerActionPacket>(packet);
                    Assert.Equal(0, mining.Action);
                    Assert.Equal(Session.Target, mining.Position);
                    Assert.Equal((byte)Direction.Up, mining.Direction);
                    Assert.Equal(1, mining.Sequence);
                },
                AssertMainSwing,
                AssertMainSwing,
                packet => Assert.IsType<ServerboundClientTickEndPacket>(packet));
            Assert.False(action.IsCompleted);
        }
        finally
        {
            try
            {
                await caller.CancelAsync();
                if (action is not null)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
                if (!session.Token.IsCancellationRequested)
                {
                    await session.Tick();
                    await session.Barrier();
                }
            }
            finally
            {
                await DisconnectAndObserveEof(session);
            }
        }
    }

    private static void AssertMainSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("expected")]
    [InlineData("unexpected")]
    public async Task AutomaticSynchronizationRunsMiningOnlyAfterSuccessfulWrite(string sendFault)
    {
        const int protocol = 770;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken token = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        await using FakeJavaServer server = FakeJavaServer.Create();
        var pipe = new FaultOncePipe(server.ClientPipe);
        var ticks = new FaultSafeTicks();
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "CarriedFault"))
            .UseConnectionFactory(new PipeFactory(pipe))
            .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(ticks)
            .ConfigureFeatures(features => { features.Physics = false; features.Pathfinding = false; }).Build();
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downstream = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStatus = new TaskCompletionSource<ClientStatusChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wireFrames = new List<(IPacket Packet, byte[] Payload)>();
        int disconnections = 0;
        bool watch = false;
        using IDisposable ended = client.Events.Subscribe<Disconnected>(message =>
        {
            Interlocked.Increment(ref disconnections);
            disconnected.TrySetResult(message.Info);
        });
        void Observe(PacketObservation frame)
        {
            if (!watch || frame.Flow != PacketFlow.Serverbound || frame.Phase != ProtocolPhase.Play
                || frame.DecodedPacket is not IPacket packet)
                return;
            if (packet is not ServerboundPlayKeepAlivePacket)
                wireFrames.Add((packet, frame.CopyPayload()));
            if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundClientTickEndPacket)
                downstream.TrySetResult(packet);
        }
        void StatusChanged(object? sender, ClientStatusChangedEventArgs status)
        {
            if (status.Current == ClientStatus.Disconnected)
                terminalStatus.TrySetResult(status);
        }
        client.PacketFrameObserved += Observe;
        client.StatusChanged += StatusChanged;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? mining = null;
        try
        {
            await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), token), ScriptedServer.DriveToPlayAsync(server, version, token));
            var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable join = client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
            await SendFaultSession(client, server, new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, Spawn, false, false, null), packet =>
            {
                Assert.Equal(1, packet.PlayerId);
                Assert.Equal(Spawn, packet.SpawnInfo);
            }, token);
            await joined.Task.WaitAsync(token);
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, (client.State.World.Dimension.MaxY - client.State.World.Dimension.MinY) / 16)
                .Select(_ => new ChunkSectionWire(0, null, empty, empty)).ToArray();
            byte[] chunkBytes = ChunkCodecs.EncodeStructural(new ModernChunkWireBody(0, 0, false, [], sections, [], new byte[7]));
            BoundPacketCodec chunkCodec = BoundDescriptorCodec.Clientbound(protocol, "level_chunk_with_light");
            var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
            var chunk = Assert.IsType<ClientboundLevelChunkPacket>(chunkCodec.Decode(chunkBytes, context));
            Assert.True(chunk.FullChunk);
            Assert.Equal((0, 0), (chunk.ChunkX, chunk.ChunkZ));
            Assert.Equal(0, chunk.Column.GetBlockStateId(Session.Target.X, Session.Target.Y, Session.Target.Z));
            Assert.Equal(chunkBytes, Encode(chunkCodec, chunk, context));
            await server.SendFrameAsync(chunkCodec.WireId, chunkBytes, token);
            int stone = client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
            await SendFaultSession(client, server, new ClientboundBlockUpdatePacket(Session.Target, stone), packet => Assert.Equal((Session.Target, stone), (packet.Position, packet.BlockStateId)), token);
            var position = new Vec3d(2.5, 65, 2.5);
            await SendFaultSession(client, server, new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)), packet =>
            {
                Assert.Equal(new PositionMoveRotation(position, Vec3d.Zero, 0, 0), packet.ModernValues);
                Assert.Equal(0, packet.RelativeFlags);
                Assert.Equal(1, packet.TeleportId);
            }, token);
            await SendFaultSession(client, server, new ClientboundPlayerAbilitiesPacket(0, .05f, .1f), packet => Assert.Equal(new ClientboundPlayerAbilitiesPacket(0, .05f, .1f), packet), token);
            await SendFaultSession(client, server, new ClientboundSetHealthPacket(20, 20, 5), packet => Assert.Equal(new ClientboundSetHealthPacket(20, 20, 5), packet), token);
            await FenceFaultSession(client, server, 0x5e1ec6, token);
            Assert.Equal(position, client.State.Self.Position);
            Assert.True(client.State.Self.HasSpawned);
            // This identical setup tick establishes player-loaded readiness in all three rows before the selected-hand action phase.
            await ticks.Fire(token);
            await FenceFaultSession(client, server, 0x5e1ec7, token);
            await SendFaultSession(client, server, new ClientboundSetHeldSlotPacket(Selected), packet => Assert.Equal(Selected, packet.Slot), token);
            await FenceFaultSession(client, server, 0x5e1ec8, token);
            Assert.Equal(Selected, client.State.Self.HeldSlot);
            Assert.True(client.State.EntityMembership.HasColumn(Session.Target));
            Assert.Equal(stone, client.State.World.GetBlock(Session.Target).StateId);
            mining = client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.Up, caller.Token);
            await client.InvokeAsync(_ => true, token);
            Assert.False(mining.IsCompleted);
            Exception? failure = sendFault switch
            {
                "none" => null,
                "expected" => new ConnectionClosedException(CloseReason.SocketEof, "controlled expected carried-item teardown"),
                "unexpected" => new IOException("controlled unexpected carried-item pre-Advance failure"),
                _ => throw new ArgumentOutOfRangeException(nameof(sendFault)),
            };
            pipe.NextFailure = failure;
            watch = true;
            Task fired = ticks.Fire(token);
            if (sendFault == "none")
            {
                await fired.WaitAsync(token);
                await FenceFaultSession(client, server, 0x5e1ec9, token);
                Assert.Collection(wireFrames.Select(frame => frame.Packet),
                    packet => Assert.Equal(Selected, Assert.IsType<ServerboundSetCarriedItemPacket>(packet).Slot),
                    packet =>
                    {
                        var start = Assert.IsType<ServerboundPlayerActionPacket>(packet);
                        Assert.Equal(0, start.Action);
                        Assert.Equal(Session.Target, start.Position);
                        Assert.Equal((byte)Direction.Up, start.Direction);
                        Assert.Equal(1, start.Sequence);
                    },
                    AssertMainSwing,
                    AssertMainSwing,
                    packet => Assert.IsType<ServerboundClientTickEndPacket>(packet));
                foreach ((IPacket packet, byte[] payload) in wireFrames)
                    Assert.Equal(payload, BoundDescriptorCodec.EncodeServerbound(protocol, packet));
                Assert.Equal(new byte[] { 0, Selected }, wireFrames[0].Payload);
                Assert.Equal(0, pipe.FailedWrites);
                Assert.Null(pipe.FailedFrame);
                Assert.False(disconnected.Task.IsCompleted);
                Assert.False(terminalStatus.Task.IsCompleted);
                Assert.Equal(ClientStatus.Playing, client.Status);
                Assert.False(mining.IsCompleted);
                return;
            }
            if (sendFault == "expected")
            {
                await fired.WaitAsync(token);
                await FenceFaultSession(client, server, 0x5e1ec9, token);
                Assert.False(downstream.Task.IsCompleted, "A failed carried phase must end the tick before mining, swings, or tick_end.");
                Assert.Empty(wireFrames);
                Assert.False(disconnected.Task.IsCompleted);
                Assert.False(terminalStatus.Task.IsCompleted);
                Assert.Equal(ClientStatus.Playing, client.Status);
                Assert.False(mining.IsCompleted);
            }
            else
            {
                Task winner = await Task.WhenAny(disconnected.Task, downstream.Task).WaitAsync(token);
                Assert.Same(disconnected.Task, winner);
                DisconnectInfo info = await disconnected.Task.WaitAsync(token);
                await fired.WaitAsync(token);
                ClientStatusChangedEventArgs terminal = await terminalStatus.Task.WaitAsync(token);
                Assert.Equal(ClientStatus.Disconnected, terminal.Current);
                Assert.Equal(info, terminal.Disconnect);
                Assert.Equal(CloseReason.Local, info.Reason);
                Assert.Same(failure, info.Fault);
                Assert.Equal(1, Volatile.Read(ref disconnections));
                Assert.Equal(ClientStatus.Disconnected, client.Status);
                Assert.False(downstream.Task.IsCompleted);
                Assert.Empty(wireFrames);
            }
            Assert.Equal(1, pipe.FailedWrites);
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound)
                .TryGetOutbound(EntityPackets.Serverbound.SetCarriedItem, out int wireId, out BoundPacketCodec selectedCodec));
            Assert.InRange(wireId, 0, 127);
            Assert.Equal(new byte[] { 3, (byte)wireId, 0, Selected }, pipe.FailedFrame);
            var selected = Assert.IsType<ServerboundSetCarriedItemPacket>(selectedCodec.Decode(pipe.FailedFrame!.AsSpan(2), context));
            Assert.Equal(Selected, selected.Slot);
            Assert.Equal(new byte[] { 0, Selected }, Encode(selectedCodec, selected, context));
        }
        finally
        {
            watch = false;
            pipe.NextFailure = null;
            client.PacketFrameObserved -= Observe;
            client.StatusChanged -= StatusChanged;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await caller.CancelAsync();
                if (mining is not null)
                {
                    try { await mining.WaitAsync(cleanup.Token); }
                    catch (OperationCanceledException) when (mining.IsCanceled) { }
                }
            }
            finally
            {
                await DisconnectAndObserveEof(client, server);
            }
        }
    }

    private static async Task SendFaultSession<TPacket>(UmpkClient client, FakeJavaServer server, TPacket packet, Action<TPacket> assertDecoded, CancellationToken token)
        where TPacket : class, IPacket
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(770, packet.Type.Id.Path);
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<TPacket>(codec.Decode(bytes, context));
        assertDecoded(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await server.SendFrameAsync(codec.WireId, bytes, token);
    }

    private static async Task FenceFaultSession(UmpkClient client, FakeJavaServer server, long nonce, CancellationToken token)
    {
        await SendFaultSession(client, server, new ClientboundPlayKeepAlivePacket(nonce), packet => Assert.Equal(nonce, packet.Id), token);
        Assert.True(JavaVersions.TryGetByProtocol(770, out JavaVersion version));
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        while (true)
        {
            InboundFrame frame = await server.NextFrameAsync(token);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object packet = codec.Decode(frame.Payload, context);
            Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet, context));
            if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == nonce)
                return;
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class FaultSafeTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        public async Task Fire(CancellationToken token)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, token);
            await completed.Task.WaitAsync(token);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken token)
        {
            long tick = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(token))
            {
                try { yield return ++tick; }
                finally { completed.TrySetResult(); }
            }
        }
    }

    private sealed class FaultOncePipe : IDuplexPipe
    {
        internal Exception? NextFailure { get; set; }
        internal byte[]? FailedFrame { get; private set; }
        internal int FailedWrites { get; private set; }
        public PipeReader Input { get; }
        public PipeWriter Output { get; }
        internal FaultOncePipe(IDuplexPipe inner)
        {
            Input = inner.Input;
            Output = new Writer(inner.Output, this);
        }
        private sealed class Writer(PipeWriter inner, FaultOncePipe owner) : PipeWriter
        {
            private Memory<byte> _buffer;
            public override bool CanGetUnflushedBytes => inner.CanGetUnflushedBytes;
            public override long UnflushedBytes => inner.UnflushedBytes;
            public override void Advance(int bytes)
            {
                if (owner.NextFailure is { } failure)
                {
                    owner.NextFailure = null;
                    owner.FailedFrame = _buffer[..bytes].ToArray();
                    owner.FailedWrites++;
                    throw failure;
                }
                inner.Advance(bytes);
            }
            public override Memory<byte> GetMemory(int sizeHint = 0) => _buffer = inner.GetMemory(sizeHint);
            public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
            public override void CancelPendingFlush() => inner.CancelPendingFlush();
            public override void Complete(Exception? exception = null) => inner.Complete(exception);
            public override ValueTask CompleteAsync(Exception? exception = null) => inner.CompleteAsync(exception);
            public override ValueTask<FlushResult> FlushAsync(CancellationToken token = default) => inner.FlushAsync(token);
        }
    }

    private static Task SelectedByServer(Session session, int slot)
        => SendKnown(session, new ClientboundSetHeldSlotPacket(slot), actual => Assert.Equal(slot, actual.Slot));

    private static async Task SendKnown<TPacket>(Session session, TPacket packet, Action<TPacket> assertDecoded)
        where TPacket : class, IPacket
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<TPacket>(codec.Decode(bytes, context));
        assertDecoded(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
        await session.Barrier();
        if (packet is ClientboundSetHeldSlotPacket held)
            Assert.Equal(held.Slot, session.Client.State.Self.HeldSlot);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static ServerboundSetCarriedItemPacket[] SelectionPackets(Session session, int start)
        => session.ObservedOutbound.Skip(start).OfType<ServerboundSetCarriedItemPacket>().ToArray();

    private static void AssertSlots(Session session, int start, params int[] slots)
    {
        ServerboundSetCarriedItemPacket[] packets = SelectionPackets(session, start);
        Assert.Equal(slots, packets.Select(packet => (int)packet.Slot).ToArray());
        foreach (ServerboundSetCarriedItemPacket packet in packets)
            Assert.Equal(new byte[] { 0, (byte)packet.Slot }, BoundDescriptorCodec.EncodeServerbound(session.Protocol, packet));
    }

    private static Task DisconnectAndObserveEof(Session session)
        => DisconnectAndObserveEof(session.Client, session.Server);

    private static async Task DisconnectAndObserveEof(UmpkClient client, Umpk.TestKit.Server.FakeJavaServer server)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.DisconnectAsync(cleanup.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(cleanup.Token);
        });
    }
}
