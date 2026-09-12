using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

/// <summary><see cref="SwimTemplate"/> driven by the real <see cref="PlayerPhysics"/> engine over the real <see cref="PathExecutor"/>, in still water and in a current.</summary>
/// <remarks>
/// <para>The current matters because the swim controller is the one template whose input choice can be beaten by the environment. Thrust in water is a flat 0.0196 blocks a tick squared and a current is 0.014, so the decisive quantity is the ratio: at 0.714 a forward swimmer beats any current in the game, and at 2.381 - which is what <c>Sneak</c> produced - the current wins and the swimmer travels BACKWARDS. Both accelerations pass through the same slow-down, so the ratio is sprint-independent and era-independent, which is why the crab angle below needs no profile.</para>
/// </remarks>
public sealed class SwimTemplateTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private static Vec3d Center(int x, int y, int z) => new(x + 0.5, y, z + 0.5);

    /// <summary>A still, enclosed 1x1 water shaft: water y=lo..hi at (0, *, 0), stone all round.</summary>
    private static FixtureWorld StillShaft(int lo, int hi)
    {
        var world = new FixtureWorld();
        world.Fill(-2, lo - 3, -2, 2, hi + 6, 2, FixtureWorld.Stone);
        world.Fill(0, lo - 1, 0, 0, hi + 5, 0, FixtureWorld.Air);
        world.Fill(0, lo, 0, 0, hi, 0, FixtureWorld.Water);
        return world;
    }

    /// <summary>A 1-wide walled channel at z=2, water y=lo..hi, flowing +x over x=0..length-1.</summary>
    private static FixtureWorld FlowingChannel(int lo, int hi, int length)
    {
        var world = new FixtureWorld();
        world.Fill(-2, lo - 3, 0, length + 2, hi + 6, 4, FixtureWorld.Stone);
        world.Fill(-1, lo - 1, 2, length + 1, hi + 5, 2, FixtureWorld.Air);
        world.FlowingRun(0, hi, 2, length, 1, 0, layers: hi - lo + 1);
        return world;
    }

    /// <summary>A wide tank whose whole body of water runs +x, so a crossing is a real cross-flow.</summary>
    private static FixtureWorld CrossFlowTank()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 48, -4, 20, 66, 20, FixtureWorld.Stone);
        world.Fill(-3, 49, -3, 19, 64, 19, FixtureWorld.Air);
        for (int z = 0; z <= 16; z++)
            world.FlowingRun(0, 55, z, 12, 1, 0, layers: 5);

        return world;
    }

    [Fact]
    public void SwimDive_DoesNotHoldSneak()
    {
        FixtureWorld world = StillShaft(40, 80);
        PlanningWorldView view = world.Capture(new BlockPos(-2, 36, -2), new BlockPos(2, 88, 2));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = Center(0, 70, 0),
                End = Center(0, 62, 0),
                MoveType = MoveType.Swim,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        var driver = new ExecutionDriver(ctx, segments, Center(0, 70, 0), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(600);

        int sneakTicks = 0;
        foreach (MovementInput input in driver.EmittedInputs)
            if (input.Sneak)
                sneakTicks++;

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            sneakTicks == 0,
            $"the dive held Sneak on {sneakTicks} of its {driver.EmittedInputs.Count} ticks");
    }

    [Fact]
    public void SwimDive_FromASurfaceFloat_GoesUnder()
    {
        // Water y=40..80 in a 1x1 shaft, so y=80 is the top water cell and the surface is y=81.
        FixtureWorld world = StillShaft(40, 80);
        PlanningWorldView view = world.Capture(new BlockPos(-2, 36, -2), new BlockPos(2, 88, 2));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = Center(0, 80, 0),
                End = Center(0, 79, 0),
                MoveType = MoveType.Swim,
                PlannedTickCost = ActionCosts.SwimOneBlock,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        // Floating: the body's eye (1.62 over the feet) sits above the y=81 water surface.
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(0.5, 79.86, 0.5), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(200);

        Assert.True(
            state == PathExecutorState.Complete,
            $"the surface dive ended {state} after {driver.Trace.Count} ticks at "
            + $"y={driver.State.Position.Y:F4}, having started at y=79.8600");
    }

    [Fact]
    public void SwimUpstreamFromAboveTheLine_CompletesInsteadOfBurningItsBudget()
    {
        FixtureWorld world = FlowingChannel(51, 55, 12);
        PlanningWorldView view = world.Capture(new BlockPos(-2, 47, 0), new BlockPos(14, 62, 4));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = Center(9, 53, 2),
                End = Center(2, 53, 2),
                MoveType = MoveType.Swim,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        var driver = new ExecutionDriver(ctx, segments, new Vec3d(9.5, 54.0, 2.5), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(400);

        double travelled = 9.5 - driver.State.Position.X;
        Assert.True(
            state == PathExecutorState.Complete,
            $"the upstream swim ended {state} after {driver.Trace.Count} ticks, having covered "
            + $"{travelled:F4} of its 7 blocks");
    }

    [Fact]
    public void SwimAcrossACurrent_HoldsTheLine()
    {
        FixtureWorld world = CrossFlowTank();
        PlanningWorldView view = world.Capture(new BlockPos(-4, 46, -4), new BlockPos(20, 68, 20));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = Center(6, 53, 2),
                End = Center(6, 53, 12),
                MoveType = MoveType.Swim,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        var driver = new ExecutionDriver(ctx, segments, Center(6, 53, 2), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(400);

        double worst = 0;
        foreach (TickSample sample in driver.Trace)
        {
            double offset = Math.Abs(sample.Position.X - 6.5);
            if (offset > worst)
                worst = offset;

        }

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(worst < 0.5, $"the crossing wandered {worst:F4} blocks off its line");
    }

    /// <summary>A rising segment aims the pitch STRAIGHT UP, not at its own destination. Looking at a destination one up and one over asks the swim-pose steering blend for 0.707 of the climb rate and buys nothing back, because <c>MoveRelative</c> is yaw-only and pitch never steers the horizontal thrust at all.</summary>
    [Fact]
    public void SwimAscent_AimsUpTheEscapeColumn()
    {
        var world = new FixtureWorld();
        world.Fill(-12, 36, -12, 12, 88, 12, FixtureWorld.Stone);
        world.Fill(-11, 37, -11, 11, 87, 11, FixtureWorld.Air);
        world.Fill(-11, 40, -11, 11, 80, 11, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(-12, 34, -12), new BlockPos(12, 90, 12));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        var segment = new PathSegment
        {
            Start = Center(0, 50, 0),
            End = Center(1, 51, 0),
            MoveType = MoveType.Swim,
            ExitTransition = PathTransitionType.FinalStop,
        };

        var template = new SwimTemplate(ctx, segment);
        var engine = new PlayerPhysics(view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(Center(0, 50, 0), 0f, 0f);
        engine.Step(MovementInput.None);

        float steepest = 0f;
        for (int tick = 0; tick < 12; tick++)
        {
            if (template.Tick(engine.State, out TemplateOutput output) != TemplateState.InProgress)
                break;

            steepest = Math.Min(steepest, output.TargetPitch);
            engine.SetRotation(output.TargetYaw, output.TargetPitch);
            engine.Step(output.Input);
        }

        // Aiming at the destination gives -45 degrees on a one-up-one-over rise; the escape column is -90. The pitch rate limit is 25 degrees a tick, so four ticks are enough to prove which.
        Assert.True(
            steepest < -80f,
            $"the rising segment never aimed steeper than {steepest:F2} degrees, so it was aiming at "
            + "its destination rather than up the escape column");
    }

    /// <summary>GUARD, passes before and after: a twenty-block climb still arrives at better than three ticks a block, which is the rate <c>BreathModel</c>'s whole threshold table is calibrated on.</summary>
    [Fact]
    public void SwimAscent_ClimbsAtBetterThanThreeTicksPerBlock()
    {
        FixtureWorld world = StillShaft(40, 80);
        PlanningWorldView view = world.Capture(new BlockPos(-2, 36, -2), new BlockPos(2, 88, 2));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        var segments = new List<PathSegment>();
        for (int i = 0; i < 20; i++)
            segments.Add(new PathSegment
            {
                Start = Center(0, 50 + i, 0),
                End = Center(0, 51 + i, 0),
                MoveType = MoveType.Swim,
                ExitTransition = i == 19 ? PathTransitionType.FinalStop : PathTransitionType.ContinueStraight,
            });

        var driver = new ExecutionDriver(ctx, segments, Center(0, 50, 0), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(1200);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            driver.Trace.Count < 60,
            $"twenty blocks of ascent took {driver.Trace.Count} ticks, {driver.Trace.Count / 20.0:F3} a block");
    }

    /// <summary>GUARD, passes before and after: the C6/C14 shape, a stone basin two blocks deep whose surface is flush with the platform beside it. The swim leg and the climb-out both still work.</summary>
    [Fact]
    public void SwimExit_ClimbsOutOfABasinFlushWithItsPlatform()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 20, -6, 8, 63);
        world.Fill(0, 61, 0, 2, 63, 2, FixtureWorld.Stone);
        world.Fill(0, 62, 0, 2, 63, 2, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(-6, 58, -6), new BlockPos(20, 70, 8));
        var ctx = new PathExecutionContext(view, Profile, PhysicsConditions.Default);
        IReadOnlyList<PathSegment> segments =
        [
            new PathSegment
            {
                Start = Center(1, 62, 1),
                End = Center(1, 63, 1),
                MoveType = MoveType.Swim,
                ExitTransition = PathTransitionType.ContinueStraight,
            },
            new PathSegment
            {
                Start = Center(1, 63, 1),
                End = Center(4, 64, 1),
                MoveType = MoveType.Swim,
                ExitTransition = PathTransitionType.FinalStop,
            },
        ];

        var driver = new ExecutionDriver(ctx, segments, Center(1, 62, 1), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(600);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(driver.State.Position.Y >= 64.0, $"the climb-out ended at y={driver.State.Position.Y:F4}");
    }
}
