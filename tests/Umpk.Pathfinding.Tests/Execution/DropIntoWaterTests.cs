using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class DropIntoWaterTests(ITestOutputHelper output)
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    private static FixtureWorld C6cWorld()
    {
        var world = new FixtureWorld();
        world.Fill(320, 99, 152, 325, 99, 156, FixtureWorld.Stone);
        world.Fill(326, 81, 152, 337, 81, 156, FixtureWorld.Stone);
        world.Fill(325, 78, 152, 329, 80, 156, FixtureWorld.Stone);
        world.Fill(326, 79, 153, 328, 79, 155, FixtureWorld.Stone);
        world.Fill(326, 80, 153, 328, 81, 155, FixtureWorld.Water);
        return world;
    }

    /// <summary>The same shape 25 blocks deep: C7's own plot, upper y=99 and lower y=74.</summary>
    private static FixtureWorld C7World()
    {
        var world = new FixtureWorld();
        world.Fill(384, 99, 128, 389, 99, 132, FixtureWorld.Stone);
        world.Fill(390, 74, 128, 401, 74, 132, FixtureWorld.Stone);
        world.Fill(389, 71, 128, 393, 73, 132, FixtureWorld.Stone);
        world.Fill(390, 72, 129, 392, 72, 131, FixtureWorld.Stone);
        world.Fill(390, 73, 129, 392, 74, 131, FixtureWorld.Water);
        return world;
    }

    /// <summary>The C6 plot's SECOND basin, twelve blocks down: the row that stayed green.</summary>
    private static FixtureWorld C6bWorld()
    {
        var world = new FixtureWorld();
        world.Fill(320, 99, 140, 325, 99, 144, FixtureWorld.Stone);
        world.Fill(326, 87, 140, 337, 87, 144, FixtureWorld.Stone);
        world.Fill(325, 84, 140, 329, 86, 144, FixtureWorld.Stone);
        world.Fill(326, 85, 141, 328, 85, 143, FixtureWorld.Stone);
        world.Fill(326, 86, 141, 328, 87, 143, FixtureWorld.Water);
        return world;
    }

    private static PathfinderCapabilities Vitals(float health) => new()
    {
        EffectsKnown = true,
        InventoryKnown = true,
        VitalsKnown = true,
        Health = health,
    };

    /// <summary>C6c, the row's own numbers: eighteen blocks into the basin, out the far side, and on to the pad.</summary>
    [Fact]
    public void C6c_DropsIntoTheBasin_AndReachesThePad()
        => AssertDropLandsInTheBasin(
            C6cWorld(),
            PathfinderOptions.Default,
            Vitals(11f),
            start: new BlockPos(321, 100, 154),
            goal: new BlockPos(332, 82, 154),
            spawn: new Vec3d(321.5, 100.0, 154.5),
            surfaceY: 82.0,
            basinMinX: 326,
            basinMinZ: 153);

    /// <summary>C6b, twelve blocks down, which was green and has to stay that way.</summary>
    [Fact]
    public void C6b_StillDropsIntoItsBasin()
        => AssertDropLandsInTheBasin(
            C6bWorld(),
            PathfinderOptions.Default,
            Vitals(20f),
            start: new BlockPos(321, 100, 142),
            goal: new BlockPos(332, 88, 142),
            spawn: new Vec3d(321.5, 100.0, 142.5),
            surfaceY: 88.0,
            basinMinX: 326,
            basinMinZ: 141);

    /// <summary>C7, twenty-five blocks down, which needs <see cref="PathfinderOptions.UnsafeFalls"/> to be planned at all - the default descend scan stops at three - and must still land in the water.</summary>
    [Fact]
    public void C7_DropsIntoTheBasin_UnderUnsafeFalls()
        => AssertDropLandsInTheBasin(
            C7World(),
            PathfinderOptions.UnsafeFalls,
            Vitals(20f),
            start: new BlockPos(385, 100, 130),
            goal: new BlockPos(396, 75, 130),
            spawn: new Vec3d(385.5, 100.0, 130.5),
            surfaceY: 75.0,
            basinMinX: 390,
            basinMinZ: 129);

    /// <summary>The gate itself, stated independently of the rows: a landing costs hearts, a body has only so many, and a policy flag can raise a height limit but not a health bar.</summary>
    /// <remarks>The expected values are vanilla's arithmetic written out rather than read back from the model: <c>ceil((blocks - 3) * 1.0)</c> hearts on stone against <c>Health - 6</c> spendable.</remarks>
    [Theory]
    [InlineData(25, 20f, false)]   // 22 hearts against 14 spendable - C7's stone lip
    [InlineData(18, 11f, false)]   // 15 against 5 - C6c's, if it ever reached stone
    [InlineData(6, 20f, true)]     // 3 against 14 - what -f is asked for on rows C4 and C5
    [InlineData(4, 20f, true)]     // 1 against 14
    [InlineData(25, 20f, true, false)]  // vitals unknown: nothing is asserted, so nothing is refused
    public void ALandingCostsHearts_AndOnlyAnObservedBarCanRefuseOne(
        int blocks, float health, bool affordable, bool vitalsKnown = true)
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, FixtureWorld.Stone);
        PlanningWorldView view = world.Capture(new BlockPos(0, 64, 0), new BlockPos(0, 64, 0), margin: 4);
        PathfinderCapabilities caps = vitalsKnown ? Vitals(health) : PathfinderCapabilities.None;
        var ctx = new CalculationContext(view, PathfinderOptions.UnsafeFalls, capabilities: caps);

        Assert.Equal(affordable, ctx.LandingIsAffordable(ctx.GetBlock(0, 64, 0), blocks));
    }

    [Fact]
    public void TheApproachIntoADrop_IsNotAJumpRunUp()
    {
        FixtureWorld world = C6cWorld();
        var start = new BlockPos(321, 100, 154);
        var goal = new BlockPos(332, 82, 154);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(
            view, PathfinderOptions.Default, start, new GoalBlock(goal), capabilities: Vitals(11f));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        int drop = IndexOfFirst(segments, MoveType.Descend);
        Assert.True(drop > 0, "the plan has no descend to approach");

        PathSegment approach = segments[drop - 1];
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"approach {approach.MoveType} exit={approach.ExitTransition} jumpReady={approach.ExitHints.RequireJumpReady} "
            + $"stable={approach.ExitHints.RequireStableFooting} minExit={approach.ExitHints.MinExitSpeed}"));

        Assert.False(approach.ExitHints.RequireJumpReady);
        Assert.True(approach.ExitHints.RequireStableFooting);
        Assert.Equal(0.0, approach.ExitHints.MinExitSpeed);
    }

    private static int IndexOfFirst(IReadOnlyList<PathSegment> segments, MoveType type)
    {
        for (int i = 0; i < segments.Count; i++)
            if (segments[i].MoveType == type)
                return i;

        return -1;
    }

    /// <summary>Plans the row, runs it against the real engine, and asserts the body was inside the basin's 3x3 column the first time it reached the water's surface height - not on the lip beside it.</summary>
    private void AssertDropLandsInTheBasin(
        FixtureWorld world,
        PathfinderOptions options,
        PathfinderCapabilities capabilities,
        BlockPos start,
        BlockPos goal,
        Vec3d spawn,
        double surfaceY,
        int basinMinX,
        int basinMinZ)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal), capabilities: capabilities);
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, view, options);
        var exec = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(exec, segments, spawn, -90f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 1500);

        Vec3d entry = default;
        bool found = false;
        foreach (TickSample sample in driver.Trace)
            if (sample.Position.Y <= surfaceY)
            {
                entry = sample.Position;
                found = true;
                break;
            }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ended {state} at ({driver.State.Position.X:F4}, {driver.State.Position.Y:F4}, "
            + $"{driver.State.Position.Z:F4}) after {driver.Trace.Count} ticks; crossed y={surfaceY:F0} at "
            + $"({entry.X:F4}, {entry.Y:F4}, {entry.Z:F4})"));

        Assert.True(found, "the body never reached the basin's surface height");
        Assert.True(
            entry.X >= basinMinX && entry.X < basinMinX + 3 && entry.Z >= basinMinZ && entry.Z < basinMinZ + 3,
            $"the body crossed the water line at ({entry.X:F4}, {entry.Z:F4}), outside the basin column "
            + $"x {basinMinX}-{basinMinX + 2}, z {basinMinZ}-{basinMinZ + 2}");
        Assert.Equal(PathExecutorState.Complete, state);
        Assert.InRange(driver.State.Position.X, goal.X, goal.X + 1.0);
    }
}
