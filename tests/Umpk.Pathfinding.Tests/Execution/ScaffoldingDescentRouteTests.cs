using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ScaffoldingDescentRouteTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    private static FixtureWorld K4aWorld()
    {
        var world = new FixtureWorld();
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);
        world.Fill(10, 100, 10, 10, 104, 10, FixtureWorld.Scaffolding);
        return world;
    }

    /// <summary>K4a's column, twenty cells tall instead of five, for the budget bound.</summary>
    private static FixtureWorld TallTowerWorld()
    {
        var world = new FixtureWorld();
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);
        world.Fill(10, 100, 10, 10, 119, 10, FixtureWorld.Scaffolding);
        return world;
    }

    /// <summary>K4a from the column's own top face. The start is the pose a body standing on the column has: feet at the integer plane the <c>SHAPE_STABLE</c> plate tops out at.</summary>
    /// <remarks>Measured with the physics exemption in and the executor arms OUT: <c>K4a ended Failed at (10.5000, 105.0000, 10.5000) after 86 ticks</c>. 86 is the segment budget plus one, to the tick: a whole-column <c>Fall</c> costs 38.33, <c>LandSlack</c> is 1.691 and <c>FixedOverheadTicks</c> is 20, so <c>ceil(1.691 x 38.33) + 20 = 85</c>. The physics could sink the body the whole time; nothing was pressing sneak.</remarks>
    [Fact]
    public void K4a_FromTheTopFace_Executes()
    {
        Run run = Drive(K4aWorld(), new BlockPos(10, 105, 10), new BlockPos(6, 100, 10), new Vec3d(10.5, 105, 10.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"K4a ended {run.State} at {Fmt(run.End)} after {run.Ticks} ticks");
        Assert.Equal(100.0, run.End.Y, 3);
    }

    /// <summary>K4a from a plate part-way down the column. This is the <see cref="ClimbTemplate"/> arm rather than the <see cref="FallTemplate"/> one: from inside the column the search emits rung-to-rung <c>Climb</c> moves down.</summary>
    /// <remarks>Measured with the physics exemption in and the executor arms OUT: <c>K4a mid-column ended Failed at (10.5000, 103.0000, 10.5000) after 41 ticks</c>, which is <c>MinBudgetTicks</c> 40 plus one. <c>ClimbTemplate</c> descends by RELEASING, which is right for a ladder and inert on a scaffold: the plate is still there, so the body never moves.</remarks>
    [Fact]
    public void K4a_FromAMidColumnPlate_Executes()
    {
        Run run = Drive(K4aWorld(), new BlockPos(10, 103, 10), new BlockPos(6, 100, 10), new Vec3d(10.5, 103, 10.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"K4a mid-column ended {run.State} at {Fmt(run.End)} after {run.Ticks} ticks");
        Assert.Equal(100.0, run.End.Y, 3);
    }

    /// <summary>The budget bound of section C.2, pinned rather than remembered. At 6.6667 charged ticks a block a single <c>Fall</c> segment exhausts <c>MaxBudgetTicks</c> 200 at about 29-30 blocks of column, so a twenty-block tower must fit and must be seen to fit with room to spare.</summary>
    [Fact]
    public void ATwentyBlockTower_FitsItsBudget()
    {
        Run run = Drive(
            TallTowerWorld(), new BlockPos(10, 120, 10), new BlockPos(6, 100, 10), new Vec3d(10.5, 120, 10.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"the 20-block tower ended {run.State} at {Fmt(run.End)} after {run.Ticks} ticks");
        Assert.Equal(100.0, run.End.Y, 3);
        Assert.True(run.Ticks < 200, $"the tower took {run.Ticks} ticks, at or past the 200-tick clamp");
    }

    [Fact]
    public void ADescendOntoAScaffoldRoof_Lands()
    {
        var world = new FixtureWorld();
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);
        world.Fill(4, 100, 6, 7, 106, 13, FixtureWorld.Stone);      // the launch mass, surface y=107
        world.Fill(9, 100, 9, 11, 104, 11, FixtureWorld.Stone);     // the roof's supporting mass
        world.Fill(9, 105, 9, 11, 105, 11, FixtureWorld.Scaffolding); // a 3x3 scaffolding roof at y=105

        Run run = Drive(world, new BlockPos(7, 107, 10), new BlockPos(10, 106, 10), new Vec3d(7.5, 107, 10.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"the roof drop ended {run.State} at {Fmt(run.End)} after {run.Ticks} ticks");
        Assert.Equal(106.0, run.End.Y, 3);
    }

    [Fact]
    public void AColumnOverAVoid_IsRefused()
    {
        var world = new FixtureWorld();
        world.Fill(8, 112, 8, 12, 112, 12, FixtureWorld.Stone);       // the high ledge, surface y=113
        world.Fill(10, 113, 10, 10, 117, 10, FixtureWorld.Scaffolding); // the column, top face y=118
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);          // the far floor, 13 below the foot

        var start = new BlockPos(10, 118, 10);
        var goal = new BlockPos(6, 100, 10);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    [Fact]
    public void ClimbTemplate_InTheWindowInsideAScaffold_DoesNotPressSneak()
    {
        (ClimbTemplate template, PhysicsState physics) = InWindowInsideTheColumn();

        Assert.Equal(TemplateState.InProgress, template.Tick(physics, out TemplateOutput output));
        Assert.False(
            output.Input.Sneak,
            "ClimbTemplate pressed Sneak in the arrival window inside a scaffolding column, which after "
            + "the climb-clamp exemption is an instruction to sink a whole cell rather than to hold height");
    }

    /// <summary>The same gate's ladder control. The in-window <c>Sneak</c> is the only thing holding a body on a ladder rung, so gating it on scaffolding must not gate it everywhere.</summary>
    [Fact]
    public void ClimbTemplate_InTheWindowOnALadder_StillPressesSneak()
    {
        (ClimbTemplate template, PhysicsState physics) = InWindowInsideTheColumn(FixtureWorld.Ladder);

        Assert.Equal(TemplateState.InProgress, template.Tick(physics, out TemplateOutput output));
        Assert.True(output.Input.Sneak, "the ladder's in-window hold was gated away with the scaffolding one");
    }

    /// <summary><c>HoldInput</c>, the completion tick's input, asserted through the same direct seam. On a ladder it is the parking brake; inside a scaffold it is a five-block slide.</summary>
    [Theory]
    [InlineData(FixtureWorld.Scaffolding, false)]
    [InlineData(FixtureWorld.Ladder, true)]
    public void ClimbTemplate_OnArrival_PressesSneakOnlyOffAScaffold(int climbable, bool expectSneak)
    {
        var world = new FixtureWorld();
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);
        world.Fill(10, 100, 10, 10, 109, 10, climbable);
        PlanningWorldView view = world.Capture(new BlockPos(10, 100, 10), new BlockPos(10, 109, 10), margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        var segment = new PathSegment
        {
            MoveType = MoveType.Climb,
            Start = new Vec3d(10.5, 104.0, 10.5),
            End = new Vec3d(10.5, 105.0, 10.5),
            PlannedTickCost = 6.6667,
        };
        var template = new ClimbTemplate(ctx, segment);

        // On the rung and inside the column: the arrival condition, which returns Complete and whose output is HoldInput's.
        PhysicsState physics = PoseInside(new Vec3d(10.5, 105.0656, 10.5));

        Assert.Equal(TemplateState.Complete, template.Tick(physics, out TemplateOutput output));
        Assert.Equal(expectSneak, output.Input.Sneak);
    }

    private static (ClimbTemplate Template, PhysicsState Physics) InWindowInsideTheColumn(
        int climbable = FixtureWorld.Scaffolding)
    {
        var world = new FixtureWorld();
        world.Fill(4, 99, 6, 13, 99, 13, FixtureWorld.Stone);
        world.Fill(10, 100, 10, 10, 109, 10, climbable);
        PlanningWorldView view = world.Capture(new BlockPos(10, 100, 10), new BlockPos(10, 109, 10), margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        var segment = new PathSegment
        {
            MoveType = MoveType.Climb,
            Start = new Vec3d(10.5, 104.0, 10.5),
            End = new Vec3d(10.5, 105.0, 10.5),
            PlannedTickCost = 6.6667,
        };
        var template = new ClimbTemplate(ctx, segment);

        // In the window (0.0656 above the rung, inside the 0.15 window) but with the footprint straddling the column edge, so the arrival's second half is false and the template stays InProgress.
        PhysicsState physics = PoseInside(new Vec3d(10.85, 105.0656, 10.5));
        return (template, physics);
    }

    private static PhysicsState PoseInside(Vec3d position) => new()
    {
        Position = position,
        Velocity = Vec3d.Zero,
        Yaw = 0f,
        Pitch = 0f,
        OnGround = false,
        OnClimbable = true,
        BoundingBox = new Aabb(
            position.X - 0.3, position.Y, position.Z - 0.3, position.X + 0.3, position.Y + 1.8, position.Z + 0.3),
    };

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");

    private static Run Drive(FixtureWorld world, BlockPos start, BlockPos goal, Vec3d startPos)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, startPos, 270f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 900);
        return new Run(state, driver.State.Position, driver.Trace.Count);
    }

    private readonly record struct Run(PathExecutorState State, Vec3d End, int Ticks);
}
