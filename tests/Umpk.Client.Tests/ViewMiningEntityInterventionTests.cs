using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ViewMiningEntityInterventionTests
{
    public static IEnumerable<object[]> InterventionCases()
    {
        foreach (int protocol in new[] { 47, 107, 766, 777 })
            foreach (string kind in new[] { "cow", "player" })
                foreach (bool started in new[] { false, true })
                    yield return [protocol, kind, started];
    }

    [Theory]
    [MemberData(nameof(InterventionCases))]
    public async Task ViewMiningReleasesWhenANativeParticipantIntervenes(int protocol, string kind, bool started)
    {
        await using var harness = new Harness(protocol);
        using var caller = new CancellationTokenSource();
        Task? mining = null;
        try
        {
            if (started)
            {
                mining = harness.Actions.DigBlockInViewAsync(Harness.Target, ct: caller.Token);
                await harness.Tick();
                Assert.False(mining.IsCompleted);
            }
            await harness.Spawn(kind, new Vec3d(.5, 64.5, 2));
            mining ??= harness.Actions.DigBlockInViewAsync(Harness.Target, ct: caller.Token);
            await harness.Tick();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(started ? 1 : 0, harness.Packets.OfType<ServerboundPlayerActionPacket>().Count(packet => packet.Action == (protocol == 777 ? 2 : 1)));
            Assert.DoesNotContain(harness.Packets.OfType<ServerboundPlayerActionPacket>(), packet => packet.Action == (protocol == 777 ? 3 : 2));
            int completedCount = harness.Packets.Count;
            await harness.Tick();
            Assert.Equal(completedCount, harness.Packets.Count);
            Assert.True(harness.BlockRay(4.5) is { Hit: true, BlockPos: var position } && position == Harness.Target);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null && !mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(766)]
    [InlineData(777)]
    public async Task ExplicitCoordinateMiningKeepsItsExistingContract(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Spawn("cow", new Vec3d(.5, 64.5, 2));
        using var caller = new CancellationTokenSource();
        Task mining = harness.Actions.DigBlockAsync(Harness.Target, ct: caller.Token);
        await harness.Tick();
        Assert.False(mining.IsCompleted);
        Assert.Contains(harness.Packets.OfType<ServerboundPlayerActionPacket>(), packet => packet.Action == 0);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(777)]
    public async Task ALocalMountPacketPreventsGuessingAnUnmountedView(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Spawn("cow", new Vec3d(100, 64, 100));
        IPacket mount = protocol == 47
            ? new ClientboundSetEntityLinkPacket(1, 42, false)
            : new ClientboundSetPassengersPacket(42, [1]);
        await harness.Apply(mount);
        using var caller = new CancellationTokenSource();
        Task mining = harness.Actions.DigBlockInViewAsync(Harness.Target, ct: caller.Token);
        try
        {
            await harness.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(harness.Packets);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ANewPlayerLifetimeClearsOnlyTheLocalMountContext()
    {
        await using var harness = new Harness(777);
        await harness.Spawn("player", new Vec3d(100, 64, 100));
        Assert.True(harness.State.Entities.TryGet(42, out Entity? remote));
        await harness.Apply(new ClientboundSetPassengersPacket(42, [1]));
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        World world = harness.State.World;
        long previous = harness.State.PlayerLifetime;

        harness.State.AdvancePlayerLifetime();

        Assert.Equal(previous + 1, harness.State.PlayerLifetime);
        Assert.Same(world, harness.State.World);
        Assert.False(harness.State.EntityMotion.IsLocalMountUnresolved);
        Assert.True(harness.State.EntityMotion.IsTrackedPlayer(Assert.IsType<Entity>(remote)));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(777)]
    public async Task MissingOrReplacedVehicleCannotProveDismount(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Apply(new ClientboundSetPassengersPacket(42, [1]));
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        await harness.Spawn("cow", new Vec3d(100, 64, 100));
        await harness.Apply(new ClientboundSetPassengersPacket(42, []));
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        await harness.Apply(new ClientboundSetPassengersPacket(42, [1]));
        await harness.Apply(new ClientboundRemoveEntitiesPacket([42]));
        await harness.Spawn("cow", new Vec3d(100, 64, 100));
        await harness.Apply(new ClientboundSetPassengersPacket(42, []));
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        await harness.Apply(new ClientboundSetPassengersPacket(42, [1]));
        await harness.Apply(new ClientboundSetPassengersPacket(42, []));
        Assert.False(harness.State.EntityMotion.IsLocalMountUnresolved);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(777)]
    public async Task WorldReplacementClearsTheLocalMountWithoutInsertingSelf(int protocol)
    {
        await using var harness = new Harness(protocol);
        IPacket mount = protocol == 47 ? new ClientboundSetEntityLinkPacket(1, 42, false) : new ClientboundSetPassengersPacket(42, [1]);
        await harness.Apply(mount);
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        harness.State.InstallWorld(harness.State.World);
        Assert.False(harness.State.EntityMotion.IsLocalMountUnresolved);
        Assert.False(harness.State.Entities.TryGet(1, out _));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(777)]
    public async Task LeashPacketsCannotEstablishOrClearALocalMount(int protocol)
    {
        await using var harness = new Harness(protocol);
        await harness.Apply(new ClientboundSetEntityLinkPacket(1, 42, protocol == 47 ? true : null));
        Assert.False(harness.State.EntityMotion.IsLocalMountUnresolved);
        IPacket mount = protocol == 47 ? new ClientboundSetEntityLinkPacket(1, 42, false) : new ClientboundSetPassengersPacket(42, [1]);
        await harness.Apply(mount);
        await harness.Apply(new ClientboundSetEntityLinkPacket(1, -1, protocol == 47 ? true : null));
        Assert.True(harness.State.EntityMotion.IsLocalMountUnresolved);
        if (protocol == 47)
        {
            await harness.Apply(new ClientboundSetEntityLinkPacket(1, -1, false));
            Assert.False(harness.State.EntityMotion.IsLocalMountUnresolved);
        }
    }

    [Theory]
    [InlineData(false, 0.0000001, false)]
    [InlineData(false, 0.00000005, true)]
    [InlineData(true, 0.00000005, false)]
    [InlineData(true, 0.0, true)]
    public void ClipRetainsTheNativeOpenModernToleranceAndClosedLegacyFaces(bool legacy, double offset, bool expected)
    {
        var box = new Aabb(0, 0, 0, 1, 1, 1);
        Assert.Equal(expected, InteractionPicker.TryClip(box, new Vec3d(2, 1 + offset, .5), new Vec3d(0, 1 + offset, .5), legacy, out _));
    }

    internal sealed class Harness : IAsyncDisposable
    {
        public static readonly BlockPos Target = new(0, 65, 4);
        private readonly int _protocol;
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _session = new();
        public ClientState State => _applier.State;
        public List<object> Packets { get; } = [];
        public InteractionActions Actions { get; }
        public ClientSessionServices Services { get; }

        public Harness(int protocol)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            _applier = new ApplierHarness(version);
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            State.Self.EntityId = 1;
            State.Self.Position = new Vec3d(.5, 64, .5);
            Assert.True(State.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out var stone));
            State.World.SetBlockStateId(Target, stone.DefaultStateId);
            State.EntityMembership.LoadColumn(new ChunkPos(0, 0));
            Services = new ClientSessionServices
            {
                Version = version,
                Options = new(),
                Policies = new(),
                State = State,
                Wire = new(version),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                CurrentSessionCancellation = () => _session.Token,
                ReadPhysicsState = () => new PhysicsState { Position = State.Self.Position, BoundingBox = Aabb.OfSize(.5, 64, .5, .6, 1.8) },
                ReadBlockHit = BlockRay,
            };
            Actions = new InteractionActions(new Sink(protocol, Packets), Services, new SequenceTracker());
        }

        public BlockHitResult? BlockRay(double distance)
        {
            Vec3d eye = State.Self.Position.Add(0, 1.62f, 0);
            BlockHitResult hit = Raycast.CastBlock(State.World, eye, eye.Add(0, 0, distance), shapes: JavaGameData.BlockShapes(_protocol));
            return hit.Hit ? hit : null;
        }

        public Task Apply(IPacket packet) => _scheduler.InvokeAsync(() => _applier.ApplyAsync(BoundDescriptorCodec.RoundTrip(_protocol, packet.Type.Id.Path, packet)), CancellationToken.None);

        public async Task Spawn(string kind, Vec3d point)
        {
            IPacket packet;
            if (kind == "player" && _protocol < 764)
                packet = new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([], []));
            else
            {
                Assert.True(State.Registries!.EntityTypes.TryGet(Identifier.Minecraft(kind), out var type));
                packet = _protocol < 759
                    ? new ClientboundAddMobPacket(42, type.NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []))
                    : new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, null);
            }
            await Apply(packet);
        }

        public Task Tick() => _scheduler.InvokeAsync(() => Actions.TickMiningOnLoopAsync(_session.Token), CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await _scheduler.DisposeAsync();
            _session.Dispose();
        }

        private sealed class Sink(int protocol, List<object> packets) : IPacketSink
        {
            public ValueTask SendAsync(object packet, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                _ = BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<IPacket>(packet));
                packets.Add(packet);
                return ValueTask.CompletedTask;
            }

            public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
        }
    }
}
