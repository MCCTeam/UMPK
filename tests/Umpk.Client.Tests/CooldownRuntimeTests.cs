using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class CooldownRuntimeTests
{
    public static IEnumerable<object[]> CooldownProtocols() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol is >= 107 and <= 776).Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(CooldownProtocols))]
    public async Task FramedItemCooldownSuppressesTheNativeToolSwing(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        await harness.Cooldown(item.Id, 100);

        await harness.Use(Direction.Up, Hand.Main);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sent));
    }

    public static IEnumerable<object[]> GroupCases()
    {
        foreach (int protocol in new[] { 768, 776, 777 })
            foreach (int scene in Enumerable.Range(0, 6))
                yield return [protocol, scene];
    }

    [Theory]
    [MemberData(nameof(GroupCases))]
    public async Task EffectiveComponentGroupsOverrideAndRemoveThePrototype(int protocol, int scene)
    {
        await using var harness = new Harness(protocol);
        await harness.Block(harness.Target, "dirt");
        Identifier a = Identifier.Parse("umpk:tools/a"), b = Identifier.Parse("umpk:tools/b");
        if (scene >= 2)
            harness.Prototype(a);
        var item = harness.State.Registries!.Items[Identifier.Minecraft("diamond_hoe")];
        ItemStack stack = new(item, 1);
        stack = scene switch
        {
            0 => stack.With(DataComponents.UseCooldown, new UseCooldownComponent(1f, a)),
            1 or 5 => stack.With(DataComponents.UseCooldown, new UseCooldownComponent(1f, null)),
            3 => stack.WithComponents(stack.Components.Without(DataComponents.UseCooldown)),
            4 => stack.With(DataComponents.UseCooldown, new UseCooldownComponent(1f, b)),
            _ => stack,
        };
        Identifier expected = scene is 0 or 2 ? a : scene == 4 ? b : item.Id;
        await harness.Slot(Hand.Main, stack);
        ItemStack observed = harness.State.Inventory.GetSlot(36);
        Assert.Equal(stack.Components.Get(DataComponents.UseCooldown), observed.Components.Get(DataComponents.UseCooldown));
        await harness.GroupCooldown(expected, 5);
        await harness.GroupCooldown(Identifier.Parse("other:tools/a"), 50);

        Assert.True(harness.State.Self.IsItemOnCooldown(observed, protocol));
        await harness.Use(Direction.Up, Hand.Main);
        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sent));

        await harness.GroupCooldown(expected, 0);
        Assert.False(harness.State.Self.IsItemOnCooldown(observed, protocol));
        Assert.Empty(harness.State.Self.ItemCooldowns);
        harness.Sent.Clear();
        await harness.Use(Direction.Up, Hand.Main);
        Assert.Equal(scene == 2 && protocol != 777 ? 2 : 1, harness.Sent.Count);
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

        public void Prototype(Identifier group)
        {
            var registries = State.Registries!;
            var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
            foreach (var item in registries.Items)
                builder.Add(item.NetworkId, item.Id, item.Id == Identifier.Minecraft("diamond_hoe")
                    ? item.Value with { DefaultComponents = new Dictionary<DataComponentType, object> { [DataComponents.UseCooldown] = new UseCooldownComponent(1f, group) } }
                    : item.Value);
            State.Registries = RegistryAccess.FromSnapshot(registries.Snapshot.With(builder.Build()));
        }

        public Task GroupCooldown(Identifier group, int ticks) => Apply("cooldown", new ClientboundCooldownPacket(group, ticks, null));

        public Task Cooldown(Identifier item, int ticks)
        {
            Assert.True(State.Registries!.Items.TryGet(item, out var entry));
            return Apply("cooldown", _protocol >= 768 ? new ClientboundCooldownPacket(item, ticks, null) : new ClientboundCooldownPacket(default, ticks, entry.NetworkId));
        }

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
