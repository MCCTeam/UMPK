using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class BlockUsePredictionRuntimeTests
{
    public static IEnumerable<object[]> ButtonCases()
    {
        foreach (int protocol in JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol >= 498))
            foreach (bool powered in new[] { false, true })
                foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                    yield return [protocol, powered, hand];
    }

    [Theory]
    [MemberData(nameof(ButtonCases))]
    public async Task RegisteredButtonsPreservePoweredConsumeAndHandFallback(int protocol, bool powered, Hand hand)
    {
        foreach (var button in JavaGameData.Registries(protocol).Blocks.Where(entry => entry.Id.Path.EndsWith("_button", StringComparison.Ordinal)))
        {
            await using var harness = new Harness(protocol);
            BlockDefinition definition = button.Value;
            int selectedState = Enumerable.Range(definition.MinStateId, definition.StateCount)
                .First(state => definition.TryGetPropertyValue(state, "powered", out string value) && value == (powered ? "true" : "false"));
            harness.State.World.SetBlockStateId(harness.Target, selectedState);
            harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);

            await harness.Use(hand);

            bool activates = hand == Hand.Main || protocol < 766;
            bool packetSwing = activates && (!powered || protocol == 498) && protocol < 777;
            Assert.True(harness.Sink.Packets.Count == (packetSwing ? 2 : 1), $"{button.Id}, powered={powered}, hand={hand}");
            Assert.Equal((int)hand, Assert.IsType<ServerboundUseItemOnPacket>(harness.Sink.Packets[0]).Hand);
            if (packetSwing)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
            Assert.Equal(selectedState, harness.State.World.GetBlock(harness.Target).StateId);
        }
    }

    public static IEnumerable<object[]> LeverCases()
    {
        foreach (int protocol in JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol >= 498))
            foreach (int scenario in Enumerable.Range(0, 7))
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(LeverCases))]
    public async Task LeverActivationUsesTheNativeHandAndSecondaryUseOrder(int protocol, int scenario)
    {
        await using var harness = new Harness(protocol);
        Assert.True(harness.State.Registries!.Blocks.TryGetValue(Identifier.Minecraft("lever"), out var lever));
        harness.State.World.SetBlockStateId(harness.Target, lever.DefaultStateId);
        int selectedItem = harness.State.Inventory.GetSlot(36).Item.NetworkId;
        Hand hand = scenario == 4 ? Hand.Off : Hand.Main;
        if (scenario is 1 or 3 or 4)
            harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);
        if (scenario is 2 or 6)
            harness.State.Self.SetItemCooldown(selectedItem, 100);
        if (scenario is 3 or 5 or 6)
            harness.State.Self.Sneaking = true;
        if (scenario == 5)
        {
            Assert.True(harness.State.Registries.Items.TryGet(Identifier.Minecraft("stick"), out var stick));
            harness.State.Inventory.SetSlot(0, 36, new ItemStack(stick, 1));
        }

        await harness.Use(hand);

        bool activates = scenario is not 5 and not 6 && (scenario != 4 || protocol < 766);
        bool packetSwing = activates && protocol < 777;
        Assert.Equal(packetSwing ? 2 : 1, harness.Sink.Packets.Count);
        Assert.Equal((int)hand, Assert.IsType<ServerboundUseItemOnPacket>(harness.Sink.Packets[0]).Hand);
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
        Assert.Equal(lever.DefaultStateId, harness.State.World.GetBlock(harness.Target).StateId);
        Assert.True(harness.State.World.GetBlock(harness.Target.Above()).IsAir);
    }

    [Fact]
    public async Task LegacyOffhandCannotSilentlyPlaceTheMainHandStack()
    {
        await using var harness = new Harness(47);

        ActionNotSupportedException error = await Assert.ThrowsAsync<ActionNotSupportedException>(() => harness.Use(Hand.Off));

        Assert.Equal(47, error.Protocol);
        Assert.Empty(harness.Sink.Packets);
        Assert.Equal(32, harness.State.Inventory.GetSlot(36).Count);
    }

    [Fact]
    public async Task LegacyOffhandCannotSilentlyUseTheMainHandStackInAir()
    {
        await using var harness = new Harness(47);

        ActionNotSupportedException error = await Assert.ThrowsAsync<ActionNotSupportedException>(() => harness.Actions.UseItemAsync(Hand.Off));

        Assert.Equal(47, error.Protocol);
        Assert.Empty(harness.Sink.Packets);
        Assert.Equal(32, harness.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task UndefinedAirUseHandCannotReachTheWire(int protocol)
    {
        await using var harness = new Harness(protocol);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Actions.UseItemAsync((Hand)2));

        Assert.Empty(harness.Sink.Packets);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task AvailableOffhandInAirPreservesPacketHand(int protocol)
    {
        await using var harness = new Harness(protocol);
        harness.State.Inventory.SetSlot(0, 45, harness.State.Inventory.GetSlot(36));
        harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);

        await harness.Actions.UseItemAsync(Hand.Off);

        Assert.Equal(1, Assert.IsType<ServerboundUseItemPacket>(Assert.Single(harness.Sink.Packets)).Hand);
        Assert.Equal(32, harness.State.Inventory.GetSlot(45).Count);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task UndefinedHandCannotReachTheWire(int protocol)
    {
        await using var harness = new Harness(protocol);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Use((Hand)2));

        Assert.Empty(harness.Sink.Packets);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task AvailableOffhandKeepsItsOwnStackAndPacketHand(int protocol)
    {
        await using var harness = new Harness(protocol);
        harness.State.Inventory.SetSlot(0, 45, harness.State.Inventory.GetSlot(36));
        harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);

        await harness.Use(Hand.Off);

        Assert.Equal(1, Assert.IsType<ServerboundUseItemOnPacket>(harness.Sink.Packets[0]).Hand);
        Assert.Equal(protocol == 777 ? 1 : 2, harness.Sink.Packets.Count);
        if (protocol != 777)
            Assert.Equal(1, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
        Assert.Equal(32, harness.State.Inventory.GetSlot(45).Count);
        Assert.True(harness.State.World.GetBlock(harness.Target.Above()).IsAir);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ClientState State { get; }
        public ChannelSessionScheduler Scheduler { get; } = new();
        public RecordingSink Sink { get; }
        public InteractionActions Actions { get; }
        public BlockPos Target { get; } = new(1, 64, 1);
        private readonly CancellationTokenSource _session = new();

        public Harness(int protocol)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            State = new ClientState(new ClientFeatures().Normalized());
            RegistryAccess registries = JavaGameData.Registries(protocol);
            State.Registries = registries;
            var world = new Umpk.Game.World.World(WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), null, protocol), new RegistryBlockDataSource(registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
            Assert.True(registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out var stone));
            world.SetBlockStateId(Target, stone.DefaultStateId);
            State.InstallWorld(world);
            Assert.True(registries.Items.TryGet(Identifier.Minecraft("stone"), out var item));
            State.Inventory.SetSlot(0, 36, new ItemStack(item, 32));
            var sequences = new SequenceTracker();
            Sink = new RecordingSink(protocol, sequences);
            var services = new ClientSessionServices
            {
                Version = version,
                Options = new(),
                Policies = new(),
                State = State,
                Wire = new(version),
                Logger = NullLogger.Instance,
                Scheduler = Scheduler,
                ReadPhysicsState = () => new PhysicsState { BoundingBox = Aabb.OfSize(.5, 64, -1.5, .6, 1.8) },
                CurrentSessionCancellation = () => _session.Token,
            };
            Actions = new InteractionActions(Sink, services, sequences);
        }

        public Task Use(Hand hand = Hand.Main) => Actions.PlaceBlockAsync(Target, Direction.Up, new Vec3d(.5, 1, .5), hand).WaitAsync(TimeSpan.FromSeconds(5));

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await Scheduler.DisposeAsync();
            _session.Dispose();
        }
    }

    private sealed class RecordingSink(int protocol, SequenceTracker sequences) : IPacketSink
    {
        public List<object> Packets { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _ = BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<IPacket>(packet));
            Packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                sequences.Acknowledge(use.Sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
