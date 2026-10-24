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

public sealed class LegacyHoeUsePredictionRuntimeTests
{
    private static IEnumerable<int> Protocols => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol < 498);

    public static IEnumerable<object[]> SoilCases()
    {
        foreach (int protocol in Protocols)
            foreach (int scene in Enumerable.Range(0, 10))
                foreach (Hand hand in protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off })
                    yield return [protocol, scene, hand];
    }

    [Theory]
    [MemberData(nameof(SoilCases))]
    public async Task OriginalHoeRulesReachLegacyAndFlattenedUsePackets(int protocol, int scene, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        string name = scene switch
        {
            1 => protocol < 393 ? "grass" : "grass_block",
            2 or 3 => protocol < 393 ? "dirt" : scene == 2 ? "coarse_dirt" : "podzol",
            4 => "stone",
            8 => protocol == 47 ? "stone" : "grass_path",
            9 => protocol < 393 ? "tallgrass" : "grass",
            _ => "dirt",
        };
        int metadata = protocol < 393 && scene is 2 or 3 ? scene - 1 : 0;
        await harness.Block(harness.Target, name, metadata);
        if (scene == 6)
            await harness.Block(harness.Target.Above(), "stone");
        if (scene == 7)
            await harness.GameMode(GameMode.Creative);
        Direction face = scene == 5 ? Direction.Down : Direction.Up;
        bool success = scene is 0 or 1 or 2 or 7 || scene == 8 && protocol != 47;
        int state = harness.State.World.GetBlock(harness.Target).StateId;
        foreach (var item in harness.State.Registries!.Items.Where(entry => entry.Id.Path.EndsWith("_hoe", StringComparison.Ordinal)))
        {
            await harness.Slot(hand, new ItemStack(item, 1));
            int before = harness.Sent.Count;

            await harness.Use(face, hand);

            Assert.Equal(before + (success ? 2 : 1), harness.Sent.Count);
            harness.AssertUse(harness.Sent[before], face, hand, item.Id);
            if (success)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[before + 1]).Hand);
            Assert.Equal(state, harness.State.World.GetBlock(harness.Target).StateId);
            Assert.Equal(1, harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count);
        }
    }

    public static IEnumerable<object[]> WornCases()
    {
        foreach (int protocol in Protocols)
            foreach (Hand hand in protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off })
                yield return [protocol, hand];
    }

    [Theory]
    [MemberData(nameof(WornCases))]
    public async Task LegacyDamageAndUnrelatedNbtDoNotSuppressTheNativeClientResult(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        var tag = new NbtCompound();
        tag.PutString("unrelated", "retained native data");
        tag.PutInt("Damage", 7);
        ItemStack stack = new ItemStack(item, 1).With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        if (protocol < 393)
            stack = stack.With(DataComponents.Damage, new DamageComponent(7));
        await harness.Slot(hand, stack);
        Assert.Equal(7, harness.Durability(hand));

        await harness.Use(Direction.Up, hand);

        Assert.Equal(2, harness.Sent.Count);
        harness.AssertUse(harness.Sent[0], Direction.Up, hand, item.Id);
        Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
        Assert.Equal(7, harness.Durability(hand));
    }

    public static IEnumerable<object[]> PermissionCases()
    {
        foreach (int protocol in Protocols)
            foreach (bool cooldown in protocol == 47 ? new[] { false } : new[] { false, true })
                yield return [protocol, cooldown];
    }

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public async Task ExistingAdventureAndCooldownOwnersKeepTheToolPacketOnly(int protocol, bool cooldown)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        if (cooldown)
            await harness.Cooldown(item.NetworkId);
        else
            await harness.GameMode(GameMode.Adventure);

        await harness.Use(Direction.Up, Hand.Main);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, Hand.Main, item.Id);
    }

    public static IEnumerable<object[]> UnclassifiedLegacyStates()
    {
        foreach (int protocol in Protocols.Where(protocol => protocol < 393))
        {
            yield return [protocol, "dirt", 3];
            yield return [protocol, "dirt", 15];
            yield return [protocol, "grass", 1];
            if (protocol != 47)
                yield return [protocol, "grass_path", 1];
        }
    }

    [Theory]
    [MemberData(nameof(UnclassifiedLegacyStates))]
    public async Task UnclassifiedLegacyWireStatesRemainUnknown(int protocol, string name, int metadata)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, name, metadata);
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));

        await harness.Use(Direction.Up, Hand.Main);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, Hand.Main, item.Id);
        VanillaHoeUse.ForProtocol(protocol).TryPredict(item.Id, harness.State.World.GetBlock(harness.Target), harness.State.World, harness.Target, Direction.Up, out BlockUsePrediction prediction);
        Assert.Equal(BlockUseResult.Unknown, prediction.Result);
    }

    [Fact]
    public async Task Protocol47RejectsAnOffhandBeforeItsMainHandFallback()
    {
        await using var harness = new Harness(47);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "dirt");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_hoe"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));

        await Assert.ThrowsAsync<ActionNotSupportedException>(() => harness.Use(Direction.Up, Hand.Off));

        Assert.Empty(harness.Sent);
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

        public Harness(int protocol)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            _applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            var services = new ClientSessionServices { Version = version, Options = new(), Policies = new(), State = State, Wire = new(version), Logger = NullLogger.Instance, Scheduler = _scheduler, CurrentSessionCancellation = () => _session.Token };
            _actions = new InteractionActions(new Sink(Sent, _applier.Sequences, protocol), services, _applier.Sequences);
        }

        public async Task ReceiveTerrain()
        {
            byte[] bytes;
            if (_protocol == 47)
                bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
            else if (_protocol >= 477)
                bytes = ChunkCodecs.EncodeStructural(new Pre1_18ChunkWireBody(0, 0, true, 0, new NbtCompound(), [], new byte[1024], [0], []));
            else
            {
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteBool(true);
                writer.WriteVarInt(0);
                int biomes = _protocol >= 393 ? 1024 : 256;
                writer.WriteVarInt(biomes);
                writer.WriteBytes(new byte[biomes]);
                if (_protocol >= 110)
                    writer.WriteVarInt(0);
                bytes = output.WrittenSpan.ToArray();
            }
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Reencode(codec, packet, context));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _session.Token);
            Assert.True(State.EntityMembership.HasColumn(Target));
        }

        public Task Block(BlockPos position, string name, int metadata = 0)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out var block));
            int state = _protocol < 393 ? block.DefaultStateId | metadata : block.DefaultStateId;
            return Apply("block_update", new ClientboundBlockUpdatePacket(position, state));
        }

        public Task Slot(Hand hand, ItemStack item) => Apply(_protocol == 47 ? "set_slot" : "container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, item));
        public Task GameMode(GameMode mode) => Apply("game_event", new ClientboundGameEventPacket(3, (float)mode));
        public Task Cooldown(int item) => Apply("cooldown", new ClientboundCooldownPacket(default, 100, item));
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
                Assert.Equal(expected.Item.Damage, observed.Item.Damage);
            }
            else
                Assert.Equal(packet, decoded);
            Assert.Equal(original.WrittenSpan.ToArray(), Reencode(codec, decoded, context));
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _session.Token);
        }
        private static byte[] Reencode(BoundPacketCodec codec, object packet, PacketCodecContext context)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, context);
            return output.WrittenSpan.ToArray();
        }

        public int Durability(Hand hand)
        {
            ItemStack item = State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
            return _protocol < 393 ? item.Damage : item.Components.Get(DataComponents.LegacyNbt)!.Nbt.GetInt("Damage");
        }

        public Task Use(Direction face, Hand hand) => _actions.PlaceBlockAsync(Target, face, new Vec3d(.5, 1, .5), hand, ct: _session.Token).WaitAsync(TimeSpan.FromSeconds(5));
        public void AssertUse(object packet, Direction face, Hand hand, Identifier item)
        {
            if (_protocol == 47)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packet);
                Assert.Equal(Target, use.Position);
                Assert.Equal((int)face, use.Face);
                Assert.Equal(item, use.HeldItem.Item.Id);
                Assert.Equal((byte)8, use.CursorX);
                Assert.Equal((byte)16, use.CursorY);
                Assert.Equal((byte)8, use.CursorZ);
            }
            else
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
                Assert.Equal(Target, use.Position);
                Assert.Equal((int)face, use.Face);
                Assert.Equal((int)hand, use.Hand);
            }
        }

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
            byte[] body = BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<IPacket>(packet));
            if (protocol == 47 && packet is ServerboundSwingPacket)
                Assert.Empty(body);
            else
                Assert.NotEmpty(body);
            packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                sequences.Acknowledge(use.Sequence);
            return ValueTask.CompletedTask;
        }
        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
