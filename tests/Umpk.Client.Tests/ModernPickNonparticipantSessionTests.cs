using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ModernPickNonparticipantSessionTests
{
    private static readonly string[] Excluded = ["area_effect_cloud", "block_display", "evoker_fangs", "eye_of_ender", "item_display", "lightning_bolt", "marker", "ominous_item_spawner", "text_display"];
    private static readonly string[] Unclassified = ["ender_dragon", "interaction", "shulker_bullet", "oak_boat", "unknown"];

    public static IEnumerable<object[]> Nonparticipants()
    {
        foreach (int protocol in Enumerable.Range(769, 9))
            foreach (string kind in Excluded)
                foreach (bool far in new[] { false, true })
                    foreach (bool held in new[] { false, true })
                        yield return [protocol, kind, far, held];
    }

    [Theory]
    [MemberData(nameof(Nonparticipants))]
    public async Task ProvenNativeNonparticipantsLeaveViewMiningActive(int protocol, string kind, bool far, bool held)
    {
        await using Session session = await Session.Create(protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Mine(caller.Token);
                await session.Queued();
                AssertStarted(await session.Tick(), protocol);
            }
            await session.Spawn(kind, far);
            mining ??= session.Mine(caller.Token);
            await session.Queued();
            List<IPacket> next = await session.Tick();
            if (held)
                AssertContinued(next, protocol);
            else
                AssertStarted(next, protocol);
            AssertContinued(await session.Tick(), protocol);
            Assert.False(mining.IsCompleted);
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
            Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> ConservativeCases()
    {
        foreach (int protocol in new[] { 769, 777 })
            foreach (string kind in Unclassified)
                foreach (bool far in new[] { false, true })
                    foreach (bool held in new[] { false, true })
                        yield return [protocol, kind, far, held];
        foreach (string kind in Excluded)
            foreach (bool held in new[] { false, true })
                yield return [768, kind, false, held];
    }

    [Theory]
    [MemberData(nameof(ConservativeCases))]
    public async Task UnclassifiedParticipantsAndEarlierErasStayConservative(int protocol, string kind, bool far, bool held)
    {
        await using Session session = await Session.Create(protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Mine(caller.Token);
                await session.Queued();
                AssertStarted(await session.Tick(), protocol);
            }
            await session.Spawn(kind, far);
            mining ??= session.Mine(caller.Token);
            await session.Queued();
            List<IPacket> packets = await session.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            if (held)
                AssertAbort(packets, protocol);
            else
                Assert.Empty(packets);
            Assert.Empty(await session.Tick());
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null && !mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> ExplicitCases()
    {
        foreach (int protocol in new[] { 769, 777 })
            foreach (string kind in Unclassified)
                yield return [protocol, kind];
    }

    [Theory]
    [MemberData(nameof(ExplicitCases))]
    public async Task ExplicitCoordinatesKeepTheirExistingContract(int protocol, string kind)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn(kind, far: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick(), protocol);
            AssertContinued(await session.Tick(), protocol);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> ReplacementCases()
    {
        foreach (int protocol in new[] { 769, 777 })
            foreach (string kind in Excluded)
                yield return [protocol, kind];
    }

    [Theory]
    [MemberData(nameof(ReplacementCases))]
    public async Task ReplacementIdRechecksItsNewNativeParticipant(int protocol, string kind)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn(kind, far: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick(), protocol);
            await session.Spawn("interaction", far: false);
            AssertAbort(await session.Tick(), protocol);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            Assert.Empty(await session.Tick());
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task SessionTeardownCancelsHeldViewMiningAndClosesThePeer(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("item", far: false);
        Task mining = session.Mine(session.Token);
        await session.Queued();
        AssertStarted(await session.Tick(), protocol);
        await session.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        Assert.True(session.Ticks.SessionCancelled);
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(777, false)]
    [InlineData(769, true)]
    [InlineData(777, true)]
    public async Task MissingTerrainCannotBecomeClearFromAnEntityExclusion(int protocol, bool held)
    {
        await using Session session = await Session.Create(protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            await session.Spawn("area_effect_cloud", far: false);
            if (held)
            {
                mining = session.Mine(caller.Token);
                await session.Queued();
                AssertStarted(await session.Tick(), protocol);
            }
            await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
            Assert.Empty(await session.Barrier());
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            mining ??= session.Mine(caller.Token);
            await session.Queued();
            List<IPacket> packets = await session.Tick();
            if (held)
            {
                InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(session.Token));
                Assert.Equal("Mining requires the target's loaded terrain.", error.Message);
                AssertAbort(packets, protocol);
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
                Assert.Empty(packets);
            }
        }
        finally
        {
            caller.Cancel();
            if (mining is not null && !mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    private static void AssertStarted(IReadOnlyList<IPacket> packets, int protocol)
    {
        Assert.Equal(3, packets.Count);
        var start = Assert.IsType<ServerboundPlayerActionPacket>(packets[0]);
        Assert.Equal(0, start.Action);
        Assert.Equal(Session.Target, start.Position);
        Assert.Equal((byte)Direction.North, start.Direction);
        Assert.True(start.Sequence > 0);
        AssertSwing(packets[1], protocol);
        AssertSwing(packets[2], protocol);
    }

    private static void AssertContinued(IReadOnlyList<IPacket> packets, int protocol)
        => AssertSwing(Assert.Single(packets), protocol);

    private static void AssertAbort(IReadOnlyList<IPacket> packets, int protocol)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
        Assert.Equal(protocol == 777 ? 2 : 1, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.Down, action.Direction);
    }

    private static void AssertSwing(IPacket packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
    }

    private sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(0, 65, 4);
        private readonly int _protocol;
        private readonly JavaVersion _version;
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _barrier;
        private bool _closed;
        internal ObservedTicks Ticks { get; } = new();
        internal UmpkClient Client { get; }
        internal CancellationToken Token => _budget.Token;
        private PacketCodecContext Context => new(Client.State.Registries!, IConnectionCodecState.Empty);

        private Session(int protocol)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "PickExclusion"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = true; features.Pathfinding = false; features.Entities = true; features.Terrain = true; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol)
        {
            var session = new Session(protocol);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session._version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable join = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
                await session.Send(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null));
                await joined.Task.WaitAsync(session.Token);
                var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable correction = session.Client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
                var position = new Vec3d(.5, 64, .5);
                await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
                await corrected.Task.WaitAsync(session.Token);
                await session.Terrain();
                int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
                await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(0, 63, 0), stone));
                await session.Send(new ClientboundBlockUpdatePacket(Target, stone));
                Assert.Empty(await session.Barrier());
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
                Assert.True(session.Client.State.Self.OnGround);
                Assert.Equal(position, session.Client.State.Self.Position);
                Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal Task Mine(CancellationToken token) => Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: token);
        internal Task Queued() => Client.InvokeAsync(_ => true, Token);

        internal async Task<List<IPacket>> Tick()
        {
            long before = Client.State.SessionTick;
            await Ticks.Fire(Token);
            List<IPacket> packets = await Barrier();
            Assert.Equal(before + 1, Client.State.SessionTick);
            return packets;
        }

        internal async Task Spawn(string kind, bool far)
        {
            int type = kind == "unknown" ? 1000000 : Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId;
            Vec3d position = far ? new(8.5, 64, 8.5) : new(.5, 65, 2.5);
            await Send(new ClientboundAddEntityPacket(42, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
            Assert.Empty(await Barrier());
            await Client.InvokeAsync(client =>
            {
                var actor = Assert.IsType<Umpk.Game.Entities.Entity>(client.State.Entities.Get(42));
                Assert.Equal(Identifier.Minecraft(kind), actor.Type.Id);
                Assert.Equal(position, actor.Position);
                return true;
            }, Token);
        }

        private async Task Terrain()
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, Client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], new byte[7])
                : new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(sections.Length, packet.Column.SectionCount);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Encode(codec, packet));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Send(packet);
            Assert.Empty(await Barrier());
            Assert.True(Client.State.EntityMembership.HasColumn(Target));
        }

        internal async Task Send(IPacket packet)
        {
            PhaseRegistry registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            byte[] bytes = Encode(codec, packet);
            object decoded = codec.Decode(bytes, Context);
            switch (packet)
            {
                case ClientboundAddEntityPacket add:
                    var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                    Assert.Equal(add.EntityId, observed.EntityId);
                    Assert.Equal(add.TypeId, observed.TypeId);
                    Assert.Equal(new Vec3d(add.X, add.Y, add.Z), new Vec3d(observed.X, observed.Y, observed.Z));
                    break;
                case ClientboundLoginPacket login:
                    Assert.Equal(login.PlayerId, Assert.IsType<ClientboundLoginPacket>(decoded).PlayerId);
                    break;
                case ClientboundPlayerPositionPacket correction:
                    Assert.Equal(correction.ModernValues, Assert.IsType<ClientboundPlayerPositionPacket>(decoded).ModernValues);
                    break;
                case ClientboundBlockUpdatePacket block:
                    Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                    break;
                case ClientboundForgetLevelChunkPacket forget:
                    Assert.Equal(forget, Assert.IsType<ClientboundForgetLevelChunkPacket>(decoded));
                    break;
                case ClientboundPlayKeepAlivePacket keepAlive:
                    Assert.Equal(keepAlive, Assert.IsType<ClientboundPlayKeepAlivePacket>(decoded));
                    break;
            }
            Assert.Equal(bytes, Encode(codec, decoded));
            await _server.SendFrameAsync(pair.WireId, bytes, Token);
        }

        private byte[] Encode(BoundPacketCodec codec, object packet)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, Context);
            return output.WrittenSpan.ToArray();
        }

        internal async Task<List<IPacket>> Barrier()
        {
            long nonce = ++_barrier;
            await Send(new ClientboundPlayKeepAlivePacket(nonce));
            List<IPacket> packets = [];
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet));
                if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == nonce)
                    return packets;
                if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket)
                    packets.Add(packet);
            }
        }

        internal async Task Close()
        {
            if (_closed)
                return;
            await Client.DisconnectAsync(Token);
            await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            {
                while (true)
                    await _server.NextFrameAsync(Token);
            });
            Assert.True(Ticks.SessionCancelled);
            _closed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Close(); }
            finally
            {
                try { await Client.DisposeAsync(); }
                finally { await _server.DisposeAsync(); _budget.Dispose(); }
            }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        internal bool SessionCancelled { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal async Task Fire(CancellationToken ct)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completion, ct);
            await completion.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            try
            {
                await foreach (TaskCompletionSource completion in _ticks.Reader.ReadAllAsync(ct))
                {
                    yield return ++sequence;
                    completion.TrySetResult();
                }
            }
            finally { SessionCancelled = ct.IsCancellationRequested; }
        }
    }
}
