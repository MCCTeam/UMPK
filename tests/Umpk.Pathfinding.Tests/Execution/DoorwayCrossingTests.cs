using System.Globalization;
using System.Text;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class DoorwayCrossingTests
{
    private const int FloorY = 99;
    private const int BodyY = 100;
    private const int CourseMargin = 24;

    private const float FacingPlusX = 270f;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    /// <summary>The lane's Z, so a cell-local lateral offset reads as <c>LaneZ + offset</c>.</summary>
    private const int LaneZ = 1;

    /// <summary>The doorway's X in the straight lane.</summary>
    private const int DoorX = 4;

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);
    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    /// <summary>Course row L6's plot: a 1-wide walled lane with a door at <see cref="DoorX"/>.</summary>
    private static FixtureWorld Lane(int doorwayState)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(DoorX, BodyY, LaneZ, doorwayState);
        world.Set(DoorX, BodyY + 1, LaneZ, doorwayState);
        return world;
    }

    private static FixtureWorld JumpCatwalk(int doorwayState, bool stopAtTheDoor)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, LaneZ, 7, FloorY, LaneZ, FixtureWorld.Stone);
        world.Set(3, FloorY, LaneZ, FixtureWorld.Air);
        if (stopAtTheDoor)
            world.Fill(5, BodyY, LaneZ, 5, BodyY + 2, LaneZ, FixtureWorld.Stone);

        world.Set(DoorX, BodyY, LaneZ, doorwayState);
        world.Set(DoorX, BodyY + 1, LaneZ, doorwayState);
        return world;
    }

    /// <summary>Course row L9's plot: a 9x9 room split by a wall at x=5 with one doorway in it, and the start in the FAR corner so the open floor lets the diagonal move set carry the body toward the door at an angle for most of the approach. The doorway is at z=5 rather than L9's z=4 for one reason: at z=4 the cheapest route happens to reach the door's row a cell early and enters cardinally anyway, which would make the row pass without the rule. At z=5 the diagonal chain <c>(0,0) -&gt; (4,4)</c> ends one diagonal step short of the doorway, and a diagonal entry (6.55 ticks) is cheaper than the two cardinals that square up (7.13), so the search takes it unless something forbids it.</summary>
    private static FixtureWorld DiagonalRoom(int doorwayState)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 8, FloorY, 8, FixtureWorld.Stone);
        world.Fill(5, BodyY, 0, 5, BodyY + 1, 8, FixtureWorld.Stone);
        world.Set(5, BodyY, 5, doorwayState);
        world.Set(5, BodyY + 1, 5, doorwayState);
        return world;
    }

    private static readonly BlockPos RoomStart = new(0, BodyY, 0);
    private static readonly BlockPos RoomGoal = new(8, BodyY, 5);

    private static PathResult Plan(FixtureWorld world, BlockPos start, BlockPos goal)
    {
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        return PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
    }

    private static double RawDriveMaxX(PlanningWorldView view, double startLateral, float yaw)
    {
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BodyY, LaneZ + startLateral), yaw, 0f);
        engine.Step(MovementInput.None);

        double maxX = engine.State.Position.X;
        var input = new MovementInput { Forward = true, Sprint = true };
        for (int tick = 0; tick < 120; tick++)
        {
            engine.Step(input);
            maxX = Math.Max(maxX, engine.State.Position.X);
        }

        return maxX;
    }

    [Theory]
    [InlineData(0.30, false)]
    [InlineData(0.40, false)]
    [InlineData(0.45, false)]
    [InlineData(0.50, true)]
    [InlineData(0.60, true)]
    [InlineData(0.70, true)]
    [InlineData(0.75, false)]
    [InlineData(0.85, false)]
    public void TheLateralBandAtAnOpenDoor_IsHalfToSevenTenths(double startLateral, bool expectedThrough)
    {
        PlanningWorldView view = Lane(FixtureWorld.OpenDoorNorth).Capture(LaneStart, LaneGoal, CourseMargin);
        double maxX = RawDriveMaxX(view, startLateral, FacingPlusX);

        Assert.Equal(expectedThrough, maxX > DoorX + 2.0);
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(2f, true)]
    [InlineData(5f, true)]
    [InlineData(-2f, false)]
    [InlineData(-5f, false)]
    public void AYawErrorTowardThePanel_StopsTheBodyAtTheDoor(float yawError, bool expectedThrough)
    {
        PlanningWorldView view = Lane(FixtureWorld.OpenDoorNorth).Capture(LaneStart, LaneGoal, CourseMargin);
        double maxX = RawDriveMaxX(view, 0.5, FacingPlusX + yawError);

        Assert.Equal(expectedThrough, maxX > DoorX + 2.0);
    }

    /// <summary>Every move whose destination column carries a panel, and every move that leaves one, must be a cardinal Traverse. L9's room is the shape that exercises it: the open floor lets the diagonal family carry the body most of the way, so the entry is a diagonal unless something forbids it.</summary>
    [Fact]
    public void ADoorwayIsEnteredAndLeftOnlyByACardinalTraverse()
    {
        FixtureWorld world = DiagonalRoom(FixtureWorld.OpenDoorNorth);
        PathResult result = Plan(world, RoomStart, RoomGoal);
        Assert.Equal(PathStatus.Success, result.Status);

        PlanningWorldView view = world.Capture(RoomStart, RoomGoal, CourseMargin);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);
        var offences = new List<string>();

        for (int i = 1; i < result.Path.Count; i++)
        {
            PathNode from = result.Path[i - 1];
            PathNode to = result.Path[i];
            bool touchesPanel = MoveHelper.HasBarrierPanel(ctx, from.X, from.Y, from.Z)
                || MoveHelper.HasBarrierPanel(ctx, to.X, to.Y, to.Z);
            if (!touchesPanel)
                continue;

            bool cardinalTraverse = to.MoveUsed == MoveType.Traverse
                && to.Y == from.Y
                && Math.Abs(to.X - from.X) + Math.Abs(to.Z - from.Z) == 1;
            if (!cardinalTraverse)
                offences.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {to.MoveUsed} ({from.X},{from.Y},{from.Z}) -> ({to.X},{to.Y},{to.Z})"));

        }

        if (offences.Count > 0)
            Assert.Fail(
                "a barrier cell was entered or left by something other than a cardinal Traverse:\n"
                    + string.Join('\n', offences) + Render(result));

    }

    /// <summary>A jump is refused both as a landing IN the doorway and as a flight path THROUGH it: airborne, the body has no steering authority at all, and 0.0125 blocks of clearance is not a margin an arc can be aimed into. The lane's floor is cut at x=3 so a jump is the only way across; with the jump refused, the row has no route, and refusing is the answer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AJumpIntoOrThroughADoorway_IsRefused(bool landingInTheDoorway)
    {
        BlockPos goal = landingInTheDoorway ? new BlockPos(DoorX, BodyY, LaneZ) : LaneGoal;
        PathResult result = Plan(JumpCatwalk(FixtureWorld.OpenDoorNorth, landingInTheDoorway), LaneStart, goal);
        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The control for the one above: with the doorway empty the same catwalk jumps the gap in both shapes, so the refusal is about the panel and not about the geometry.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSameCatwalkWithNoDoor_StillJumpsTheGap(bool landingWhereTheDoorWouldBe)
    {
        BlockPos goal = landingWhereTheDoorWouldBe ? new BlockPos(DoorX, BodyY, LaneZ) : LaneGoal;
        PathResult result = Plan(JumpCatwalk(FixtureWorld.Air, landingWhereTheDoorWouldBe), LaneStart, goal);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.MoveUsed == MoveType.Parkour);
    }

    [Fact]
    public void AnOpenFenceGate_IsNotRestricted()
    {
        FixtureWorld world = DiagonalRoom(FixtureWorld.OpenFenceGate);
        PlanningWorldView view = world.Capture(RoomStart, RoomGoal, CourseMargin);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.False(MoveHelper.HasBarrierPanel(ctx, 5, BodyY, 5));

        PathResult gate = Plan(world, RoomStart, RoomGoal);
        Assert.Equal(PathStatus.Success, gate.Status);
    }

    /// <summary>The segment that enters the doorway (and the one that leaves it) carries the crossing, and the crossing names the side the panel is on so the executor can bias away from it. The fixture's panel is on the cell's north face, so the free side is +Z.</summary>
    [Fact]
    public void TheSegmentsTouchingTheDoorway_CarryTheCrossing()
    {
        FixtureWorld world = Lane(FixtureWorld.OpenDoorNorth);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);
        var crossing = segments
            .Where(s => s.Crossing is not null)
            .Select(s => (s.Start, s.End, s.Crossing!.Value))
            .ToList();

        Assert.Equal(2, crossing.Count);
        foreach ((Vec3d _, Vec3d _, BarrierCrossing c) in crossing)
        {
            Assert.Equal(0, c.PanelX);
            Assert.Equal(-1, c.PanelZ);
        }

        // The entering segment ends in the doorway; the leaving one starts there.
        Assert.Equal(DoorX + 0.5, crossing[0].Item2.X, 6);
        Assert.Equal(DoorX + 0.5, crossing[1].Item1.X, 6);
    }

    /// <summary>The straight lane, driven by the real executor over the real engine. R6c already showed the engine walks a centred body through; this asserts the whole plan completes and that the body never leaves the measured band while its footprint is inside the doorway.</summary>
    [Fact]
    public void TheL6Lane_ExecutorCrossesTheOpenDoor()
    {
        AssertCrossesTheDoorway(
            Lane(FixtureWorld.OpenDoorNorth), LaneStart, LaneGoal, DoorX, LaneZ, FacingPlusX);
    }

    [Fact]
    public void TheL9Room_SquaresUpAndCrossesTheOpenDoor()
    {
        AssertCrossesTheDoorway(
            DiagonalRoom(FixtureWorld.OpenDoorNorth), RoomStart, RoomGoal, 5, 5, FacingPlusX);
    }

    private static void AssertCrossesTheDoorway(
        FixtureWorld world, BlockPos start, BlockPos goal, int doorX, int doorZ, float startYaw)
    {
        PlanningWorldView view = world.Capture(start, goal, CourseMargin);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view);
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5), startYaw);
        PathExecutorState state = driver.Run();

        double minLateral = double.PositiveInfinity;
        double maxLateral = double.NegativeInfinity;
        foreach (TickSample sample in driver.Trace)
        {
            // While any part of the body is inside the doorway column, record where its centre is.
            if (sample.Position.X + 0.3 > doorX && sample.Position.X - 0.3 < doorX + 1)
            {
                minLateral = Math.Min(minLateral, sample.Position.Z - doorZ);
                maxLateral = Math.Max(maxLateral, sample.Position.Z - doorZ);
            }
        }

        // The measured pass band is 0.5 to 0.7, and an unbiased crossing reaches exactly 0.5000, i.e. on the band's very edge with 0.0125 blocks of clearance. The floor here is 0.55 so the

        // L6 holds 0.5575 to 0.6157 and L9 holds 0.5790 to 0.6338.
        Assert.Equal(PathExecutorState.Complete, state);
        Assert.InRange(minLateral, 0.55, 0.7);
        Assert.InRange(maxLateral, 0.55, 0.7);
    }

    private static string Render(PathResult result)
    {
        var text = new StringBuilder("\n  route: ");
        foreach (PathNode node in result.Path)
            text.Append(string.Create(CultureInfo.InvariantCulture, $"({node.X},{node.Y},{node.Z}) "));

        return text.ToString();
    }
}
