using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class DoorActivationTests(ITestOutputHelper output)
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    private const int CourseMargin = 24;

    private const int LaneZ = 1;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    /// <summary>L1's plot in plot-local coordinates: a 1-wide walled lane, an iron door at both body cells of x=4, and an activator at x=3 on the north wall - face-adjacent to the door's lower half, which is the placement the row's own note calls the point of it.</summary>
    private static FixtureWorld ActivatorLane(int activatorState, int activatorX = 3, int length = 7)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, length, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, length, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, length, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, LaneZ, FixtureWorld.IronDoorClosed);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.IronDoorClosed);
        if (activatorState != FixtureWorld.Air)
            world.Set(activatorX, BodyY, LaneZ, activatorState);

        return world;
    }

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);

    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    private static readonly BlockPos Door = new(4, BodyY, LaneZ);

    private static CalculationContext Context(FixtureWorld world, BlockPos? goal = null)
        => new(world.Capture(LaneStart, goal ?? LaneGoal, CourseMargin), PathfinderOptions.Default);

    [Theory]
    [InlineData(2, 15, 15)]
    [InlineData(3, 22, 21)]
    [InlineData(4, 27, 22)]
    [InlineData(5, 29, 26)]
    [InlineData(6, 36, 29)]
    [InlineData(8, 45, 39)]
    public void RealTicksToClear_CoversBothTheReviewsMeasurementAndThisHarnessOwn(
        int blocks, int reviewTicks, int measuredHere)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, blocks + 2, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, blocks + 2, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, blocks + 2, BodyY + 1, 2, FixtureWorld.Stone);

        var goal = new BlockPos(blocks, BodyY, LaneZ);
        PlanningWorldView view = world.Capture(LaneStart, goal, CourseMargin);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, LaneStart, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        var driver = new ExecutionDriver(
            new PathExecutionContext(view, Profile),
            PathSegmentBuilder.FromPath(result.Path, view),
            new Vec3d(0.5, BodyY, LaneZ + 0.5),
            FacingPlusX);
        Assert.Equal(PathExecutorState.Complete, driver.Run());

        int ticks = driver.Trace.Count;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"blocks={blocks} plannerCost={result.Cost:F4} realTicks={ticks} model={DoorActivation.RealTicksToClear(blocks):F1}"));

        Assert.Equal(measuredHere, ticks);
        Assert.True(
            DoorActivation.RealTicksToClear(blocks) >= reviewTicks,
            $"the window model must cover the review's {reviewTicks} real ticks for {blocks} blocks");
        Assert.True(
            DoorActivation.RealTicksToClear(blocks) >= ticks,
            $"the window model must cover this harness's own {ticks} real ticks for {blocks} blocks");
    }

    [Theory]
    [InlineData(2, DoorActivation.StoneButtonWindowTicks, false)]
    [InlineData(2, DoorActivation.WoodenButtonWindowTicks, true)]
    [InlineData(1, DoorActivation.StoneButtonWindowTicks, false)]
    [InlineData(1, DoorActivation.WoodenButtonWindowTicks, true)]
    [InlineData(3, DoorActivation.WoodenButtonWindowTicks, false)]
    [InlineData(9, DoorActivation.StoneButtonWindowTicks, false)]
    [InlineData(9, DoorActivation.WoodenButtonWindowTicks, false)]
    [InlineData(9, DoorActivation.NoWindow, true)]
    [InlineData(40, DoorActivation.NoWindow, true)]
    public void FitsWindow_ChargesTheRealTickModelPlusTheObserveLatency(
        int blocks, int windowTicks, bool expected)
        => Assert.Equal(expected, DoorActivation.FitsWindow(blocks, windowTicks));

    [Fact]
    public void L1_WoodenButtonOneCellBeforeTheDoor_Resolves()
    {
        CalculationContext ctx = Context(ActivatorLane(FixtureWorld.OakButtonWallSouth));

        Assert.True(DoorActivation.TryResolve(ctx, Door, out DoorActivation activation));
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), activation.Activator);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), activation.StandCell);
        Assert.Equal(ActivatorKind.Button, activation.Kind);
        Assert.Equal(DoorActivation.WoodenButtonWindowTicks, activation.WindowTicks);
    }

    /// <summary>L1 built on STONE instead. Everything about the geometry is identical and the activation is refused, because 17 model ticks plus 10 of latency is 27 against a 20-tick window. This is the row's own arithmetic and the reason the course build was corrected from stone to wood.</summary>
    [Fact]
    public void L1_WithAStoneButton_IsRefusedOnTheWindow()
    {
        CalculationContext ctx = Context(ActivatorLane(FixtureWorld.StoneButtonWallSouth));

        Assert.False(DoorActivation.TryResolve(ctx, Door, out _));
    }

    /// <summary>L2: a lever latches, so the same geometry resolves with no window at all.</summary>
    [Fact]
    public void L2_ALeverResolvesWithNoWindow()
    {
        CalculationContext ctx = Context(ActivatorLane(FixtureWorld.LeverWallSouth));

        Assert.True(DoorActivation.TryResolve(ctx, Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.Lever, activation.Kind);
        Assert.Equal(DoorActivation.NoWindow, activation.WindowTicks);
    }

    /// <summary>A lever beats a button even when the button is nearer, because a latch imposes no window and a press does. The button here is the one L1 accepts on its own.</summary>
    [Fact]
    public void ALeverIsPreferredOverAnEquallyReachableButton()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.OakButtonWallSouth);
        world.Set(5, BodyY, LaneZ, FixtureWorld.LeverWallSouth);
        CalculationContext ctx = Context(world);

        Assert.True(DoorActivation.TryResolve(ctx, Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.Lever, activation.Kind);
        Assert.Equal(new BlockPos(5, BodyY, LaneZ), activation.Activator);
    }

    /// <summary>L3: no switch anywhere. The refusal is the feature.</summary>
    [Fact]
    public void L3_NoActivator_IsRefused()
        => Assert.False(DoorActivation.TryResolve(Context(ActivatorLane(FixtureWorld.Air)), Door, out _));

    [Fact]
    public void L4_AFarButtonThroughARedstoneRelay_IsRefused()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.Air, length: 14);
        world.Set(4, BodyY, LaneZ, FixtureWorld.IronDoorClosed);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.IronDoorClosed);

        // The button at x=-5 relative to the door, dust filling the cells between it and the door.
        world.Set(0, BodyY, LaneZ, FixtureWorld.StoneButtonWallSouth);
        for (int x = 1; x <= 3; x++)
            world.Set(x, BodyY, LaneZ, FixtureWorld.RedstoneWire);

        Assert.False(DoorActivation.TryResolve(Context(world, new BlockPos(13, BodyY, LaneZ)), Door, out _));
    }

    [Fact]
    public void AButtonHangingOnTheWallBesideTheDoor_PowersItThroughTheConductor()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.Air);

        // (5, BodyY, 0) is in the north wall and is NOT face-adjacent to either door half - the only cells that are, are (3,*,1), (5,*,1), (4,*,0), (4,*,2) and the halves' own vertical neighbours. The button faces EAST, so it hangs on (4, BodyY, 0), the solid wall cell that IS face-adjacent to the lower half, and its direct signal goes into exactly that cell.
        world.Set(5, BodyY, 0, FixtureWorld.OakButtonWallEast);

        Assert.True(
            DoorActivation.TryResolve(Context(world), Door, out DoorActivation activation),
            "a button hanging on a solid block that touches the door's lower half powers the door "
                + "through SignalGetter's conductor arm");
        Assert.Equal(new BlockPos(5, BodyY, 0), activation.Activator);
        Assert.Equal(ActivatorKind.Button, activation.Kind);
    }

    /// <summary>A button whose own cell is not adjacent to a half and whose ATTACHMENT is not adjacent either powers nothing, and pressing it would be worse than refusing: the bot would stand there watching a door that never moves.</summary>
    [Fact]
    public void AButtonOutOfSignalRange_IsNotAnActivator()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.Air);
        world.Set(2, BodyY, LaneZ, FixtureWorld.OakButtonWallSouth);

        Assert.False(DoorActivation.TryResolve(Context(world), Door, out _));
    }

    /// <summary>L8's shape: the same button-and-door arithmetic behind a right-angle bend, so the activator is never on a straight line from the start. The resolver reads geometry rather than sight lines, so the bend changes nothing about what it finds - which is the point of asserting it.</summary>
    [Fact]
    public void L8_TheSameButtonRoundACorner_Resolves()
    {
        var world = new FixtureWorld();

        // Leg A runs +x along z=1 for five cells; leg B turns and runs +z along x=5.
        world.Fill(0, FloorY, 0, 6, FloorY, 12, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 6, BodyY + 1, 12, FixtureWorld.Stone);
        world.Fill(0, BodyY, LaneZ, 5, BodyY + 1, LaneZ, FixtureWorld.Air);
        world.Fill(5, BodyY, LaneZ, 5, BodyY + 1, 11, FixtureWorld.Air);

        var door = new BlockPos(5, BodyY, 8);
        world.Set(door.X, BodyY, door.Z, FixtureWorld.IronDoorClosed);
        world.Set(door.X, BodyY + 1, door.Z, FixtureWorld.IronDoorClosed);
        world.Set(5, BodyY, 7, FixtureWorld.OakButtonWallEast);

        var ctx = new CalculationContext(
            world.Capture(LaneStart, new BlockPos(5, BodyY, 11), CourseMargin), PathfinderOptions.Default);

        Assert.True(DoorActivation.TryResolve(ctx, door, out DoorActivation activation));
        Assert.Equal(new BlockPos(5, BodyY, 7), activation.Activator);
        Assert.Equal(new BlockPos(5, BodyY, 7), activation.StandCell);
        Assert.Equal(DoorActivation.WoodenButtonWindowTicks, activation.WindowTicks);
    }

    /// <summary>The door's UPPER half counts as a face for the signal, because vanilla's Either door half can receive a neighboring signal. A button beside the upper half alone still opens the door.</summary>
    [Fact]
    public void AButtonBesideTheUpperHalf_Resolves()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.Air);
        world.Set(3, BodyY + 1, LaneZ, FixtureWorld.OakButtonWallSouth);

        Assert.True(DoorActivation.TryResolve(Context(world), Door, out DoorActivation activation));
        Assert.Equal(new BlockPos(3, BodyY + 1, LaneZ), activation.Activator);

        // The stand cell is the floor cell under it: the body cannot stand in the head cell, and the eye reaches a button 1.62 above its feet without leaving the ground.
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), activation.StandCell);
    }

    /// <summary>L1 end to end at plan level: the lane plans, the route goes through the doorway, and the segment that enters it carries a PRESS - the button as the target, the DOOR as the witness, and the wooden button's 30-tick window.</summary>
    [Fact]
    public void L1_PlansThroughTheIronDoorAndCarriesThePress()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.OakButtonWallSouth);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, n => n.X == Door.X && n.Y == Door.Y && n.Z == Door.Z);

        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        InteractionRequirement requirement = Assert.Single(
            segments.Where(s => s.Interaction is not null).Select(s => s.Interaction!.Value));

        Assert.Equal(InteractionKind.PressActivator, requirement.Kind);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), requirement.Target);
        Assert.Equal(Door, requirement.Witness);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), requirement.From);
        Assert.Equal(DoorActivation.WoodenButtonWindowTicks, requirement.WindowTicks);
    }

    /// <summary>The same lane on a STONE button does not plan at all. The refusal is the window arithmetic reaching the search: with nothing to open the door inside its own press duration, the door is a wall and there is no other way down the lane.</summary>
    [Fact]
    public void L1_WithAStoneButton_DoesNotPlan()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.StoneButtonWallSouth);
        PathResult result = PathPlanner.FindPath(
            world.Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>L2: the lever plans, and its requirement carries no window at all.</summary>
    [Fact]
    public void L2_PlansThroughTheIronDoorOnALever()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.LeverWallSouth);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));

        Assert.Equal(PathStatus.Success, result.Status);
        InteractionRequirement requirement = Assert.Single(
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default)
                .Where(s => s.Interaction is not null)
                .Select(s => s.Interaction!.Value));

        Assert.Equal(InteractionKind.PressActivator, requirement.Kind);
        Assert.Equal(DoorActivation.NoWindow, requirement.WindowTicks);
    }

    /// <summary>L3 at plan level: no switch, no route, and never promotable.</summary>
    [Fact]
    public void L3_WithNoActivator_DoesNotPlan()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.Air);
        PathResult result = PathPlanner.FindPath(
            world.Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>An iron door with a lever costs the open door's price plus exactly one interaction, so the search weighs a switched door against a detour the same way it weighs a wooden one.</summary>
    [Fact]
    public void AnActivatedDoorCostsOneInteractionOverTheOpenDoorsPrice()
    {
        FixtureWorld withDoor = ActivatorLane(FixtureWorld.LeverWallSouth);
        FixtureWorld bare = ActivatorLane(FixtureWorld.LeverWallSouth);
        bare.Set(4, BodyY, LaneZ, FixtureWorld.Air);
        bare.Set(4, BodyY + 1, LaneZ, FixtureWorld.Air);

        PathResult open = PathPlanner.FindPath(
            bare.Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal));
        PathResult gated = PathPlanner.FindPath(
            withDoor.Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal));

        Assert.Equal(PathStatus.Success, gated.Status);
        Assert.Equal(open.Cost + ActionCosts.InteractLatency, gated.Cost, 6);
    }

    /// <summary>A hand-openable door is never an activator's business: it resolves nothing, because <see cref="BarrierKind.NeedsInteraction"/> already covers it and a plan that pressed a button for an oak door would pay twice for one crossing.</summary>
    [Fact]
    public void AHandOpenableDoor_IsNotAnActivatorCase()
    {
        FixtureWorld world = ActivatorLane(FixtureWorld.OakButtonWallSouth);
        world.Set(4, BodyY, LaneZ, FixtureWorld.ClosedDoorWest);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.ClosedDoorWest);

        Assert.False(DoorActivation.TryResolve(Context(world), Door, out _));
    }
}
