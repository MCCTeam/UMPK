using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ModernCowMotionRuntimeTests
{
    public static IEnumerable<object[]> Compositions()
    {
        foreach (int protocol in new[] { 769, 770 })
            foreach (bool physics in new[] { false, true })
                yield return [protocol, physics];
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public async Task FramedCowMovementPublishesBoundsOnlyOnEligibleSessionTicks(int protocol, bool physics)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnCow();
        await session.Move();
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.PacketBase());
        foreach (double x in new[] { .8333333333333333, 1.1666666666666665, 1.5 })
        {
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
            Assert.Equal(new Aabb(x - (double)(.9f / 2f), 65, .5 - (double)(.9f / 2f), x + (double)(.9f / 2f), 65 + (double)1.4f, .5 + (double)(.9f / 2f)), await session.Bounds());
        }
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task PlacementUsesTheCowAnimationRatherThanItsPacketEndpoint(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow(new Vec3d(.5, 65, 1.5));
        await session.Move();
        Assert.Equal(2, (await session.Use()).Count);
        Assert.Empty(await session.Tick());
        Assert.Single(await session.Use());
        Assert.True((await session.Bounds()).Intersects(Aabb.BlockAt(1, 65, 1)));
        await session.Move(8192);
        for (int tick = 0; tick < 3; tick++)
            Assert.Empty(await session.Tick());
        Assert.Equal(2, (await session.Use()).Count);
    }

    public static IEnumerable<object[]> PartialCases()
    {
        foreach (int protocol in new[] { 769, 770 })
            foreach (bool rotation in new[] { false, true })
                foreach (bool tickBefore in new[] { false, true })
                    yield return [protocol, rotation, tickBefore];
    }

    [Theory]
    [MemberData(nameof(PartialCases))]
    public async Task PartialRetargetingKeepsTheNativeCurrentOrPendingComponents(int protocol, bool rotation, bool tickBefore)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Send(new ClientboundMoveEntityPosRotPacket(42, 4096, 0, 0, 90, 45, false));
        if (tickBefore)
            Assert.Empty(await session.Tick());
        Vec3d before = await session.Position();
        await session.Send(rotation ? new ClientboundMoveEntityRotPacket(42, 270, 315, false) : new ClientboundMoveEntityPosPacket(42, 4096, 0, 0, false));
        Assert.Equal(before, await session.Position());
        for (int tick = 0; tick < 3; tick++)
            Assert.Empty(await session.Tick());
        Assert.Equal(rotation ? protocol == 769 ? new Vec3d(1.5, 65, .5) : before : new Vec3d(2.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(rotation ? 1.5 : 2.5, 65, .5), await session.PacketBase());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = client.State.Entities.Get(42)!;
            Assert.Equal(rotation ? -90F : protocol == 769 ? 90F : tickBefore ? 30F : 0F, actor.Yaw);
            Assert.Equal(rotation ? -45F : protocol == 769 ? 45F : tickBefore ? 15F : 0F, actor.Pitch);
            return true;
        }, session.Token);
    }

    public static IEnumerable<object[]> VelocityCases()
    {
        foreach (object[] composition in Compositions())
            foreach (bool active in new[] { false, true })
                foreach (double value in new[] { .002875, .003, .003125, .01 })
                    yield return [composition[0], composition[1], active, value];
    }

    [Theory]
    [MemberData(nameof(VelocityCases))]
    public async Task ImmediateVelocityUsesCowCutoffAndInactiveDecayWithoutLocalTravel(int protocol, bool physics, bool active, double value)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnCow();
        if (active)
            await session.Move();
        await session.Send(new ClientboundSetEntityMotionPacket(42, (short)(value * 8000), (short)(-value * 8000), (short)(value * 8000)));
        Assert.Equal(new Vec3d(value, -value, value), await session.Velocity());
        double current = value;
        for (int tick = 1; tick <= 5; tick++)
        {
            if (!active || tick > 3)
                current *= .98;
            if (Math.Abs(current) < .003)
                current = 0;
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(current, -current, current), await session.Velocity());
            double x = active ? tick == 1 ? .8333333333333333 : tick == 2 ? 1.1666666666666665 : 1.5 : .5;
            Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
        }
    }

    public static IEnumerable<object[]> MembershipCases()
    {
        foreach (object[] composition in Compositions())
            foreach (string scenario in new[] { "absent", "synthetic", "full", "unload", "reload", "cross-column" })
                yield return [composition[0], composition[1], scenario];
    }

    [Theory]
    [MemberData(nameof(MembershipCases))]
    public async Task RealNativeColumnsControlTicksSeparatelyFromQueryMembership(int protocol, bool physics, string scenario)
    {
        bool cross = scenario == "cross-column";
        await using Session session = await Session.Create(protocol, physics, terrain: scenario is "full" or "unload" or "reload" or "cross-column");
        Vec3d start = cross ? new(15.5, 65, .5) : new(.5, 65, .5);
        await session.SpawnCow(start);
        if (scenario == "synthetic")
        {
            await session.Block(new BlockPos(0, 65, 0), "air");
            Assert.NotNull(session.Client.State.World.GetColumn(new ChunkPos(0, 0)));
            Assert.False(session.Client.State.EntityMembership.HasColumn(new BlockPos(0, 65, 0)));
        }
        if (scenario is "unload" or "reload")
            await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        if (scenario == "reload")
            await session.Terrain();
        await session.Move(cross ? (short)12288 : (short)4096);
        Assert.Empty(await session.Tick());
        bool eligible = scenario is "full" or "reload" or "cross-column";
        Assert.Equal(eligible ? new Vec3d(cross ? 16.5 : .8333333333333333, 65, .5) : start, await session.Position());
        Assert.True(await session.Client.InvokeAsync(client => client.State.EntityMembership.IsWorldQueryMember(client.State.Entities.Get(42)!) == true, session.Token));
        if (cross)
        {
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(16.5, 65, .5), await session.Position());
            await session.Terrain(1);
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(17.5, 65, .5), await session.Position());
        }
    }

    public static IEnumerable<object[]> FreezeCases()
    {
        foreach (object[] composition in Compositions())
            foreach (float rate in new[] { 5F, 20F, 40F })
                yield return [composition[0], composition[1], rate];
    }

    [Theory]
    [MemberData(nameof(FreezeCases))]
    public async Task WorldFreezeAndExactStepsGateCowMotionOnTheExistingClock(int protocol, bool physics, float rate)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnCow();
        await session.Move();
        await session.Send(new ClientboundTickingStatePacket(rate, true));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        await session.Send(new ClientboundTickingStepPacket(1));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(.8333333333333333, 65, .5), await session.Position());
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(.8333333333333333, 65, .5), await session.Position());
        await session.Send(new ClientboundTickingStatePacket(rate, false));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(1.1666666666666665, 65, .5), await session.Position());
    }

    public static IEnumerable<object[]> UnknownCases()
    {
        foreach (int protocol in new[] { 769, 770 })
            foreach (string scenario in new[] { "pose", "baby", "scale", "mount", "passenger", "health", "fluid", "local-push", "malformed" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(UnknownCases))]
    public async Task UnsupportedMovingCowContextStaysUnknownUntilReplacement(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        await session.Unsupported(scenario);
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        if (scenario == "fluid")
            await session.Block(new BlockPos(0, 65, 0), "air");
        if (scenario == "local-push")
            await session.CorrectSelf(new Vec3d(2.5, 64, 2.5));
        if (scenario is "mount" or "passenger")
            await session.Send(new ClientboundRemoveEntitiesPacket([43]));
        await session.SpawnCow();
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, false)]
    [InlineData(769, true)]
    [InlineData(770, true)]
    public async Task InitialAndHeldViewMiningObserveTheCowTickPhase(int protocol, bool held)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnCow(new Vec3d(1.5, 65, 2.5));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Mine(caller.Token);
                await session.Queued();
                AssertStarted(await session.Tick());
            }
            await session.Move(-4096);
            mining ??= session.Mine(caller.Token);
            await session.Queued();
            List<IPacket> first = await session.Tick();
            if (held) AssertContinued(first); else AssertStarted(first);
            AssertContinued(await session.Tick());
            AssertAbort(await session.Tick());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null && !mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task ExplicitCoordinateMiningKeepsItsExistingContract(int protocol)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnCow(new Vec3d(.5, 65, 2.5));
        await session.Move();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick());
            AssertContinued(await session.Tick());
            Assert.False(mining.IsCompleted);
        }
        finally { caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token)); }
    }

    public static IEnumerable<object[]> CollisionCases()
    {
        foreach (int protocol in new[] { 769, 770 })
            foreach (string scenario in new[] { "empty", "stone", "cow", "shulker", "border", "boat" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(CollisionCases))]
    public async Task FarSyncKeepsNativeCowMembershipAndSourceCollisionContext(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        for (int column = 1; column <= 6; column++)
            await session.Terrain(column);
        await session.Terrain(12);
        if (scenario == "stone")
            await session.Block(new BlockPos(202, 130, 0), "stone");
        else if (scenario is "cow" or "shulker" or "boat")
            await session.SpawnActor(43, scenario == "boat" ? "oak_boat" : scenario, new Vec3d(202.5, 130, .5));
        else if (scenario == "border")
            await session.Send(new ClientboundInitializeBorderPacket(96, 0, 10, 10, 0, 29999984, 5, 15));
        await session.Move();
        await session.Send(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(new Vec3d(100.5, 65, .5), Vec3d.Zero, 0, 0), false));
        Assert.Equal(new Vec3d(100.5, 65, .5), await session.Position());
        double[] expected = protocol == 770 && scenario is "empty" or "cow"
            ? [100.83333333333333, 101.16666666666666, 101.5]
            : [67.5, 34.5, 1.5];
        foreach (double x in expected)
        {
            Assert.Empty(await session.Tick());
            Assert.Equal(new Vec3d(100.5, 65, .5), await session.PacketBase());
            if (protocol == 770 && scenario == "boat" || protocol == 769 && scenario == "shulker")
            {
                Assert.False(await session.GeometryKnown());
                Assert.Equal(new Vec3d(100.5, 65, .5), await session.Position());
            }
            else
            {
                Assert.True(await session.GeometryKnown());
                Assert.Equal(new Vec3d(x, 65, .5), await session.Position());
            }
        }
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, false)]
    [InlineData(769, true)]
    [InlineData(770, true)]
    public async Task PositionSyncUsesNativeTickMembershipBeforeChoosingSnapOrInterpolation(int protocol, bool loaded)
    {
        await using Session session = await Session.Create(protocol, terrain: loaded);
        await session.SpawnCow();
        await session.Send(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(new Vec3d(2.5, 65, .5), Vec3d.Zero, 0, 0), false));
        Assert.Equal(new Vec3d(loaded ? .5 : 2.5, 65, .5), await session.Position());
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(loaded ? 1.1666666666666665 : 2.5, 65, .5), await session.Position());
        Assert.Equal(new Vec3d(2.5, 65, .5), await session.PacketBase());
    }

    [Theory]
    [InlineData(769, "replacement")]
    [InlineData(770, "replacement")]
    [InlineData(769, "removal")]
    [InlineData(770, "removal")]
    [InlineData(769, "respawn")]
    [InlineData(770, "respawn")]
    public async Task ActorAndWorldLifetimesDiscardPendingCowControllers(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        if (scenario == "respawn")
            await session.Send(new ClientboundRespawnPacket(new CommonPlayerSpawnInfo(1, "minecraft:the_nether", 0, 0, -1, false, false, null, 0, 63), 0, null));
        else if (scenario == "removal")
            await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        if (scenario != "replacement")
            Assert.Null(session.Client.State.Entities.Get(42));
        await session.SpawnCow(new Vec3d(8.5, 65, 8.5));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(8.5, 65, 8.5), await session.Position());
        Assert.Equal(new Vec3d(8.5, 65, 8.5), await session.PacketBase());
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task SessionTeardownCancelsTheClockAndClosesThePeer(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        await session.Close();
        Assert.True(session.Ticks.SessionCancelled);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task SameWorldRespawnKeepsTheExistingPeerAndItsPendingMotion(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        Entity actor = session.Client.State.Entities.Get(42)!;
        await session.Send(new ClientboundRespawnPacket(new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63), 0, null));
        Assert.Same(actor, session.Client.State.Entities.Get(42));
        Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        await session.CorrectSelf(new Vec3d(2.5, 64, 2.5));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(.8333333333333333, 65, .5), await session.Position());
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, false)]
    [InlineData(769, true)]
    [InlineData(770, true)]
    public async Task FarMovingCowKeepsInitialAndHeldViewMiningActive(int protocol, bool held)
    {
        await using Session session = await Session.Create(protocol, mining: true);
        await session.SpawnCow(new Vec3d(8.5, 65, 8.5));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Mine(caller.Token);
                await session.Queued();
                AssertStarted(await session.Tick());
            }
            await session.Move(-4096);
            mining ??= session.Mine(caller.Token);
            await session.Queued();
            List<IPacket> first = await session.Tick();
            if (held) AssertContinued(first); else AssertStarted(first);
            AssertContinued(await session.Tick());
            AssertContinued(await session.Tick());
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(768)]
    [InlineData(771)]
    public async Task AdjacentUnmeasuredCowMotionKeepsItsExistingBehavior(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        Assert.Equal(new Vec3d(1.5, 65, .5), await session.Position());
        Assert.True(await session.GeometryKnown());
    }

    public static IEnumerable<object[]> SelfContextCases()
    {
        foreach (object[] composition in Compositions())
            foreach (bool bed in new[] { false, true })
                yield return [composition[0], composition[1], bed];
    }

    [Theory]
    [MemberData(nameof(SelfContextCases))]
    public async Task IgnoredSelfPoseAndBedMetadataCannotProveLocalPushNonrelevance(int protocol, bool physics, bool bed)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnCow();
        await session.Send(new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new(bed ? 14 : 6, bed ? MetadataValue.OptionalPosition(new BlockPos(0, 64, 0)) : MetadataValue.Pose(EntityPose.Sleeping))], [])));
        await session.Move();
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
        Assert.Single(await session.Use());
        await session.Send(new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new(6, MetadataValue.Pose(EntityPose.Standing)), new(14, MetadataValue.OptionalPosition(null))], [])));
        await session.SpawnCow();
        await session.Move();
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
        await session.CorrectSelf(new Vec3d(2.5, 64, 2.5));
        await session.SpawnCow();
        await session.Move();
        Assert.Empty(await session.Tick());
        Assert.True(await session.GeometryKnown());
        Assert.Equal(new Vec3d(.8333333333333333, 65, .5), await session.Position());
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, false)]
    [InlineData(769, true)]
    [InlineData(770, true)]
    public async Task PhysicsDisabledNearOrAmbiguousLocalContextStaysUnknown(int protocol, bool authority)
    {
        await using Session session = await Session.Create(protocol, physics: false);
        await session.SpawnCow();
        if (authority)
            await session.Send(new ClientboundSetCameraPacket(42));
        else
            await session.CorrectSelf(new Vec3d(.5, 64, .5));
        await session.Move();
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
        Assert.Single(await session.Use());
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public async Task UnrelatedSelfMetadataRetainsTheProvenLocalPushContext(int protocol, bool physics)
    {
        await using Session session = await Session.Create(protocol, physics);
        await session.SpawnCow();
        await session.Send(new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new(1, MetadataValue.VarInt(250))], [])));
        Assert.Equal(250, session.Client.State.Self.AirSupply);
        await session.Move();
        Assert.Empty(await session.Tick());
        Assert.True(await session.GeometryKnown());
        Assert.Equal(new Vec3d(.8333333333333333, 65, .5), await session.Position());
        Assert.Equal(physics ? 254 : 250, session.Client.State.Self.AirSupply);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task CowDeathCallbackInvalidatesMovingBoundsBeforePublishingItsEvent(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move();
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<EntityStatusChanged>(evt =>
        {
            if (evt.EntityId == 42 && evt.EventId == 3)
            {
                Entity actor = session.Client.State.Entities.Get(42)!;
                observed.TrySetResult(session.Client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
            }
        });
        await session.Send(new ClientboundEntityEventPacket(42, 3));
        Assert.False(await observed.Task.WaitAsync(session.Token));
        Assert.False(await session.GeometryKnown());
        Assert.Single(await session.Use());
        await session.Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(9, MetadataValue.Float(10))], [])));
        Assert.False(await session.GeometryKnown());
        await session.SpawnCow();
        Assert.True(await session.GeometryKnown());
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    public async Task ZeroRelativePacketStillRunsTheCowListenerAndValidityOwner(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnCow();
        await session.Move(0);
        for (int tick = 0; tick < 3; tick++)
        {
            Assert.Empty(await session.Tick());
            Assert.True(await session.GeometryKnown());
            Assert.Equal(new Vec3d(.5, 65, .5), await session.Position());
        }
        await session.Unsupported("baby");
        await session.Move(0);
        Assert.Empty(await session.Tick());
        Assert.False(await session.GeometryKnown());
    }

    private static void AssertStarted(IReadOnlyList<IPacket> packets)
    {
        Assert.Equal(3, packets.Count);
        Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(packets[0]).Action);
        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[2]).Hand);
    }

    private static void AssertContinued(IReadOnlyList<IPacket> packets) => Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(Assert.Single(packets)).Hand);

    private static void AssertAbort(IReadOnlyList<IPacket> packets)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
        Assert.Equal(1, action.Action);
        Assert.Equal(Session.Target, action.Position);
    }

    private sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(0, 65, 4);
        private readonly int _protocol;
        private readonly JavaVersion _version;
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _barrier;
        private bool _closed;
        internal ObservedTicks Ticks { get; } = new();
        internal UmpkClient Client { get; }
        internal CancellationToken Token => _budget.Token;
        private PacketCodecContext Context => new(Client.State.Registries!, IConnectionCodecState.Empty);

        private Session(int protocol, bool physics)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "PickExclusion"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = physics; features.Pathfinding = false; features.Entities = true; features.Terrain = true; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol, bool physics = true, bool terrain = true, bool mining = false)
        {
            var session = new Session(protocol, physics);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session._version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable join = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
                await session.Send(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null));
                await joined.Task.WaitAsync(session.Token);
                var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable correction = session.Client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
                var position = mining ? new Vec3d(.5, 64, .5) : new Vec3d(2.5, 64, 2.5);
                await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
                await corrected.Task.WaitAsync(session.Token);
                if (terrain)
                    await session.Terrain();
                var stoneItem = session.Client.State.Registries!.Items[Identifier.Minecraft("stone")];
                await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(stoneItem, 32)));
                Assert.Empty(await session.Barrier());
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
                if (terrain && physics)
                    Assert.True(session.Client.State.Self.OnGround);
                if (mining)
                    Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal Task Mine(CancellationToken token) => Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: token);
        internal Task Queued() => Client.InvokeAsync(_ => true, Token);

        internal async Task<List<IPacket>> Tick()
        {
            long before = Client.State.SessionTick;
            await Ticks.Fire(Token);
            List<IPacket> packets = await Barrier();
            Assert.Equal(before + 1, Client.State.SessionTick);
            return packets;
        }

        internal Task SpawnCow(Vec3d? position = null) => SpawnActor(42, "cow", position ?? new Vec3d(.5, 65, .5));
        internal async Task SpawnActor(int id, string kind, Vec3d position)
        {
            int type = Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId;
            await Send(new ClientboundAddEntityPacket(id, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
            Assert.Empty(await Barrier());
            Assert.Equal(position, await Client.InvokeAsync(client => client.State.Entities.Get(id)!.Position, Token));
        }
        internal Task Move(short dx = 4096) => Send(new ClientboundMoveEntityPosPacket(42, dx, 0, 0, false));
        internal Task<Vec3d> Position() => Client.InvokeAsync(client => client.State.Entities.Get(42)!.Position, Token);
        internal Task<Vec3d> Velocity() => Client.InvokeAsync(client => client.State.Entities.Get(42)!.Velocity, Token);
        internal Task<Vec3d> PacketBase() => Client.InvokeAsync(client => client.State.PacketBases.Get(client.State.Entities.Get(42)!), Token);
        internal Task<bool> GeometryKnown() => Client.InvokeAsync(client => client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out _, out _, _protocol), Token);
        internal Task<Aabb> Bounds() => Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(_protocol), out Aabb bounds, out _, _protocol));
            return bounds;
        }, Token);
        internal Task Block(BlockPos cell, string kind) => Send(new ClientboundBlockUpdatePacket(cell, Client.State.Registries!.Blocks[Identifier.Minecraft(kind)].Value.DefaultStateId));
        internal async Task CorrectSelf(Vec3d position)
        {
            await Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
            Assert.Empty(await Barrier());
        }
        internal async Task Unsupported(string scenario)
        {
            Assert.True(JavaGameData.EntityGeometryData(_protocol).TryGetRule(Identifier.Minecraft("cow"), out EntityGeometryRule rule));
            if (scenario is "pose" or "baby" or "health" or "malformed")
            {
                int index = scenario == "baby" ? rule.MetadataIndex : scenario == "pose" ? rule.PoseMetadataIndex : 9;
                MetadataValue value = scenario switch
                {
                    "baby" => MetadataValue.Boolean(true),
                    "pose" => MetadataValue.Pose(EntityPose.Sleeping),
                    "health" => MetadataValue.Float(0),
                    _ => MetadataValue.VarInt(1),
                };
                await Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(index, value)], [])));
            }
            else if (scenario == "scale")
            {
                int scale = Client.State.Registries!.Attributes[Identifier.Minecraft("scale")].NetworkId;
                await Send(new ClientboundUpdateAttributesPacket(42, [new(null, scale, 2, [])]));
            }
            else if (scenario is "mount" or "passenger")
            {
                await SpawnActor(43, "cow", new Vec3d(8.5, 65, 8.5));
                await Send(scenario == "mount" ? new ClientboundSetPassengersPacket(43, [42]) : new ClientboundSetPassengersPacket(42, [43]));
            }
            else if (scenario == "fluid")
                await Block(new BlockPos(0, 65, 0), "water");
            else if (scenario == "local-push")
                await CorrectSelf(new Vec3d(.5, 64, .5));
            else
                throw new ArgumentOutOfRangeException(nameof(scenario));
            Assert.Empty(await Barrier());
        }
        internal async Task<List<IPacket>> Use()
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(new BlockPos(1, 64, 1), Direction.Up, new Vec3d(.5, 1, .5), ct: Token);
            var packets = new List<IPacket>();
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var decoded = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, decoded));
                if (decoded is ServerboundUseItemOnPacket use)
                {
                    packets.Add(use);
                    await Send(new ClientboundBlockChangedAckPacket(use.Sequence), barrier: false);
                    break;
                }
                if (decoded is ServerboundSwingPacket)
                    packets.Add(decoded);
            }
            await action.WaitAsync(Token);
            packets.AddRange(await Barrier());
            Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            if (packets.Count == 2)
                Assert.IsType<ServerboundSwingPacket>(packets[1]);
            return packets;
        }

        internal async Task Terrain(int columnX = 0)
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, Client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(columnX, 0, _protocol >= 775, [], sections, [], new byte[7])
                : new NbtHeightmapChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(columnX, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(sections.Length, packet.Column.SectionCount);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Encode(codec, packet));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Send(packet);
            Assert.Empty(await Barrier());
            Assert.True(Client.State.EntityMembership.HasColumn(new BlockPos(columnX * 16, 65, 0)));
            if (columnX == 0)
            {
                await Block(new BlockPos(0, 63, 0), "stone");
                await Block(new BlockPos(2, 63, 2), "stone");
                await Block(new BlockPos(1, 64, 1), "dirt");
                await Block(Target, "stone");
                Assert.Empty(await Barrier());
            }
        }

        internal async Task Send(IPacket packet, bool barrier = true)
        {
            PhaseRegistry registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            byte[] bytes = Encode(codec, packet);
            object decoded = codec.Decode(bytes, Context);
            switch (packet)
            {
                case ClientboundAddEntityPacket add:
                    var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                    Assert.Equal(add.EntityId, observed.EntityId);
                    Assert.Equal(add.TypeId, observed.TypeId);
                    Assert.Equal(new Vec3d(add.X, add.Y, add.Z), new Vec3d(observed.X, observed.Y, observed.Z));
                    break;
                case ClientboundMoveEntityPosPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityPosPacket>(decoded));
                    break;
                case ClientboundMoveEntityPosRotPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityPosRotPacket>(decoded));
                    break;
                case ClientboundMoveEntityRotPacket move:
                    Assert.Equal(move, Assert.IsType<ClientboundMoveEntityRotPacket>(decoded));
                    break;
                case ClientboundEntityEventPacket status:
                    Assert.Equal(status, Assert.IsType<ClientboundEntityEventPacket>(decoded));
                    break;
                case ClientboundSetEntityMotionPacket motion:
                    Assert.Equal(motion, Assert.IsType<ClientboundSetEntityMotionPacket>(decoded));
                    break;
                case ClientboundTickingStatePacket ticking:
                    Assert.Equal(ticking, Assert.IsType<ClientboundTickingStatePacket>(decoded));
                    break;
                case ClientboundTickingStepPacket ticking:
                    Assert.Equal(ticking, Assert.IsType<ClientboundTickingStepPacket>(decoded));
                    break;
                case ClientboundSetEntityDataPacket data:
                    var observedData = Assert.IsType<ClientboundSetEntityDataPacket>(decoded);
                    Assert.Equal(data.EntityId, observedData.EntityId);
                    Assert.Equal(data.Metadata.Entries.Count, observedData.Metadata.Entries.Count);
                    for (int i = 0; i < data.Metadata.Entries.Count; i++)
                    {
                        Assert.Equal(data.Metadata.Entries[i].Index, observedData.Metadata.Entries[i].Index);
                        Assert.Equal(data.Metadata.Entries[i].Value, observedData.Metadata.Entries[i].Value);
                    }
                    break;
                case ClientboundUpdateAttributesPacket attributes:
                    var observedAttributes = Assert.IsType<ClientboundUpdateAttributesPacket>(decoded);
                    Assert.Equal(attributes.EntityId, observedAttributes.EntityId);
                    Assert.Equal(attributes.Attributes.Count, observedAttributes.Attributes.Count);
                    Assert.Equal(attributes.Attributes[0].ModernId, observedAttributes.Attributes[0].ModernId);
                    Assert.Equal(attributes.Attributes[0].BaseValue, observedAttributes.Attributes[0].BaseValue);
                    Assert.Equal(attributes.Attributes[0].Modifiers, observedAttributes.Attributes[0].Modifiers);
                    break;
                case ClientboundSetPassengersPacket passengers:
                    var observedPassengers = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                    Assert.Equal(passengers.VehicleId, observedPassengers.VehicleId);
                    Assert.Equal(passengers.Passengers, observedPassengers.Passengers);
                    break;
                case ClientboundRemoveEntitiesPacket removal:
                    Assert.Equal(removal.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                    break;
                case ClientboundEntityPositionSyncPacket sync:
                    Assert.Equal(sync, Assert.IsType<ClientboundEntityPositionSyncPacket>(decoded));
                    break;
                case ClientboundInitializeBorderPacket border:
                    Assert.Equal(border, Assert.IsType<ClientboundInitializeBorderPacket>(decoded));
                    break;
                case ClientboundRespawnPacket respawn:
                    Assert.Equal(respawn, Assert.IsType<ClientboundRespawnPacket>(decoded));
                    break;
                case ClientboundSetCameraPacket camera:
                    Assert.Equal(camera, Assert.IsType<ClientboundSetCameraPacket>(decoded));
                    break;
                case ClientboundLoginPacket login:
                    Assert.Equal(login.PlayerId, Assert.IsType<ClientboundLoginPacket>(decoded).PlayerId);
                    break;
                case ClientboundPlayerPositionPacket correction:
                    Assert.Equal(correction.ModernValues, Assert.IsType<ClientboundPlayerPositionPacket>(decoded).ModernValues);
                    break;
                case ClientboundBlockUpdatePacket block:
                    Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                    break;
                case ClientboundForgetLevelChunkPacket forget:
                    Assert.Equal(forget, Assert.IsType<ClientboundForgetLevelChunkPacket>(decoded));
                    break;
                case ClientboundPlayKeepAlivePacket keepAlive:
                    Assert.Equal(keepAlive, Assert.IsType<ClientboundPlayKeepAlivePacket>(decoded));
                    break;
            }
            Assert.Equal(bytes, Encode(codec, decoded));
            await _server.SendFrameAsync(pair.WireId, bytes, Token);
            if (barrier)
                Assert.Empty(await Barrier());
        }

        private byte[] Encode(BoundPacketCodec codec, object packet)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, Context);
            return output.WrittenSpan.ToArray();
        }

        internal async Task<List<IPacket>> Barrier()
        {
            long nonce = ++_barrier;
            await Send(new ClientboundPlayKeepAlivePacket(nonce), barrier: false);
            List<IPacket> packets = [];
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet));
                if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == nonce)
                    return packets;
                if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket)
                    packets.Add(packet);
            }
        }

        internal async Task Close()
        {
            if (_closed)
                return;
            await Client.DisconnectAsync(Token);
            await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            {
                while (true)
                    await _server.NextFrameAsync(Token);
            });
            Assert.True(Ticks.SessionCancelled);
            _closed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Close(); }
            finally
            {
                try { await Client.DisposeAsync(); }
                finally { await _server.DisposeAsync(); _budget.Dispose(); }
            }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        internal bool SessionCancelled { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal async Task Fire(CancellationToken ct)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completion, ct);
            await completion.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            try
            {
                await foreach (TaskCompletionSource completion in _ticks.Reader.ReadAllAsync(ct))
                {
                    yield return ++sequence;
                    completion.TrySetResult();
                }
            }
            finally { SessionCancelled = ct.IsCancellationRequested; }
        }
    }
}
