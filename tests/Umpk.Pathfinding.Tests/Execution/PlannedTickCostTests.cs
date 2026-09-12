using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class PlannedTickCostTests
{
    private const int FloorY = 64;

    private static PathResult FlatSprintPath(out PlanningWorldView view)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 20, -4, 4, FloorY);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(8, FloorY + 1, 0);
        view = world.Capture(start, goal, margin: 6);
        return PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
    }

    /// <summary>The invariant the plumbing rests on, written down first so a later A* change cannot quietly break it. On a flat cardinal run every step is a sprinting Traverse, so every GCost delta is exactly <see cref="ActionCosts.SprintOneBlock"/>. This passes today; that is the point.</summary>
    [Fact]
    public void GCostDelta_EqualsTheChargedMoveCost()
    {
        PathResult result = FlatSprintPath(out _);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.True(result.Path.Count > 2, "the fixture must produce a multi-step path");
        Assert.Equal(0.0, result.Path[0].GCost, 9);

        for (int i = 1; i < result.Path.Count; i++)
        {
            Assert.Equal(MoveType.Traverse, result.Path[i].MoveUsed);
            Assert.Equal(ActionCosts.SprintOneBlock, result.Path[i].GCost - result.Path[i - 1].GCost, 9);
        }
    }

    [Fact]
    public void PlannedTickCost_IsPopulatedOnEverySegment()
    {
        PathResult result = FlatSprintPath(out _);
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        Assert.NotEmpty(segments);

        for (int i = 0; i < segments.Count; i++)
        {
            Assert.Equal(
                result.Path[i + 1].GCost - result.Path[i].GCost,
                segments[i].PlannedTickCost,
                9);
            Assert.True(segments[i].PlannedTickCost > 0.0, $"segment {i} was priced at nothing");
        }
    }

    /// <summary>The value is the move's own price, not "the sprint constant, everywhere". A flooded channel is charged <see cref="ActionCosts.SwimOneBlock"/> per swim move, 2.55x what the same distance costs on land, and that is exactly the difference a per-medium budget has to be able to see.</summary>
    [Fact]
    public void PlannedTickCost_TracksTheMoveFamily()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 16, -4, 4, FloorY);
        world.Fill(1, FloorY + 1, 0, 8, FloorY + 3, 0, FixtureWorld.Water);   // three-deep channel

        var start = new BlockPos(1, FloorY + 2, 0);
        var goal = new BlockPos(8, FloorY + 2, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        int swimSegments = 0;
        foreach (PathSegment segment in segments)
            if (segment.MoveType == MoveType.Swim)
            {
                swimSegments++;
                Assert.Equal(ActionCosts.SwimOneBlock, segment.PlannedTickCost, 9);
            }

        Assert.True(swimSegments > 0, "the fixture must route through the water");
        Assert.NotEqual(ActionCosts.SprintOneBlock, ActionCosts.SwimOneBlock);
    }

    /// <summary>A hand-built segment has no planner behind it, so its price is honestly zero rather than a plausible-looking guess. Consumers have to treat <c>&lt;= 0</c> as "unknown"; this pins that the default really is zero so a fallback can key on it.</summary>
    [Fact]
    public void HandBuiltSegment_LeavesPlannedTickCostAtZero()
    {
        var segment = new PathSegment
        {
            Start = new Vec3d(0.5, FloorY + 1, 0.5),
            End = new Vec3d(1.5, FloorY + 1, 0.5),
            MoveType = MoveType.Traverse,
        };

        Assert.Equal(0.0, segment.PlannedTickCost, 9);
    }

    // SegmentGeometry's two unnormalised helpers

    private static PathSegment Diagonal() => new()
    {
        Start = new Vec3d(0.5, FloorY + 1, 0.5),
        End = new Vec3d(1.5, FloorY + 1, 1.5),
        MoveType = MoveType.Diagonal,
    };

    [Fact]
    public void RemainingDistanceAlongSegment_IsNotInflatedOnDiagonals()
    {
        PathSegment segment = Diagonal();

        Assert.Equal(
            ActionCosts.DiagonalMultiplier,
            SegmentGeometry.RemainingDistanceAlongSegment(segment.Start, segment),
            9);

        // Half way along, half the distance is left.
        var half = new Vec3d(1.0, FloorY + 1, 1.0);
        Assert.Equal(
            ActionCosts.DiagonalMultiplier / 2.0,
            SegmentGeometry.RemainingDistanceAlongSegment(half, segment),
            9);

        // And the cardinal case is unchanged, which is what makes this a normalisation and not a scale.
        var cardinal = new PathSegment
        {
            Start = new Vec3d(0.5, FloorY + 1, 0.5),
            End = new Vec3d(1.5, FloorY + 1, 0.5),
            MoveType = MoveType.Traverse,
        };
        Assert.Equal(1.0, SegmentGeometry.RemainingDistanceAlongSegment(cardinal.Start, cardinal), 9);
    }

    /// <summary>The same inflation on the speed projection, and this one feeds the <c>MinExitSpeed</c>/<c>MaxExitSpeed</c> handoff gate. A body moving at 0.2 b/t along a diagonal has velocity components 0.2/sqrt each, and its speed along that heading is 0.2 - not 0.2828.</summary>
    [Fact]
    public void ProjectSpeedAlongHeading_IsNotInflatedOnDiagonals()
    {
        const double Speed = 0.2;
        double component = Speed / ActionCosts.DiagonalMultiplier;
        var physics = new PhysicsState { Velocity = new Vec3d(component, 0.0, component) };

        Assert.Equal(Speed, SegmentGeometry.ProjectSpeedAlongHeading(physics, 1, 1), 9);

        // Cardinal is untouched.
        var cardinalPhysics = new PhysicsState { Velocity = new Vec3d(Speed, 0.0, 0.0) };
        Assert.Equal(Speed, SegmentGeometry.ProjectSpeedAlongHeading(cardinalPhysics, 1, 0), 9);

        // Moving backwards along the heading still reads negative.
        var reversed = new PhysicsState { Velocity = new Vec3d(-component, 0.0, -component) };
        Assert.Equal(-Speed, SegmentGeometry.ProjectSpeedAlongHeading(reversed, 1, 1), 9);
    }
}
