using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PlacementAnimationTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    public async Task MetadataEraPlacementWritesUseThenSwing(int protocol)
    {
        await using var harness = new Harness(protocol);

        await harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, harness.Sink.Packets.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(harness.Sink.Packets[0]);
            Assert.Equal(harness.Target, use.Position);
            Assert.Equal(32, use.HeldItem.Count);
        }
        else
            Assert.IsType<ServerboundUseItemOnPacket>(harness.Sink.Packets[0]);
        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
        Assert.True(harness.State.World.GetBlock(harness.Target.Above()).IsAir);
        Assert.Equal(32, harness.State.Inventory.GetSlot(36).Count);
    }
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(480)]
    [InlineData(485)]
    [InlineData(490)]
    [InlineData(498)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(776)]
    public async Task PredictedPlacementWritesSwingBeforeWaitingForAcknowledgment(int protocol)
    {
        await using var harness = new Harness(protocol);

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Collection(harness.Sink.Packets,
            packet => Assert.IsType<ServerboundUseItemOnPacket>(packet),
            packet => Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand));
        Assert.True(harness.State.World.GetBlock(harness.Target.Above()).IsAir);
        Assert.Equal(32, harness.State.Inventory.GetSlot(36).Count);
        if (protocol >= 759)
            Assert.False(place.IsCompleted);
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OffhandPlacementSwingsTheOffhand()
    {
        await using var harness = new Harness(770);
        harness.State.Inventory.SetSlot(0, 45, harness.State.Inventory.GetSlot(36));
        harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5), Hand.Off);
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(1, Assert.IsType<ServerboundUseItemOnPacket>(harness.Sink.Packets[0]).Hand);
        Assert.Equal(1, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(498, 0.00000005, true)]
    [InlineData(766, 0.00000005, true)]
    [InlineData(770, 0.00000005, true)]
    [InlineData(776, 0.00000005, true)]
    [InlineData(770, 0.0000002, false)]
    public async Task PlacementUsesNativeVoxelContactTolerance(int protocol, double overlap, bool swings)
    {
        await using var harness = new Harness(protocol);
        double minX = harness.Target.X + 1.0 - overlap;
        harness.Physics = harness.Physics with { BoundingBox = new Aabb(minX, 65, 1.2, minX + 0.6, 66.8, 1.8) };

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(swings ? 2 : 1, harness.Sink.Packets.Count);
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(498, "item")]
    [InlineData(766, "item")]
    [InlineData(770, "item")]
    [InlineData(498, "experience_orb")]
    [InlineData(770, "experience_orb")]
    [InlineData(770, "arrow")]
    public async Task NonblockingEntitiesDoNotSuppressSuccessfulPlacementSwing(int protocol, string type)
    {
        await using var harness = new Harness(protocol);
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft(type), out RegistryEntry<EntityTypeDefinition> entry));
        harness.State.Entities.Add(new Entity(42, Guid.NewGuid(), entry) { Position = new Vec3d(1.5, 65, 1.5) });

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(2, harness.Sink.Packets.Count);
        Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]);
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(498, 8.5, true)]
    [InlineData(754, 8.5, true)]
    [InlineData(766, 8.5, true)]
    [InlineData(770, 8.5, true)]
    [InlineData(776, 8.5, true)]
    [InlineData(770, 1.5, false)]
    [InlineData(770, 2.9999998, false)]
    [InlineData(770, 2.99999995, true)]
    public async Task FixedEntityBoundsOnlySuppressSwingWhenTheyObstruct(int protocol, double x, bool swings)
    {
        await using var harness = new Harness(protocol);
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("end_crystal"), out RegistryEntry<EntityTypeDefinition> type));
        harness.State.Entities.Add(new Entity(42, Guid.NewGuid(), type) { Position = new Vec3d(x, 65, 1.5) });

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(swings ? 2 : 1, harness.Sink.Packets.Count);
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("self-collision")]
    [InlineData("spectator")]
    [InlineData("cooldown")]
    [InlineData("pass")]
    public async Task AnUnsuccessfulLocalUseDoesNotInventASwing(string condition)
    {
        await using var harness = new Harness(770);
        switch (condition)
        {
            case "occupied": harness.State.World.SetBlockStateId(harness.Target.Above(), harness.StoneState); break;
            case "self-collision": harness.Physics = harness.Physics with { BoundingBox = Aabb.BlockAt(harness.Target.X, harness.Target.Y + 1, harness.Target.Z) }; break;
            case "spectator": harness.State.Self.GameMode = GameMode.Spectator; break;
            case "cooldown": harness.State.Self.SetItemCooldown(harness.State.Inventory.GetSlot(36).Item.NetworkId, 10); break;
            case "pass":
                Assert.True(harness.State.Registries!.Items.TryGet(Identifier.Minecraft("stick"), out RegistryEntry<ItemDefinition> stick));
                harness.State.Inventory.SetSlot(0, 36, new ItemStack(stick, 1));
                break;
        }

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sink.Packets));
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Version26_3DoesNotSendAPunchForSuccessfulPlacement()
    {
        await using var harness = new Harness(777);

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sink.Packets));
        harness.Acknowledge();
        await place.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(47, true)]
    [InlineData(107, true)]
    [InlineData(340, true)]
    [InlineData(393, true)]
    [InlineData(490, true)]
    [InlineData(498, true)]
    [InlineData(754, true)]
    [InlineData(766, true)]
    [InlineData(767, true)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(770, false)]
    [InlineData(776, false)]
    [InlineData(777, false)]
    public async Task SpectatorBlockUsePreservesNativeSwingResult(int protocol, bool swings)
    {
        await using var harness = new Harness(protocol);
        harness.State.Self.GameMode = GameMode.Spectator;
        harness.State.Inventory.SetSlot(0, 36, ItemStack.Empty);
        harness.State.World.SetBlockStateId(harness.Target.Above(), harness.StoneState);
        harness.Physics = harness.Physics with { BoundingBox = Aabb.BlockAt(harness.Target.X, harness.Target.Y + 1, harness.Target.Z) };

        Task use = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(swings ? 2 : 1, harness.Sink.Packets.Count);
        if (swings)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]).Hand);
        if (protocol != 47)
            harness.Acknowledge();
        await use.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(harness.StoneState, harness.State.World.GetBlock(harness.Target.Above()).StateId);
        Assert.True(harness.State.Inventory.GetSlot(36).IsEmpty);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(767)]
    public async Task SpectatorSwingDoesNotRequireInventoryOrEntityTracking(int protocol)
    {
        await using var harness = new Harness(protocol, new ClientFeatures { Inventory = false, Entities = false });
        harness.State.Self.GameMode = GameMode.Spectator;

        Task use = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Equal(2, harness.Sink.Packets.Count);
        Assert.IsType<ServerboundSwingPacket>(harness.Sink.Packets[1]);
        if (protocol != 47)
            harness.Acknowledge();
        await use.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PlacementRejectsAnAcknowledgmentWaitOnTheSessionLoop()
    {
        await using var harness = new Harness(770);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Scheduler.InvokeAsync(
            () => new ValueTask(harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5))), CancellationToken.None));

        Assert.Empty(harness.Sink.Packets);
    }

    [Fact]
    public async Task SessionTeardownCancelsTheOutstandingAcknowledgment()
    {
        await using var harness = new Harness(770);
        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await harness.Sink.UseWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        harness.SessionLifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => place.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, harness.Sink.Packets.Count);
    }

    [Fact]
    public async Task ADisconnectedClientDoesNotWriteBlockUse()
    {
        await using var harness = new Harness(770);
        harness.Connected = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5)));

        Assert.Empty(harness.Sink.Packets);
    }

    [Fact]
    public async Task ATargetOutsideTheWorldBorderDoesNotWriteBlockUse()
    {
        await using var harness = new Harness(770);
        harness.State.World.SetBorder(new Umpk.Game.World.WorldBorderState(0, 0, 1, 1, 0, 5, 15));

        await harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(harness.Sink.Packets);
    }

    [Fact]
    public async Task CallerCancellationAfterUseOnWriteStillFinishesItsLiveAnimation()
    {
        await using var harness = new Harness(770);
        using var caller = new CancellationTokenSource();
        harness.Sink.AfterUse = caller.Cancel;

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5), ct: caller.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => place.WaitAsync(TimeSpan.FromSeconds(5)));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.Collection(harness.Sink.Packets,
            packet => Assert.IsType<ServerboundUseItemOnPacket>(packet),
            packet => Assert.IsType<ServerboundSwingPacket>(packet));
    }

    [Fact]
    public async Task SessionCancellationAfterUseOnDoesNotWriteAnAnimationToTheDeadSession()
    {
        await using var harness = new Harness(770);
        harness.Sink.AfterUse = harness.SessionLifetime.Cancel;

        Task place = harness.Actions.PlaceBlockAsync(harness.Target, Direction.Up, new Vec3d(0.5, 1, 0.5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => place.WaitAsync(TimeSpan.FromSeconds(5)));
        await harness.Scheduler.InvokeAsync(() => { }, CancellationToken.None);

        Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(harness.Sink.Packets));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ClientState State { get; }
        public ChannelSessionScheduler Scheduler { get; } = new();
        public RecordingSink Sink { get; } = new();
        public InteractionActions Actions { get; }
        public BlockPos Target { get; } = new(1, 64, 1);
        public int StoneState { get; }
        public PhysicsState Physics { get; set; } = new() { BoundingBox = Aabb.OfSize(0.5, 64, -1.5, 0.6, 1.8) };
        public CancellationTokenSource SessionLifetime { get; } = new();
        public bool Connected { get; set; } = true;
        private readonly SequenceTracker _sequences = new();

        public Harness(int protocol, ClientFeatures? features = null)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            State = new ClientState((features ?? new ClientFeatures()).Normalized());
            RegistryAccess registries = JavaGameData.Registries(protocol);
            Assert.True(registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            StoneState = stone.DefaultStateId;
            var world = new Umpk.Game.World.World(WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), null, protocol), new RegistryBlockDataSource(registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
            world.SetBlockStateId(Target, StoneState);
            State.Registries = registries;
            State.InstallWorld(world);
            Assert.True(registries.Items.TryGet(Identifier.Minecraft("stone"), out RegistryEntry<ItemDefinition> item));
            if (State.Features.Inventory)
                State.Inventory.SetSlot(0, 36, new ItemStack(item, 32));
            var services = new ClientSessionServices { Version = version!, Options = new(), Policies = new(), State = State, Wire = new(version!), Logger = NullLogger.Instance, Scheduler = Scheduler, ReadPhysicsState = () => Physics, CurrentSessionCancellation = () => Connected ? SessionLifetime.Token : null };
            Actions = new InteractionActions(Sink, services, _sequences);
        }

        public void Acknowledge() => _sequences.Acknowledge(Assert.IsType<ServerboundUseItemOnPacket>(Sink.Packets[0]).Sequence);
        public async ValueTask DisposeAsync()
        {
            SessionLifetime.Cancel();
            await Scheduler.DisposeAsync();
            SessionLifetime.Dispose();
        }
    }

    private sealed class RecordingSink : IPacketSink
    {
        public List<object> Packets { get; } = [];
        public TaskCompletionSource UseWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? AfterUse { get; set; }
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket)
            {
                UseWritten.TrySetResult();
                AfterUse?.Invoke();
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
