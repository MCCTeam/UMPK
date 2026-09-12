using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class JumpApproachHeadingTests
{
    private const int FloorY = 64;

    private const float FacingPlusX = 270f;

    /// <summary>41 run-up phases 0.05 apart span 2.0 blocks, which is seven sprint ticks.</summary>
    private const int SweepPoints = 41;

    private const double SweepStep = 0.05;
    private const double SweepStartX = -13.5;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    [Fact]
    public void CardinalRunUp_IntoA45DegreeJump_TakesOffFromEveryPhase()
    {
        int arrived = Sweep(degrees: 45, out string detail);
        Assert.True(
            arrived == SweepPoints,
            $"a 45-degree jump exit took off from only {arrived} of {SweepPoints} run-up phases: {detail}");
    }

    [Fact]
    public void CardinalRunUp_IntoA90DegreeJump_TakesOffFromEveryPhase()
    {
        int arrived = Sweep(degrees: 90, out string detail);
        Assert.True(
            arrived == SweepPoints,
            $"a 90-degree jump exit took off from only {arrived} of {SweepPoints} run-up phases: {detail}");
    }

    [Fact]
    public void ShortGap_WalkedRatherThanJumped_StillCompletes()
    {
        var world = new FixtureWorld();
        world.Fill(0, 99, 192, 5, 99, 194, FixtureWorld.Stone);
        world.Fill(7, 99, 192, 12, 99, 194, FixtureWorld.Stone);

        var start = new BlockPos(1, 100, 193);
        var goal = new BlockPos(9, 100, 193);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult plan = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, plan.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(plan.Path);
        int parkour = -1;
        for (int i = 0; i < segments.Count; i++)
            if (segments[i].MoveType == MoveType.Parkour)
            {
                parkour = i;
                break;
            }

        Assert.True(parkour >= 0, "the planner stopped offering a parkour here, so this row no longer tests it");

        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(1.5, 100, 193.5), FacingPlusX, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 800);
        Vec3d end = driver.State.Position;

        Assert.True(
            state == PathExecutorState.Complete
                && Math.Abs(end.X - (goal.X + 0.5)) < 0.9
                && Math.Abs(end.Z - (goal.Z + 0.5)) < 0.9,
            string.Create(
                CultureInfo.InvariantCulture,
                $"D1 ended {state} at ({end.X:F4}, {end.Y:F4}, {end.Z:F4}); ")
                + DescribeStall(driver.Trace, parkour));
    }

    [Fact]
    public void SafeStableTurn_IsUntouched()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 8, -8, 8, FloorY);

        PathNode[] nodes =
        [
            new(0, FloorY + 1, 0) { MoveUsed = MoveType.Traverse },
            new(1, FloorY + 1, 1) { MoveUsed = MoveType.Diagonal },
            new(2, FloorY + 1, 0) { MoveUsed = MoveType.Diagonal },
        ];

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes);
        Assert.Equal(PathTransitionType.Turn, segments[0].ExitTransition);
        Assert.True(segments[0].ExitHints.RequireStableFooting);
        Assert.False(segments[0].ExitHints.RequireJumpReady);

        PlanningWorldView view = world.Capture(new BlockPos(0, FloorY + 1, 0), new BlockPos(2, FloorY + 1, 0));
        var ctx = new PathExecutionContext(view, Profile);
        IReadOnlyList<YawSample> trace = DriveWithYaw(ctx, segments, segments[0].Start, 0f, out PathExecutorState state);

        Assert.Equal(PathExecutorState.Complete, state);

        SegmentGeometry.GetExitHeading(segments[0], out int exitX, out int exitZ);
        double closest = 360.0;
        foreach (YawSample sample in trace)
            if (sample.SegmentIndex == 0)
                closest = Math.Min(closest, SegmentGeometry.HeadingPenaltyDegrees(sample.Yaw, exitX, exitZ));

        string closestText = closest.ToString("F2", CultureInfo.InvariantCulture);
        Assert.True(
            closest > 8.0,
            $"a safe stable turn steered to its EXIT heading (closest approach {closestText} degrees), which "
                + "is the yaw handoff SegmentTurnHandoffTests depends on being absent");
    }

    [Fact]
    public void LaunchColumn_FacesTheJumpHeadingBeforeItRunsOut()
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments) = TurnPlan(degrees: 45);
        int launch = LaunchSegmentIndex(segments);
        PathSegment segment = segments[launch];
        SegmentGeometry.GetExitHeading(segment, out int exitX, out int exitZ);
        Assert.True(
            exitX != segment.HeadingX || exitZ != segment.HeadingZ,
            "the fixture's launch segment does not actually turn, so it cannot exercise the steer");

        IReadOnlyList<YawSample> trace = DriveWithYaw(
            ctx, segments, new Vec3d(SweepStartX, FloorY + 1, 0.5), FacingPlusX, out _);

        double closest = 360.0;
        int ticksInColumn = 0;
        foreach (YawSample sample in trace)
        {
            if (sample.SegmentIndex != launch
                || !SegmentGeometry.IsCenterInsideTargetBlock(sample.Position, segment.End))
                continue;

            ticksInColumn++;
            closest = Math.Min(closest, SegmentGeometry.HeadingPenaltyDegrees(sample.Yaw, exitX, exitZ));
        }

        Assert.True(ticksInColumn > 0, "the bot never got its centre inside the launch column at all");
        string closestText = closest.ToString("F2", CultureInfo.InvariantCulture);
        Assert.True(
            closest <= 8.0,
            $"inside the launch column over {ticksInColumn} ticks the yaw came no closer than "
                + $"{closestText} degrees to the exit heading, against the 8 the jump-ready gate demands");
    }

    private static (PathExecutionContext Ctx, IReadOnlyList<PathSegment> Segments) TurnPlan(int degrees)
    {
        var world = new FixtureWorld();
        world.Fill(-20, FloorY, -4, 0, FloorY, 4, FixtureWorld.Stone);

        (int jx, int jz) = degrees == 45 ? (2, 2) : (0, 3);
        world.Fill(jx, FloorY, jz, jx + 5, FloorY, jz + 2, FixtureWorld.Stone);

        var nodes = new List<PathNode>();
        for (int x = -16; x <= 0; x++)
            nodes.Add(new PathNode(x, FloorY + 1, 0) { MoveUsed = MoveType.Traverse });

        nodes.Add(new PathNode(jx, FloorY + 1, jz) { MoveUsed = MoveType.Parkour, ParkourProfile = ParkourProfile.Default });
        nodes.Add(new PathNode(jx + 1, FloorY + 1, jz) { MoveUsed = MoveType.Traverse });
        nodes.Add(new PathNode(jx + 2, FloorY + 1, jz) { MoveUsed = MoveType.Traverse });

        PlanningWorldView view = world.Capture(
            new BlockPos(-16, FloorY + 1, 0), new BlockPos(jx + 4, FloorY + 1, jz), margin: 12);
        return (new PathExecutionContext(view, Profile), PathSegmentBuilder.FromPath(nodes));
    }

    /// <summary>How long the parkour segment held one position at the end of its life, which is the deadlock's own signature and the thing a bare end position cannot show.</summary>
    private static string DescribeStall(IReadOnlyList<TickSample> trace, int segmentIndex)
    {
        var samples = new List<TickSample>();
        foreach (TickSample sample in trace)
            if (sample.SegmentIndex == segmentIndex)
                samples.Add(sample);

        if (samples.Count == 0)
            return "the parkour segment never ran";

        Vec3d last = samples[^1].Position;
        int held = 0;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            double dx = samples[i].Position.X - last.X;
            double dz = samples[i].Position.Z - last.Z;
            if ((dx * dx) + (dz * dz) > 1.0E-4)
                break;

            held++;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"the parkour segment ran {samples.Count} ticks and held ({last.X:F4}, {last.Y:F4}, {last.Z:F4}) "
                + $"for the last {held} of them");
    }

    private static int LaunchSegmentIndex(IReadOnlyList<PathSegment> segments)
    {
        for (int i = 0; i < segments.Count; i++)
            if (segments[i].ExitTransition == PathTransitionType.PrepareJump)
                return i;

        throw new InvalidOperationException("the fixture plan has no PrepareJump exit");
    }

    private static int Sweep(int degrees, out string detail)
    {
        int arrived = 0;
        var missed = new List<string>();
        for (int i = 0; i < SweepPoints; i++)
        {
            double startX = SweepStartX + (i * SweepStep);
            if (RunTurn(degrees, startX, out string outcome))
                arrived++;

            else if (missed.Count < 3)
                missed.Add(string.Create(CultureInfo.InvariantCulture, $"x0={startX:F2} {outcome}"));

        }

        detail = missed.Count == 0 ? "every phase arrived" : string.Join(" | ", missed);
        return arrived;
    }

    private static bool RunTurn(int degrees, double startX, out string outcome)
    {
        (PathExecutionContext ctx, IReadOnlyList<PathSegment> segments) = TurnPlan(degrees);
        int jz = degrees == 45 ? 2 : 3;
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(startX, FloorY + 1, 0.5), FacingPlusX, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 600);
        Vec3d end = driver.State.Position;
        outcome = string.Create(
            CultureInfo.InvariantCulture, $"{state} end=({end.X:F3}, {end.Y:F3}, {end.Z:F3})");
        return state == PathExecutorState.Complete
            && Math.Abs(end.Y - (FloorY + 1)) < 0.1
            && end.Z >= jz;
    }

    private static IReadOnlyList<YawSample> DriveWithYaw(
        PathExecutionContext ctx,
        IReadOnlyList<PathSegment> segments,
        Vec3d startPos,
        float startYaw,
        out PathExecutorState state)
    {
        var engine = new PlayerPhysics(ctx.World, ctx.Profile);
        engine.SetConditions(ctx.Conditions);
        engine.Reset(startPos, startYaw, 0f);
        engine.Step(MovementInput.None);

        var executor = new PathExecutor(ctx, segments);
        var trace = new List<YawSample>();
        state = PathExecutorState.InProgress;
        for (int tick = 0; tick < 600; tick++)
        {
            trace.Add(new YawSample(executor.CurrentIndex, engine.State.Position, engine.State.Yaw));
            PathExecutorTick step = executor.Tick(engine.State);
            if (step.State != PathExecutorState.InProgress)
            {
                state = step.State;
                break;
            }

            engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            engine.Step(step.Output.Input);
        }

        return trace;
    }

    private readonly record struct YawSample(int SegmentIndex, Vec3d Position, float Yaw);
}
