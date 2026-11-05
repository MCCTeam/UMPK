using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
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

public sealed class OwnedLivingPickingSessionTests
{
    private static readonly string[] Families = ["cat", "chicken", "horse", "iron_golem", "pig", "sheep", "villager"];

    public static IEnumerable<object[]> FamilyCases()
    {
        foreach (int protocol in new[] { 769, 770 })
            foreach (string family in Families)
                yield return [protocol, family];
    }

    public static IEnumerable<object[]> Compositions()
    {
        foreach (object[] row in FamilyCases())
            foreach (bool physics in new[] { false, true })
                yield return [row[0], row[1], physics];
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public async Task RealFamilyPacketsPreserveDisplayAndPacketBaseUntilEligibleTicks(int protocol, string family, bool physics)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnFamily(family);
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetDimensions(Identifier.Minecraft(family), false, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        await session.Move();
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.PacketBase());
        foreach (double x in new[] { .8333333333333333, 1.1666666666666665, 1.5 })
        {
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
            Assert.Equal(new Aabb(x - (double)(dimensions.Width / 2f), 65, .5 - (double)(dimensions.Width / 2f), x + (double)(dimensions.Width / 2f), 65 + (double)dimensions.Height, .5 + (double)(dimensions.Width / 2f)), await session.Bounds());
        }
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task KnownFamilyBesideTheRayDoesNotCancelViewMining(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnFamily(family, new Vec3d(1.5, 65, 2.5));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick());
            AssertContinued(await session.Tick());
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task HeldViewMiningStopsOnTheFamilyNativeInterpolationPhase(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnFamily(family, new Vec3d(1.5, 65, 2.5));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick());
            await session.Move(-4096);
            int abortTick = family is "horse" or "iron_golem" ? 2 : family is "pig" or "sheep" ? 3 : 4;
            for (int tick = 1; tick <= abortTick; tick++)
            {
                List<IPacket> outgoing = await session.Tick();
                if (tick == abortTick)
                    AssertAbort(outgoing);
                else
                    AssertContinued(outgoing);
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public async Task PositiveVelocityUsesTheOriginalLivingDecayWithoutLocalTravel(int protocol, string family, bool physics)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnFamily(family);
        await session.Send(new ClientboundSetEntityMotionPacket(42, 80, 80, 80));
        Assert.Equal(new Vec3d(.01, .01, .01), await session.Velocity());
        double value = .01;
        for (int tick = 0; tick < 5; tick++)
        {
            value *= .98;
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(value, value, value), await session.Velocity());
            Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        }
    }

