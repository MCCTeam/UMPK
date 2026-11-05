using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Protocol47FallingBlockRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task Original189FramesAndSessionPhasesMatchNativeBoundsAndMembership(int scene)
    {
        using JsonDocument native = ReadNative();
        JsonElement row = native.RootElement.GetProperty("rows")[scene];
        JsonElement[] packets = row.GetProperty("packets").EnumerateArray().ToArray();
        await using var harness = new Harness();
        foreach (JsonElement packet in packets.Where(packet => packet.GetProperty("action").GetString() == "terrain"))
            await harness.Native(packet, native);
        if (scene == 3)
            await harness.Block(new BlockPos(1, 99, 1), 16);
        if (scene == 13)
            for (int y = 100; y <= 105; y++)
                for (int z = 0; z <= 4; z++)
                    await harness.Block(new BlockPos(2, y, z), 16);
        if (scene == 14)
            for (int y = 100; y <= 105; y++)
                for (int x = 0; x <= 3; x++)
                    await harness.Block(new BlockPos(x, y, 2), 16);
        if (scene == 10)
            await harness.Block(new BlockPos(1, 101, 1), 12 << 4);
        await harness.Native(packets.Single(packet => packet.GetProperty("action").GetString() == "add_entity"), native);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.Equal(Vec3d.Zero, actor.Velocity);
        AssertSnapshot(row.GetProperty("observations")[0]);
        for (int tick = 0; tick < 12; tick++)
        {
            if (scene >= 12 && tick == 0)
                await Apply("set_entity_motion");
            if (scene == 11 && tick == 2)
                await Apply("crossing_teleport");
            if (scene == 11 && tick == 4)
                await Apply("crossing_terrain");
            if (scene == 6 && tick == 2)
                await Apply("late_terrain");
            if (scene == 7 && tick == 2)
            {
                await Apply("replacement");
                actor = harness.State.Entities.Get(42)!;
            }
            if (scene == 5 && tick == 2)
                await Apply("unload");
            if (scene == 5 && tick == 5)
                await Apply("reload");
            await harness.Tick(false);
            AssertSnapshot(row.GetProperty("observations")[tick * 2 + 1]);
            await harness.WorldPhase();
            AssertSnapshot(row.GetProperty("observations")[tick * 2 + 2]);
        }
        return;

        Task Apply(string action) => harness.Native(packets.Single(packet => packet.GetProperty("action").GetString() == action), native);
        void AssertSnapshot(JsonElement expected)
        {
            Assert.Equal(Point(expected.GetProperty("position")), actor.Position);
            Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
            Assert.Equal(expected.GetProperty("query_member").GetBoolean(), harness.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(47), out Aabb box, out bool blocksBuilding, 47));
            Assert.True(blocksBuilding);
            JsonElement value = expected.GetProperty("box");
            Assert.Equal(new Aabb(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble()), box);
        }
    }

    // Original 1.8.9 executes all128liquid state/context combinations and yields the same boxes and velocities as air for12ticks; native receipt stays outside Git.
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task Original189LiquidStatesRetainNativeMovingBounds(int blockId)
    {
        using JsonDocument native = ReadNative();
        JsonElement expected = native.RootElement.GetProperty("rows")[1].GetProperty("observations");
        for (int metadata = 0; metadata < 16; metadata++)
            foreach (bool side in new[] { false, true })
            {
                await using var harness = new Harness();
                await harness.Column();
                await harness.Block(new BlockPos(side ? 2 : 1, 101, 1), (blockId << 4) | metadata);
                await harness.Spawn(42, 101);
                Entity actor = harness.State.Entities.Get(42)!;
                for (int tick = 0; tick < 12; tick++)
                {
                    await harness.Tick();
                    JsonElement observation = expected[tick * 2 + 2];
                    Assert.Equal(Point(observation.GetProperty("position")), actor.Position);
                    Assert.Equal(Point(observation.GetProperty("velocity")), actor.Velocity);
                    Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(47), out Aabb box, out bool blocksBuilding, 47));
                    Assert.True(blocksBuilding);
                    JsonElement value = observation.GetProperty("box");
                    Assert.Equal(new Aabb(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble()), box);
                }
            }
    }

    [Fact]
    public async Task Retained184FallingSpawnFramesDoNotSuppressDistantPlacementSwing()
    {
        using JsonDocument native = ReadNative();
        await using var harness = new Harness();
        await harness.Column(-3, 16);
        await harness.Column(-1, 14);
        await harness.Column(0, 16);
        JsonElement[] fallers = native.RootElement.GetProperty("historical184Fallers").EnumerateArray().ToArray();
        Assert.Equal(14, fallers.Length);
        foreach (JsonElement faller in fallers)
        {
            var packet = Assert.IsType<ClientboundAddEntityPacket>(await harness.Raw("add_entity", Convert.FromBase64String(faller.GetProperty("body").GetString()!)));
            Assert.Equal(faller.GetProperty("id").GetInt32(), packet.EntityId);
            Assert.Equal(70, packet.TypeId);
            Assert.Equal(13, packet.Data);
            Assert.Equal(Point(faller.GetProperty("position")), new Vec3d(packet.X, packet.Y, packet.Z));
        }
        await harness.Tick();
        await harness.Place(new BlockPos(-41, 99, 259), new Vec3d(-42.5, 100, 259.5), 2);
        foreach (Entity actor in harness.State.Entities.All)
        {
            Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(47), out Aabb box, out bool blocksBuilding, 47));
            Assert.True(blocksBuilding);
            Assert.True(box.MaxY < 100);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public async Task PlacementUsesKnownNativeBoundsAndRemoval(int scene, int expectedPackets)
    {
        await using var harness = new Harness();
        await harness.Column();
        if (scene != 0)
            await harness.Spawn(42, scene == 2 ? 101 : 30);
        if (scene == 3)
            await harness.Apply("remove_entities", new ClientboundRemoveEntitiesPacket([42]));
        await harness.Tick();
        await harness.Place(new BlockPos(1, 100, 1), new Vec3d(.5, 100, -1.5), expectedPackets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task UnclassifiedContextRemainsUnknownAfterLaterPackets(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity actor = harness.State.Entities.Get(42)!;
        if (scene == 0)
            await harness.Block(new BlockPos(1, 100, 1), 30 << 4);
        if (scene is 1 or 2)
            await harness.Apply("add_entity", new ClientboundAddEntityPacket(43, Guid.Empty, scene == 1 ? 1 : 126, 1.5, 101, 1.5, 0, 0, 0, 0, 0, 0, 0, null));
        if (scene == 3)
            harness.State.World.SetBorder(harness.State.World.Border.WithSize(4).WithCenter(1.5, 1.5));
        if (scene == 4)
        {
            await harness.Spawn(43, 30);
            await harness.Apply("set_entity_link", new ClientboundSetEntityLinkPacket(42, 43, false));
        }
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(47), out _, out _, 47));
        await harness.Block(new BlockPos(1, 100, 1), 0);
        await harness.Apply("remove_entities", new ClientboundRemoveEntitiesPacket([43]));
        harness.State.World.SetBorder(Umpk.Game.World.WorldBorderState.Default);
        await harness.Apply("teleport_entity", new ClientboundTeleportEntityPacket(42, 1.5, 30, 1.5, 0, 0, false, null, 0));
        await harness.Apply("set_entity_motion", new ClientboundSetEntityMotionPacket(42, 0, 0, 0, null));
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(47), out _, out _, 47));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RemovalAndWorldLifetimeDiscardProtocol47State(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity original = harness.State.Entities.Get(42)!;
        await harness.Tick();
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(original, out _));
        if (scene == 0)
            await harness.Apply("remove_entities", new ClientboundRemoveEntitiesPacket([42]));
        else if (scene == 1)
            harness.State.ResetForRespawn(harness.State.World);
        else if (scene == 2)
            harness.State.ResetForConfigurationReentry();
        else if (scene == 3)
            harness.State.ResetForSessionEnd();
        else
            await harness.Spawn(42, 30);
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(original, out _));
        if (scene is 1 or 2 or 3)
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), 47);
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity current = harness.State.Entities.Get(42)!;
        Assert.NotSame(original, current);
        await harness.Tick();
        Assert.Equal(100.96000000089407, current.Position.Y);
        Assert.Equal(-0.03919999988675116, current.Velocity.Y);
    }

    private static Vec3d Point(JsonElement row) => new(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(Protocol47FallingBlockRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.Protocol47FallingBlock.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(30));
        public ClientState State => _applier.State;

        public Harness()
        {
            _applier = new ApplierHarness(BoundPacketApplierHarness.Version(47), new ClientFeatures { Terrain = true, Entities = true, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(47);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), 47);
        }

        public Task<object> Native(JsonElement packet, JsonDocument native)
        {
            string action = packet.GetProperty("action").GetString()!;
            string name = action is "add_entity" or "replacement" ? "add_entity" : action == "crossing_teleport" ? "teleport_entity" : action == "set_entity_motion" ? action : "level_chunk_with_light";
            return Raw(name, Convert.FromHexString(native.RootElement.GetProperty("packetBodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!));
        }

        public Task<object> Column(int x = 0, int z = 0)
        {
            using JsonDocument native = ReadNative();
            JsonElement packet = native.RootElement.GetProperty("rows")[1].GetProperty("packets")[0];
            byte[] body = Convert.FromHexString(native.RootElement.GetProperty("packetBodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
            BinaryPrimitives.WriteInt32BigEndian(body, x);
            BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), z);
            return Raw("level_chunk_with_light", body);
        }

        public Task<object> Spawn(int id, double y) => Apply("add_entity", new ClientboundAddEntityPacket(id, Guid.Empty, 70, 1.5, y, 1.5, 0, 0, 0, 12, 8000, 16000, -8000, null));
        public Task<object> Block(BlockPos cell, int state) => Apply("block_update", new ClientboundBlockUpdatePacket(cell, state));

        public Task Tick(bool worldPhase = true) => _scheduler.InvokeAsync(() =>
        {
            State.TickEntities(47, JavaGameData.BlockShapes(47), []);
            if (worldPhase)
                State.EntityMembership.FinishWorldPhase();
        }, _lifetime.Token);

        public Task WorldPhase() => _scheduler.InvokeAsync(State.EntityMembership.FinishWorldPhase, _lifetime.Token);

        public Task<object> Apply(string name, object packet)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(47, name);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty));
            return Raw(name, buffer.WrittenSpan.ToArray());
        }

        public async Task<object> Raw(string name, byte[] body)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(47, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            object packet = codec.Decode(body, context);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(body, buffer.WrittenSpan.ToArray());
            await _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _lifetime.Token);
            return packet;
        }

        public async Task Place(BlockPos support, Vec3d self, int expectedPackets)
        {
            await Block(support, 16);
            Assert.True(State.Registries!.Items.TryGet(Identifier.Minecraft("stone"), out var item));
            await Apply("set_slot", new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(item, 32)));
            var sink = new Sink();
            var services = new ClientSessionServices
            {
                Version = BoundPacketApplierHarness.Version(47),
                Options = new(),
                Policies = new(),
                State = State,
                Wire = new(BoundPacketApplierHarness.Version(47)),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                CurrentSessionCancellation = () => _lifetime.Token,
                ReadPhysicsState = () => new PhysicsState { BoundingBox = Aabb.OfSize(self.X, self.Y, self.Z, .6, 1.8) },
            };
            var actions = new InteractionActions(sink, services, _applier.Sequences);
            await actions.PlaceBlockAsync(support, Direction.Up, new Vec3d(.5, 1, .5), ct: _lifetime.Token);
            Assert.Equal(expectedPackets, sink.Packets.Count);
            if (expectedPackets != 0)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(sink.Packets[0]);
                Assert.Equal(support, use.Position);
                Assert.Equal((int)Direction.Up, use.Face);
                Assert.Equal(Identifier.Minecraft("stone"), use.HeldItem.Item.Id);
            }
            if (expectedPackets == 2)
                Assert.IsType<ServerboundSwingPacket>(sink.Packets[1]);
            Assert.Equal(32, State.Inventory.GetSlot(36).Count);
            Assert.True(State.World.GetBlock(support.Above()).IsAir);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await _scheduler.DisposeAsync();
            _lifetime.Dispose();
        }
    }

    private sealed class Sink : IPacketSink
    {
        public List<object> Packets { get; } = [];
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var value = Assert.IsAssignableFrom<IPacket>(packet);
            byte[] body = BoundDescriptorCodec.EncodeServerbound(47, value);
            Assert.True(BoundPacketApplierHarness.Version(47).Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry));
            Assert.True(registry.TryGetOutbound(value.Type, out _, out BoundPacketCodec codec));
            object decoded = codec.Decode(body, new PacketCodecContext(JavaGameData.Registries(47), IConnectionCodecState.Empty));
            if (value is ServerboundSwingPacket)
            {
                Assert.Empty(body);
                var swing = Assert.IsType<ServerboundSwingPacket>(decoded);
                Assert.Equal(0, swing.Hand);
                Assert.False(swing.HasHand);
            }
            else
                Assert.NotEmpty(body);
            Assert.Equal(body, BoundDescriptorCodec.EncodeServerbound(47, Assert.IsAssignableFrom<IPacket>(decoded)));
            Packets.Add(decoded);
            return ValueTask.CompletedTask;
        }
        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
