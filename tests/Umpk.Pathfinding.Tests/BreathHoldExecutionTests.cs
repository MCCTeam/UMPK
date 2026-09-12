using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

/// <summary>Whether the executor STOPS at the cell the plan told it to breathe at.</summary>
/// <remarks>
/// <para>The plan can carry a pause (<see cref="PathSegment.BreathHoldTicks"/>) and the validator already assumes one is taken. Nothing performed it: <c>PathExecutor</c> knew three phases - <c>Executing</c>, <c>Aligning</c>, <c>AwaitingInteraction</c> - and a segment's completion advanced straight into the next segment's template whatever the plan said about waiting first.</para>
/// <para><b>Why the hold is modelled on the door and not on the surfacing.</b> A door is the shape already in the tree for "the body must not go on yet": the executor emits a requirement, presses nothing, and returns BEFORE <c>_segmentTicks++</c>, so the wait accrues no segment budget and <c>SegmentBudgetPolicy</c> never sees it. A breath hold wants exactly that. The reactive surfacing is a different animal - it is a vertical CLIMB that takes the navigation away from the executor - and reusing it here would make a scheduled wait indistinguishable from an emergency.</para>
/// <para>These rows are deliberately built from hand-made segments on dry ground rather than from a planned dive. The mechanism under test is the executor's phase machine, and a fixture that also has to swim eighty blocks would fail for a dozen reasons that are not this one. The planner half is pinned separately by <see cref="BreathHoldSchedulingTests"/>.</para>
/// </remarks>
public sealed class BreathHoldExecutionTests
{
    private const int FloorY = 64;

    /// <summary>The pause the plan schedules, in ticks. Round, and well clear of any template's budget.</summary>
    private const int PlannedHoldTicks = 40;

    private readonly ITestOutputHelper _output;

    public BreathHoldExecutionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheExecutorMustHoldAtTheCellThePlanScheduledAPauseFor()
    {
        (ExecutionDriver driver, PathExecutor executor, int baseline) = Run(holdTicks: PlannedHoldTicks);

        PathExecutorState state = driver.Run();
        int withHold = driver.Trace.Count;

        _output.WriteLine(
            $"state={state} ticks={withHold} baseline={baseline} planned hold={PlannedHoldTicks} "
            + $"holdTicksSeen={executor.BreathHoldTicksPerformed}");

        Assert.Equal(PathExecutorState.Complete, state);

        // The run has to be LONGER by about the scheduled pause. A tolerance of five ticks either way covers the one tick the hold begins on and the settle at the segment boundary.
        Assert.True(
            withHold >= baseline + PlannedHoldTicks - 5,
            $"the executor finished in {withHold} ticks where the same route without a scheduled pause "
            + $"takes {baseline}: the {PlannedHoldTicks}-tick hold was not performed");
    }

    [Fact]
    public void TheHoldIsSurfacedToTheDriverAsARequirement()
    {
        (ExecutionDriver driver, PathExecutor executor, _) = Run(holdTicks: PlannedHoldTicks);

        BreathHoldRequirement? seen = null;
        int heldTicks = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            PathExecutorTick step = driver.Step();
            if (step.PendingBreathHold is { } hold)
            {
                seen ??= hold;
                heldTicks++;

                // While it holds it presses nothing: the body must not swim away from the air.
                Assert.Equal(MovementInput.None, step.Output.Input);
            }

            if (step.State != PathExecutorState.InProgress)
                break;

        }

        _output.WriteLine($"first requirement={seen} heldTicks={heldTicks}");

        Assert.NotNull(seen);
        Assert.Equal(PlannedHoldTicks, seen.Value.PlannedTicks, 3);
        Assert.InRange(heldTicks, PlannedHoldTicks - 2, PlannedHoldTicks + 2);
        Assert.Equal(heldTicks, executor.BreathHoldTicksPerformed);
    }

    [Fact]
    public void TheHoldAccruesNoSegmentBudget()
    {
        (ExecutionDriver driver, PathExecutor executor, _) = Run(holdTicks: PlannedHoldTicks);

        int maxSegmentTicks = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            PathExecutorTick step = driver.Step();
            maxSegmentTicks = Math.Max(maxSegmentTicks, executor.CurrentSegmentTicks);
            if (step.State != PathExecutorState.InProgress)
                break;

        }

        _output.WriteLine(
            $"max segment ticks={maxSegmentTicks} over a {PlannedHoldTicks}-tick hold "
            + $"(performed {executor.BreathHoldTicksPerformed})");

        Assert.True(executor.BreathHoldTicksPerformed >= PlannedHoldTicks - 2);
        Assert.True(
            maxSegmentTicks < PlannedHoldTicks,
            $"a segment accumulated {maxSegmentTicks} ticks across a {PlannedHoldTicks}-tick breath hold, "
            + "so the wait is being charged to the segment budget");
    }

    /// <summary>Four one-block walks along a flat stone lane, with the scheduled pause hung on the SECOND of them, and the same run with no pause as the baseline.</summary>
    private (ExecutionDriver Driver, PathExecutor Executor, int BaselineTicks) Run(double holdTicks)
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 1, -4, 12, FloorY - 1, 4, FixtureWorld.Stone);
        world.Fill(-4, FloorY, -4, 12, FloorY + 3, 4, FixtureWorld.Air);

        // The baseline must actually be DRIVEN. Reading Trace.Count off a driver that was only constructed returns 0, which would make the comparison below pass for any route at all.
        ExecutionDriver control = Drive(world, 0.0);
        Assert.Equal(PathExecutorState.Complete, control.Run());
        int baseline = control.Trace.Count;
        Assert.True(baseline > 0, "the control run must have driven some ticks");

        ExecutionDriver driver = Drive(world, holdTicks);
        return (driver, driver.Executor, baseline);
    }

    private static ExecutionDriver Drive(FixtureWorld world, double holdTicks)
    {
        CalculationContext ctx = FixtureContext.Build(
            world, new BlockPos(0, FloorY, 0), new BlockPos(4, FloorY, 0));
        var execution = new PathExecutionContext(ctx.World, PhysicsProfile.ForProtocol(772), PhysicsConditions.Default);

        var segments = new List<PathSegment>();
        for (int i = 0; i < 4; i++)
        {
            segments.Add(new PathSegment
            {
                Start = new Vec3d(i + 0.5, FloorY, 0.5),
                End = new Vec3d(i + 1.5, FloorY, 0.5),
                StartFeetY = FloorY,
                EndFeetY = FloorY,
                MoveType = MoveType.Traverse,
                PlannedTickCost = 4.0,

                // The pause hangs on the second segment, so the run has real travel on both sides of it.
                BreathHoldTicks = i == 1 ? holdTicks : 0.0,
            });
        }

        return new ExecutionDriver(execution, segments, new Vec3d(0.5, FloorY, 0.5));
    }
}
