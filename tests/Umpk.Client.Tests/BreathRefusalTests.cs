using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The production wiring of the breath validator: <c>PhysicsEngineHolder.BuildExecutor</c> refuses to build an executor for a route the player drowns on, so an unsurvivable plan surfaces to the caller as the ordinary "no path to the goal" failure rather than as a bot that swims into a bore and dies.</summary>
public sealed class BreathRefusalTests
{
    private const int Protocol = 772;
    private const int FloorY = 70;

    /// <summary>Eighty blocks of lidded, flooded bore. The planner is perfectly happy with it (it charges the submerged bottom-walk at sprint speed, 285 ticks) and the player drowns two thirds of the way through.</summary>
    [Fact]
    public async Task ASealedEightyBlockBoreProducesNoExecutor()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(80);
        await using (loop)
        {
            _ = harness;
            PlanCapture? capture = holder.CapturePlan(new GoalNear(81, FloorY, 0, 0));
            Assert.NotNull(capture);

            // Not vacuous: a search WITHOUT the air dimension succeeds and yields segments, so the null below is a refusal and not "there was no route". With the dimension on - the default, and what BuildExecutor uses - the search refuses it too, which is a second refusal in front of the validator's rather than a replacement for it.
            var notBreathAware = PathfinderOptions.Default with { BreathAware = false };
            PathResult probe = PathPlanner.FindPath(
                capture.Value.Planning, notBreathAware, capture.Value.Start, capture.Value.Goal);
            Assert.Equal(PathStatus.Success, probe.Status);
            Assert.NotEmpty(PathSegmentBuilder.FromPath(probe.Path));

            PathResult breathAware = PathPlanner.FindPath(
                capture.Value.Planning, PathfinderOptions.Default, capture.Value.Start, capture.Value.Goal);
            Assert.NotEqual(PathStatus.Success, breathAware.Status);

            PathExecutor? executor = holder.BuildExecutor(capture.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;

            Assert.Null(executor);
        }
    }

    /// <summary>The same bore at twenty blocks is 214 real ticks of a 300-tick lung, and is built.</summary>
    [Fact]
    public async Task ATwentyBlockBoreStillProducesAnExecutor()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(20);
        await using (loop)
        {
            _ = harness;
            PlanCapture? capture = holder.CapturePlan(new GoalNear(21, FloorY, 0, 0));
            Assert.NotNull(capture);

            PathExecutor? executor = holder.BuildExecutor(capture.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;

            Assert.NotNull(executor);
        }
    }

    /// <summary>Joins on the version's real block data and builds a two-cell-tall corridor through solid stone, dry at both mouths and flooded for <paramref name="length"/> blocks under a solid lid.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync(int length)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Pathfinding = true, Terrain = true });
        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        int stoneState = stone.DefaultStateId;
        int waterState = water.MinStateId;

        // A solid block of stone with a corridor bored through it at z = 0.
        for (int x = -4; x <= length + 3; x++)
            for (int y = FloorY - 1; y <= FloorY + 3; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        for (int x = -3; x <= length + 2; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), 0);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), 0);
        }

        for (int x = 0; x < length; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(-2.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