    [Theory]
    [InlineData(769, false, false)]
    [InlineData(769, false, true)]
    [InlineData(769, true, false)]
    [InlineData(769, true, true)]
    [InlineData(770, false, false)]
    [InlineData(770, false, true)]
    [InlineData(770, true, false)]
    [InlineData(770, true, true)]
    public async Task ChickenFallingVelocityUsesThePacketGroundedStateAfterTheLivingPhase(int protocol, bool physics, bool grounded)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnFamily("chicken");
        await session.Send(new ClientboundMoveEntityPosPacket(42, 4096, 0, 0, grounded));
        await session.Send(new ClientboundSetEntityMotionPacket(42, 80, -80, 80));
        double x = .01, y = -.01;
        for (int tick = 1; tick <= 5; tick++)
        {
            if (tick > 3)
            {
                x *= .98;
                y *= .98;
            }
            if (Math.Abs(x) < .003)
                x = 0;
            if (Math.Abs(y) < .003)
                y = 0;
            if (!grounded && y < 0)
                y *= .6;
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(x, y, x), await session.Velocity());
            Assert.Equal(new Vec3d(tick == 1 ? .8333333333333333 : tick == 2 ? 1.1666666666666665 : 1.5, 65, .5), await session.Position());
            Assert.True(await session.GeometryKnown());
        }
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task FreshFamilyBoundsCannotIgnoreAnUnownedLocalPush(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnFamily(family, new Vec3d(2.5, 65, 2.5));
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task CachedScaleValuesCannotPromoteAnUnprovenCurrentPositionIntoViewMining(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        var point = new Vec3d(2, 65, 2.5);
        await session.SpawnFamily(family, point);
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetDimensions(Identifier.Minecraft(family), false, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick());
            Assert.Equal(NativeBox(point, dimensions.Width, dimensions.Height), await session.CachedBounds());
            int scale = session.Client.State.Registries!.Attributes[Identifier.Minecraft("scale")].NetworkId;
            await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scale, 2, [])]));
            Assert.False(await session.GeometryKnown());
            Assert.Equal(NativeBox(point, dimensions.Width, dimensions.Height), await session.CachedBounds());
            AssertAbort(await session.Tick());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            Assert.Equal(NativeBox(point, dimensions.Width * 2F, dimensions.Height * 2F), await session.CachedBounds());
            Assert.False(await session.GeometryKnown());
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> SelfPositionConfidenceCases()
    {
        foreach (object[] row in FamilyCases())
            foreach (bool suppliedPosition in new[] { false, true })
                yield return [row[0], row[1], suppliedPosition];
    }

    [Theory]
    [MemberData(nameof(SelfPositionConfidenceCases))]
    public async Task CachedFamilyGeometryCannotReplaceTheMissingSelfPositionContext(int protocol, string family, bool suppliedPosition)
    {
        await using Session session = await Session.Create(protocol, correctSelf: suppliedPosition);
        var point = new Vec3d(8.5, 65, 8.5);
        await session.SpawnFamily(family, point);
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetDimensions(Identifier.Minecraft(family), false, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        Assert.Empty(await session.Tick());
        Assert.Equal(NativeBox(point, dimensions.Width, dimensions.Height), await session.CachedBounds());
        Assert.Equal(suppliedPosition, await session.GeometryKnown());
        Assert.Equal(suppliedPosition ? 2 : 1, (await session.Use()).Count);
    }

    private static Aabb NativeBox(Vec3d point, float width, float height)
        => new(point.X - (double)(width / 2F), point.Y, point.Z - (double)(width / 2F), point.X + (double)(width / 2F), point.Y + (double)height, point.Z + (double)(width / 2F));

    public static IEnumerable<object[]> UnsupportedCases()
    {
        foreach (object[] row in FamilyCases())
            foreach (string scenario in new[] { "pose", "scale", "mount", "passenger", "health", "fluid", "local-push", "malformed" })
                yield return [row[0], row[1], scenario];
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public async Task UnsupportedFamilyStateCannotProveMovingBounds(int protocol, string family, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnFamily(family);
        await session.Move();
        await session.Unsupported(scenario);
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        if (scenario == "fluid")
            await session.Block(new BlockPos(0, 65, 0), "air");
        if (scenario == "local-push")
            await session.CorrectSelf(new Vec3d(2.5, 64, 2.5));
        if (scenario is "mount" or "passenger")
            await session.Send(new ClientboundRemoveEntitiesPacket([43]));
        await session.SpawnFamily(family);
        Assert.True(await session.GeometryKnown());
    }

    public static IEnumerable<object[]> UnownedBands()
    {
        foreach (int protocol in new[] { 768, 771, 777 })
            foreach (string family in Families)
                yield return [protocol, family];
    }

    [Theory]
    [MemberData(nameof(UnownedBands))]
    public async Task GeometryWithoutOwnedFamilyMotionRemainsUnknown(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnFamily(family, new Vec3d(1.5, 65, 2.5));
        Task mining = session.Mine(session.Token);
        await session.Queued();
        Assert.Empty(await session.Tick());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task ExplicitCoordinateMiningRetainsItsContract(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnFamily(family, new Vec3d(.5, 65, 2.5));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick());
            AssertContinued(await session.Tick());
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public async Task ReplacementDiscardsThePreviousFamilyController(int protocol, string family)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnFamily(family);
        await session.Move();
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        await session.SpawnFamily(family, new Vec3d(8.5, 65, 8.5));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(8.5, 65, 8.5), await session.Position());
        Assert.True(await session.GeometryKnown());
    }

    private static void AssertStarted(IReadOnlyList<IPacket> packets)
    {
        Assert.Equal(3, packets.Count);
        Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(packets[0]).Action);
        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[2]).Hand);
    }

    private static void AssertContinued(IReadOnlyList<IPacket> packets) => Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(Assert.Single(packets)).Hand);

    private static void AssertAbort(IReadOnlyList<IPacket> packets)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
        Assert.Equal(1, action.Action);
        Assert.Equal(Session.Target, action.Position);
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

        private Session(int protocol, bool physics)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "PickExclusion"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = physics; features.Pathfinding = false; features.Entities = true; features.Terrain = true; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol, bool physics = true, bool terrain = true, bool mining = false, bool correctSelf = true)
        {
            var session = new Session(protocol, physics);
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
                var position = mining ? new Vec3d(.5, 64, .5) : new Vec3d(2.5, 64, 2.5);
                if (correctSelf)
                {
                    await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
                    await corrected.Task.WaitAsync(session.Token);
                }
                if (terrain)
                    await session.Terrain();
                var stoneItem = session.Client.State.Registries!.Items[Identifier.Minecraft("stone")];
                await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(stoneItem, 32)));
                Assert.Empty(await session.Barrier());
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
                if (terrain && physics && correctSelf)
                    Assert.True(session.Client.State.Self.OnGround);
                if (mining)
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

        internal string Family { get; private set; } = "cow";
        internal Task SpawnFamily(string family, Vec3d? position = null)
        {
            Family = family;
            return SpawnActor(42, family, position ?? new Vec3d(.5, 65, .5));
        }
        internal async Task SpawnActor(int id, string kind, Vec3d position)
        {
            int type = Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId;
            await Send(new ClientboundAddEntityPacket(id, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
            Assert.Empty(await Barrier());
            Assert.Equal(position, await Client.InvokeAsync(client => client.State.Entities.Get(id)!.Position, Token));
        }
        internal Task Move(short dx = 4096) => Send(new ClientboundMoveEntityPosPacket(42, dx, 0, 0, false));
        internal Task<Vec3d> Position() => Client.InvokeAsync(client => client.State.Entities.Get(42)!.Position, Token);
        internal Task<Vec3d> Velocity() => Client.InvokeAsync(client => client.State.Entities.Get(42)!.Velocity, Token);
        internal Task<Vec3d> PacketBase() => Client.InvokeAsync(client => client.State.PacketBases.Get(client.State.Entities.Get(42)!), Token);
        internal Task<bool> GeometryKnown() => Client.InvokeAsync(client => client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out _, out _, _protocol), Token);
        internal Task<Aabb> Bounds() => Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out Aabb bounds, out _, _protocol));
            return bounds;
        }, Token);
        internal Task<Aabb> CachedBounds() => Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGetCachedGeometry(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out Aabb bounds, out bool blocking, _protocol));
            Assert.True(blocking);
            return bounds;
        }, Token);
        internal Task Block(BlockPos cell, string kind) => Send(new ClientboundBlockUpdatePacket(cell, Client.State.Registries!.Blocks[Identifier.Minecraft(kind)].Value.DefaultStateId));
        internal async Task CorrectSelf(Vec3d position)
        {
            await Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
            Assert.Empty(await Barrier());
        }
        internal async Task Unsupported(string scenario)
        {
            Assert.True(JavaGameData.EntityGeometryData(_protocol).TryGetRule(Identifier.Minecraft(Family), out EntityGeometryRule rule));
            if (scenario is "pose" or "baby" or "health" or "malformed")
            {
                int index = scenario == "baby" ? rule.MetadataIndex : scenario == "pose" ? rule.PoseMetadataIndex : 9;
                MetadataValue value = scenario switch
                {
                    "baby" => MetadataValue.Boolean(true),
                    "pose" => MetadataValue.Pose(EntityPose.Sleeping),
                    "health" => MetadataValue.Float(0),
                    _ => MetadataValue.VarInt(1),
                };
                await Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(index, value)], [])));
            }
            else if (scenario == "scale")
            {
                int scale = Client.State.Registries!.Attributes[Identifier.Minecraft("scale")].NetworkId;
                await Send(new ClientboundUpdateAttributesPacket(42, [new(null, scale, 2, [])]));
            }
            else if (scenario is "mount" or "passenger")
            {
                await SpawnActor(43, "cow", new Vec3d(8.5, 65, 8.5));
                await Send(scenario == "mount" ? new ClientboundSetPassengersPacket(43, [42]) : new ClientboundSetPassengersPacket(42, [43]));
            }
            else if (scenario == "fluid")
                await Block(new BlockPos(0, 65, 0), "water");
            else if (scenario == "local-push")
                await CorrectSelf(new Vec3d(.5, 64, .5));
            else
                throw new ArgumentOutOfRangeException(nameof(scenario));
            Assert.Empty(await Barrier());
        }
        internal async Task<List<IPacket>> Use()
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(new BlockPos(1, 64, 1), Direction.Up, new Vec3d(.5, 1, .5), ct: Token);
            var packets = new List<IPacket>();
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var decoded = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, decoded));
                if (decoded is ServerboundUseItemOnPacket use)
                {
                    packets.Add(use);
                    await Send(new ClientboundBlockChangedAckPacket(use.Sequence), barrier: false);
                    break;
                }
                if (decoded is ServerboundSwingPacket)
                    packets.Add(decoded);
            }
            await action.WaitAsync(Token);
            packets.AddRange(await Barrier());
            Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            if (packets.Count == 2)
                Assert.IsType<ServerboundSwingPacket>(packets[1]);
            return packets;
        }

        internal async Task Terrain(int columnX = 0)
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, Client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(columnX, 0, _protocol >= 775, [], sections, [], new byte[7])
                : new NbtHeightmapChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(columnX, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(sections.Length, packet.Column.SectionCount);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Encode(codec, packet));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Send(packet);
            Assert.Empty(await Barrier());
            Assert.True(Client.State.EntityMembership.HasColumn(new BlockPos(columnX * 16, 65, 0)));
            if (columnX == 0)
            {
                await Block(new BlockPos(0, 63, 0), "stone");
                await Block(new BlockPos(2, 63, 2), "stone");
                await Block(new BlockPos(1, 64, 1), "dirt");
                await Block(Target, "stone");
                Assert.Empty(await Barrier());
            }
        }

        internal async Task Send(IPacket packet, bool barrier = true)
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
                case ClientboundMoveEntityPosPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityPosPacket>(decoded));
                    break;
                case ClientboundMoveEntityPosRotPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityPosRotPacket>(decoded));
                    break;
                case ClientboundMoveEntityRotPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityRotPacket>(decoded));
                    break;
                case ClientboundEntityEventPacket status:
                    Assert.Equal(status, Assert.IsType<ClientboundEntityEventPacket>(decoded));
                    break;
                case ClientboundSetEntityMotionPacket motion:
                    Assert.Equal(motion, Assert.IsType<ClientboundSetEntityMotionPacket>(decoded));
                    break;
                case ClientboundTickingStatePacket ticking:
                    Assert.Equal(ticking, Assert.IsType<ClientboundTickingStatePacket>(decoded));
                    break;
                case ClientboundTickingStepPacket ticking:
                    Assert.Equal(ticking, Assert.IsType<ClientboundTickingStepPacket>(decoded));
                    break;
                case ClientboundSetEntityDataPacket data:
                    var observedData = Assert.IsType<ClientboundSetEntityDataPacket>(decoded);
                    Assert.Equal(data.EntityId, observedData.EntityId);
                    Assert.Equal(data.Metadata.Entries.Count, observedData.Metadata.Entries.Count);
                    for (int i = 0; i < data.Metadata.Entries.Count; i++)
                    {
                        Assert.Equal(data.Metadata.Entries[i].Index, observedData.Metadata.Entries[i].Index);
                        Assert.Equal(data.Metadata.Entries[i].Value, observedData.Metadata.Entries[i].Value);
                    }
                    break;
                case ClientboundUpdateAttributesPacket attributes:
                    var observedAttributes = Assert.IsType<ClientboundUpdateAttributesPacket>(decoded);
                    Assert.Equal(attributes.EntityId, observedAttributes.EntityId);
                    Assert.Equal(attributes.Attributes.Count, observedAttributes.Attributes.Count);
                    Assert.Equal(attributes.Attributes[0].ModernId, observedAttributes.Attributes[0].ModernId);
                    Assert.Equal(attributes.Attributes[0].BaseValue, observedAttributes.Attributes[0].BaseValue);
                    Assert.Equal(attributes.Attributes[0].Modifiers, observedAttributes.Attributes[0].Modifiers);
                    break;
                case ClientboundSetPassengersPacket passengers:
                    var observedPassengers = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                    Assert.Equal(passengers.VehicleId, observedPassengers.VehicleId);
                    Assert.Equal(passengers.Passengers, observedPassengers.Passengers);
                    break;
                case ClientboundRemoveEntitiesPacket removal:
                    Assert.Equal(removal.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                    break;
                case ClientboundEntityPositionSyncPacket sync:
                    Assert.Equal(sync, Assert.IsType<ClientboundEntityPositionSyncPacket>(decoded));
                    break;
                case ClientboundInitializeBorderPacket border:
                    Assert.Equal(border, Assert.IsType<ClientboundInitializeBorderPacket>(decoded));
                    break;
                case ClientboundRespawnPacket respawn:
                    Assert.Equal(respawn, Assert.IsType<ClientboundRespawnPacket>(decoded));
                    break;
                case ClientboundSetCameraPacket camera:
                    Assert.Equal(camera, Assert.IsType<ClientboundSetCameraPacket>(decoded));
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
            if (barrier)
                Assert.Empty(await Barrier());
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
            await Send(new ClientboundPlayKeepAlivePacket(nonce), barrier: false);
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
                if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket)
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
