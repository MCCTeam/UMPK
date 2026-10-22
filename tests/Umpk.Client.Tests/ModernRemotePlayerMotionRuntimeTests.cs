using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
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

public sealed class ModernRemotePlayerMotionRuntimeTests
{
    public static IEnumerable<object[]> Protocols() => Enumerable.Range(769, 9).Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task FramedMovementAdvancesCurrentBoundsOnlyOnSessionTicks(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.Move();
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        int length = protocol == 777 ? 2 : 3;
        for (int tick = 1; tick <= length; tick++)
        {
            await session.Tick();
            double expected = .5 + (double)tick / length;
            Assert.Equal(new Vec3d(expected, 65, .5), await session.Position());
            Aabb bounds = await session.Bounds();
            Assert.Equal(expected - (double)(.6f / 2f), bounds.MinX);
            Assert.Equal(expected + (double)(.6f / 2f), bounds.MaxX);
            Assert.Equal(65d + (double)1.8f, bounds.MaxY);
        }
        await session.Tick();
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.Position());
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task PlacementReadsTheAnimatedPlayerBounds(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer(z: 1.5);
        await session.Move();
        Assert.Equal(new Vec3d(.5, 65, 1.5), await session.Position());
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
        await session.Tick();
        Assert.Single(await session.Use());
        Assert.True((await session.Bounds()).Intersects(Aabb.BlockAt(1, 65, 1)));
    }

