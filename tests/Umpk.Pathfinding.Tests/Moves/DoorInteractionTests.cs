using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class DoorInteractionTests
{
    private const int FloorY = 99;

    /// <summary>The course's walking plane, a body's feet cell (<c>SURFACE_Y</c>).</summary>
    private const int BodyY = 100;

    private const int CourseMargin = 24;

    private const int LaneZ = 1;

    private const int DoorX = 4;

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);

    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    /// <summary>G6's plot: a 1-wide walled lane with whatever the caller puts in both body cells of x=4.</summary>
    private static FixtureWorld Lane(int doorwayState, int length = 7)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, length, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, length, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, length, BodyY + 1, 2, FixtureWorld.Stone);
        if (doorwayState != FixtureWorld.Air)
        {
            world.Set(DoorX, BodyY, LaneZ, doorwayState);
            world.Set(DoorX, BodyY + 1, LaneZ, doorwayState);
        }

        return world;
    }

    /// <summary>L7's plot: the same lane, twelve cells long, with a closed oak door at x=4 and another at x=9. One door cannot show whether the interaction machinery is per-crossing; a second one five cells later can.</summary>
    private static FixtureWorld TwoDoorLane()
    {
        FixtureWorld world = Lane(FixtureWorld.ClosedDoorWest, length: 12);
        world.Set(9, BodyY, LaneZ, FixtureWorld.ClosedDoorWest);
        world.Set(9, BodyY + 1, LaneZ, FixtureWorld.ClosedDoorWest);
        return world;
    }

    private static readonly BlockPos TwoDoorGoal = new(12, BodyY, LaneZ);

    private static FixtureWorld GateLane()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 4, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 4, 7, BodyY + 1, 4, FixtureWorld.Stone);
        world.Set(DoorX, BodyY, 1, FixtureWorld.Fence);
        world.Set(DoorX, BodyY, 3, FixtureWorld.Fence);
        world.Set(DoorX, BodyY, 2, FixtureWorld.ClosedFenceGate);
        return world;
    }

    private static readonly BlockPos GateStart = new(0, BodyY, 2);

    private static readonly BlockPos GateGoal = new(7, BodyY, 2);

    private static FixtureWorld FloorHatch(int lidState)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);

        // The shaft: solid rock around x=5, hollowed at the two cells under the lid.
        world.Fill(4, FloorY - 4, 0, 6, FloorY - 1, 2, FixtureWorld.Stone);
        world.Fill(5, FloorY - 2, LaneZ, 5, FloorY - 1, LaneZ, FixtureWorld.Air);
        world.Set(5, FloorY, LaneZ, lidState);
        return world;
    }

    private static readonly BlockPos HatchGoal = new(5, FloorY - 2, LaneZ);

    private static CalculationContext Context(FixtureWorld world, PathfinderOptions? options = null)
        => new(world.Capture(LaneStart, LaneGoal, CourseMargin), options ?? PathfinderOptions.Default);

    private static PathResult Plan(
        FixtureWorld world, BlockPos start, BlockPos goal, PathfinderOptions? options = null)
    {
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        return PathPlanner.FindPath(view, options ?? PathfinderOptions.Default, start, new GoalBlock(goal));
    }

    /// <summary>With interaction disabled, closed barriers are not passable.</summary>
    private static readonly PathfinderOptions NoInteraction =
        PathfinderOptions.Default with { AllowDoorInteraction = false };

    /// <summary>The third answer. A closed member of the family is <see cref="BarrierKind.NeedsInteraction"/> when a bare hand opens it and <see cref="BarrierKind.Wall"/> when it does not, and the split is Only <c>iron_door</c> and <c>iron_trapdoor</c> refuse hand activation, so they fall on the wrong side of it.</summary>
    [Theory]
    [InlineData(FixtureWorld.ClosedDoorWest, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.ClosedTrapdoorBottom, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.ClosedTrapdoorTop, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.ClosedFenceGate, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.IronDoorClosed, BarrierKind.Wall)]
    [InlineData(FixtureWorld.IronTrapdoorClosedTop, BarrierKind.Wall)]
    [InlineData(FixtureWorld.OpenDoorNorth, BarrierKind.PassableNow)]
    [InlineData(FixtureWorld.UnreadableDoor, BarrierKind.Wall)]
    public void ClassifyBarrier_SplitsAClosedBarrierOnCanOpenByHand(int stateId, BarrierKind expected)
    {
        var world = new FixtureWorld();
        world.Set(DoorX, BodyY, LaneZ, stateId);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);

        Assert.Equal(expected, MoveHelper.ClassifyBarrier(view.GetBlock(new BlockPos(DoorX, BodyY, LaneZ))));
    }

    [Fact]
    public void AClosedWoodenDoor_IsPassableWithAnInteractionAndAWallWithout()
    {
        Assert.True(
            Context(Lane(FixtureWorld.ClosedDoorWest)).CanWalkThrough(DoorX, BodyY, LaneZ),
            "a closed oak door is one use_item_on away from open, which is what vanilla's own "
                + "DoorBlock.useWithoutItem does for every non-iron set type");
        Assert.False(Context(Lane(FixtureWorld.ClosedDoorWest), NoInteraction).CanWalkThrough(DoorX, BodyY, LaneZ));
    }

    [Fact]
    public void AClosedIronDoor_StaysAWallWhateverTheOptions()
    {
        Assert.False(Context(Lane(FixtureWorld.IronDoorClosed)).CanWalkThrough(DoorX, BodyY, LaneZ));
        Assert.False(Context(Lane(FixtureWorld.IronDoorClosed), NoInteraction).CanWalkThrough(DoorX, BodyY, LaneZ));
    }

    [Fact]
    public void TheCellAboveAClosedGate_IsClearOnlyWhenThePlanWillOpenIt()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Set(DoorX, BodyY, LaneZ, FixtureWorld.ClosedFenceGate);

        Assert.True(Context(world).CanWalkThrough(DoorX, BodyY + 1, LaneZ));
        Assert.False(Context(world, NoInteraction).CanWalkThrough(DoorX, BodyY + 1, LaneZ));
    }

    /// <summary>A crossing that needs an interaction costs the crossing plus <see cref="ActionCosts.InteractLatency"/> and nothing else, so a detour cheaper than the latency still wins and the search can weigh the door against going round.</summary>
    [Fact]
    public void AClosedDoorCostsTheOpenDoorsPricePlusOneInteraction()
    {
        PathResult open = Plan(Lane(FixtureWorld.OpenDoorNorth), LaneStart, LaneGoal);
        PathResult closed = Plan(Lane(FixtureWorld.ClosedDoorWest), LaneStart, LaneGoal);

        Assert.Equal(PathStatus.Success, open.Status);
        Assert.Equal(PathStatus.Success, closed.Status);
        Assert.Equal(open.Cost + ActionCosts.InteractLatency, closed.Cost, 6);
    }

    /// <summary>Two doors are two interactions, not one and not three.</summary>
    [Fact]
    public void TwoClosedDoorsInOneLaneCostTwoInteractions()
    {
        FixtureWorld withDoors = TwoDoorLane();
        FixtureWorld empty = Lane(FixtureWorld.Air, length: 12);

        PathResult bare = Plan(empty, LaneStart, TwoDoorGoal);
        PathResult doors = Plan(withDoors, LaneStart, TwoDoorGoal);

        Assert.Equal(PathStatus.Success, doors.Status);
        Assert.Equal(bare.Cost + (2 * ActionCosts.InteractLatency), doors.Cost, 6);
    }

    /// <summary>The segment that ENTERS the doorway carries what to open and where to stand while opening it. The target is the door's own block position; <c>From</c> is the cell the segment starts in, which is the adjacent cell the body is standing in when it presses.</summary>
    [Fact]
    public void TheSegmentEnteringAClosedDoorway_CarriesTheRequirement()
    {
        FixtureWorld world = Lane(FixtureWorld.ClosedDoorWest);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        var carried = segments.Where(s => s.Interaction is not null).ToList();

        Assert.Single(carried);
        InteractionRequirement requirement = carried[0].Interaction!.Value;
        Assert.Equal(new BlockPos(DoorX, BodyY, LaneZ), requirement.Target);
        Assert.Equal(new BlockPos(DoorX - 1, BodyY, LaneZ), requirement.From);
        Assert.Equal(InteractionKind.OpenByHand, requirement.Kind);
    }

    [Fact]
    public void TheTwoDoorLane_CarriesOneRequirementPerDoor()
    {
        FixtureWorld world = TwoDoorLane();
        PlanningWorldView view = world.Capture(LaneStart, TwoDoorGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(TwoDoorGoal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        var targets = segments
            .Where(s => s.Interaction is not null)
            .Select(s => s.Interaction!.Value.Target)
            .ToList();

        Assert.Equal([new BlockPos(DoorX, BodyY, LaneZ), new BlockPos(9, BodyY, LaneZ)], targets);
    }

    [Fact]
    public void TheG6Lane_PlansThroughTheClosedDoor()
    {
        PathResult result = Plan(Lane(FixtureWorld.ClosedDoorWest), LaneStart, LaneGoal);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.X == DoorX && node.Y == BodyY && node.Z == LaneZ);
    }

    [Fact]
    public void TheG6Lane_WithInteractionDisallowed_StillRefuses()
        => Assert.NotEqual(
            PathStatus.Success, Plan(Lane(FixtureWorld.ClosedDoorWest), LaneStart, LaneGoal, NoInteraction).Status);

    [Fact]
    public void TheG7Lane_PlansThroughTheClosedGate()
    {
        PathResult result = Plan(GateLane(), GateStart, GateGoal);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.X == DoorX && node.Y == BodyY && node.Z == 2);
    }

    [Fact]
    public void TheL7Lane_PlansThroughBothClosedDoors()
    {
        PathResult result = Plan(TwoDoorLane(), LaneStart, TwoDoorGoal);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.X == DoorX && node.Z == LaneZ);
        Assert.Contains(result.Path, node => node.X == 9 && node.Z == LaneZ);
    }

    /// <summary>L3: the iron door with no switch anywhere. It cannot be opened by hand, so the refusal is permanent by design and no interaction capability may ever promote it.</summary>
    [Fact]
    public void TheL3Lane_WithAnIronDoorAndNoActivator_Refuses()
        => Assert.NotEqual(PathStatus.Success, Plan(Lane(FixtureWorld.IronDoorClosed), LaneStart, LaneGoal).Status);

    /// <summary>G5a: the lid over the shaft. The barrier is not in front of the body, it is UNDER its feet, and the move that needs it is a straight-down fall out of the cell the body is standing in. The requirement therefore has to be resolved from the cell below the segment's START, not from its destination column.</summary>
    [Fact]
    public void TheG5aHatch_PlansDownThroughTheClosedLid()
    {
        FixtureWorld world = FloorHatch(FixtureWorld.ClosedTrapdoorTop);
        PlanningWorldView view = world.Capture(LaneStart, HatchGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(HatchGoal));

        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        InteractionRequirement requirement = Assert.Single(
            segments.Where(s => s.Interaction is not null).Select(s => s.Interaction!.Value));
        Assert.Equal(new BlockPos(5, FloorY, LaneZ), requirement.Target);

        // The body opens the lid from ON it. That is not an accident of the search: with the lid priced at an interaction, walking one more cell and dropping straight down is cheaper than stepping sideways into the shaft column, and vanilla puts no restriction on clicking the block you are standing on - the reach test is eye (feet + 1.62) to the nearest point of the block's cube.
        Assert.Equal(new BlockPos(5, BodyY, LaneZ), requirement.From);
        Assert.Equal(MoveType.Fall, segments[^1].MoveType);
    }

    /// <summary>The same shaft under an IRON lid has no way down at all.</summary>
    [Fact]
    public void TheG5aHatch_WithAnIronLid_Refuses()
    {
        FixtureWorld world = FloorHatch(FixtureWorld.IronTrapdoorClosedTop);
        PlanningWorldView view = world.Capture(LaneStart, HatchGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(HatchGoal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>G5b: one OPEN trapdoor panel in the FEET cell, head cell clear. The row was designed as the CLOSING toggle - the panel read as a full-height 3/16 wall across the lane - but an open panel leaves 0.8125 blocks of free width for a 0.6-wide body, which is exactly the lateral band E1b measured and pinned. So it passes with no interaction at all, and the interaction it was designed for is not the reason. This asserts the row's outcome AND the reason: no requirement is carried.</summary>
    [Fact]
    public void TheG5bPanel_PassesOnTheLateralBandRatherThanOnAToggle()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(DoorX, BodyY, LaneZ, FixtureWorld.OpenTrapdoorNorth);

        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));

        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        Assert.All(segments, s => Assert.Null(s.Interaction));
    }

    /// <summary>A closed door the plan is going to open becomes a PANEL cell the moment it opens, so it inherits E1b's entry restriction whole: a diagonal has no lateral authority over a 0.0125-block clearance whether the panel is there yet or not. The room below is the one that offers a diagonal entry.</summary>
    [Fact]
    public void ADiagonalIntoAClosedDoorway_IsRefusedExactlyAsIntoAnOpenOne()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 8, FloorY, 8, FixtureWorld.Stone);
        world.Fill(5, BodyY, 0, 5, BodyY + 1, 8, FixtureWorld.Stone);
        world.Set(5, BodyY, 5, FixtureWorld.ClosedDoorWest);
        world.Set(5, BodyY + 1, 5, FixtureWorld.ClosedDoorWest);

        var start = new BlockPos(0, BodyY, 0);
        var goal = new BlockPos(8, BodyY, 5);
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        var ctx = new CalculationContext(view, PathfinderOptions.Default);
        for (int i = 1; i < result.Path.Count; i++)
        {
            PathNode from = result.Path[i - 1];
            PathNode to = result.Path[i];
            if (!MoveHelper.HasBarrierPanel(ctx, to.X, to.Y, to.Z)
                && !MoveHelper.HasBarrierPanel(ctx, from.X, from.Y, from.Z))
                continue;

            Assert.Equal(MoveType.Traverse, to.MoveUsed);
            Assert.Equal(from.Y, to.Y);
            Assert.Equal(1, Math.Abs(to.X - from.X) + Math.Abs(to.Z - from.Z));
        }
    }
}
