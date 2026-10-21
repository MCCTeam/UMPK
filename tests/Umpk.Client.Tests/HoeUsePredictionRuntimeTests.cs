using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class HoeUsePredictionRuntimeTests
{
    public static IEnumerable<object[]> TransformCases()
    {
        foreach (int protocol in JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol is >= 498 and <= 776))
            foreach (int scene in Enumerable.Range(0, protocol >= 755 ? 11 : 8))
                foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                    yield return [protocol, scene, hand];
    }

    [Theory]
    [MemberData(nameof(TransformCases))]
    public async Task NativeHoeRulesReachTheUseAndSwingPacketGroup(int protocol, int scene, Hand hand)
    {
        string block = scene switch
        {
            1 => "grass_block",
            2 => "coarse_dirt",
            3 => protocol >= 755 ? "dirt_path" : "grass_path",
            4 => "stone",
            >= 8 => "rooted_dirt",
            _ => "dirt",
        };
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, block);
        if (scene is 6 or 10)
            await harness.Block(harness.Target.Above(), "stone");
        Direction face = scene is 5 or 9 ? Direction.Down : Direction.Up;
        bool succeeds = scene is not (4 or 5 or 6);
        foreach (var item in harness.State.Registries!.Items.Where(entry => entry.Id.Path.EndsWith("_hoe", StringComparison.Ordinal)))
        {
            await harness.Slot(hand, new ItemStack(item, 1));
            if (scene == 7)
                await harness.GameMode(GameMode.Creative);
            int before = harness.Sent.Count;

            await harness.Use(face, hand);

            Assert.Equal(before + (succeeds ? 2 : 1), harness.Sent.Count);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(harness.Sent[before]);
            Assert.Equal((int)hand, use.Hand);
            Assert.Equal((int)face, use.Face);
            Assert.Equal(harness.Target, use.Position);
            if (succeeds)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[before + 1]).Hand);
            Assert.Equal(Identifier.Minecraft(block), harness.State.World.GetBlock(harness.Target).Block.Id);
            Assert.Equal(1, harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count);
        }
    }

    [Theory]
    [InlineData(498)]
    [InlineData(755)]
    [InlineData(766)]
    [InlineData(776)]
    public async Task CooldownAndAdventureDoNotInventAToolSwing(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        harness.State.Self.SetItemCooldown(item.NetworkId, 100);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Single(harness.Sent);
        harness.State.Self.SetItemCooldown(item.NetworkId, 0);
        await harness.GameMode(GameMode.Adventure);
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(2, harness.Sent.Count);
        Assert.All(harness.Sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet));
    }

    [Theory]
    [InlineData(490)]
    [InlineData(777)]
    public async Task AnUnclassifiedNativeToolEraStaysPacketOnly(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));

        await harness.Use(Direction.Up, Hand.Main);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sent));
    }

    [Theory]
    [InlineData(498, Hand.Main)]
    [InlineData(498, Hand.Off)]
    [InlineData(755, Hand.Main)]
    [InlineData(755, Hand.Off)]
    [InlineData(766, Hand.Main)]
    [InlineData(766, Hand.Off)]
    [InlineData(776, Hand.Main)]
    [InlineData(776, Hand.Off)]
    public async Task NativeDurabilityDataDoesNotSuppressASecondToolUse(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        ItemStack stack = new(item, 1);
        if (protocol >= 766)
            stack = stack.With(DataComponents.Damage, new DamageComponent(1));
        else
        {
            var tag = new NbtCompound();
            tag.PutInt("Damage", 1);
            tag.PutString("unrelated", "retained native data");
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        }
        await harness.Slot(hand, stack);
        ItemStack observed = harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        Assert.Equal(1, protocol >= 766 ? observed.Damage : observed.Components.Get(DataComponents.LegacyNbt)!.Nbt.GetInt("Damage"));

        await harness.Use(Direction.Up, hand);

        Assert.Equal(2, harness.Sent.Count);
        Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task OnlyReceivedTerrainCanSupplyTheHoeContext(bool entities, int scene)
    {
        await using var harness = new Harness(498, entities, nativeTerrain: false);
        if (scene is 1 or 2 or 3)
            await harness.ReceiveColumn();
        if (scene is 2 or 3)
            await harness.ForgetColumn();
        if (scene == 3)
            await harness.ReceiveColumn();
        if (scene == 4)
            await harness.ReceiveColumn(fullChunk: false);
        await harness.Block(harness.Target, "dirt");
        Assert.NotNull(harness.State.World.GetColumn(harness.Target));
        Assert.Equal(Identifier.Minecraft("dirt"), harness.State.World.GetBlock(harness.Target).Block.Id);
        bool nativeTerrain = scene is 1 or 3;
        Assert.Equal(nativeTerrain, harness.State.EntityMembership.HasColumn(harness.Target));
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));

        await harness.Use(Direction.Up, Hand.Main);

        Assert.Equal(nativeTerrain ? 2 : 1, harness.Sent.Count);
        Assert.IsType<ServerboundUseItemOnPacket>(harness.Sent[0]);
        if (nativeTerrain)
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly int _protocol;
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _session = new(TimeSpan.FromSeconds(30));
        private readonly InteractionActions _actions;
        public BlockPos Target { get; } = new(1, 64, 1);
        public ClientState State => _applier.State;
        public List<object> Sent { get; } = [];

        public Harness(int protocol, bool entities = true, bool nativeTerrain = true)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            _applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = entities, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            if (nativeTerrain)
                State.EntityMembership.LoadColumn(new ChunkPos(0, 0));
            var services = new ClientSessionServices { Version = version, Options = new(), Policies = new(), State = State, Wire = new(version), Logger = NullLogger.Instance, Scheduler = _scheduler, CurrentSessionCancellation = () => _session.Token };
            _actions = new InteractionActions(new Sink(Sent, _applier.Sequences, protocol), services, _applier.Sequences);
        }

        public Task Block(BlockPos position, string name)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out var block));
            return Apply("block_update", new ClientboundBlockUpdatePacket(position, block.DefaultStateId));
        }

        public Task ReceiveColumn(bool fullChunk = true)
        {
            var body = new Pre1_18ChunkWireBody(0, 0, fullChunk, 0, new NbtCompound(), [], fullChunk ? new byte[256 * 4] : [], [0], []);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            Assert.True(JavaVersions.TryGetByProtocol(_protocol, out var version));
            Assert.True(version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out var registry));
            string name = Assert.Single(registry.Packets, pair => pair.Type.Id.Path is "level_chunk" or "level_chunk_with_light").Type.Id.Path;
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var decoded = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
            Assert.Equal(fullChunk, decoded.FullChunk);
            Assert.Equal(0, decoded.ChunkX);
            Assert.Equal(0, decoded.ChunkZ);
            Assert.Equal(16, decoded.Column.SectionCount);
            Assert.Equal(0, decoded.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(decoded)));
            var roundTrip = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(roundTrip);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(bytes, roundTrip.WrittenSpan.ToArray());
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _session.Token);
        }

        public Task ForgetColumn() => Apply("forget_level_chunk", new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));

        public Task Slot(Hand hand, ItemStack item) => Apply("container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, item));
        public Task GameMode(GameMode mode) => Apply("game_event", new ClientboundGameEventPacket(3, (float)mode));
        private Task Apply(string name, object packet)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var original = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(original);
            codec.Encode(ref writer, packet, context);
            object decoded = codec.Decode(original.WrittenSpan, context);
            if (packet is ClientboundContainerSetSlotPacket expected)
            {
                var observed = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded);
                Assert.Equal(expected.Slot, observed.Slot);
                Assert.Equal(expected.Item.Item.Id, observed.Item.Item.Id);
                Assert.Equal(expected.Item.Count, observed.Item.Count);
            }
            else
                Assert.Equal(packet, decoded);
            var roundTrip = new ArrayBufferWriter<byte>();
            writer = new PacketWriter(roundTrip);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(original.WrittenSpan.ToArray(), roundTrip.WrittenSpan.ToArray());
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _session.Token);
        }
        public Task Use(Direction face, Hand hand) => _actions.PlaceBlockAsync(Target, face, new Vec3d(.5, 1, .5), hand, ct: _session.Token).WaitAsync(TimeSpan.FromSeconds(5));

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await _scheduler.DisposeAsync();
            _session.Dispose();
        }
    }

    private sealed class Sink(List<object> packets, SequenceTracker sequences, int protocol) : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<IPacket>(packet)));
            packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                sequences.Acknowledge(use.Sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
