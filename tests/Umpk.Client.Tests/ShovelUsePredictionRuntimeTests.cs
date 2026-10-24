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

public sealed class ShovelUsePredictionRuntimeTests
{
    private static IEnumerable<int> Protocols => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol <= 776);
    private static IEnumerable<Hand> Hands(int protocol) => protocol == 47 ? [Hand.Main] : [Hand.Main, Hand.Off];

    public static IEnumerable<object[]> PathCases()
    {
        foreach (int protocol in Protocols)
            foreach (int scene in Enumerable.Range(0, 12))
                foreach (Hand hand in Hands(protocol))
                    yield return [protocol, scene, hand];
    }

    [Theory]
    [MemberData(nameof(PathCases))]
    public async Task NativePathTargetsAndPassesReachTheSessionPacketGroup(int protocol, int scene, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        string name = scene switch
        {
            1 or 2 or 3 => protocol < 393 ? "dirt" : scene == 1 ? "dirt" : scene == 2 ? "coarse_dirt" : "podzol",
            4 => "mycelium",
            5 => "stone",
            9 => protocol == 47 ? "stone" : protocol >= 755 ? "dirt_path" : "grass_path",
            10 => protocol < 393 ? "tallgrass" : protocol >= 765 ? "short_grass" : "grass",
            11 => protocol >= 755 ? "rooted_dirt" : "stone",
            _ => protocol < 393 ? "grass" : "grass_block",
        };
        int metadata = protocol < 393 && scene is 2 or 3 ? scene - 1 : 0;
        await harness.Block(harness.Target, name, metadata);
        if (scene == 7)
            await harness.Block(harness.Target.Above(), "stone");
        if (scene == 8)
            await harness.GameMode(GameMode.Creative);
        bool succeeds = protocol >= 107 && (scene is 0 or 8 || protocol >= 755 && scene is 1 or 2 or 3 or 4 or 11);
        Direction face = scene == 6 ? Direction.Down : Direction.Up;
        int state = harness.State.World.GetBlock(harness.Target).StateId;
        foreach (var item in harness.State.Registries!.Items.Where(entry => entry.Id.Path.EndsWith("_shovel", StringComparison.Ordinal)))
        {
            await harness.Slot(hand, new ItemStack(item, 1));
            int before = harness.Sent.Count;

            await harness.Use(face, hand);

            Assert.Equal(before + (succeeds ? 2 : 1), harness.Sent.Count);
            harness.AssertUse(harness.Sent[before], face, hand, item.Id);
            if (succeeds)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[before + 1]).Hand);
            Assert.Equal(state, harness.State.World.GetBlock(harness.Target).StateId);
            Assert.Equal(1, harness.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45).Count);
        }
    }

    public static IEnumerable<object[]> CampfireCases()
    {
        foreach (int protocol in Protocols.Where(protocol => protocol >= 477))
            foreach (int scene in Enumerable.Range(0, 4))
                foreach (bool sneaking in new[] { false, true })
                    foreach (Hand hand in Hands(protocol))
                        yield return [protocol, scene, sneaking, hand];
    }

    [Theory]
    [MemberData(nameof(CampfireCases))]
    public async Task SecondaryUseReachesTheNativeCampfireToolResultWithoutGuessingRecipes(int protocol, int scene, bool sneaking, Hand hand)
    {
        foreach (var block in JavaGameData.Registries(protocol).Blocks.Where(entry => entry.Id.Path is "campfire" or "soul_campfire"))
        {
            await using var harness = new Harness(protocol);
            await harness.ReceiveTerrain();
            await harness.Block(harness.Target, block.Id.Path, lit: scene != 1);
            if (scene == 3)
                await harness.Block(harness.Target.Above(), "stone");
            Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
            await harness.Slot(hand, new ItemStack(item, 1));
            if (sneaking)
                await harness.Sneak();
            Direction face = scene == 2 ? Direction.Down : Direction.Up;
            bool succeeds = protocol >= 573 && sneaking && scene is 0 or 3;
            int state = harness.State.World.GetBlock(harness.Target).StateId;

            await harness.Use(face, hand);

            Assert.Equal(succeeds ? 2 : 1, harness.Sent.Count);
            harness.AssertUse(harness.Sent[0], face, hand, item.Id);
            if (succeeds)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sent[1]).Hand);
            Assert.Equal(state, harness.State.World.GetBlock(harness.Target).StateId);
        }
    }

    public static IEnumerable<object[]> WornCases()
    {
        foreach (int protocol in Protocols.Where(protocol => protocol >= 107))
            foreach (Hand hand in Hands(protocol))
                yield return [protocol, hand];
    }

    [Theory]
    [MemberData(nameof(WornCases))]
    public async Task TheNativeDamageRepresentationDoesNotSuppressASecondPathUse(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, protocol < 393 ? "grass" : "grass_block");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
        ItemStack stack = new(item, 1);
        if (protocol >= 766)
            stack = stack.With(DataComponents.Damage, new DamageComponent(7));
        else
        {
            var tag = new NbtCompound();
            tag.PutInt("Damage", 7);
            tag.PutString("unrelated", "retained native data");
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
            if (protocol < 393)
                stack = stack.With(DataComponents.Damage, new DamageComponent(7));
        }
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
    public async Task AdventureAndCooldownOrderingStayWithTheirExistingOwners(int protocol, bool cooldown)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, protocol < 393 ? "grass" : "grass_block");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        if (cooldown)
            await harness.Cooldown(item.NetworkId);
        else
            await harness.GameMode(GameMode.Adventure);

        await harness.Use(Direction.Up, Hand.Main);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, Hand.Main, item.Id);
    }

    public static IEnumerable<object[]> MissingTerrainCases()
    {
        foreach (int protocol in new[] { 107, 498, 755, 776 })
            foreach (bool entities in new[] { false, true })
                foreach (int scene in Enumerable.Range(0, 4))
                    yield return [protocol, entities, scene];
    }

    [Theory]
    [MemberData(nameof(MissingTerrainCases))]
    public async Task ACacheColumnCannotReplaceReceivedNativeTerrain(int protocol, bool entities, int scene)
    {
        await using var harness = new Harness(protocol, entities);
        if (scene != 0)
            await harness.ReceiveTerrain();
        if (scene is 2 or 3)
            await harness.ForgetTerrain();
        if (scene == 3)
            await harness.ReceiveTerrain();
        await harness.Block(harness.Target, protocol < 393 ? "grass" : "grass_block");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
        await harness.Slot(Hand.Main, new ItemStack(item, 1));
        bool known = scene is 1 or 3;
        Assert.Equal(known, harness.State.EntityMembership.HasColumn(harness.Target));
        Assert.NotNull(harness.State.World.GetColumn(harness.Target));

        await harness.Use(Direction.Up, Hand.Main);

        Assert.Equal(known ? 2 : 1, harness.Sent.Count);
        harness.AssertUse(harness.Sent[0], Direction.Up, Hand.Main, item.Id);
    }

    [Theory]
    [InlineData(766, Hand.Main)]
    [InlineData(766, Hand.Off)]
    [InlineData(776, Hand.Main)]
    [InlineData(776, Hand.Off)]
    public async Task UnclassifiedModernComponentPatchesRemainPacketOnly(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "grass_block");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1).With(DataComponents.Unbreakable, new UnbreakableComponent()));

        await harness.Use(Direction.Up, hand);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, hand, item.Id);
    }

    [Theory]
    [InlineData(777, Hand.Main)]
    [InlineData(777, Hand.Off)]
    public async Task TheDataDrivenTransformerEraIsNotInferredFromTheOldClass(int protocol, Hand hand)
    {
        await using var harness = new Harness(protocol);
        await harness.ReceiveTerrain();
        await harness.Block(harness.Target, "grass_block");
        Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_shovel"), out var item));
        await harness.Slot(hand, new ItemStack(item, 1));

        await harness.Use(Direction.Up, hand);

        harness.AssertUse(Assert.Single(harness.Sent), Direction.Up, hand, item.Id);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly int _protocol;
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _session = new(TimeSpan.FromSeconds(30));
        private readonly InteractionActions _actions;
        private readonly MovementActions _movement;
        public BlockPos Target { get; } = new(1, 64, 1);
        public ClientState State => _applier.State;
        public List<object> Sent { get; } = [];

        public Harness(int protocol, bool entities = true)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            _applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = entities, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            var services = new ClientSessionServices { Version = version, Options = new(), Policies = new(), State = State, Wire = new(version), Logger = NullLogger.Instance, Scheduler = _scheduler, CurrentSessionCancellation = () => _session.Token };
            var sink = new Sink(Sent, _applier.Sequences, protocol);
            _actions = new InteractionActions(sink, services, _applier.Sequences);
            _movement = new MovementActions(sink, services, () => null);
        }

        public async Task ReceiveTerrain()
        {
            byte[] bytes;
            if (_protocol == 47)
                bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
            else if (_protocol >= 757)
            {
                var empty = new ChunkPalettedContainerWire(0, 0, null, []);
                var sections = Enumerable.Range(0, (State.World.Dimension.MaxY - State.World.Dimension.MinY) / 16)
                    .Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
                byte[] tail = new byte[_protocol <= 762 ? 8 : 7];
                ChunkWireBody body = _protocol >= 770
                    ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], tail)
                    : _protocol >= 768 ? new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], tail)
                    : new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], tail, _protocol >= 764 ? NbtWireFormat.JavaUnnamedRoot : NbtWireFormat.JavaNamedRoot);
                bytes = ChunkCodecs.EncodeStructural(body);
            }
            else if (_protocol >= 735)
            {
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                writer.WriteInt(0);
                writer.WriteInt(0);
                if (_protocol < 755)
                    writer.WriteBool(true);
                if (_protocol <= 736)
                    writer.WriteBool(false);
                writer.WriteVarInt(0);
                writer.WriteNbt(new NbtCompound(), NbtWireFormat.JavaNamedRoot);
                if (_protocol <= 736)
                    writer.WriteBytes(new byte[4096]);
                else
                {
                    writer.WriteVarInt(1024);
                    writer.WriteBytes(new byte[1024]);
                }
                writer.WriteVarInt(0);
                writer.WriteVarInt(0);
                bytes = output.WrittenSpan.ToArray();
            }
            else if (_protocol >= 477)
                bytes = ChunkCodecs.EncodeStructural(new Pre1_18ChunkWireBody(0, 0, true, 0, new NbtCompound(), [], _protocol < 573 ? new byte[1024] : [], [0], _protocol >= 573 ? new byte[4096] : []));
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

        public Task Block(BlockPos position, string name, int metadata = 0, bool? lit = null)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out var block));
            int state = _protocol < 393 ? block.DefaultStateId | metadata : block.DefaultStateId;
            if (lit is { } expected)
                state = Enumerable.Range(block.MinStateId, block.StateCount).First(id => block.TryGetPropertyValue(id, "lit", out string value) && value == (expected ? "true" : "false"));
            return Apply("block_update", new ClientboundBlockUpdatePacket(position, state));
        }

        public Task Slot(Hand hand, ItemStack item) => Apply(_protocol == 47 ? "set_slot" : "container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, item));
        public Task GameMode(GameMode mode) => Apply("game_event", new ClientboundGameEventPacket(3, (float)mode));
        public Task Cooldown(int item) => Apply("cooldown", new ClientboundCooldownPacket(_protocol >= 768 ? Identifier.Minecraft("diamond_shovel") : default, 100, _protocol < 768 ? item : null));
        public Task ForgetTerrain() => Apply("forget_level_chunk", new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        public async Task Sneak()
        {
            await _scheduler.InvokeAsync(() => new ValueTask(_movement.SetSneakingAsync(true, _session.Token)), _session.Token);
            Assert.True(State.Self.Sneaking);
            Assert.Single(Sent);
            Assert.True(Sent[0] is ServerboundPlayerCommandPacket or ServerboundPlayerInputPacket);
            Sent.Clear();
        }
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
            return _protocol < 393 || _protocol >= 766 ? item.Damage : item.Components.Get(DataComponents.LegacyNbt)!.Nbt.GetInt("Damage");
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
