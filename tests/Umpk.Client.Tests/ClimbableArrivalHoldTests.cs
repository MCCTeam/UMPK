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

/// <summary>
/// The client seam that makes a ladder arrival stick: <c>PhysicsEngineHolder</c>'s idle tick. UMPK skips the idle tick while a movement lease is held and resumes it the moment the navigator releases, so the tick right after <c>MoveToAsync</c> returns is what decides whether the player is still where the command said it went.
/// <para>A climber's descent is clamped to -0.15 and pinned to zero only while sneaking. An idle tick that presses nothing slides the player down the whole shaft at 3 blocks a second: measured two blocks in 0.7 seconds, which is why every reading of the server's own copy of the player found it back on the platform and <c>move.ladder_climb</c> recorded FAIL on every protocol it was measured on.</para>
/// </summary>
public sealed class ClimbableArrivalHoldTests
{
    private const int Protocol = 772;
    private const int FloorY = 70;

    /// <summary>The whole chain the live run uses: the real planner, the real executor, the real engine, and the real idle tick, on the movement probe's fixture. The assertion is the probe's own verdict, read off self state (which is what the position reporter puts on the wire) a full ten seconds of ticks after the navigation finished.</summary>
    [Fact]
    public async Task AnArrivalOnARungIsStillThereTenSecondsLater()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            PathExecutor executor = Navigate(harness, holder, new GoalNear(1, FloorY + 3, 0, 1));

            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.Equal(PathExecutorState.Complete, state);
            double arrival = harness.State.Self.Position.Y;
            Assert.True(arrival >= FloorY + 2.0, $"the climb did not reach the rung: y={arrival}");

            // The lease is released here, so UmpkClient.OnTickOnLoop starts calling TickIdle again.
            for (int tick = 0; tick < 200; tick++)
                holder.TickIdle();

            Vec3d rest = harness.State.Self.Position;
            Assert.True(rest.Y >= FloorY + 2.0, $"the arrival was undone by the idle tick: {rest}");
            Assert.True(rest.X > 1.0 && rest.X < 2.0, $"the bot drifted out of the ladder column: {rest}");
        }
    }

    /// <summary>The latch is narrow on purpose. A navigation that ends on solid ground must leave the idle tick pressing nothing, because pressing sneak there would crouch the player for the rest of the session: a locally-crouched player has a shorter hitbox than the one the server is simulating. The server re-runs the move it is sent and rubber-bands a position it cannot reproduce.</summary>
    [Fact]
    public async Task AnArrivalOnTheGroundDoesNotHoldAnything()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            // Walk along the platform instead of up the ladder.
            PathExecutor executor = Navigate(harness, holder, new GoalNear(-2, FloorY, 0, 0));

            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.Equal(PathExecutorState.Complete, state);

            for (int tick = 0; tick < 40; tick++)
                holder.TickIdle();

            Assert.True(harness.State.Self.OnGround, "a walk on the platform should end grounded");
            Assert.Equal(FloorY, harness.State.Self.Position.Y, 3);
            Assert.NotEqual(Umpk.Game.Entities.EntityPose.Crouching, holder.EngineState!.Value.Pose);
        }
    }

    /// <summary>A server teleport ends the hold: the player is no longer where it was holding on, and continuing to press sneak would mean the client's own simulation was still acting on a position the server has replaced.</summary>
    [Fact]
    public async Task ATeleportEndsTheHold()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            PathExecutor executor = Navigate(harness, holder, new GoalNear(1, FloorY + 3, 0, 1));
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.Equal(PathExecutorState.Complete, state);

            // Back onto the bottom rung, the way a server teleport would put it there.
            harness.State.Self.Position = new Vec3d(1.5, FloorY + 2, 0.5);
            harness.State.Self.Velocity = Vec3d.Zero;
            holder.ResyncPosition();

            for (int tick = 0; tick < 60; tick++)
                holder.TickIdle();

            // No hold, so vanilla's -0.15 clamp takes it back to the floor, which is what a vanilla client that is not pressing shift does.
            Assert.Equal(FloorY, harness.State.Self.Position.Y, 3);
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

    /// <summary>Joins, installs a world built on the version's REAL block data (so the ladder is climbable and its collision slab is the real one), builds the movement probe's ladder fixture, and puts the player on the anchor.</summary>
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
        int stoneState = stone.DefaultStateId;
        int ladder = LadderStates.FacingSouthDry(blocks, data);

        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY - 1, z), stoneState);

        for (int y = FloorY; y <= FloorY + 3; y++)
            world.SetBlockStateId(new BlockPos(1, y, -1), stoneState);

        for (int y = FloorY; y <= FloorY + 2; y++)
            world.SetBlockStateId(new BlockPos(1, y, 0), ladder);

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
