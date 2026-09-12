using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The scaffolding counterpart of <see cref="ClimbableArrivalHoldTests"/>, and the reason <c>PhysicsEngineHolder</c>'s climbable hold needed a scaffolding term rather than being left as it was.</summary>
/// <remarks>
/// <para><b>The latch.</b> <c>_holdClimbable</c> is armed by a navigation that completes <c>OnClimbable &amp;&amp; !OnGround</c>, and while it is armed the idle tick presses <c>Sneak</c> every tick. On a ladder that is the only thing holding a player on a rung, which is what the sibling file pins. After the climb-clamp exemption the same press inside a scaffolding column means the opposite: the plates vanish and the body sinks 0.15 a tick until it runs out of column.</para>
/// <para><b>It is not self-limiting.</b> "A body resting on a plate is <c>OnGround</c>, so the latch never arms" is true of a navigation that ends ON the column's top face and false of one that ends on a rung INSIDE it. A climb completes inside its arrival window, i.e. up to 0.15 above the rung, with vanilla's climbable lift still in the body - measured at <c>y = rung + 0.0656</c>, feet cell scaffolding, not grounded. That arms the latch, and without the guard the idle tick then undoes the arrival it was added to protect.</para>
/// <para><b>Why this test is here and not in the pathfinding suite.</b> The latch lives on the client seam, past where any route test reaches: the pathfinding harness's <c>ExecutionDriver</c> stops at the executor and never runs an idle tick at all. This is the whole chain - real planner, real executor, real engine, real idle tick, real protocol-772 block data.</para>
/// </remarks>
public sealed class ScaffoldingArrivalHoldTests
{
    private const int Protocol = 772;
    private const int FloorY = 70;

    /// <summary>The rung the navigation ends on, part-way up a ten-cell column.</summary>
    private const int RungY = FloorY + 5;

    /// <summary>A navigation that ends on a mid-rung INSIDE a scaffolding column is still on that rung ten seconds later.</summary>
    /// <remarks>Measured before the guard, with everything else of this item in place: the idle <c>Sneak</c> sank the body the whole height of the column, from <c>y = 75.0656</c> to the floor, and stopped only because it ran out of scaffolding. That is a five-block silent slide AFTER the navigator has reported arrival.</remarks>
    [Fact]
    public async Task AMidRungArrivalInsideAScaffoldIsStillThere()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            PathExecutor executor = Navigate(harness, holder, new GoalBlock(new BlockPos(1, RungY, 0)));

            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.Equal(PathExecutorState.Complete, state);
            double arrival = harness.State.Self.Position.Y;
            Assert.True(
                arrival >= RungY - 0.01,
                $"the climb did not reach the rung: y={arrival:F4}");

            // The pose this test exists for. If the navigation ended grounded, the latch never armed and the run proves nothing, so say so instead of passing silently.
            Assert.True(
                holder.EngineState!.Value.OnClimbable && !holder.EngineState!.Value.OnGround,
                $"the arrival did not arm the climbable latch (onClimbable="
                + $"{holder.EngineState!.Value.OnClimbable}, onGround={holder.EngineState!.Value.OnGround}); "
                + "this run cannot prove anything about the idle hold");

            // The lease is released here, so UmpkClient.OnTickOnLoop starts calling TickIdle again.
            for (int tick = 0; tick < 200; tick++)
                holder.TickIdle();

            Vec3d rest = harness.State.Self.Position;
            Assert.True(
                rest.Y >= RungY - 0.01,
                $"the idle tick sank the body through its own column: arrived at y={arrival:F4}, "
                + $"rested at {rest}");
        }
    }

    private static PathExecutor Navigate(ApplierHarness harness, PhysicsEngineHolder holder, IGoal goal)
    {
        PlanCapture? capture = holder.CapturePlan(goal);
        Assert.NotNull(capture);
        PathExecutor? executor = holder.BuildExecutor(
            capture.Value, Umpk.Pathfinding.PathfinderOptions.Default, CancellationToken.None)?.Executor;
        Assert.NotNull(executor);
        return executor;
    }

    /// <summary>Joins, installs a world built on the version's REAL block data, and stands a free-standing ten-cell scaffolding column on a stone pad with the player on the pad beside it.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync()
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
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("scaffolding"), out BlockDefinition? scaffolding));
        int stoneState = stone.DefaultStateId;
        int scaffoldState = scaffolding.DefaultStateId;
        Assert.True(
            new BlockState(data, scaffoldState).IsScaffolding,
            "the fixture's scaffolding state is not recognised as scaffolding");

        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY - 1, z), stoneState);

        // A free-standing column, y = 70..79, so the rung the goal sits on has five cells of scaffolding below it to slide down and four above it.
        for (int y = FloorY; y <= FloorY + 9; y++)
            world.SetBlockStateId(new BlockPos(1, y, 0), scaffoldState);

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(-0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
