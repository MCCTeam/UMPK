using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class SoftLandingTests
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    /// <summary>A capability snapshot with vitals observed at a given health.</summary>
    private static PathfinderCapabilities Vitals(float health, int food = 20) => new()
    {
        EffectsKnown = false,
        InventoryKnown = false,
        VitalsKnown = true,
        Health = health,
        Food = food,
    };

    private static PathfinderOptions Options => PathfinderOptions.Default;

    /// <summary>Course row N1 "drop12 onto slime", from the row's own build: <c>fill 0 99 1088 5 99 1090 stone</c>, <c>fill 6 87 1088 17 87 1090 stone</c>, <c>fill 6 87 1088 8 87 1090 slime_block</c>, start <c>(1.5, 100.0, 1089.5)</c>, goal <c>(12, 88, 1089)</c> - four cells past the landing lane, so the row measures settle-then-continue rather than survival.</summary>
    private static FixtureWorld N1World() => LandingWorld(FixtureWorld.SlimeBlock);

    /// <summary>Course row N2 "drop12 onto hay": the same build with a hay pad.</summary>
    private static FixtureWorld N2World() => LandingWorld(FixtureWorld.HayBlock);

    private static FixtureWorld LandingWorld(int pad)
    {
        var world = new FixtureWorld();
        world.Fill(0, 99, 1088, 5, 99, 1090, FixtureWorld.Stone);
        world.Fill(6, 87, 1088, 17, 87, 1090, FixtureWorld.Stone);
        world.Fill(6, 87, 1088, 8, 87, 1090, pad);
        return world;
    }

    private static PathResult Plan(FixtureWorld world, BlockPos start, BlockPos goal, PathfinderCapabilities caps)
    {
        PlanningWorldView view = world.Capture(start, goal, margin: 20);
        return PathPlanner.FindPath(view, Options, start, new GoalBlock(goal), capabilities: caps);
    }

    /// <summary>Vanilla's fall-damage arithmetic, at the two heights the course rows use and on the three multipliers the game has.</summary>
    /// <remarks>The rounding is an era boundary and this model takes the DEARER side: <c>ceil</c> up to 1.21.1, which is an upper bound on 1.21.5+'s <c>floor(x + 1e-6)</c>. On the course's own 1.21.11 the twelve-block hay landing really costs 1, not the 2 charged here, and over-charging refuses a route rather than killing a bot.</remarks>
    [Theory]
    [InlineData(12, 0.0, 0)]
    [InlineData(12, 0.2, 2)]
    [InlineData(12, 1.0, 9)]
    [InlineData(6, 0.0, 0)]
    [InlineData(6, 0.2, 1)]
    [InlineData(3, 1.0, 0)]
    [InlineData(4, 1.0, 1)]
    [InlineData(256, 0.0, 0)]
    public void FallDamage_MatchesVanillasArithmetic(int blocks, double multiplier, int expected)
        => Assert.Equal(expected, FallDamageModel.Damage(blocks, multiplier));

    /// <summary>The three multipliers, keyed off the block rather than off a guess.</summary>
    [Theory]
    [InlineData(FixtureWorld.SlimeBlock, 0.0)]
    [InlineData(FixtureWorld.HayBlock, 0.2)]
    [InlineData(FixtureWorld.Stone, 1.0)]
    public void TheMultiplier_ComesFromTheLandingBlock(int stateId, double expected)
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.Equal(expected, FallDamageModel.MultiplierFor(ctx.GetBlock(0, 64, 0)));
    }

    /// <summary>The budget's polarity, which is the one thing that can get a bot killed here. An unobserved session spends nothing.</summary>
    [Fact]
    public void TheBudget_IsZeroWhenTheSessionCannotSeeHealth()
    {
        Assert.Equal(0.0, FallDamageModel.Budget(PathfinderCapabilities.None));
        Assert.Equal(14.0, FallDamageModel.Budget(Vitals(20f)));
        Assert.Equal(1.0, FallDamageModel.Budget(Vitals(7f)));
        Assert.Equal(0.0, FallDamageModel.Budget(Vitals(6f)));
        Assert.Equal(0.0, FallDamageModel.Budget(Vitals(1f)));
    }

    /// <summary>N1: twelve blocks onto slime, and it does not need to know the player's health.</summary>
    [Fact]
    public void N1_PlansOnSlime_WithoutKnowingHealth()
    {
        PathResult result = Plan(
            N1World(), new BlockPos(1, 100, 1089), new BlockPos(12, 88, 1089), PathfinderCapabilities.None);

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((12, 88, 1089), (last.X, last.Y, last.Z));
    }

    /// <summary>N2: twelve blocks onto hay, at full health.</summary>
    [Fact]
    public void N2_PlansOnHay_AtFullHealth()
    {
        PathResult result = Plan(N2World(), new BlockPos(1, 100, 1089), new BlockPos(12, 88, 1089), Vitals(20f));

        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((12, 88, 1089), (last.X, last.Y, last.Z));
    }

    /// <summary>And N2 REFUSES when the budget cannot cover it, which is what makes the hay arm a health decision rather than a licence. Two points of damage against a budget of 1 (health 7, reserve 6).</summary>
    [Fact]
    public void N2_RefusesOnHay_WhenTheHealthBudgetIsTooThin()
    {
        PathResult result = Plan(N2World(), new BlockPos(1, 100, 1089), new BlockPos(12, 88, 1089), Vitals(7f));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The polarity control: the same twelve-block drop onto plain stone is still refused, at full health and with the budget wide open. The arm admits absorbers and nothing else.</summary>
    [Fact]
    public void AnOrdinaryFloor_IsStillRefusedAtTwelveBlocks()
    {
        PathResult result = Plan(
            LandingWorld(FixtureWorld.Stone), new BlockPos(1, 100, 1089), new BlockPos(12, 88, 1089), Vitals(20f));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>C8-slime and C8-hay: the same landings at six blocks, where the drop is an optimisation rather than the only route. Both flip.</summary>
    [Theory]
    [InlineData(FixtureWorld.SlimeBlock)]
    [InlineData(FixtureWorld.HayBlock)]
    public void C8Family_PlansASixBlockDropOntoAnAbsorber(int pad)
    {
        var world = new FixtureWorld();
        world.Fill(448, 99, 128, 453, 99, 130, FixtureWorld.Stone);
        world.Fill(454, 93, 128, 465, 93, 130, FixtureWorld.Stone);
        world.Fill(454, 93, 128, 456, 93, 130, pad);

        PathResult result = Plan(world, new BlockPos(449, 100, 129), new BlockPos(460, 94, 129), Vitals(20f));

        Assert.Equal(PathStatus.Success, result.Status);
    }

    /// <summary>N1's plan, walked by the real engine: the body must SETTLE on the pad and then continue to the goal four cells past it, not complete mid-bounce.</summary>
    /// <remarks>Measured on the raw engine, a twelve-block drop onto slime inverts ten times - peak vertical speeds 1.0928, 0.8580, 0.6997, 0.5519, 0.4604, 0.3946, 0.3084, 0.2378, 0.1537, 0.0809 - and is not stable on the pad until tick 152. With <c>Sneak</c> held it settles at tick 18, the same as the identical drop onto hay. The template holds the sneak while airborne, so the executor takes the second number.</remarks>
    [Theory]
    [InlineData(FixtureWorld.SlimeBlock)]
    [InlineData(FixtureWorld.HayBlock)]
    public void N1AndN2_Execute(int pad)
    {
        FixtureWorld world = LandingWorld(pad);
        var start = new BlockPos(1, 100, 1089);
        var goal = new BlockPos(12, 88, 1089);
        PlanningWorldView view = world.Capture(start, goal, margin: 20);
        PathResult result = PathPlanner.FindPath(view, Options, start, new GoalBlock(goal), capabilities: Vitals(20f));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var exec = new PathExecutionContext(
            view, Profile, PhysicsConditions.Default, allowSprint: true, capabilities: Vitals(20f));
        var driver = new ExecutionDriver(exec, segments, new Vec3d(1.5, 100, 1089.5), -90f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 1200);

        Assert.True(
            state == PathExecutorState.Complete,
            $"the drop onto {pad} ended {state} at {Fmt(driver.State.Position)} after {driver.Trace.Count} ticks");
        Assert.True(
            Math.Abs(driver.State.Position.Y - 88.0) < 0.2,
            $"the body ended at y = {driver.State.Position.Y:F4}, not settled on the pad at 88");
    }

    private static string Fmt(Vec3d v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})");
}
