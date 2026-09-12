using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class InteractionHoldTests(ITestOutputHelper output)
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    private const int CourseMargin = 24;

    private const int LaneZ = 1;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);

    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    /// <summary>A 1-wide walled lane with a closed oak door at both body cells of x=4.</summary>
    private static FixtureWorld DoorLane()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, LaneZ, FixtureWorld.ClosedDoorWest);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.ClosedDoorWest);
        return world;
    }

    /// <summary>L5's and G5a's plot: the lane with a CLOSED TOP trapdoor at x=5 as the floor, and a three-deep shaft under it. The lid is flush with the corridor floor, so the body walks over it and can only go down by opening it.</summary>
    private static FixtureWorld FloorHatch()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        world.Fill(4, FloorY - 4, 0, 6, FloorY - 1, 2, FixtureWorld.Stone);
        world.Fill(5, FloorY - 2, LaneZ, 5, FloorY - 1, LaneZ, FixtureWorld.Air);
        world.Set(5, FloorY, LaneZ, FixtureWorld.ClosedTrapdoorTop);
        return world;
    }

    private static readonly BlockPos HatchGoal = new(5, FloorY - 2, LaneZ);

    private static (IReadOnlyList<PathSegment> Segments, PlanningWorldView View) Plan(
        FixtureWorld world, BlockPos start, BlockPos goal)
    {
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        return (PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default), view);
    }

    /// <summary>The executor reaches the doorway, reports what has to be opened, and stops. It presses nothing - a body drifting while the driver is mid-round-trip is a body that may have left the switch's reach - and it does not advance past the segment.</summary>
    [Fact]
    public void TheExecutorHoldsAndReportsTheRequirementInsteadOfCrossing()
    {
        (IReadOnlyList<PathSegment> segments, PlanningWorldView view) = Plan(DoorLane(), LaneStart, LaneGoal);
        int held = segments.Select((s, i) => (s, i)).First(p => p.s.Interaction is not null).i;

        var executor = new PathExecutor(new PathExecutionContext(view, Profile), segments);
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        PathExecutorTick tick = default;
        for (int i = 0; i < 200; i++)
        {
            tick = executor.Tick(engine.State);
            if (tick.PendingInteraction is not null)
                break;

            Assert.Equal(PathExecutorState.InProgress, tick.State);
            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        InteractionRequirement requirement = Assert.NotNull(tick.PendingInteraction);
        Assert.Equal(new BlockPos(4, BodyY, LaneZ), requirement.Target);
        Assert.Equal(InteractionKind.OpenByHand, requirement.Kind);
        Assert.Equal(held, executor.CurrentIndex);

        // Held, not crossing: the same requirement comes back every tick, the input is empty, and the index does not move.
        for (int i = 0; i < 10; i++)
        {
            PathExecutorTick again = executor.Tick(engine.State);
            Assert.Equal(PathExecutorState.InProgress, again.State);
            Assert.Equal(requirement, Assert.NotNull(again.PendingInteraction));
            Assert.Equal(MovementInput.None, again.Output.Input);
            Assert.Equal(held, executor.CurrentIndex);
        }
    }

    /// <summary>Once the door is reported open, the hold releases and the crossing runs to the goal.</summary>
    [Fact]
    public void TheExecutorCrossesOnceTheInteractionIsSatisfied()
    {
        FixtureWorld world = DoorLane();

        // The world the EXECUTOR simulates against is the frozen plan capture, in which the door is still shut. Driving the body through it needs the door actually open, which is what the driver above has just made true; the plan is built against the closed one, exactly as production does.
        (IReadOnlyList<PathSegment> planned, _) = Plan(world, LaneStart, LaneGoal);
        world.Set(4, BodyY, LaneZ, FixtureWorld.OpenDoorNorth);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.OpenDoorNorth);
        PlanningWorldView opened = world.Capture(LaneStart, LaneGoal, CourseMargin);

        var ctx = new PathExecutionContext(opened, Profile);
        var executor = new PathExecutor(ctx, planned);
        var engine = new PlayerPhysics(opened, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        bool satisfied = false;
        PathExecutorState state = PathExecutorState.InProgress;
        double minLateral = double.PositiveInfinity;
        double maxLateral = double.NegativeInfinity;
        for (int i = 0; i < 400 && state == PathExecutorState.InProgress; i++)
        {
            if (engine.State.Position.X + 0.3 > 4 && engine.State.Position.X - 0.3 < 5)
            {
                minLateral = Math.Min(minLateral, engine.State.Position.Z - LaneZ);
                maxLateral = Math.Max(maxLateral, engine.State.Position.Z - LaneZ);
            }

            PathExecutorTick tick = executor.Tick(engine.State);
            state = tick.State;
            if (tick.PendingInteraction is not null)
            {
                Assert.False(satisfied, "the executor asked for the same interaction twice");
                satisfied = true;

                // The side the driver OBSERVED once the door moved: the frozen capture still holds the closed panel, which is ninety degrees off.
                Assert.True(BarrierCrossing.TryResolve(opened, 4, BodyY, LaneZ, out BarrierCrossing seen));
                executor.NotifyInteractionSatisfied(seen);
                continue;
            }

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        Assert.True(satisfied, "the executor never asked for the door to be opened");
        Assert.Equal(PathExecutorState.Complete, state);

        // The observed side is what makes the lane hold aim off the panel. Without it the crossing runs at the cell centre, which is the band's own edge.
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"doorway lateral {minLateral:F4}..{maxLateral:F4}"));
        Assert.InRange(minLateral, 0.55, 0.7);
        Assert.InRange(maxLateral, 0.55, 0.7);
    }

    /// <summary><see cref="BarrierCrossing.CommitOffset"/> is the body's half-width plus the panel's own depth, and what it marks is the point past which a closing panel no longer overlaps the body.</summary>
    [Fact]
    public void TheCommitPointIsHalfWidthPlusPanelDepth()
    {
        Assert.Equal(0.3 + 0.1875, BarrierCrossing.CommitOffset, 10);
        Assert.Equal(0.4875, BarrierCrossing.CommitOffset, 10);

        // And it is exactly where the panel stops touching the body: a body centred at CommitOffset has its trailing face on the panel's far edge.
        Assert.Equal(0.1875, BarrierCrossing.CommitOffset - 0.3, 10);
    }

    /// <summary>The executor latches commitment once the body's centre is past that point along the crossing's own direction of travel, and not before. This is the switch a reclose is judged on: benign on one side, a verify-fail on the other.</summary>
    [Fact]
    public void CommitmentLatchesOnlyOnceTheBodyIsPastTheCommitPoint()
    {
        FixtureWorld world = DoorLane();
        (IReadOnlyList<PathSegment> planned, _) = Plan(world, LaneStart, LaneGoal);
        world.Set(4, BodyY, LaneZ, FixtureWorld.OpenDoorNorth);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.OpenDoorNorth);
        PlanningWorldView opened = world.Capture(LaneStart, LaneGoal, CourseMargin);

        var executor = new PathExecutor(new PathExecutionContext(opened, Profile), planned);
        var engine = new PlayerPhysics(opened, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        double committedAt = double.NaN;
        for (int i = 0; i < 400; i++)
        {
            PathExecutorTick tick = executor.Tick(engine.State);
            if (executor.HasCommittedToCrossing && double.IsNaN(committedAt))
                committedAt = engine.State.Position.X;

            if (tick.PendingInteraction is not null)
            {
                Assert.False(executor.HasCommittedToCrossing, "a body still outside the doorway is not committed");
                Assert.True(BarrierCrossing.TryResolve(opened, 4, BodyY, LaneZ, out BarrierCrossing seen));
                executor.NotifyInteractionSatisfied(seen);
                continue;
            }

            if (tick.State != PathExecutorState.InProgress)
                break;

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"committed at x={committedAt:F4}"));
        Assert.False(double.IsNaN(committedAt), "the crossing never reported commitment");
        Assert.True(
            committedAt >= 4 + BarrierCrossing.CommitOffset,
            $"commitment latched at x={committedAt}, before the door face plus {BarrierCrossing.CommitOffset}");
    }

    /// <summary>A closed trapdoor's opened panel side is readable from <c>facing</c> before anything moves: the open panel occupies the wall opposite that direction.</summary>
    [Fact]
    public void AClosedTrapdoorsOpenedPanelSideIsPredictedFromFacing()
    {
        PlanningWorldView view = FloorHatch().Capture(LaneStart, HatchGoal, CourseMargin);

        Assert.True(
            BarrierCrossing.TryPredictAfterOpening(view, new BlockPos(5, FloorY, LaneZ), out BarrierCrossing predicted));

        // The fixture's lid is facing=east, so the panel lands on the cell's WEST face and the free side is +X.
        Assert.Equal(-1, predicted.PanelX);
        Assert.Equal(0, predicted.PanelZ);
        Assert.Equal(1, predicted.FreeX);
    }

    /// <summary>An already-open barrier is not predicted: its side is simply read off the shape.</summary>
    [Fact]
    public void AnOpenTrapdoorIsNotPredicted()
    {
        var world = new FixtureWorld();
        world.Set(5, FloorY, LaneZ, FixtureWorld.OpenTrapdoorNorth);
        PlanningWorldView view = world.Capture(LaneStart, HatchGoal, CourseMargin);

        Assert.False(BarrierCrossing.TryPredictAfterOpening(view, new BlockPos(5, FloorY, LaneZ), out _));
    }

    /// <summary>The segment that drops through the lid carries the PREDICTED crossing, so the executor has something to square up against before anything is opened.</summary>
    [Fact]
    public void TheFallThroughTheLidCarriesThePredictedCrossing()
    {
        (IReadOnlyList<PathSegment> segments, _) = Plan(FloorHatch(), LaneStart, HatchGoal);
        PathSegment fall = Assert.Single(segments, s => s.Interaction is not null);

        Assert.Equal(MoveType.Fall, fall.MoveType);
        BarrierCrossing crossing = Assert.NotNull(fall.Crossing);
        Assert.Equal(-1, crossing.PanelX);
    }

    [Fact]
    public void L5_TheBandIsSatisfiedBeforeTheStepOff()
    {
        (IReadOnlyList<PathSegment> segments, PlanningWorldView view) = Plan(FloorHatch(), LaneStart, HatchGoal);

        var executor = new PathExecutor(new PathExecutionContext(view, Profile), segments);
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        Vec3d? stepOff = null;
        for (int i = 0; i < 400; i++)
        {
            PathExecutorTick tick = executor.Tick(engine.State);
            if (tick.PendingInteraction is not null)
            {
                stepOff = engine.State.Position;
                break;
            }

            Assert.Equal(PathExecutorState.InProgress, tick.State);
            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        Vec3d at = Assert.NotNull(stepOff);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"step-off at {at.X:F4}, {at.Z:F4}"));

        // The lid's panel will stand on the cell's WEST face, x in [5, 5.1875]. A 0.6-wide body clears it from a centre of 5.4875, and the aim is 5.6; the far wall bounds it at 5.7.
        Assert.InRange(at.X - 5.0, 0.55, 0.7);
    }

    /// <summary>The control for the row above: without the crossing on the segment there is nothing to square up against, and the body holds where the walk left it - the cell centre, which is the band's very edge with 0.0125 blocks to spare. This is what the alignment phase exists to move away from.</summary>
    [Fact]
    public void WithoutAPredictedCrossingTheStepOffIsOnTheBandsEdge()
    {
        (IReadOnlyList<PathSegment> segments, PlanningWorldView view) = Plan(FloorHatch(), LaneStart, HatchGoal);
        var stripped = segments.Select(s => s with { Crossing = null }).ToList();

        var executor = new PathExecutor(new PathExecutionContext(view, Profile), stripped);
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        Vec3d? stepOff = null;
        for (int i = 0; i < 400; i++)
        {
            PathExecutorTick tick = executor.Tick(engine.State);
            if (tick.PendingInteraction is not null)
            {
                stepOff = engine.State.Position;
                break;
            }

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        Vec3d at = Assert.NotNull(stepOff);
        Assert.True(
            at.X - 5.0 < 0.55,
            $"the unaligned step-off should sit at the cell centre, not inside the band: {at.X - 5.0}");
    }
}
