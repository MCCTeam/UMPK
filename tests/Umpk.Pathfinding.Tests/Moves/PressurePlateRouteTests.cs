using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

/// <summary>
/// A plate opens a door because a body is STANDING ON IT, which makes it the only activator whose effect depends on where the body is. This library's A* has no world-state axis at all - a <c>NodeKey</c> is <c>(position, entry preparation, air band)</c> and the planning snapshot is immutable, which is the justification every context memo rests on - so the whole feature has to be expressible without one.
///
/// <para>It is, for exactly one shape: <b>the plate is the cell the crossing starts from</b>. Then the "state change" and the "route position" are the same fact and there is nothing to reason about forwards in time. The price is a per-EDGE rule rather than a per-cell one, which <see cref="MoveHelper.IsBarrierCrossingLegal"/> already exists to carry: a door whose only opener is a plate may be entered from that plate and from nowhere else.</para>
///
/// <para><b>Why the edge rule is the whole item and not a detail.</b> Without it, "a plate exists beside this door" would make the door's CELL passable to every move that reaches it, from any side - including the side with no plate on it. The bot would then plan straight into a door that nothing had pressed. <see cref="AnIronDoorWhoseOnlyPlateIsOnTheFarSide_IsRefusedFromTheNearSide"/> is that case and it fails without the rule.</para>
/// </summary>
public sealed class PressurePlateRouteTests
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    private const int CourseMargin = 24;

    private const int LaneZ = 1;

    private const int LaneLength = 7;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private const float FacingPlusX = 270f;

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);

    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    private static readonly BlockPos Door = new(4, BodyY, LaneZ);

    /// <summary>A 1-wide walled lane with a closed iron door filling both body cells of x=4, and a plate wherever the caller puts one. <paramref name="plateX"/> 3 is the near approach cell (the body reaches it walking +X from the start); 5 is the FAR side, which the body can only reach by first crossing the door it is meant to open.</summary>
    private static FixtureWorld PlateLane(int plateState, int plateX)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, LaneLength, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, LaneLength, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, LaneLength, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, LaneZ, FixtureWorld.IronDoorClosed);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.IronDoorClosed);
        if (plateState != FixtureWorld.Air)
            world.Set(plateX, BodyY, LaneZ, plateState);

        return world;
    }

    private static PathResult Plan(FixtureWorld world, out PlanningWorldView view)
    {
        view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        return PathPlanner.FindPath(view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));
    }

    /// <summary>The feature, end to end at plan level. The lane plans, the route goes through the doorway, and the segment that enters it carries an interaction whose <c>From</c> is the PLATE's own cell.</summary>
    [Fact]
    public void APlateInTheApproachCell_PlansThroughTheIronDoor()
    {
        PathResult result = Plan(PlateLane(FixtureWorld.OakPressurePlate, plateX: 3), out PlanningWorldView view);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, n => n.X == Door.X && n.Y == Door.Y && n.Z == Door.Z);

        InteractionRequirement requirement = Assert.Single(
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default)
                .Where(s => s.Interaction is not null)
                .Select(s => s.Interaction!.Value));

        Assert.Equal(InteractionKind.StandOnPlate, requirement.Kind);
        Assert.False(requirement.SendsAUse);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), requirement.From);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), requirement.Target);
        Assert.Equal(Door, requirement.Witness);
        Assert.Equal(DoorActivation.PlatePressedTicks, requirement.WindowTicks);
    }

    /// <summary>The control for the kind: the SAME lane with a button in the SAME cell still emits a <see cref="InteractionKind.PressActivator"/> that DOES send. Without this, "a plate emits StandOnPlate" would be equally true of an implementation that had quietly stopped sending for every activator.</summary>
    [Fact]
    public void AButtonInTheSameCell_StillEmitsAPressThatSends()
    {
        FixtureWorld world = PlateLane(FixtureWorld.Air, plateX: 3);
        world.Set(3, BodyY, LaneZ, FixtureWorld.OakButtonWallSouth);
        PathResult result = Plan(world, out PlanningWorldView view);

        Assert.Equal(PathStatus.Success, result.Status);
        InteractionRequirement requirement = Assert.Single(
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default)
                .Where(s => s.Interaction is not null)
                .Select(s => s.Interaction!.Value));

        Assert.Equal(InteractionKind.PressActivator, requirement.Kind);
        Assert.True(requirement.SendsAUse);
    }

    /// <summary>Every other kind sends, and this states that as a table rather than leaving it to the two rows above. <see cref="InteractionRequirement.SendsAUse"/> is what the driver routes on, so a change that silently widened the exemption would take the whole door family down with it.</summary>
    [Theory]
    [InlineData(InteractionKind.OpenByHand, true)]
    [InlineData(InteractionKind.PressActivator, true)]
    [InlineData(InteractionKind.CloseByHand, true)]
    [InlineData(InteractionKind.StandOnPlate, false)]
    public void SendsAUse_IsFalseForExactlyOneKind(InteractionKind kind, bool expected)
        => Assert.Equal(
            expected,
            new InteractionRequirement(Door, kind, Door, Door).SendsAUse);

    /// <summary>The executor's hold, on a plate. It reaches the plate cell, reports the requirement, presses nothing and does not advance - the same shape a button's hold has, which is the point: no new executor machinery was needed, because a plate is a requirement the existing hold already knows how to carry.</summary>
    [Fact]
    public void TheExecutorHoldsOnThePlateAndReportsAStandOnRequirement()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate, plateX: 3);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        var executor = new PathExecutor(new PathExecutionContext(view, Profile), segments);
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + 0.5), FacingPlusX, 0f);
        engine.Step(MovementInput.None);

        PathExecutorTick tick = default;
        for (int i = 0; i < 200 && tick.PendingInteraction is null; i++)
        {
            tick = executor.Tick(engine.State);
            if (tick.PendingInteraction is not null)
                break;

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        InteractionRequirement requirement = Assert.NotNull(tick.PendingInteraction);
        Assert.Equal(InteractionKind.StandOnPlate, requirement.Kind);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), requirement.From);

        // Standing ON the cell it is holding for, which is the whole claim: the plate is pressed by the body's own position while the driver waits for the door to answer.
        Assert.Equal(3, (int)Math.Floor(engine.State.Position.X));
        Assert.Equal(BodyY, (int)Math.Floor(engine.State.Position.Y));

        for (int i = 0; i < 10; i++)
        {
            PathExecutorTick again = executor.Tick(engine.State);
            Assert.Equal(requirement, Assert.NotNull(again.PendingInteraction));
            Assert.Equal(MovementInput.None, again.Output.Input);
        }
    }

    /// <summary><b>The discriminating case.</b> The same door, the same plate, on the OTHER side. The body cannot reach that plate without first crossing the door the plate is there to open, so the route has to be refused - and the refusal cannot come from anything the resolver knows, because the resolver's answer is "yes, a plate powers this door" and it is correct. Only a rule about the EDGE can separate the two sides of one door.</summary>
    [Fact]
    public void AnIronDoorWhoseOnlyPlateIsOnTheFarSide_IsRefusedFromTheNearSide()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate, plateX: 5);

        Assert.True(
            DoorActivation.TryResolve(
                new CalculationContext(
                    world.Capture(LaneStart, LaneGoal, CourseMargin), PathfinderOptions.Default),
                Door,
                out DoorActivation activation),
            "the resolver must still SEE the plate; if it does not, this row is refusing for the wrong reason");
        Assert.Equal(new BlockPos(5, BodyY, LaneZ), activation.StandCell);

        Assert.NotEqual(PathStatus.Success, Plan(world, out _).Status);
    }

    /// <summary>The mirror image, and it is what stops the rule above from being "a plate never works": approach the SAME far-side plate from the far side and the crossing is legal. Without this the rule could be a blanket refusal of every plate-driven door and nothing here would notice.</summary>
    [Fact]
    public void TheSameFarSidePlate_OpensTheDoorForABodyApproachingFromThatSide()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate, plateX: 5);
        var start = new BlockPos(7, BodyY, LaneZ);
        var goal = new BlockPos(0, BodyY, LaneZ);
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, n => n.X == Door.X && n.Y == Door.Y && n.Z == Door.Z);
        Assert.Equal(
            new BlockPos(5, BodyY, LaneZ),
            Assert.Single(
                PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default)
                    .Where(s => s.Interaction is not null)
                    .Select(s => s.Interaction!.Value)).From);
    }

    [Fact]
    public void TheSameLaneWithNoPlateAtAll_IsStillAWall()
        => Assert.NotEqual(PathStatus.Success, Plan(PlateLane(FixtureWorld.Air, plateX: 3), out _).Status);

    /// <summary>A weighted plate in the approach cell is refused at plan level too, not merely at the resolver. The arithmetic is in <see cref="PressurePlateActivationTests"/>; this is that arithmetic reaching the search.</summary>
    [Theory]
    [InlineData(FixtureWorld.LightWeightedPressurePlate)]
    [InlineData(FixtureWorld.HeavyWeightedPressurePlate)]
    public void AWeightedPlateInTheApproachCell_DoesNotPlan(int plateState)
        => Assert.NotEqual(PathStatus.Success, Plan(PlateLane(plateState, plateX: 3), out _).Status);

    [Fact]
    public void APlateBesideAnAlreadyOpenDoor_ChangesNothingAboutTheRoute()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, LaneLength, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, LaneLength, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, LaneLength, BodyY + 1, 2, FixtureWorld.Stone);

        // OpenDoorNorth's panel hugs the cell's NORTH face, so a +X walk runs ALONG it: the one heading MoveHelper.IsBarrierCrossingLegal lets through a panel column.
        world.Set(4, BodyY, LaneZ, FixtureWorld.OpenDoorNorth);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.OpenDoorNorth);
        world.Set(3, BodyY, LaneZ, FixtureWorld.OakPressurePlate);

        PathResult result = Plan(world, out PlanningWorldView view);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, n => n.X == Door.X && n.Y == Door.Y && n.Z == Door.Z);
        Assert.DoesNotContain(
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default),
            s => s.Interaction is not null);
    }

    [Fact]
    public void APlateBesideAClosedWOODENDoor_IsStillOpenedByHand()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, LaneLength, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, LaneLength, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, LaneLength, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, LaneZ, FixtureWorld.ClosedDoorWest);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.ClosedDoorWest);
        world.Set(3, BodyY, LaneZ, FixtureWorld.OakPressurePlate);

        PathResult result = Plan(world, out PlanningWorldView view);

        Assert.Equal(PathStatus.Success, result.Status);
        InteractionRequirement requirement = Assert.Single(
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default)
                .Where(s => s.Interaction is not null)
                .Select(s => s.Interaction!.Value));

        Assert.Equal(InteractionKind.OpenByHand, requirement.Kind);
        Assert.Equal(Door, requirement.Target);
        Assert.True(requirement.SendsAUse);

        // ...and the resolver agrees from the other side: it never returns an activation for a door a hand opens, whatever is lying next to it.
        Assert.False(
            DoorActivation.TryResolve(
                new CalculationContext(
                    world.Capture(LaneStart, LaneGoal, CourseMargin), PathfinderOptions.Default),
                Door,
                out _));
    }

    /// <summary>A plate wired to the door through dust plans nothing. Redstone wire is not traced and this item does not start tracing it; the honest answer on the far side of that line is a refusal, not a bot standing in front of a door it has no reason to think will move. Course row L12.</summary>
    [Fact]
    public void APlateWiredToTheDoorThroughDust_DoesNotPlan()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate, plateX: 1);
        world.Set(2, BodyY, LaneZ, FixtureWorld.RedstoneWire);
        world.Set(3, BodyY, LaneZ, FixtureWorld.RedstoneWire);

        Assert.NotEqual(PathStatus.Success, Plan(world, out _).Status);
    }
}
