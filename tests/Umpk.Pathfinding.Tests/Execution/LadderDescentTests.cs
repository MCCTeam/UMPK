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

public sealed class LadderDescentTests
{
    /// <summary>The probe horizon. Ten ticks, because a run that climbs leaves the shaft's mouth at y = 100 on the eleventh (measured), and past that the body is no longer on a ladder at all and simply falls, which would hide the very effect being pinned behind a longer number.</summary>
    private const int Ticks = 10;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    [Theory]
    [InlineData(FixtureWorld.Ladder)]
    [InlineData(FixtureWorld.LadderWest)]
    public void EnclosedShaft_ReachesTheBottom(int ladder)
    {
        Run run = DriveShaft(ladder);

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"the C11 shaft descent ended {run.State} at {Fmt(run.End)} after {run.Ticks} ticks, "
                + $"deepest y = {run.DeepestY:F4} against a shaft floor at 92");
        Assert.True(
            Math.Abs(run.End.Y - 92.0) < 0.01,
            $"the shaft descent stopped at y = {run.End.Y:F4}, not the shaft floor at 92");
        Assert.True(
            run.Ticks <= 90,
            $"the shaft descent took {run.Ticks} ticks (measured 69 plateless and 72 plated; the bob took "
                + "the whole 206)");
    }

    [Fact]
    public void PressingIntoAShaftWall_ClimbsTheLadderInsteadOfDescendingIt()
    {
        Probe forward = FallOnLadder(new MovementInput { Forward = true }, Ticks);
        Probe back = FallOnLadder(new MovementInput { Back = true }, Ticks);
        Probe released = FallOnLadder(MovementInput.None, Ticks);

        Assert.True(
            forward.OnClimbable && back.OnClimbable && released.OnClimbable,
            "the probe pose is not on the ladder");
        Assert.True(
            forward.DeltaY > 0.0 && forward.EndVelocityY > 0.0,
            $"holding Forward into the shaft wall moved {forward.DeltaY:F4} blocks in {Ticks} ticks at a "
                + $"final vy of {forward.EndVelocityY:F4} (measured +0.98 and +0.1176, a climb); the lift "
                + "clause no longer cancels the clamp and this test no longer describes the engine");
        Assert.True(
            back.EndVelocityY > 0.0,
            $"holding Back ended at vy {back.EndVelocityY:F4} after crossing the shaft (measured +0.1176)");
        Assert.True(
            released.DeltaY < -1.4 && released.EndVelocityY < 0.0,
            $"releasing the stick descended only {released.DeltaY:F4} blocks in {Ticks} ticks, against the "
                + "0.15 clamp's 1.58");
    }

    [Fact]
    public void LadderGrabRow_StillCompletes()
    {
        var world = new FixtureWorld();
        world.Fill(646, 99, 134, 653, 99, 141, FixtureWorld.Stone);
        world.Fill(650, 100, 138, 650, 105, 138, FixtureWorld.Stone);
        world.Fill(649, 100, 138, 649, 105, 138, FixtureWorld.LadderWest);

        Run run = Drive(
            world, new BlockPos(650, 106, 138), new BlockPos(647, 100, 138), new Vec3d(650.5, 106, 138.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"C10 laddergrab ended {run.State} at {Fmt(run.End)}; it completed at (647.8393, 100, 138.5) "
                + "before the arm and (647.8555, 100, 138.5) after it");
        Assert.True(Math.Abs(run.End.Y - 100.0) < 0.01, $"C10 ended at y = {run.End.Y:F4}, not 100");
    }

    [Fact]
    public void ClimbDownRow_StillCompletes()
    {
        var world = new FixtureWorld();
        world.Fill(776, 91, 136, 780, 99, 140, FixtureWorld.Stone);
        world.Fill(778, 92, 138, 778, 99, 138, FixtureWorld.LadderWest);

        Run run = Drive(
            world, new BlockPos(778, 100, 137), new BlockPos(778, 92, 138), new Vec3d(778.5, 100, 137.5));

        Assert.True(
            run.State == PathExecutorState.Complete,
            $"C13 climbdown ended {run.State} at {Fmt(run.End)}");
        Assert.True(Math.Abs(run.End.Y - 92.0) < 0.01, $"C13 ended at y = {run.End.Y:F4}, not 92");
    }

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");

    private static FixtureWorld ShaftWorld(int ladder = FixtureWorld.Ladder)
    {
        var world = new FixtureWorld();
        world.Fill(712, 91, 136, 716, 99, 140, FixtureWorld.Stone);
        world.Fill(714, 92, 138, 714, 99, 138, ladder);
        return world;
    }

    private static Run DriveShaft(int ladder = FixtureWorld.Ladder)
        => Drive(
            ShaftWorld(ladder), new BlockPos(712, 100, 138), new BlockPos(714, 92, 138), new Vec3d(712.5, 100, 138.5));

    /// <summary>How the player moves vertically over <paramref name="ticks"/> from one fixed pose inside the shaft, holding <paramref name="input"/>, driven straight through <see cref="PlayerPhysics"/>.</summary>
    private static Probe FallOnLadder(MovementInput input, int ticks)
    {
        FixtureWorld world = ShaftWorld();
        PlanningWorldView view = world.Capture(
            new BlockPos(712, 100, 138), new BlockPos(714, 92, 138), margin: 10);
        var ctx = new PathExecutionContext(view, Profile);

        var engine = new PlayerPhysics(ctx.World, ctx.Profile);
        engine.SetConditions(ctx.Conditions);
        // Facing +x, pressed against the shaft's +x wall, which is where the executor puts the body.
        engine.Reset(new Vec3d(714.7, 99.0, 138.5), 270f, 0f);
        engine.Step(MovementInput.None);

        double startY = engine.State.Position.Y;
        bool climbable = engine.State.OnClimbable;
        for (int i = 0; i < ticks; i++)
            engine.Step(input);

        return new Probe(engine.State.Position.Y - startY, engine.State.Velocity.Y, climbable);
    }

    private static Run Drive(FixtureWorld world, BlockPos start, BlockPos goal, Vec3d startPos)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(ctx, segments, startPos, 270f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 600);

        double deepest = double.PositiveInfinity;
        foreach (TickSample sample in driver.Trace)
            deepest = Math.Min(deepest, sample.Position.Y);

        return new Run(state, driver.State.Position, driver.Trace.Count, deepest);
    }

    private readonly record struct Run(PathExecutorState State, Vec3d End, int Ticks, double DeepestY);

    private readonly record struct Probe(double DeltaY, double EndVelocityY, bool OnClimbable);
}
