using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The world-install to physics-engine seam.
/// <para><c>PhysicsEngineHolder.EnsureEngine</c> returned early whenever an engine already existed, and the <c>WorldPhysicsView</c> it had built closed over whatever <c>World</c> instance existed at first install. A respawn or a dimension change installs a BRAND NEW <c>World</c>, so from then on the engine collided against the world the session had left behind: it fell through the new floor and stood on blocks that were no longer there. Nothing caught it because the engine kept working, just against the wrong terrain, and every existing test installed exactly one world.</para>
/// </summary>
public sealed class PhysicsWorldRebuildTests
{
    private const int SolidStateId = 1;
    private static readonly Vec3d Spawn = new(0.5, 70.0, 0.5);

    /// <summary>Two worlds whose floors are at DIFFERENT heights. The engine lands on world one's floor, then a second world is installed and the engine must land on THAT one's floor instead. The height it stops at is what says which world it is colliding against, and nothing else in the test changes.</summary>
    [Fact]
    public async Task InstallingASecondWorld_RebuildsTheView_SoCollisionUsesTheNewWorld()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            // World one's floor has its top surface at y = 21.
            InstallWorldWithFloor(harness, floorY: 20);
            SeedAt(harness, holder, Spawn);
            Tick(holder, 80);
            Assert.True(harness.State.Self.OnGround);
            Assert.Equal(21.0, harness.State.Self.Position.Y, 6);

            // World two's is at y = 65. Landing there is reachable only through a rebuilt view; the stale view carries the player straight past it and back down to 21.
            InstallWorldWithFloor(harness, floorY: 64);
            SeedAt(harness, holder, Spawn);
            Tick(holder, 80);

            Assert.True(harness.State.Self.OnGround, "the engine never landed, so it is still colliding against world one");
            Assert.Equal(65.0, harness.State.Self.Position.Y, 6);
        }
    }

    /// <summary>The control against over-fixing. The guard is world IDENTITY, not "has an engine", so re-entering <c>EnsureEngine</c> with the SAME world must not rebuild anything: it runs on the idle tick and on both navigation paths, and a rebuild would reset the engine mid-flight and quietly erase accumulated velocity. The identity comparison must retain the existing engine for the same world.</summary>
    [Fact]
    public async Task EnsureEngine_OnTheSameWorld_DoesNotResetTheEngine()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            // The world the join already installed, not a new one: the engine is bound to it either way.
            AddFloor(harness.State.World, floorY: 0);
            SeedAt(harness, holder, Spawn);
            Tick(holder, 5);

            Vec3d fallingPosition = harness.State.Self.Position;
            Vec3d fallingVelocity = harness.State.Self.Velocity;
            Assert.True(fallingVelocity.Y < 0, "the player should be falling by now");

            holder.EnsureEngine();
            holder.TickIdle();

            // Had EnsureEngine rebuilt, Reset would have zeroed the velocity and this tick would have been the first of a fresh fall rather than the sixth of this one.
            Assert.True(
                harness.State.Self.Velocity.Y < fallingVelocity.Y,
                "the fall was restarted, so the engine was rebuilt against an unchanged world");
            Assert.True(harness.State.Self.Position.Y < fallingPosition.Y);
        }
    }

    /// <summary>The live respawn shape: a second <c>minecraft:respawn</c> for a different dimension runs the same world-setup seam <c>UmpkClient</c> wires, so the applier chain alone must be enough to get the engine onto the new world.</summary>
    [Fact]
    public async Task Respawn_IntoANewDimension_MovesTheEngineOntoTheNewWorld()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            SeedAt(harness, holder, Spawn);
            Umpk.Game.World.World first = harness.State.World;

            await harness.ApplyAsync(new ClientboundRespawnPacket(
                new CommonPlayerSpawnInfo(
                    DimensionTypeId: 0, Dimension: "minecraft:the_nether", Seed: 0, GameType: 0,
                    PreviousGameType: 0, IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null,
                    PortalCooldown: 0, SeaLevel: 32),
                DataToKeep: 0,
                Legacy: null));

            Umpk.Game.World.World second = harness.State.World;
            Assert.NotSame(first, second);

            // Give the new world a floor, then run the seam the client runs every tick.
            AddFloor(second, floorY: 64);
            SeedAt(harness, holder, Spawn);
            Tick(holder, 40);

            Assert.True(harness.State.Self.OnGround, "the engine is still colliding against the pre-respawn world");
            Assert.Equal(65.0, harness.State.Self.Position.Y, 6);
        }
    }

    private static void SeedAt(ApplierHarness harness, PhysicsEngineHolder holder, Vec3d position)
    {
        harness.State.Self.Position = position;
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.ResyncPosition();
    }

    private static void Tick(PhysicsEngineHolder holder, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            holder.TickIdle();

    }

    private static void InstallWorldWithFloor(ApplierHarness harness, int floorY)
    {
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(
                new CommonWorldSetup("minecraft:overworld", 0), registries: null, JavaVersions.V1_21_5.Version.Protocol),
            new RegistryBlockDataSource(WorldFactory.EmptyBlocks(), isLegacy: false),
            WorldFactory.EmptyBiomes());
        AddFloor(world, floorY);
        harness.State.InstallWorld(world);
    }

    private static void AddFloor(Umpk.Game.World.World world, int floorY)
    {
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, floorY, z), SolidStateId);

    }

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync()
    {
        JavaVersion version = JavaVersions.V1_21_5;
        var harness = new ApplierHarness(version, new ClientFeatures { Physics = true, Entities = true });
        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        var holder = new PhysicsEngineHolder(services, new NonZeroStateIsSolid(), NullLogger.Instance);
        harness.PositionResync = holder.ResyncPosition;

        await JoinAsync(harness);
        holder.EnsureEngine();
        return (harness, holder, scheduler);
    }

    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        var join = new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
        await harness.ApplyAsync(join);
    }

    /// <summary>A shape source keyed on the raw state id, so the test controls collision without needing a populated block registry: state 0 (air) is empty, anything else is a full cube.</summary>
    private sealed class NonZeroStateIsSolid : IBlockShapeSource
    {
        private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
        private static readonly Aabb[] Empty = [];

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => GetCollisionShapes(state.StateId);

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => stateId == 0 ? Empty : Cube;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetCollisionShapes(state.StateId);

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => GetCollisionShapes(stateId);
    }
}
