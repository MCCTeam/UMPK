using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PoseAwareTargetingTests
{
    public static IEnumerable<object[]> Versions() => NativePlayerDimensions.Versions();

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task AimUsesTheObservedPoseAtTheCurrentTrackedPosition(string version, int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Position = new Vec3d(13.5, 64, -2.5);
        var target = new Vec3d(11, 65, .5);
        foreach (NativePlayerDimension native in NativePlayerDimensions.ForVersion(version).Where(row => row.Position == Vec3d.Zero))
        {
            fixture.Physics = Snapshot(native, protocol);
            fixture.Sink.Packets.Clear();

            await fixture.Movement.LookAtAsync(target);

            Vec3d delta = target.Subtract(fixture.State.Self.Position.Add(0, native.EyeHeight, 0));
            double horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            var packet = Assert.IsType<ServerboundMovePlayerRotPacket>(Assert.Single(fixture.Sink.Packets));
            Assert.Equal((float)(Math.Atan2(-delta.X, delta.Z) * 180 / Math.PI), packet.Yaw);
            Assert.Equal((float)(-Math.Atan2(delta.Y, horizontal) * 180 / Math.PI), packet.Pitch);
            Assert.True(fixture.ReadOnLoop);

            fixture.Sink.Packets.Clear();
            fixture.State.Self.Yaw = 0;
            fixture.State.Self.Pitch = 0;
            await fixture.Movement.LookAtLocalAsync(target);
            Assert.Empty(fixture.Sink.Packets);
            Assert.Equal(packet.Yaw, fixture.State.Self.Yaw);
            Assert.Equal(packet.Pitch, fixture.State.Self.Pitch);
            Assert.True(fixture.ReadOnLoop);
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task VerifiedUseMeasuresReachFromTheObservedEye(string version, int protocol)
    {
        await using var fixture = new Fixture(protocol);
        foreach (NativePlayerDimension native in NativePlayerDimensions.ForVersion(version).Where(row => row.Position == Vec3d.Zero))
        {
            fixture.Physics = Snapshot(native, protocol);
            foreach ((double feetY, int blockY) in new[] { (-.1, 6), (-1.07, -5) })
            {
                fixture.State.Self.Position = new Vec3d(.5, feetY, .5);
                fixture.Sink.Packets.Clear();
                var target = new BlockPos(0, blockY, 0);
                double eyeY = feetY + native.EyeHeight;
                double gap = eyeY < blockY ? blockY - eyeY : eyeY > blockY + 1 ? eyeY - (blockY + 1) : 0;
                bool reachable = gap <= InteractionActions.BlockInteractionRange;

                UseBlockOutcome result = await fixture.Interaction.UseBlockVerifiedAsync(target, target, "open", "true", confirmationWindow: TimeSpan.Zero);

                Assert.Equal(reachable ? UseBlockOutcome.Unconfirmed : UseBlockOutcome.OutOfReach, result);
                Assert.Equal(reachable ? 1 : 0, fixture.Sink.Packets.Count(packet => packet is ServerboundUseItemOnPacket or ServerboundLegacyBlockPlacePacket));
                Assert.True(fixture.ReadOnLoop);
            }
        }
    }

    [Fact]
    public async Task MissingPhysicsFallsBackToStandingAndReadsTheLatestPosition()
    {
        await using var fixture = new Fixture(777);
        fixture.State.Self.Sneaking = true;
        fixture.State.Self.Position = new Vec3d(1, 64, 2);
        var target = new Vec3d(1, 65.62, 3);
        await fixture.Movement.LookAtAsync(target);

        var packet = Assert.IsType<ServerboundMovePlayerRotPacket>(Assert.Single(fixture.Sink.Packets));
        double standingEye = NativePlayerDimensions.ForVersion("26.3").First(row => row.Pose == EntityPose.Standing).EyeHeight;
        Assert.Equal((float)(-Math.Atan2(target.Y - (64 + standingEye), 1) * 180 / Math.PI), packet.Pitch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AimReadsPositionAndPoseAfterEarlierSessionWork(bool send)
    {
        await using var fixture = new Fixture(777);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task earlier = fixture.Scheduler.InvokeAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(budget.Token);
            fixture.State.Self.Position = new Vec3d(8, 64, 8);
            fixture.Physics = new PhysicsState { Pose = EntityPose.Swimming };
        }, budget.Token);
        await entered.Task.WaitAsync(budget.Token);
        var target = new Vec3d(8, 64.4, 9);
        Task aim = send ? fixture.Movement.LookAtAsync(target, budget.Token) : fixture.Movement.LookAtLocalAsync(target, budget.Token);
        try
        {
            Assert.False(aim.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(earlier, aim).WaitAsync(budget.Token);
        }
        if (!send)
            Assert.Empty(fixture.Sink.Packets);
        double nativeEye = NativePlayerDimensions.ForVersion("26.3").First(row => row.Pose == EntityPose.Swimming).EyeHeight;
        Assert.Equal((float)(-Math.Atan2(target.Y - (64 + nativeEye), 1) * 180 / Math.PI), fixture.State.Self.Pitch);
        Assert.Equal(0, fixture.State.Self.Yaw);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AimCanRunInsideTheSessionLoop(bool send)
    {
        await using var fixture = new Fixture(777);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        fixture.Physics = new PhysicsState { Pose = EntityPose.Crouching };

        await fixture.Scheduler.InvokeAsync(async () =>
        {
            if (send)
                await fixture.Movement.LookAtAsync(new Vec3d(1, 1, 1), budget.Token);
            else
                await fixture.Movement.LookAtLocalAsync(new Vec3d(1, 1, 1), budget.Token);
        }, budget.Token);

        Assert.Equal(send ? 1 : 0, fixture.Sink.Packets.Count);
        Assert.True(fixture.ReadOnLoop);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanceledAimDoesNotChangeRotationOrSend(bool send)
    {
        await using var fixture = new Fixture(777);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send
            ? fixture.Movement.LookAtAsync(new Vec3d(1, 1, 1), canceled.Token)
            : fixture.Movement.LookAtLocalAsync(new Vec3d(1, 1, 1), canceled.Token));

        Assert.Empty(fixture.Sink.Packets);
        Assert.Equal(0, fixture.State.Self.Yaw);
        Assert.Equal(0, fixture.State.Self.Pitch);
    }

    private static PhysicsState Snapshot(NativePlayerDimension native, int protocol) => new()
    {
        Position = new Vec3d(1000, 1000, 1000),
        Pose = native.Pose,
        PoseDimensions = PhysicsProfile.PoseDimensionsForProtocol(protocol),
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ChannelSessionScheduler _scheduler = new();

        public Fixture(int protocol)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            State = new ClientState(new ClientFeatures().Normalized());
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            var sequences = new SequenceTracker();
            Sink = new AckSink(sequences);
            var services = new ClientSessionServices
            {
                Version = version,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = State,
                Wire = new WireIndex(version),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                Events = new EventBus(NullLogger.Instance, TimeSpan.Zero, 256),
                ReadPhysicsState = () =>
                {
                    ReadOnLoop = _scheduler.IsCurrent;
                    return Physics;
                },
            };
            Movement = new MovementActions(Sink, services, () => null);
            Interaction = new InteractionActions(Sink, services, sequences);
        }

        public ClientState State { get; }
        public PhysicsState? Physics { get; set; }
        public bool ReadOnLoop { get; private set; }
        public ChannelSessionScheduler Scheduler => _scheduler;
        public AckSink Sink { get; }
        public MovementActions Movement { get; }
        public InteractionActions Interaction { get; }

        public ValueTask DisposeAsync() => _scheduler.DisposeAsync();
    }

    private sealed class AckSink(SequenceTracker sequences) : IPacketSink
    {
        public List<object> Packets { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            Packets.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                sequences.Acknowledge(use.Sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
