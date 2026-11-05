using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
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

public sealed class CooldownSessionRuntimeTests
{
    public static IEnumerable<object[]> TickCases()
    {
        foreach (int protocol in new[] { 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777 })
            foreach (bool frozen in new[] { false, true })
                yield return [protocol, frozen];
    }

    [Theory]
    [MemberData(nameof(TickCases))]
    public async Task TheSessionPlayerTickExpiresAndRefreshesCooldownsWithoutPhysics(int protocol, bool frozen)
    {
        await using var session = await Session.Create(protocol);
        await session.Send(new ClientboundTickingStatePacket(5f, frozen));
        if (frozen)
            await session.Send(new ClientboundTickingStepPacket(1));
        await session.Cooldown(3);
        Assert.Single(await session.Use());
        await session.Tick();
        await session.Cooldown(2);
        await session.Tick();
        Assert.True(await session.Active());
        await session.Tick();
        Assert.False(await session.Active());
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
        await session.Cooldown(10);
        await session.Cooldown(1);
        await session.Tick();
        Assert.False(await session.Active());
        await session.Cooldown(100);
        await session.Cooldown(0);
        Assert.False(await session.Active());
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task MissingTerrainUsesTheNativePlayerTickBoundary(int protocol)
    {
        await using var session = await Session.Create(protocol, terrain: false);
        await session.Cooldown(3);
        for (int tick = 1; tick <= 62; tick++)
        {
            await session.Tick();
            bool expires = protocol == 768 ? tick >= 3 : protocol is >= 769 and <= 773 && tick >= 62;
            Assert.Equal(!expires, await session.Active());
        }
        await session.Terrain();
        for (int tick = 0; tick < 3; tick++)
            await session.Tick();
        Assert.False(await session.Active());
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task AnUnloadDoesNotUndoALatchedModernPlayerLoad(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.Tick();
        await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        await session.Cooldown(2);
        await session.Tick();
        await session.Tick();
        Assert.Equal(protocol == 767, await session.Active());
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task ABlockUpdateDoesNotEstablishNativePlayerTickTerrain(int protocol)
    {
        await using var session = await Session.Create(protocol, terrain: false);
        await session.SyntheticUpdate();
        Assert.True(session.Client.State.IsLocalChunkLoaded);
        await session.Cooldown(3);
        for (int tick = 0; tick < 3; tick++)
            await session.Tick();
        Assert.Equal(protocol != 768, await session.Active());
        await session.Terrain();
        for (int tick = 0; tick < 3; tick++)
            await session.Tick();
        Assert.False(await session.Active());
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task InventoryOnlyUsesReceivedChunkHeadersWithoutRetainingTerrain(int protocol)
    {
        await using var session = await Session.Create(protocol, terrainStorage: false);
        Assert.False(session.Client.State.HasWorld);
        Assert.NotNull(session.Client.State.WorldDimension);
        await session.Cooldown(3);
        for (int tick = 0; tick < 3; tick++)
            await session.Tick();
        Assert.False(await session.Active());
        await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        await session.Cooldown(2);
        await session.Tick();
        await session.Tick();
        Assert.Equal(protocol == 767, await session.Active());
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task InventoryOnlyRespawnCannotReuseEarlierPlayerReadiness(int protocol)
    {
        await using var session = await Session.Create(protocol, terrainStorage: false);
        await session.Tick();
        await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<Respawned>(_ => done.TrySetResult());
        await session.Send(new ClientboundRespawnPacket(Session.Spawn with { SeaLevel = protocol >= 768 ? 63 : 0 }, 0, null));
        await done.Task.WaitAsync(session.Token);
        Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        var item = session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_hoe")];
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(item, 1)));
        await session.Cooldown(3);
        for (int tick = 0; tick < 3; tick++)
            await session.Tick();
        Assert.Equal(protocol != 768, await session.Active());
        Assert.False(session.Client.State.HasWorld);
    }

    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    public async Task NativePlayerTicksExistBeforeTheInitialPositionPacket(int protocol)
    {
        await using var session = await Session.Create(protocol, terrain: false, position: false);
        await session.Cooldown(3);
        int count = protocol == 768 ? 3 : 62;
        for (int tick = 0; tick < count; tick++)
            await session.Tick();
        Assert.False(await session.Active());
    }

    [Theory]
    [InlineData(767, 0)]
    [InlineData(767, 3)]
    [InlineData(768, 0)]
    [InlineData(768, 3)]
    [InlineData(776, 0)]
    [InlineData(776, 3)]
    public async Task EveryRespawnBuildsANewNativeCooldownOwner(int protocol, byte keep)
    {
        await using var session = await Session.Create(protocol);
        await session.Cooldown(100);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<Respawned>(_ => done.TrySetResult());
        await session.Send(new ClientboundRespawnPacket(Session.Spawn with { SeaLevel = protocol >= 768 ? 63 : 0 }, keep, null));
        await done.Task.WaitAsync(session.Token);
        Assert.False(await session.Active());
        Assert.Empty(session.Client.State.Self.ItemCooldowns);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(776)]
    public async Task ConfigurationReentryDropsTheNativePlayerCooldownOwner(int protocol)
    {
        await using var session = await Session.Create(protocol);
        ItemStack held = session.Client.State.Inventory.GetSlot(36);
        await session.Cooldown(100);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<PhaseChanged>(change =>
        {
            if (change.Phase == ProtocolPhase.Configuration)
                done.TrySetResult();
        });
        await session.Send(new ClientboundStartConfigurationPacket(), barrier: false);
        await done.Task.WaitAsync(session.Token);
        Assert.False(session.Client.State.Self.IsItemOnCooldown(held, protocol));
        Assert.Empty(session.Client.State.Self.ItemCooldowns);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(776)]
    public async Task WorldStorageReplacementRetainsThePlayerButSessionEndClearsIt(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.Cooldown(100);
        await session.Client.InvokeAsync(client =>
        {
            WorldFactory.InstallWorld(client.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            return true;
        }, session.Token);
        Assert.True(await session.Active());
        await session.Client.DisconnectAsync(session.Token);
        Assert.Empty(session.Client.State.Self.ItemCooldowns);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly int _protocol;
        private readonly JavaVersion _version;
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTicks _ticks = new();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _barrier;
        public UmpkClient Client { get; }
        public CancellationToken Token => _budget.Token;
        public static CommonPlayerSpawnInfo Spawn { get; } = new(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        private BlockPos Target { get; } = new(1, 64, 1);

        private Session(int protocol, bool terrainStorage)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "CooldownProbe"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(_ticks)
                .ConfigureFeatures(features => { features.Terrain = terrainStorage; features.Physics = false; features.Pathfinding = false; features.Entities = false; })
                .Build();
        }

        public static async Task<Session> Create(int protocol, bool terrain = true, bool position = true, bool terrainStorage = true)
        {
            var session = new Session(protocol, terrainStorage);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session._version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable subscription = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                await session.Send(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, Spawn, false, false, null), barrier: false);
                await joined.Task.WaitAsync(session.Token);
                if (position)
                {
                    var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using IDisposable correction = session.Client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
                    await session.Send(new ClientboundPlayerPositionPacket(2.5, 65, 2.5, 0, 0, 0, 1, new PositionMoveRotation(new Vec3d(2.5, 65, 2.5), Vec3d.Zero, 0, 0)), barrier: false);
                    await corrected.Task.WaitAsync(session.Token);
                }
                await session.Barrier();
                var item = session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_hoe")];
                await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(item, 1)));
                if (terrain)
                    await session.Terrain();
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async Task Terrain()
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), Client.State.Registries, _protocol).SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], new byte[7])
                : _protocol >= 768 ? new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[7])
                : new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            var codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            var chunk = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
            Assert.True(chunk.FullChunk);
            Assert.Equal(0, chunk.ChunkX);
            Assert.Equal(0, chunk.ChunkZ);
            Assert.Equal(sections.Length, chunk.Column.SectionCount);
            Assert.Equal(0, chunk.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(chunk)));
            await Send(chunk);
            await SyntheticUpdate();
        }

        public Task SyntheticUpdate()
        {
            var dirt = Client.State.Registries!.Blocks[Identifier.Minecraft("dirt")];
            return Send(new ClientboundBlockUpdatePacket(Target, dirt.Value.DefaultStateId));
        }

        public Task Cooldown(int duration)
        {
            var item = Client.State.Inventory.GetSlot(36).Item;
            return Send(new ClientboundCooldownPacket(_protocol >= 768 ? item.Id : default, duration, _protocol < 768 ? item.NetworkId : null));
        }

        public Task Tick() => _ticks.Fire(Token);
        public Task<bool> Active() => Client.InvokeAsync(client => client.State.Self.IsItemOnCooldown(client.State.Inventory.GetSlot(36), _protocol), Token);

        public async Task<List<object>> Use()
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(Target, Direction.Up, new Vec3d(.5, 1, .5), ct: Token);
            List<object> sent = [];
            ServerboundUseItemOnPacket? use = null;
            while (use is null)
            {
                object? packet = await Next();
                if (packet is ServerboundUseItemOnPacket or ServerboundSwingPacket)
                    sent.Add(packet);
                use = packet as ServerboundUseItemOnPacket;
            }
            await Send(new ClientboundBlockChangedAckPacket(use.Sequence), barrier: false);
            await action.WaitAsync(Token);
            sent.AddRange(await Barrier());
            Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            if (sent.Count == 2)
                Assert.IsType<ServerboundSwingPacket>(sent[1]);
            return sent;
        }

        public async Task Send(IPacket packet, bool barrier = true)
        {
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, packet, context);
            object decoded = codec.Decode(bytes.WrittenSpan, context);
            if (packet is ClientboundCooldownPacket expected)
                Assert.Equal(expected, Assert.IsType<ClientboundCooldownPacket>(decoded));
            if (packet is ClientboundContainerSetSlotPacket slot)
                Assert.Equal(slot.Item, Assert.IsType<ClientboundContainerSetSlotPacket>(decoded).Item);
            if (packet is ClientboundRespawnPacket respawn)
                Assert.Equal(respawn, Assert.IsType<ClientboundRespawnPacket>(decoded));
            if (packet is ClientboundTickingStatePacket ticking)
                Assert.Equal(ticking, Assert.IsType<ClientboundTickingStatePacket>(decoded));
            if (packet is ClientboundTickingStepPacket step)
                Assert.Equal(step, Assert.IsType<ClientboundTickingStepPacket>(decoded));
            if (packet is ClientboundForgetLevelChunkPacket forget)
                Assert.Equal(forget, Assert.IsType<ClientboundForgetLevelChunkPacket>(decoded));
            if (packet is ClientboundPlayerPositionPacket position)
            {
                var observed = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
                Assert.Equal(new Vec3d(position.X, position.Y, position.Z), observed.ModernValues?.Position ?? new Vec3d(observed.X, observed.Y, observed.Z));
                Assert.Equal(position.TeleportId, observed.TeleportId);
            }
            var repeated = new ArrayBufferWriter<byte>();
            writer = new PacketWriter(repeated);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(bytes.WrittenSpan.ToArray(), repeated.WrittenSpan.ToArray());
            await _server.SendFrameAsync(pair.WireId, bytes.WrittenSpan.ToArray(), Token);
            if (barrier)
                _ = await Barrier();
        }

        private async Task<List<object>> Barrier()
        {
            long nonce = ++_barrier;
            await Send(new ClientboundPlayKeepAlivePacket(nonce), barrier: false);
            List<object> actions = [];
            while (true)
            {
                object? packet = await Next();
                if (packet is ServerboundPlayKeepAlivePacket reply && reply.Id == nonce)
                    return actions;
                if (packet is ServerboundUseItemOnPacket or ServerboundSwingPacket)
                    actions.Add(packet);
            }
        }

        private async Task<object?> Next()
        {
            InboundFrame frame = await _server.NextFrameAsync(Token);
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            if (codec.Type.Id != ItemPackets.Serverbound.UseItemOn.Id && codec.Type.Id != EntityPackets.Serverbound.Swing.Id && codec.Type.Id != PlayPackets.Serverbound.KeepAlive.Id)
                return null;
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            object packet = codec.Decode(frame.Payload, context);
            var repeated = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(repeated);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(frame.Payload.ToArray(), repeated.WrittenSpan.ToArray());
            return packet;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Client.DisposeAsync(); }
            finally { await _server.DisposeAsync(); _budget.Dispose(); }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        public async Task Fire(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }
}
