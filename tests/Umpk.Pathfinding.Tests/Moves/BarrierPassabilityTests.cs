using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class BarrierPassabilityTests
{
    private const int FloorY = 99;

    /// <summary>The course's walking plane, a body's feet cell (<c>SURFACE_Y</c>).</summary>
    private const int BodyY = 100;

    /// <summary>The harness's plan capture margin.</summary>
    private const int CourseMargin = 24;

    private static readonly BlockPos LaneStart = new(0, BodyY, 1);
    private static readonly BlockPos LaneGoal = new(7, BodyY, 1);

    /// <summary>L6's plot: floor at <c>FloorY</c> over x 0..7 / z 0..2, walls at z=0 and z=2 filling both body cells, and whatever the caller puts in the doorway at x=4.</summary>
    private static FixtureWorld Lane(int doorwayState)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        if (doorwayState != FixtureWorld.Air)
        {
            world.Set(4, BodyY, 1, doorwayState);
            world.Set(4, BodyY + 1, 1, doorwayState);
        }

        return world;
    }

    private static CalculationContext Context(FixtureWorld world)
        => new(world.Capture(LaneStart, LaneGoal, CourseMargin), Sealed);

    private static PathResult Plan(FixtureWorld world)
    {
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        return PathPlanner.FindPath(view, Sealed, LaneStart, new GoalBlock(LaneGoal));
    }

    private static readonly PathfinderOptions Sealed =
        PathfinderOptions.Default with { AllowDoorInteraction = false };

    /// <summary>The classification is one property read over a registry-id suffix, and it never consults the shape. The two states that make that visible are the open door (a collision box, and passable) and the unreadable door (the SAME collision box, and refused).</summary>
    [Theory]
    [InlineData(FixtureWorld.OpenDoorNorth, BarrierKind.PassableNow)]
    [InlineData(FixtureWorld.ClosedDoorWest, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.OpenTrapdoorNorth, BarrierKind.PassableNow)]
    [InlineData(FixtureWorld.ClosedTrapdoorBottom, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.OpenFenceGate, BarrierKind.PassableNow)]
    [InlineData(FixtureWorld.ClosedFenceGate, BarrierKind.NeedsInteraction)]
    [InlineData(FixtureWorld.UnreadableDoor, BarrierKind.Wall)]
    [InlineData(FixtureWorld.Stone, BarrierKind.None)]
    [InlineData(FixtureWorld.Air, BarrierKind.None)]
    [InlineData(FixtureWorld.Fence, BarrierKind.None)]
    public void ClassifyBarrier_ReadsTheOpenPropertyAndNotTheShape(int stateId, BarrierKind expected)
    {
        var world = new FixtureWorld();
        world.Set(4, BodyY, 1, stateId);
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);

        Assert.Equal(expected, MoveHelper.ClassifyBarrier(view.GetBlock(new BlockPos(4, BodyY, 1))));
    }

    /// <summary>The palette gate that keeps the family free on terrain that has none of it: FALSE is exact, so a region with no door in it never pays the suffix test or the extra block read.</summary>
    [Fact]
    public void MayContainBarrier_IsFalseOnTerrainWithNoneAndTrueWithADoor()
    {
        var plain = new FixtureWorld();
        plain.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        Assert.False(plain.Capture(LaneStart, LaneGoal, CourseMargin).MayContainBarrier);

        Assert.True(Lane(FixtureWorld.OpenDoorNorth).Capture(LaneStart, LaneGoal, CourseMargin).MayContainBarrier);
    }

    [Fact]
    public void AnOpenDoor_IsPassable()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.OpenDoorNorth));
        Assert.True(
            ctx.CanWalkThrough(4, BodyY, 1),
            "an open door's cell is physically clear for a 0.6-wide body (0.8125 of free width) and "
                + "vanilla's own pathfinder reads OPEN, so the planner must admit it");
    }

    [Fact]
    public void AClosedDoor_IsNotPassable()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.ClosedDoorWest));
        Assert.False(ctx.CanWalkThrough(4, BodyY, 1));
    }

    [Fact]
    public void AnOpenTrapdoorPanel_IsPassable()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.OpenTrapdoorNorth));
        Assert.True(ctx.CanWalkThrough(4, BodyY, 1));
    }

    [Fact]
    public void AClosedTrapdoor_IsNotABodyCellAndIsStillAFloor()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.ClosedTrapdoorBottom));
        Assert.False(ctx.CanWalkThrough(4, BodyY, 1));
        Assert.True(ctx.CanWalkOn(4, BodyY, 1));
    }

    [Fact]
    public void AnOpenFenceGate_IsPassableAsItAlreadyWas()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.OpenFenceGate));
        Assert.True(ctx.CanWalkThrough(4, BodyY, 1));
    }

    [Fact]
    public void AClosedFenceGate_IsNotPassable()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.ClosedFenceGate));
        Assert.False(ctx.CanWalkThrough(4, BodyY, 1));
    }

    [Fact]
    public void TheCellAboveAClosedFenceGate_IsNotPassable()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, 1, FixtureWorld.ClosedFenceGate);
        var ctx = new CalculationContext(world.Capture(LaneStart, LaneGoal, CourseMargin), Sealed);

        Assert.False(
            ctx.CanWalkThrough(4, BodyY + 1, 1),
            "the gate reaches 1.5, so half a block of it is inside the cell above");
    }

    [Fact]
    public void ADoorWhoseOpenPropertyCannotBeRead_IsNotPassable()
    {
        CalculationContext ctx = Context(Lane(FixtureWorld.UnreadableDoor));
        Assert.False(ctx.CanWalkThrough(4, BodyY, 1));
    }

    [Fact]
    public void TheL6Lane_PlansThroughTheOpenDoor()
    {
        PathResult result = Plan(Lane(FixtureWorld.OpenDoorNorth));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.X == 4 && node.Y == BodyY && node.Z == 1);
        Assert.Equal(LaneGoal.X, result.Path[^1].X);
        Assert.Equal(LaneGoal.Z, result.Path[^1].Z);
    }

    [Fact]
    public void TheL6Lane_WithTheDoorClosed_StillRefuses()
    {
        PathResult result = Plan(Lane(FixtureWorld.ClosedDoorWest));
        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The control: with nothing in the doorway the same lane has always planned.</summary>
    [Fact]
    public void TheL6Lane_WithAnEmptyDoorway_Plans()
    {
        PathResult result = Plan(Lane(FixtureWorld.Air));
        Assert.Equal(PathStatus.Success, result.Status);
    }
}