    public static IEnumerable<object[]> CollisionCases()
    {
        foreach (int protocol in Enumerable.Range(770, 8))
            foreach (string scenario in new[] { "empty", "stone", "cow", "shulker", "border" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(CollisionCases))]
    public async Task FarSyncRetainsPendingTargetAndUsesActualCollisionContext(int protocol, string scenario)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        int queryX = protocol == 770 ? 202 : 101;
        int queryY = protocol == 770 ? 130 : 65;
        await session.Terrain(queryX >> 4);
        if (scenario == "stone")
            await session.Block(new BlockPos(queryX, queryY, 0), "stone");
        else if (scenario is "cow" or "shulker")
            await session.SpawnActor(43, scenario, new Vec3d(queryX + .5, queryY, .5));
        else if (scenario == "border")
            await session.Send(new ClientboundInitializeBorderPacket(96, 0, 10, 10, 0, 29999984, 5, 15));
        await session.Move();
        await session.Sync(new Vec3d(100.5, 65, .5));
        Assert.Equal(new Vec3d(100.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(100.5, 65, .5), await session.PacketBase());
        double[] expected = scenario is "empty" or "cow"
            ? protocol == 777 ? [101, 101.5, 101.5] : [100.83333333333333, 101.16666666666666, 101.5]
            : protocol == 777 ? [1, 1.5, 1.5] : [67.5, 34.5, 1.5];
        foreach (double x in expected)
        {
            await session.Tick();
            Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
            Assert.Equal(new Vec3d(100.5, 65, .5), await session.PacketBase());
            Assert.True(await session.GeometryKnown());
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task UnownedHardPeerIsUnknownRatherThanClear(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.SpawnActor(43, "oak_boat", new Vec3d(10000.5, 65, .5));
        await session.Move();
        await session.Sync(new Vec3d(100.5, 65, .5));
        await session.Tick();
        if (protocol == 769)
            Assert.True(await session.GeometryKnown());
        else
        {
            Assert.False(await session.GeometryKnown());
            Assert.Equal(new Vec3d(100.5, 65, .5), await session.Position());
            await session.Send(new ClientboundRemoveEntitiesPacket([43]));
            for (int tick = 0; tick < 4; tick++)
                await session.Tick();
            Assert.False(await session.GeometryKnown());
            Assert.Single(await session.Use());
            await session.Send(new ClientboundRemoveEntitiesPacket([42]));
            await session.SpawnPeer();
            Assert.True(await session.GeometryKnown());
        }
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public async Task ShulkerDeathStatePrecedesItsPublishedEvent(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnActor(43, "shulker", new Vec3d(.5, 65, .5));
        var observed = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<EntityStatusChanged>(evt =>
        {
            if (evt.EntityId == 43 && evt.EventId == 3)
            {
                bool known = session.Client.State.Entities.Get(43)!.Metadata.TryGet(EntityMetadataKeys.Health, out float health);
                observed.TrySetResult(known ? health : float.NaN);
            }
        });
        await session.Send(new ClientboundEntityEventPacket(43, 3));
        Assert.Equal(0f, await observed.Task.WaitAsync(session.Token));
        await session.Health(43, 30);
        Assert.True(await session.Client.InvokeAsync(client => client.State.Entities.Get(43)!.Metadata.TryGet(EntityMetadataKeys.Health, out float health) && health == 30, session.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EverySteppedKnotAndTickOffsetReachesTheSessionCaller(bool absolute)
    {
        await using var session = await Session.Create(777);
        await session.SpawnPeer();
        await session.Path(absolute);
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.PacketBase());
        Vec3d[] expected = [new(.5003662109375, 64.9993896484375, .5008544921875), new(.75, 65, .5), new(1.25, 65, .5), new(1.5, 65, .5)];
        foreach (Vec3d point in expected)
        {
            await session.Tick();
            Assert.Equal(point, await session.Position());
            Assert.Equal(new Vec3d(1.5, 65, .5), await session.PacketBase());
        }
    }

    public static IEnumerable<object[]> ClockCases()
    {
        foreach (int protocol in new[] { 769, 770, 776, 777 })
            foreach (float rate in new[] { 5f, 20f, 40f })
                foreach (bool frozen in new[] { false, true })
                    yield return [protocol, rate, frozen];
    }

    [Theory]
    [MemberData(nameof(ClockCases))]
    public async Task ExistingWorldClockSuppliesRateAndPlayerFreezeExemption(int protocol, float rate, bool frozen)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.Send(new ClientboundTickingStatePacket(rate, frozen));
        if (protocol == 777)
            await session.Path(absolute: true);
        else
            await session.Move();
        Vec3d[] expected = protocol == 777
            ? rate > 20 ? [new(.5003662109375, 64.9993896484375, .5008544921875), new(1.25, 65, .5), new(1.5, 65, .5)]
                : [new(.5003662109375, 64.9993896484375, .5008544921875), new(.75, 65, .5), new(1.25, 65, .5)]
            : [new(.8333333333333333, 65, .5), new(1.1666666666666665, 65, .5), new(1.5, 65, .5)];
        for (int tick = 0; tick < expected.Length; tick++)
        {
            if (frozen && tick == 1)
                await session.Send(new ClientboundTickingStepPacket(1));
            await session.Tick();
            Assert.Equal(expected[tick], await session.Position());
            Assert.Equal(!frozen || tick == 1, await session.Client.InvokeAsync(client => client.State.WorldTicks.RunsNormally, session.Token));
        }
    }

    [Theory]
    [InlineData(770, "pose")]
    [InlineData(777, "pose")]
    [InlineData(770, "scale")]
    [InlineData(777, "scale")]
    [InlineData(770, "mount")]
    [InlineData(777, "mount")]
    public async Task ScaleIsOwnedWhileUnownedPoseAndRiderContextStayUnknown(int protocol, string scenario)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.Move();
        if (scenario == "pose")
            await session.Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(6, MetadataValue.Pose(EntityPose.Swimming))], [])));
        else if (scenario == "scale")
        {
            int scale = session.Client.State.Registries!.Attributes[Identifier.Minecraft("scale")].NetworkId;
            await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scale, 2, [])]));
        }
        else
        {
            await session.SpawnActor(43, "cow", new Vec3d(.5, 65, .5));
            await session.Send(new ClientboundSetPassengersPacket(43, [42]));
        }
        await session.Tick();
        if (scenario == "scale")
        {
            Assert.True(await session.GeometryKnown());
            double x = protocol == 777 ? 1 : .5 + 1d / 3;
            Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
            Assert.Equal(new Aabb(x - (double)(1.2f / 2f), 65, .5 - (double)(1.2f / 2f), x + (double)(1.2f / 2f), 65 + (double)3.6f, .5 + (double)(1.2f / 2f)), await session.Bounds());
        }
        else
            Assert.False(await session.GeometryKnown());
    }

    public static IEnumerable<object[]> TerrainCases()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (string scenario in new[] { "unloaded", "synthetic", "full", "unload", "reload", "no-storage" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public async Task CollisionChunkAvailabilityComesFromNativePackets(int protocol, string scenario)
    {
        await using var session = await Session.Create(protocol, terrainStorage: scenario != "no-storage");
        await session.SpawnPeer();
        int queryX = protocol == 770 ? 202 : 101;
        int queryY = protocol == 770 ? 130 : 65;
        if (scenario is "full" or "unload" or "reload" or "no-storage")
            await session.Terrain(queryX >> 4);
        if (scenario is "unload" or "reload")
            await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(queryX >> 4, 0)));
        if (scenario == "reload")
            await session.Terrain(queryX >> 4);
        if (scenario != "unloaded")
            await session.Block(new BlockPos(queryX, queryY, 0), "stone");
        await session.Move();
        await session.Sync(new Vec3d(100.5, 65, .5));
        await session.Tick();
        if (scenario == "no-storage")
        {
            Assert.False(await session.GeometryKnown());
            return;
        }
        bool blocked = scenario is "full" or "reload";
        Assert.Equal(new Vec3d(blocked ? protocol == 777 ? 1 : 67.5 : protocol == 777 ? 101 : 100.83333333333333, 65, .5), await session.Position());
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public async Task ProvenBlockCollisionShortCircuitsAnUnownedHardPeer(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        int queryX = protocol == 770 ? 202 : 101;
        await session.Terrain(queryX >> 4);
        await session.Block(new BlockPos(queryX, protocol == 770 ? 130 : 65, 0), "stone");
        await session.SpawnActor(43, "oak_boat", new Vec3d(10000.5, 65, .5));
        await session.Move();
        await session.Sync(new Vec3d(100.5, 65, .5));
        await session.Tick();
        Assert.Equal(new Vec3d(protocol == 777 ? 1 : 67.5, 65, .5), await session.Position());
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [InlineData(770, false)]
    [InlineData(777, false)]
    [InlineData(770, true)]
    [InlineData(777, true)]
    public async Task ShulkerHealthAndDeathAreSeparateFromPlacementObstruction(int protocol, bool deathEvent)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.SpawnActor(43, "shulker", new Vec3d(protocol == 770 ? 202.5 : 101.5, protocol == 770 ? 130 : 65, .5));
        await session.Health(43, 30);
        if (deathEvent)
            await session.Send(new ClientboundEntityEventPacket(43, 3));
        else
            await session.Health(43, 0);
        await session.Move();
        await session.Sync(new Vec3d(100.5, 65, .5));
        await session.Tick();
        Assert.Equal(new Vec3d(protocol == 777 ? 101 : 100.83333333333333, 65, .5), await session.Position());
        Assert.True(await session.Client.InvokeAsync(client => client.State.EntityGeometry.TryGet(client.State.Entities.Get(43)!, JavaGameData.EntityGeometryData(protocol), out _, out bool obstructs, protocol) && obstructs, session.Token));
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public async Task ReplacementIdDiscardsItsPreviousPendingMotion(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.Move();
        await session.SpawnPeer(z: 2.5);
        await session.Tick();
        Assert.Equal(new Vec3d(.5, 65, 2.5), await session.Position());
        Assert.Equal(new Vec3d(.5, 65, 2.5), await session.PacketBase());
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task RotationOnlyUpdatesRetainTheNativeCurrentOrPendingPosition(int protocol)
    {
        await using var session = await Session.Create(protocol);
        await session.SpawnPeer();
        await session.Move();
        await session.Tick();
        Vec3d before = await session.Position();
        await session.Send(new ClientboundMoveEntityRotPacket(42, 90, 45, false));
        Assert.Equal(before, await session.Position());
        for (int tick = 0; tick < 8; tick++)
            await session.Tick();
        Assert.Equal(protocol is >= 770 and <= 772 ? before : new Vec3d(1.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.PacketBase());
        await session.Client.InvokeAsync(client =>
        {
            Assert.Equal(90f, client.State.Entities.Get(42)!.Yaw);
            Assert.Equal(45f, client.State.Entities.Get(42)!.Pitch);
            return true;
        }, session.Token);
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
                .UseProfile(new GameProfile(Guid.NewGuid(), "MotionProbe"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(_ticks)
                .ConfigureFeatures(features => { features.Terrain = terrainStorage; features.Physics = terrainStorage; features.Pathfinding = false; features.Entities = true; })
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
                var item = session.Client.State.Registries!.Items[Identifier.Minecraft("stone")];
                await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(item, 1)));
                if (terrain)
                    await session.Terrain();
                await session.Tick();
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async Task Terrain(int columnX = 0)
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), Client.State.Registries, _protocol).SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(columnX, 0, _protocol >= 775, [], sections, [], new byte[7])
                : _protocol >= 768 ? new NbtHeightmapChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7])
                : new Protocol764To767ChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            var codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            var chunk = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
            Assert.True(chunk.FullChunk);
            Assert.Equal(columnX, chunk.ChunkX);
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

        public Task Tick() => _ticks.Fire(Token);
        public Task<Vec3d> Position() => Client.InvokeAsync(client => client.State.Entities.Get(42)!.Position, Token);
        public Task<Vec3d> PacketBase() => Client.InvokeAsync(client => client.State.PacketBases.Get(client.State.Entities.Get(42)!), Token);
        public Task<bool> GeometryKnown() => Client.InvokeAsync(client => client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out _, out _, _protocol), Token);
        public Task<Aabb> Bounds() => Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out Aabb box, out _, _protocol));
            return box;
        }, Token);
        public Task SpawnPeer(double z = .5) => SpawnActor(42, "player", new Vec3d(.5, 65, z));
        public Task SpawnActor(int id, string name, Vec3d position)
        {
            var type = Client.State.Registries!.EntityTypes[Identifier.Minecraft(name)];
            return Send(new ClientboundAddEntityPacket(id, Guid.Empty, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        }
        public Task Move() => Send(new ClientboundMoveEntityPosPacket(42, 4096, 0, 0, false));
        public Task Sync(Vec3d position) => Send(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(position, Vec3d.Zero, 0, 0), false) { Path = _protocol == 777 ? new EntityPositionPath.Linear(position) : null });
        public Task Path(bool absolute)
        {
            if (!absolute)
                return Send(new ClientboundMoveEntityPosPacket(42, 3, -5, 7, false) { Steps = [new(0, 3, -5, 7), new(2, -3, 5, -7), new(4, 4096, 0, 0)] });
            return Send(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(new Vec3d(1.5, 65, .5), Vec3d.Zero, 0, 0), false)
            {
                Path = new EntityPositionPath.Stepped([new(new Vec3d(.500732421875, 64.998779296875, .501708984375), 0), new(new Vec3d(.5, 65, .5), 2), new(new Vec3d(1.5, 65, .5), 4)]),
            });
        }
        public Task Block(BlockPos cell, string name) => Send(new ClientboundBlockUpdatePacket(cell, Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId));
        public Task Health(int id, float value)
        {
            Entity entity = Client.State.Entities.Get(id)!;
            Assert.True(JavaGameData.EntityMetadataKeys(_protocol).TryResolveIndex(entity.Type, EntityMetadataKeys.Health, out int index));
            return Send(new ClientboundSetEntityDataPacket(id, new EntityMetadataList([new(index, MetadataValue.Float(value))], [])));
        }

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
            if (packet is ClientboundMoveEntityPosPacket move)
            {
                var observed = Assert.IsType<ClientboundMoveEntityPosPacket>(decoded);
                Assert.Equal(42, observed.EntityId);
                Assert.Equal(move.DeltaX, observed.DeltaX);
                Assert.Equal(move.DeltaY, observed.DeltaY);
                Assert.Equal(move.DeltaZ, observed.DeltaZ);
                Assert.False(observed.OnGround);
                Assert.Equal(move.Steps, observed.Steps);
                if (move.Steps.Count != 0)
                    Assert.Equal("2a06000003fffb000702fffd0005fff904100000000000", Convert.ToHexString(bytes.WrittenSpan).ToLowerInvariant());
            }
            if (packet is ClientboundMoveEntityRotPacket rotation)
                Assert.Equal(rotation, Assert.IsType<ClientboundMoveEntityRotPacket>(decoded));
            if (packet is ClientboundEntityPositionSyncPacket sync)
            {
                var observed = Assert.IsType<ClientboundEntityPositionSyncPacket>(decoded);
                Assert.Equal(sync.Values, observed.Values);
                if (sync.Path is EntityPositionPath.Stepped path)
                {
                    Assert.Equal(path.Steps, Assert.IsType<EntityPositionPath.Stepped>(observed.Path).Steps);
                    Assert.Equal("2a01033fe006000000000040503fec000000003fe00e0000000000003fe000000000000040504000000000003fe0000000000000023ff800000000000040504000000000003fe000000000000004000000000000000000", Convert.ToHexString(bytes.WrittenSpan).ToLowerInvariant());
                }
                else
                    Assert.Equal(sync.Path, observed.Path);
            }
            if (packet is ClientboundEntityEventPacket status)
                Assert.Equal(status, Assert.IsType<ClientboundEntityEventPacket>(decoded));
            if (packet is ClientboundAddEntityPacket spawn)
            {
                var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal(spawn.EntityId, observed.EntityId);
                Assert.Equal(spawn.TypeId, observed.TypeId);
                Assert.Equal(new Vec3d(spawn.X, spawn.Y, spawn.Z), new Vec3d(observed.X, observed.Y, observed.Z));
            }
            if (packet is ClientboundSetEntityDataPacket metadata)
            {
                var observed = Assert.IsType<ClientboundSetEntityDataPacket>(decoded);
                Assert.Equal(metadata.EntityId, observed.EntityId);
                Assert.Equal(metadata.Metadata.Entries.Count, observed.Metadata.Entries.Count);
                for (int index = 0; index < metadata.Metadata.Entries.Count; index++)
                {
                    EntityDataEntry expected = metadata.Metadata.Entries[index];
                    EntityDataEntry actual = observed.Metadata.Entries[index];
                    Assert.Equal(expected.Index, actual.Index);
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(expected.Value.Kind == MetadataValueKind.Float ? 3 : _protocol == 770 ? 21 : 20, actual.WireSerializerId);
                }
                Assert.Equal(metadata.Metadata.RawTail, observed.Metadata.RawTail);
            }
            if (packet is ClientboundUpdateAttributesPacket attributes)
            {
                var observed = Assert.IsType<ClientboundUpdateAttributesPacket>(decoded);
                Assert.Equal(attributes.EntityId, observed.EntityId);
                Assert.Equal(attributes.Attributes.Count, observed.Attributes.Count);
                for (int index = 0; index < attributes.Attributes.Count; index++)
                {
                    Assert.Equal(attributes.Attributes[index].ModernId, observed.Attributes[index].ModernId);
                    Assert.Equal(attributes.Attributes[index].BaseValue, observed.Attributes[index].BaseValue);
                    Assert.Equal(attributes.Attributes[index].Modifiers, observed.Attributes[index].Modifiers);
                }
            }
            if (packet is ClientboundSetPassengersPacket passengers)
            {
                var observed = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                Assert.Equal(passengers.VehicleId, observed.VehicleId);
                Assert.Equal(passengers.Passengers, observed.Passengers);
            }
            if (packet is ClientboundBlockUpdatePacket block)
                Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
            if (packet is ClientboundInitializeBorderPacket border)
                Assert.Equal(border, Assert.IsType<ClientboundInitializeBorderPacket>(decoded));
            if (packet is ClientboundRemoveEntitiesPacket removal)
                Assert.Equal(removal.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
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
