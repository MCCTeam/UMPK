using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class MiningItemIdentityRuntimeTests
{
    public static IEnumerable<object[]> LegacyProtocols => JavaVersions.All.Where(row => row.Version.Protocol < 393).Select(row => new object[] { row.Version.Protocol });

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public async Task WireDurabilityChangeRetainsLegacyDestroyProgress(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await fixture.HoldAsync("diamond_pickaxe", 1);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.HoldAsync("diamond_pickaxe", 2);
            for (int tick = 1; tick < 6; tick++)
                await fixture.TickAsync();

            Assert.Equal(new[] { 0, 2 }, fixture.ActionsSent.Select(packet => packet.Action));
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, fixture.Sent.Count(packet => packet is ServerboundSwingPacket));
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public async Task UnbreakableLegacyToolStillComparesWireDamage(int protocol)
        => await AssertRestartAsync(protocol, "diamond_pickaxe", unbreakable: true);

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public async Task LegacySubtypeChangeRestartsTheDestroyTarget(int protocol)
        => await AssertRestartAsync(protocol, "wool");

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(498)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(769)]
    [InlineData(777)]
    public async Task FlattenedDamageRemainsPartOfTheTagOrComponentIdentity(int protocol)
        => await AssertRestartAsync(protocol, "diamond_pickaxe");

    [Theory]
    [InlineData(47, "diamond_helmet")]
    [InlineData(47, "fishing_rod")]
    [InlineData(47, "shears")]
    [InlineData(47, "bow")]
    [InlineData(107, "elytra")]
    [InlineData(340, "shield")]
    public async Task LegacyDurabilityIdentityAppliesToEveryHeldDamageableFamily(int protocol, string item)
    {
        await using var fixture = new Fixture(protocol, "bedrock");
        await fixture.HoldAsync(item, 1);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.HoldAsync(item, 2);
            await fixture.TickAsync();
            Assert.Equal(new[] { 0 }, fixture.ActionsSent.Select(packet => packet.Action));
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(777)]
    public async Task CountChangesRetainTheTargetButNbtOrComponentsRestartIt(int protocol)
    {
        await using var fixture = new Fixture(protocol, "bedrock");
        await fixture.HoldAsync("diamond_pickaxe", 1);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.HoldAsync("diamond_pickaxe", 1, count: 2);
            await fixture.TickAsync();
            Assert.Equal(new[] { 0 }, fixture.ActionsSent.Select(packet => packet.Action));
            await fixture.HoldAsync("diamond_pickaxe", 1, unbreakable: true);
            await fixture.TickAsync();
            Assert.Equal(new[] { 0, protocol == 777 ? 2 : 1, 0 }, fixture.ActionsSent.Select(packet => packet.Action));
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static async Task AssertRestartAsync(int protocol, string item, bool unbreakable = false)
    {
        await using var fixture = new Fixture(protocol, "bedrock");
        await fixture.HoldAsync(item, 1, unbreakable);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.HoldAsync(item, 2, unbreakable);
            await fixture.TickAsync();
            Assert.Equal(new[] { 0, protocol == 777 ? 2 : 1, 0 }, fixture.ActionsSent.Select(packet => packet.Action));
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(1, 64, 1);
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _session = new();
        private readonly ApplierHarness _harness;
        private readonly int _protocol;
        internal InteractionActions Actions { get; }
        internal List<object> Sent { get; } = [];
        internal IEnumerable<ServerboundPlayerActionPacket> ActionsSent => Sent.OfType<ServerboundPlayerActionPacket>();
        internal ClientState State => _harness.State;
        internal Umpk.Physics.PhysicsState? Physics { get; set; }

        internal Fixture(int protocol, string block = "stone")
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            _harness = new ApplierHarness(version);
            _harness.State.Registries = JavaGameData.Registries(protocol);
            var world = new Umpk.Game.World.World(WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), null, protocol), new RegistryBlockDataSource(_harness.State.Registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
            Assert.True(_harness.State.Registries.Blocks.TryGetValue(Identifier.Minecraft(block), out BlockDefinition? definition));
            Assert.True(_harness.State.Registries.Blocks.TryGetNetworkId(Identifier.Minecraft(block), out int blockId));
            _harness.State.RegistryTags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft(block == "clay" ? "mineable/shovel" : "mineable/pickaxe"), [blockId])])]);
            world.SetBlockStateId(Target, definition.DefaultStateId);
            _harness.State.InstallWorld(world);
            var services = new ClientSessionServices
            {
                Version = version,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = _harness.State,
                Wire = new WireIndex(version),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                CurrentSessionCancellation = () => _session.Token,
                ReadPhysicsState = () => Physics,
            };
            Actions = new InteractionActions(new Sink(Sent, _harness.Sequences), services, _harness.Sequences);
        }

        internal Task HoldAsync(string name, int damage, bool unbreakable = false, int count = 1)
        {
            Assert.True(_harness.State.Registries!.Items.TryGet(Identifier.Minecraft(name), out RegistryEntry<ItemDefinition> item));
            ItemStack stack = new(item, count);
            if (_protocol < 393 || _protocol >= 766)
                stack = stack.With(DataComponents.Damage, new DamageComponent(damage));
            var tag = new NbtCompound();
            if (_protocol is >= 393 and < 766)
                tag.PutInt("Damage", damage);
            if (unbreakable)
            {
                if (_protocol >= 766)
                    stack = stack.With(DataComponents.Unbreakable, new UnbreakableComponent());
                else
                    tag.PutBool("Unbreakable", true);
            }
            if (tag.Count > 0)
                stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
            return SetSlotAsync(36, stack);
        }

        internal Task SetSlotAsync(short slot, ItemStack stack)
            => ApplyAsync(_protocol == 47 ? "set_slot" : "container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, slot, stack));

        internal Task ApplyAsync(string identifier, object packet)
        {
            object decoded = BoundDescriptorCodec.RoundTrip(_protocol, identifier, packet);
            return _scheduler.InvokeAsync(() => _harness.ApplyAsync(decoded), CancellationToken.None);
        }

        internal Task TickAsync() => _scheduler.InvokeAsync(() => Actions.TickMiningOnLoopAsync(_session.Token), CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await _scheduler.DisposeAsync();
            _session.Dispose();
        }
    }

    private sealed class Sink(List<object> sent, SequenceTracker sequences) : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            sent.Add(packet);
            if (packet is ServerboundPlayerActionPacket { Sequence: int sequence })
                sequences.Acknowledge(sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
