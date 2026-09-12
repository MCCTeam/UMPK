using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SprintExecutionTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    private static Vec3d Center(BlockPos p) => new(p.X + 0.5, p.Y, p.Z + 0.5);

    private static IReadOnlyList<PathSegment> Plan(
        PlanningWorldView view, PathfinderOptions options, BlockPos start, BlockPos goal)
    {
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        Assert.NotEmpty(segments);
        return segments;
    }

    private static FixtureWorld Runway(int length)
    {
        var world = new FixtureWorld();
        world.Floor(-8, length + 8, -8, 8, FloorY);
        return world;
    }

    // Coherence: the lookahead and the live engine are the same predictor

    [Fact]
    public void ExpectedState_MatchesTheLiveEngine_WhileSprinting()
    {
        FixtureWorld world = Runway(16);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(12, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(ctx.Conditions);
        engine.Reset(segments[0].Start, FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        var executor = new PathExecutor(ctx, segments);
        int comparedWhileSprinting = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            PathExecutorTick step = executor.Tick(engine.State);
            if (step.State != PathExecutorState.InProgress)
                break;

            engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            StepResult result = engine.Step(step.Output.Input);

            if (step.Output.ExpectedState is not { } expected || !step.Output.Input.Sprint)
                continue;

            comparedWhileSprinting++;
            Assert.Equal(expected.Position.X, result.State.Position.X, 9);
            Assert.Equal(expected.Position.Y, result.State.Position.Y, 9);
            Assert.Equal(expected.Position.Z, result.State.Position.Z, 9);
            Assert.Equal(expected.Velocity.X, result.State.Velocity.X, 9);
            Assert.Equal(expected.Velocity.Z, result.State.Velocity.Z, 9);
        }

        Assert.True(comparedWhileSprinting > 10, $"only {comparedWhileSprinting} sprinting ticks were compared");
    }

    // The cost model

    [Fact]
    public void Traverse_ExecutesAtTheCostThePlannerCharged()
    {
        const int Blocks = 24;
        FixtureWorld world = Runway(Blocks);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(Blocks, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start), FacingPlusX);
        PathExecutorState state = driver.Run(maxTicks: 400);

        Assert.Equal(PathExecutorState.Complete, state);

        double cruise = TicksPerBlockBetween(driver.Trace, fromX: start.X + 6.5, toX: goal.X - 5.5);
        Assert.Equal(ActionCosts.SprintOneBlock, cruise, 2);

        int ticks = driver.EmittedInputs.Count;
        double walked = Blocks * ActionCosts.WalkOneBlock;
        Assert.True(
            ticks < walked,
            string.Create(
                CultureInfo.InvariantCulture,
                $"a {Blocks}-block straight run took {ticks} ticks, which walking it would have cost " +
                $"({walked:F1}); cruise was {cruise:F4} ticks a block against a charge of " +
                $"{ActionCosts.SprintOneBlock:F4}"));
    }

    /// <summary>Ticks per block over the stretch of the trace between two X coordinates, which is the cruise rate with the acceleration ramp and the braking tail cut off both ends.</summary>
    private static double TicksPerBlockBetween(IReadOnlyList<TickSample> trace, double fromX, double toX)
    {
        int first = -1;
        int last = -1;
        for (int i = 0; i < trace.Count; i++)
        {
            if (first < 0 && trace[i].Position.X >= fromX)
                first = i;

            if (trace[i].Position.X <= toX)
                last = i;

        }

        Assert.True(first >= 0 && last > first, $"the trace never spanned x {fromX} to {toX}");
        return (last - first) / (trace[last].Position.X - trace[first].Position.X);
    }

    /// <summary>The straight run is actually sprinting, not merely fast. Pins the bit itself so the tick-count assertion above cannot be satisfied by some other speed source.</summary>
    [Fact]
    public void Traverse_EmitsSprint_WhenSprintingIsAllowed()
    {
        const int Blocks = 24;
        FixtureWorld world = Runway(Blocks);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(Blocks, FloorY + 1, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<PathSegment> segments = Plan(view, PathfinderOptions.Default, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start), FacingPlusX);
        Assert.Equal(PathExecutorState.Complete, driver.Run(maxTicks: 400));

        int sprintTicks = driver.EmittedInputs.Count(i => i.Sprint);
        Assert.True(sprintTicks > driver.EmittedInputs.Count / 2, $"only {sprintTicks} of {driver.EmittedInputs.Count} ticks sprinted");
    }

    // AllowSprint

    [Fact]
    public void AllowSprintFalse_IsHonouredByTheExecutor()
    {
        const int Blocks = 24;
        FixtureWorld world = Runway(Blocks);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(Blocks, FloorY + 1, 0);
        PathfinderOptions options = PathfinderOptions.Default with { AllowSprint = false };

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default, allowSprint: false);
        IReadOnlyList<PathSegment> segments = Plan(view, options, start, goal);

        var driver = new ExecutionDriver(ctx, segments, Center(start), FacingPlusX);
        PathExecutorState state = driver.Run(maxTicks: 400);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.DoesNotContain(driver.EmittedInputs, i => i.Sprint);

        int ticks = driver.EmittedInputs.Count;
        double charged = Blocks * ActionCosts.WalkOneBlock;
        Assert.True(
            ticks <= charged * 1.10,
            string.Create(
                CultureInfo.InvariantCulture,
                $"a {Blocks}-block walk-only run took {ticks} ticks; the planner charged {charged:F1}"));
    }

    [Fact]
    public void AllowSprintFalse_IsHonouredByTheBrakingLookahead()
    {
        const int Blocks = 12;
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(Blocks, FloorY + 1, 0);

        bool CarriesSprint(bool allowSprint)
        {
            FixtureWorld world = Runway(Blocks);
            PathfinderOptions options = PathfinderOptions.Default with { AllowSprint = allowSprint };
            PlanningWorldView view = world.Capture(start, goal, margin: 8);
            IReadOnlyList<PathSegment> segments = Plan(view, options, start, goal);

            var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default, allowSprint);
            var engine = new PlayerPhysics(view, Profile);
            engine.SetConditions(ctx.Conditions);
            engine.Reset(segments[0].Start, FacingPlusX, 0f);
            engine.Step(MovementInput.None);
            for (int i = 0; i < 20; i++)
                engine.Step(new MovementInput { Forward = true, Sprint = allowSprint });

            PathSegment carrying = segments.First(s => s.PreserveSprint);
            return ctx.Braking.Plan(carrying, engine.State.Position, engine.State).HoldSprint;
        }

        Assert.True(CarriesSprint(allowSprint: true), "the control never produced a sprinting handoff at all");
        Assert.False(CarriesSprint(allowSprint: false), "the braking planner offered a sprinting handoff with AllowSprint off");
    }

    // Stopping distance.

    /// <summary>A <c>FinalStop</c> entered at the sprint steady velocity still stops on the goal block, and stops no worse than the same approach at walking speed. The free-coast distance <c>v / (1 - 0.546)</c> grows from 0.2596 to 0.3375 blocks when the entry velocity goes 0.11786 -> 0.15322, about 0.078 of a block of extra carry; this is the assertion that says the braking planner absorbed it rather than spending it overshooting the goal cell.</summary>
    /// <remarks>The predicate is the executor's own <c>FinalStop</c> contract, <c>IsSettledAtEnd</c>, which accepts a centre inside the destination column and does NOT require the whole 0.6-wide footprint to clear the rims. Asserting the stricter footprint test here would be asserting something the executor has never done at a final stop: measured on this fixture it stops 0.38 of a block short of the block centre while sprinting and 0.44 short while walking, so a footprint assertion fails on BOTH and says nothing about sprint. The walk run is therefore carried as the comparison: what matters is that the faster entry did not push the stop past the goal cell, and it landed slightly deeper into it.</remarks>
    [Fact]
    public void FinalStop_EnteredAtSprintSpeed_SettlesOnTheTargetBlock()
    {
        const int Blocks = 16;

        (PathExecutorState State, Vec3d End, Vec3d Target) Run(bool allowSprint)
        {
            FixtureWorld world = Runway(Blocks);
            var start = new BlockPos(0, FloorY + 1, 0);
            var goal = new BlockPos(Blocks, FloorY + 1, 0);
            PlanningWorldView view = world.Capture(start, goal, margin: 8);
            var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default, allowSprint);
            IReadOnlyList<PathSegment> segments = Plan(
                view, PathfinderOptions.Default with { AllowSprint = allowSprint }, start, goal);
            Assert.Equal(PathTransitionType.FinalStop, segments[^1].ExitTransition);

            var driver = new ExecutionDriver(ctx, segments, Center(start), FacingPlusX);
            PathExecutorState state = driver.Run(maxTicks: 400);
            return (state, driver.State.Position, segments[^1].End);
        }

        (PathExecutorState state, Vec3d end, Vec3d target) = Run(allowSprint: true);
        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            SegmentGeometry.IsCenterInsideTargetBlock(end, target),
            string.Create(CultureInfo.InvariantCulture, $"the final stop settled at {end}, outside the goal block at {target}"));

        // Two-sided against the walking approach over the same geometry: the extra 0.078 of coast must not have carried the stop further from the goal centre than walking leaves it.
        (_, Vec3d walkEnd, Vec3d walkTarget) = Run(allowSprint: false);
        double sprintError = Math.Abs(target.X - end.X);
        double walkError = Math.Abs(walkTarget.X - walkEnd.X);
        Assert.True(
            sprintError <= walkError + 0.05,
            string.Create(
                CultureInfo.InvariantCulture,
                $"sprinting stopped {sprintError:F4} from the goal centre against walking's {walkError:F4}"));
    }
}
