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
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The client half of a door: <c>PhysicsEngineHolder.TickNavigation</c> surfacing the executor's hold as <see cref="NavigationPreemption.Interaction"/>, and <c>CompleteInteraction</c> releasing it with the panel side read out of the LIVE world.
///
/// <para>The hold is a pause and not a pre-emption, and the difference is visible in what the tick does to the engine: a surfacing steps it (the supervisor is driving), an interaction hold does not. A body that drifts while the driver is mid-round-trip is a body that may have left the switch's reach, and the requirement it is holding for names a cell it is supposed to be standing in.</para>
///
/// <para>The panel side has to come from here rather than from the executor. The executor's world is the frozen plan capture, in which the door is still shut, and a closed door's panel is ninety degrees off the one the body is about to squeeze past: shape 1 <c>[[0,0,0,0.1875,1,1]]</c> against shape 4 <c>[[0,0,0,1,1,0.1875]]</c>.</para>
/// </summary>
public sealed class InteractionPreemptionTests
{
    private const int Protocol = 772;

    private const int FloorY = 70;

    private const int LaneZ = 0;

    private static readonly BlockPos Door = new(4, FloorY, LaneZ);

    /// <summary>The walled lane with a closed oak door at x=4, driven through the real holder, the real planner and the real engine. The tick that reaches the doorway reports the requirement and moves nothing.</summary>
    [Fact]
    public async Task TheHolderSurfacesTheExecutorsHoldAsAnInteractionPreemption()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(closed: true);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, new GoalBlock(new BlockPos(7, FloorY, LaneZ)));
            Assert.Contains(executor.Segments, s => s.Interaction is not null);

            NavigationTickOutcome outcome = default;
            Vec3d before = default;
            for (int tick = 0; tick < 400; tick++)
            {
                before = harness.State.Self.Position;
                outcome = holder.TickNavigation(executor);
                if (outcome.Preemption == NavigationPreemption.Interaction)
                    break;

                Assert.Equal(PathExecutorState.InProgress, outcome.State);
            }

            Assert.Equal(NavigationPreemption.Interaction, outcome.Preemption);
            InteractionRequirement requirement = Assert.NotNull(outcome.PendingInteraction);
            Assert.Equal(Door, requirement.Target);
            Assert.Equal(InteractionKind.OpenByHand, requirement.Kind);

            // The engine was not stepped: the hold is a pause, not a pre-emption.
            Assert.Equal(before.X, harness.State.Self.Position.X, 9);
            Assert.Equal(before.Z, harness.State.Self.Position.Z, 9);

            // And it keeps reporting the same thing rather than making progress on its own.
            for (int tick = 0; tick < 5; tick++)
            {
                NavigationTickOutcome again = holder.TickNavigation(executor);
                Assert.Equal(NavigationPreemption.Interaction, again.Preemption);
                Assert.Equal(requirement, Assert.NotNull(again.PendingInteraction));
            }
        }
    }

    /// <summary>Releasing the hold reads the door's REAL state. The plan was built against the closed door, the world now holds the open one, and the crossing the executor is handed is the open one's side.</summary>
    [Fact]
    public async Task CompletingTheInteractionReadsThePanelSideFromTheLiveWorld()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(closed: true);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, new GoalBlock(new BlockPos(7, FloorY, LaneZ)));

            NavigationTickOutcome outcome = default;
            for (int tick = 0; tick < 400 && outcome.Preemption != NavigationPreemption.Interaction; tick++)
                outcome = holder.TickNavigation(executor);

            Assert.Equal(NavigationPreemption.Interaction, outcome.Preemption);

            // What the driver's UseBlockVerifiedAsync would have observed: the server opened the door.
            OpenTheDoor(harness);
            holder.CompleteInteraction(executor, Door);

            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 400 && state == PathExecutorState.InProgress; tick++)
            {
                NavigationTickOutcome step = holder.TickNavigation(executor);
                Assert.NotEqual(NavigationPreemption.Interaction, step.Preemption);
                state = step.State;
            }

            Assert.Equal(PathExecutorState.Complete, state);
            Assert.True(
                harness.State.Self.Position.X > 6.0,
                $"the crossing did not reach the far end of the lane: {harness.State.Self.Position}");
        }
    }

    /// <summary>The reclose discipline. A door that shuts again with the body already past <c>BarrierCrossing.CommitOffset</c> is a verify-fail: the crossing is abandoned on the spot and the navigator replans from where the body is, which is survivable precisely because the panel never overlaps a centred body.</summary>
    [Fact]
    public async Task ARecloseAfterTheCommitPointFailsTheCrossing()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(closed: true);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, new GoalBlock(new BlockPos(7, FloorY, LaneZ)));

            NavigationTickOutcome outcome = default;
            for (int tick = 0; tick < 400 && outcome.Preemption != NavigationPreemption.Interaction; tick++)
                outcome = holder.TickNavigation(executor);

            OpenTheDoor(harness);
            holder.CompleteInteraction(executor, Door);

            // Walk until the body is committed, then shut the door on it.
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 400 && !executor.HasCommittedToCrossing && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.True(executor.HasCommittedToCrossing, "the body never committed to the crossing");
            CloseTheDoor(harness);

            NavigationTickOutcome reclosed = holder.TickNavigation(executor);
            Assert.Equal(PathExecutorState.Failed, reclosed.State);
        }
    }

    /// <summary>The other side of the same rule: a door that shuts again BEFORE the commit point is benign. The body is still outside the doorway and nothing is trapped, so the crossing is not abandoned on the spot - it simply meets the wall, and the ordinary segment machinery handles that.</summary>
    [Fact]
    public async Task ARecloseBeforeTheCommitPointIsNotTreatedAsAFailure()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(closed: true);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, new GoalBlock(new BlockPos(7, FloorY, LaneZ)));

            NavigationTickOutcome outcome = default;
            for (int tick = 0; tick < 400 && outcome.Preemption != NavigationPreemption.Interaction; tick++)
                outcome = holder.TickNavigation(executor);

            OpenTheDoor(harness);
            holder.CompleteInteraction(executor, Door);
            Assert.False(executor.HasCommittedToCrossing);

            CloseTheDoor(harness);
            NavigationTickOutcome next = holder.TickNavigation(executor);
            Assert.Equal(PathExecutorState.InProgress, next.State);
        }
    }

    /// <summary>An interaction is bought by something going RIGHT, so it gets its own budget for the reason a surfacing does. A corridor of doors must arrive at its first real deviation with every replan intact.</summary>
    [Fact]
    public void InteractionAttempts_DoNotConsumeTheReplanBudget()
    {
        var budget = new NavigationBudget(PathfinderOptions.Default, breathingStops: 0, plannedInteractions: 0);
        int replansAtStart = budget.ReplansLeft;
        int surfacingsAtStart = budget.SurfacingsLeft;

        Assert.Equal(NavigationBudget.MaxInteractionAttempts, budget.InteractionsLeft);
        Assert.True(budget.TryInteract());
        Assert.True(budget.TryInteract());
        Assert.False(budget.TryInteract());

        Assert.Equal(replansAtStart, budget.ReplansLeft);
        Assert.Equal(surfacingsAtStart, budget.SurfacingsLeft);
        Assert.True(budget.TryReplan());
    }

    /// <summary>The allowance is the route's OWN interaction count plus one retry, never below the floor: a corridor with four doors gets five attempts and a corridor with one gets two, so the retry is a retry rather than a door's worth of budget taken from the next door.</summary>
    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    public void InteractionBudget_IsSizedByThePlansOwnDoors(int plannedInteractions, int expected)
    {
        var budget = new NavigationBudget(PathfinderOptions.Default, breathingStops: 0, plannedInteractions);

        Assert.Equal(expected, budget.InteractionsLeft);
        for (int i = 0; i < expected; i++)
            Assert.True(budget.TryInteract(), $"attempt {i + 1} of {expected} was refused");

        Assert.False(budget.TryInteract());
    }

    private static void OpenTheDoor(ApplierHarness harness) => SetDoor(harness, open: true);

    private static void CloseTheDoor(ApplierHarness harness) => SetDoor(harness, open: false);

    private static void SetDoor(ApplierHarness harness, bool open)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("oak_door"), out BlockDefinition? door));
        harness.State.World.SetBlockStateId(Door, DoorState(data, door!, open, "lower"));
        harness.State.World.SetBlockStateId(
            new BlockPos(Door.X, Door.Y + 1, Door.Z), DoorState(data, door!, open, "upper"));
    }

    /// <summary>The <c>minecraft:oak_door</c> state with the wanted <c>open</c> and <c>half</c>, read out of the generated table rather than hardcoded, so the fixture cannot disagree with the client.</summary>
    /// <remarks><c>facing=east, hinge=left</c> gives shape 1 <c>[[0,0,0,0.1875,1,1]]</c> closed and shape 4 <c>[[0,0,0,1,1,0.1875]]</c> open: the panel across the lane while shut, and along it once open. Any other facing rotates the open panel back across the crossing, which is a doorway nothing can walk through and a different test.</remarks>
    private static int DoorState(IBlockDataSource data, BlockDefinition door, bool open, string half)
    {
        for (int id = door.MinStateId; id <= door.MaxStateId; id++)
            if (data.TryGetPropertyValue(id, "open", out string isOpen)
                && isOpen == (open ? "true" : "false")
                && data.TryGetPropertyValue(id, "half", out string which)
                && which == half
                && data.TryGetPropertyValue(id, "facing", out string facing)
                && facing == "east"
                && data.TryGetPropertyValue(id, "hinge", out string hinge)
                && hinge == "left")
                return id;

        Assert.Fail($"no oak_door state with open={open} half={half} facing=east hinge=left");
        return 0;
    }

    private static PathExecutor Navigate(PhysicsEngineHolder holder, IGoal goal)
    {
        PlanCapture? capture = holder.CapturePlan(goal);
        Assert.NotNull(capture);
        PathExecutor? executor = holder.BuildExecutor(
            capture.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
        Assert.NotNull(executor);
        holder.BeginNavigation();
        return executor;
    }

    /// <summary>A one-wide walled lane along z=0 with a door at x=4, on the version's REAL block table, so the door's states and shapes are the ones the client reads everywhere else.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync(
        bool closed)
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

        var holder = new PhysicsEngineHolder(services, JavaGameData.BlockShapes(Protocol), NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        int stoneState = stone.DefaultStateId;
        for (int x = -1; x <= 8; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY - 1, LaneZ), stoneState);
            for (int y = FloorY; y <= FloorY + 1; y++)
            {
                world.SetBlockStateId(new BlockPos(x, y, LaneZ - 1), stoneState);
                world.SetBlockStateId(new BlockPos(x, y, LaneZ + 1), stoneState);
            }
        }

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("oak_door"), out BlockDefinition? door));
        world.SetBlockStateId(Door, DoorState(data, door!, !closed, "lower"));
        world.SetBlockStateId(new BlockPos(Door.X, Door.Y + 1, Door.Z), DoorState(data, door!, !closed, "upper"));

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, LaneZ + 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
