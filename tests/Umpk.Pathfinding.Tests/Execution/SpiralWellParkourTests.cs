using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SpiralWellParkourTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private static FixtureWorld SpiralWorld()
    {
        var world = new FixtureWorld();
        world.Fill(72, 99, 392, 78, 110, 398, FixtureWorld.Stone);
        world.Fill(73, 99, 393, 77, 110, 397, FixtureWorld.Air);
        int[][] treads =
        [
            [73, 99, 393], [74, 99, 393], [75, 100, 393], [76, 100, 393],
            [77, 101, 393], [77, 101, 394], [77, 102, 395], [77, 102, 396],
            [77, 103, 397], [76, 103, 397], [75, 104, 397], [74, 104, 397],
            [73, 105, 397], [73, 105, 396], [73, 106, 395], [73, 106, 394],
            [73, 107, 393],
        ];
        foreach (int[] tread in treads)
            world.Set(tread[0], tread[1], tread[2], FixtureWorld.Stone);

        return world;
    }

    /// <summary>The row: plan it, run it, and require both that it arrives and that it never planned a jump over the well. The second half matters on its own - an arrival that still contained the jump would mean the executor had got lucky, not that the planner had stopped offering an impossible move.</summary>
    [Fact]
    public void H2Spiral_ReachesTheTopWithoutJumpingTheWell()
    {
        FixtureWorld world = SpiralWorld();
        var start = new BlockPos(73, 100, 393);
        var goal = new BlockPos(73, 108, 393);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        Assert.DoesNotContain(segments, segment => segment.MoveType == MoveType.Parkour);

        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(73.5, 100.0, 393.5), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 600);

        Vec3d end = driver.State.Position;
        double endX = end.X;
        double endY = end.Y;
        double endZ = end.Z;
        int ticks = driver.Trace.Count;
        string where = string.Create(
            CultureInfo.InvariantCulture, $"({endX:F4}, {endY:F4}, {endZ:F4}) after {ticks} ticks");
        Assert.True(
            state == PathExecutorState.Complete,
            $"the spiral ended {state} at {where}; before the fix it failed on segment 8 at "
                + "(74.3000, 103.8549, 396.4457), which is the live row's position to four decimals");
        Assert.True(
            Math.Abs(endY - 108.0) < 0.01,
            string.Create(CultureInfo.InvariantCulture, $"the spiral ended at y = {endY:F4}, not the top tread at 108"));
    }

    [Theory]
    [InlineData(-2, -1, 1, false)]   // H2's jump: sums to 3, over the 2.5 ascending boundary, so it asks
    [InlineData(-1, -1, 1, true)]    // a one-cell diagonal hop sums to 2 and is still free
    [InlineData(-2, 0, 1, true)]     // CONTROL, cardinal two-cell: free before and after
    [InlineData(-3, 0, 1, false)]    // CONTROL, cardinal three-cell: asked before and after
    public void RunUpIsAskedFor_WhenTheOffsetsSumPastTheBoundary(int xOffset, int zOffset, int yDelta, bool freeWithoutARunway)
    {
        Assert.True(
            ParkourFeasibility.HasRunUp(Takeoff(walled: false), 0, 64, 0, xOffset, zOffset, yDelta),
            "a takeoff with a clear runway behind it must always pass");

        Assert.Equal(
            freeWithoutARunway,
            ParkourFeasibility.HasRunUp(Takeoff(walled: true), 0, 64, 0, xOffset, zOffset, yDelta));
    }

    /// <summary>A floor to stand on and jump from, with the two cells behind the takeoff either open or filled to head height - H2's shell wall, reduced to the one thing the check reads.</summary>
    private static CalculationContext Takeoff(bool walled)
    {
        var world = new FixtureWorld();
        world.Fill(-8, 63, -8, 8, 63, 8, FixtureWorld.Stone);
        if (walled)
            world.Fill(1, 64, 0, 2, 66, 2, FixtureWorld.Stone);

        return FixtureContext.Around(world, 0, 64, 0, margin: 12);
    }
}
