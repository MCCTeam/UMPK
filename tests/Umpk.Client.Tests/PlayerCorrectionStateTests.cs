using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PlayerCorrectionStateTests
{
    public static IEnumerable<object[]> Protocols() => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Select(protocol => new object[] { protocol });

    public static IEnumerable<object[]> CrawlProtocols() => Protocols().Where(row => (int)row[0] >= 477);

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task CrouchedCorrectionPreservesPoseAndContact(int protocol)
    {
        await using Fixture fixture = await Fixture.CreateAsync(protocol, floor: true);
        fixture.Harness.State.Self.Sneaking = true;
        fixture.Holder.TickIdle();
        fixture.Holder.TickIdle();
        Assert.Equal(EntityPose.Crouching, fixture.Holder.EngineState!.Value.Pose);
        Assert.True(fixture.Holder.EngineState.Value.OnGround);

        await CorrectAndAssertRetainedAsync(fixture);
    }

    [Theory]
    [MemberData(nameof(CrawlProtocols))]
    public async Task CrawlingCorrectionKeepsTheLowEyeWithoutSneakInput(int protocol)
    {
        await using Fixture fixture = await Fixture.CreateAsync(protocol, floor: true);
        fixture.Harness.State.World.SetBlockStateId(new BlockPos(0, 65, 0), fixture.Stone);
        fixture.Holder.TickIdle();
        fixture.Holder.TickIdle();
        Assert.False(fixture.Harness.State.Self.Sneaking);
        Assert.Equal(EntityPose.Swimming, fixture.Holder.EngineState!.Value.Pose);

        await CorrectAndAssertRetainedAsync(fixture);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task FallingCorrectionDoesNotStartANewFall(int protocol)
    {
        await using Fixture fixture = await Fixture.CreateAsync(protocol, floor: false);
        for (int i = 0; i < 8; i++)
            fixture.Holder.TickIdle();
        Assert.True(fixture.Holder.EngineState!.Value.FallDistance > 1, "The loaded-chunk fixture must establish a fall before applying a correction.");

        await CorrectAndAssertRetainedAsync(fixture);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task RespawnCreatesFreshPlayerPhysics(int protocol)
    {
        await using Fixture fixture = await Fixture.CreateAsync(protocol, floor: true);
        fixture.Harness.State.Self.Sneaking = true;
        fixture.Holder.TickIdle();
        fixture.Holder.TickIdle();
        Assert.Equal(EntityPose.Crouching, fixture.Holder.EngineState!.Value.Pose);
        long previous = fixture.Harness.State.PlayerLifetime;

        await fixture.Harness.ApplyAsync(new ClientboundRespawnPacket(
            new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, 0, false, false, null, 0, 63), 0, new LegacyRespawnFields(0, 2, 0, "flat")));
        fixture.Holder.EnsureEngine();

        Assert.NotEqual(previous, fixture.Harness.State.PlayerLifetime);
        PhysicsState fresh = fixture.Holder.EngineState!.Value;
        Assert.Equal(EntityPose.Standing, fresh.Pose);
        Assert.Equal(0, fresh.FallDistance);
        Assert.False(fresh.HorizontalCollision);
        Assert.False(fresh.VerticalCollision);
    }

    private static async Task CorrectAndAssertRetainedAsync(Fixture fixture)
    {
        foreach (int flags in new[] { 0, 7 })
        {
            PhysicsState before = fixture.Holder.EngineState!.Value;
            var input = new Vec3d(.75, 65.25, -.25);
            Vec3d destination = flags == 0 ? input : before.Position.Add(input);
            Vec3d velocity = fixture.Protocol >= 768 ? new Vec3d(.125, -.25, .5) : flags == 0 ? Vec3d.Zero : before.Velocity;
            var packet = new ClientboundPlayerPositionPacket(input.X, input.Y, input.Z, 33.25F, -24.5F, flags, 7,
                fixture.Protocol >= 768 ? new PositionMoveRotation(input, velocity, 33.25F, -24.5F) : null);

            await fixture.Harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(fixture.Protocol, "player_position", packet));

            PhysicsState after = fixture.Holder.EngineState!.Value;
            Assert.Equal(destination, after.Position);
            Assert.Equal(velocity, after.Velocity);
            Assert.Equal(33.25F, after.Yaw);
            Assert.Equal(-24.5F, after.Pitch);
            Assert.Equal(before.Pose, after.Pose);
            Assert.Equal(before.EyeHeight, after.EyeHeight);
            Assert.Equal(before.OnGround, after.OnGround);
            Assert.Equal(before.HorizontalCollision, after.HorizontalCollision);
            Assert.Equal(before.VerticalCollision, after.VerticalCollision);
            Assert.Equal(before.FallDistance, after.FallDistance);
            Assert.Equal(before.InWater, after.InWater);
            Assert.Equal(before.IsUnderWater, after.IsUnderWater);
            Assert.Equal(before.InLava, after.InLava);
            Assert.Equal(before.OnClimbable, after.OnClimbable);
            Assert.Equal(destination.Add(0, before.EyeHeight, 0), fixture.Services.EyePosition);
            Assert.Equal(Aabb.OfSize(destination.X, destination.Y, destination.Z, PhysicsProfileFactory.FromJavaVersion(fixture.Services.Version).Width, before.BoundingBox.YSize), after.BoundingBox);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ChannelSessionScheduler _scheduler = new();

        private Fixture(int protocol, JavaVersion version)
        {
            Protocol = protocol;
            Harness = new ApplierHarness(version);
            Services = new ClientSessionServices
            {
                Version = version,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = Harness.State,
                Wire = new WireIndex(version),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                ReadPhysicsState = () => Holder?.EngineState,
            };
            Holder = new PhysicsEngineHolder(Services, JavaGameData.BlockShapes(protocol), NullLogger.Instance);
            Harness.PositionResync = Holder.ResyncPosition;
            Stone = JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
        }

        public int Protocol { get; }
        public ApplierHarness Harness { get; }
        public ClientSessionServices Services { get; }
        public PhysicsEngineHolder Holder { get; }
        public int Stone { get; }

        public static async Task<Fixture> CreateAsync(int protocol, bool floor)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            var fixture = new Fixture(protocol, version);
            var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
            await fixture.Harness.ApplyAsync(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, new LegacyLoginFields(0, 2, "flat")));
            fixture.Harness.State.InstallWorld(new Umpk.Game.World.World(
                WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), null, protocol),
                new RegistryBlockDataSource(JavaGameData.Registries(protocol).Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes()));
            if (floor)
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        fixture.Harness.State.World.SetBlockStateId(new BlockPos(x, 63, z), fixture.Stone);
            else
                // Load the column without putting a floor under the falling player.
                fixture.Harness.State.World.SetBlockStateId(new BlockPos(0, 0, 0), fixture.Stone);
            fixture.Harness.State.Self.Position = new Vec3d(.5, floor ? 64 : 200, .5);
            fixture.Holder.ResyncPosition();
            return fixture;
        }

        public ValueTask DisposeAsync() => _scheduler.DisposeAsync();
    }
}
