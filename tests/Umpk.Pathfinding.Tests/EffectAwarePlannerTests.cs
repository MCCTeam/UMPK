using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class EffectAwarePlannerTests
{
    private const int FloorY = 99;
    private const int BodyY = 100;
    private const int CourseMargin = 24;

    private static readonly Identifier FireResistance = Identifier.Minecraft("fire_resistance");
    private static readonly Identifier WaterBreathing = Identifier.Minecraft("water_breathing");
    private static readonly Identifier ConduitPower = Identifier.Minecraft("conduit_power");

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public EffectAwarePlannerTests(ITestOutputHelper output) => _output = output;

    /// <summary>Course row M1's plot: fire at x 4-5, campfires at x 7-8, magma floor at x 10-11.</summary>
    private static FixtureWorld FireCorridor()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 13, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 13, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 13, BodyY + 1, 2, FixtureWorld.Stone);
        world.Fill(4, BodyY, 1, 5, BodyY, 1, FixtureWorld.Fire);
        world.Fill(7, BodyY, 1, 8, BodyY, 1, FixtureWorld.Campfire);
        world.Fill(10, FloorY, 1, 11, FloorY, 1, FixtureWorld.MagmaBlock);
        return world;
    }

    private static FixtureWorld BareCorridor()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 13, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 13, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 13, BodyY + 1, 2, FixtureWorld.Stone);
        return world;
    }

    private static readonly BlockPos LaneStart = new(0, BodyY, 1);
    private static readonly BlockPos LaneGoal = new(13, BodyY, 1);

    /// <summary>How many cells of sealed bore the M5 replica carries; one lung is 300 ticks.</summary>
    private const int BoreLength = 90;

    private static readonly BlockPos BoreStart = new(0, BodyY, 1);
    private static readonly BlockPos BoreGoal = new(BoreLength + 2, BodyY, 1);

    private static FixtureWorld SealedBore()
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY - 1, -2, BoreLength + 4, BodyY + 4, 3, FixtureWorld.Stone);
        for (int x = 0; x <= BoreLength + 2; x++)
        {
            world.Set(x, BodyY, 1, FixtureWorld.Water);
            world.Set(x, BodyY + 1, 1, FixtureWorld.Water);
        }

        // The dry mouths: the first and last two cells of the lane are drained, so the route starts and ends breathing and the whole of the middle is the crossing.
        for (int x = 0; x <= 1; x++)
        {
            world.Set(x, BodyY, 1, FixtureWorld.Air);
            world.Set(x, BodyY + 1, 1, FixtureWorld.Air);
        }

        for (int x = BoreLength + 1; x <= BoreLength + 2; x++)
        {
            world.Set(x, BodyY, 1, FixtureWorld.Air);
            world.Set(x, BodyY + 1, 1, FixtureWorld.Air);
        }

        return world;
    }

    private static PathfinderCapabilities Holding(
        Identifier id, int remainingTicks, bool infinite = false, bool estimated = true, bool known = true)
        => new()
        {
            EffectsKnown = known,
            InventoryKnown = false,
            VitalsKnown = false,
            Effects = known
                ? [new CapabilityEffect(id, NetworkId: 12, Amplifier: 0, remainingTicks, infinite, estimated)]
                : [],
        };

    private static PathfinderCapabilities Nothing => new() { EffectsKnown = true, InventoryKnown = false, VitalsKnown = false };

    private static EffectAwarePlan Plan(
        FixtureWorld world, BlockPos start, BlockPos goal, PathfinderCapabilities? capabilities)
        => EffectAwarePlanner.Plan(
            world.Capture(start, goal, CourseMargin),
            PathfinderOptions.Default,
            start,
            new GoalBlock(goal),
            capabilities,
            Profile);

    /// <summary>M1's corridor with a 120-second grant: one search, the hazards cleared, and the plan says out loud that it leans on the potion.</summary>
    [Fact]
    public void TheM1Corridor_WithAnAmpleGrant_PlansThroughTheFireInOnePass()
    {
        EffectAwarePlan plan = Plan(FireCorridor(), LaneStart, LaneGoal, Holding(FireResistance, 2400));

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.True(plan.FireHazardsCleared);
        Assert.Contains(FireResistance, plan.DependsOnEffects);
        Assert.Equal(1, plan.Searches);
    }

    /// <summary>M3: the same corridor with a two-second grant. The first pass finds the route, the coverage check prices it in real ticks and refuses, and the second pass with hazards intact has nowhere to go. The result must be a refusal rather than a route the potion cannot cover.</summary>
    [Fact]
    public void TheM1Corridor_WithATwoSecondGrant_RefusesOnTheSecondPass()
    {
        EffectAwarePlan plan = Plan(FireCorridor(), LaneStart, LaneGoal, Holding(FireResistance, 40));

        _output.WriteLine($"status={plan.Result.Status} searches={plan.Searches} cleared={plan.FireHazardsCleared}");
        Assert.NotEqual(PathStatus.Success, plan.Result.Status);
        Assert.False(plan.FireHazardsCleared);
        Assert.Empty(plan.DependsOnEffects);
        Assert.Equal(2, plan.Searches);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unobservable")]
    [InlineData("absent")]
    public void TheFireArmIsRefusedOnDoubt_BeforeASearchIsSpent(string doubt)
    {
        PathfinderCapabilities capabilities = doubt switch
        {
            "unknown" => Holding(FireResistance, CapabilityEffect.UnknownRemaining),
            "unobservable" => Holding(FireResistance, 2400, known: false),
            _ => Nothing,
        };

        EffectAwarePlan plan = Plan(FireCorridor(), LaneStart, LaneGoal, capabilities);

        Assert.NotEqual(PathStatus.Success, plan.Result.Status);
        Assert.False(plan.FireHazardsCleared);
        Assert.Equal(1, plan.Searches);
    }

    /// <summary>An infinite grant covers any route, so it never needs a duration and never needs a second search.</summary>
    [Fact]
    public void AnInfiniteGrant_CoversTheCorridor()
    {
        EffectAwarePlan plan = Plan(
            FireCorridor(),
            LaneStart,
            LaneGoal,
            Holding(FireResistance, CapabilityEffect.InfiniteRemaining, infinite: true, estimated: false));

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.True(plan.FireHazardsCleared);
        Assert.Equal(1, plan.Searches);
    }

    /// <summary>A potion the route does not USE is not a dependency, and does not buy a second search either. The clearance is a relaxation, so a route that touches none of the cleared cells is the same route the strict world would have produced - and a plan that claimed to depend on the potion would replan every time one expired for no reason.</summary>
    [Fact]
    public void AGrantTheRouteNeverUses_IsNotADependency()
    {
        EffectAwarePlan plan = Plan(BareCorridor(), LaneStart, LaneGoal, Holding(FireResistance, 40));

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.False(plan.FireHazardsCleared);
        Assert.Empty(plan.DependsOnEffects);

        // A two-second grant against a fourteen-block route: the coverage check would have refused it, so a second search here would be the whole cost of the feature charged to every dry plan.
        Assert.Equal(1, plan.Searches);
    }

    /// <summary>The dry default uses one search, depends on no effects, and matches the ordinary planner.</summary>
    [Fact]
    public void ADryPlanWithNoCapabilities_CostsExactlyOneSearch()
    {
        EffectAwarePlan plan = Plan(BareCorridor(), LaneStart, LaneGoal, capabilities: null);

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.Equal(1, plan.Searches);
        Assert.False(plan.FireHazardsCleared);
        Assert.False(plan.BreathSuspended);
        Assert.Empty(plan.DependsOnEffects);

        PathResult shipped = PathPlanner.FindPath(
            BareCorridor().Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal));
        Assert.Equal(shipped.Path.Count, plan.Result.Path.Count);
    }

    /// <summary>Course row E3's own refusal, offline: ninety cells of sealed bore is more than one lung, and the breath dimension refuses it at the search.</summary>
    [Fact]
    public void TheSealedBore_RefusesOnAPlainLung()
    {
        EffectAwarePlan plan = Plan(SealedBore(), BoreStart, BoreGoal, Nothing);

        Assert.NotEqual(PathStatus.Success, plan.Result.Status);
        Assert.False(plan.BreathSuspended);
    }

    /// <summary>Course row M5: the same bore with water breathing held. The suspension collapses the node key back to the dry-terrain shape, the crossing plans, and the plan records the effect it leans on.</summary>
    [Fact]
    public void TheSealedBore_WithWaterBreathing_PlansUnderTheSuspension()
    {
        EffectAwarePlan plan = Plan(SealedBore(), BoreStart, BoreGoal, Holding(WaterBreathing, 12000));

        _output.WriteLine($"status={plan.Result.Status} segments={plan.Segments.Count} searches={plan.Searches}");
        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.True(plan.BreathSuspended);
        Assert.Contains(WaterBreathing, plan.DependsOnEffects);
        Assert.Equal(1, plan.Searches);
    }

    [Fact]
    public void ConduitPower_SuspendsTheBreathDimensionToo()
    {
        EffectAwarePlan plan = Plan(SealedBore(), BoreStart, BoreGoal, Holding(ConduitPower, 12000));

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.True(plan.BreathSuspended);
        Assert.Contains(ConduitPower, plan.DependsOnEffects);
        Assert.DoesNotContain(WaterBreathing, plan.DependsOnEffects);
    }

    /// <summary>The duration gate on the water arm, which is the half a boolean cannot express: a five-second grant does not cover ninety blocks of bottom-walk, so the suspension is revoked and the second pass - breath-aware again - refuses exactly as row E3 does.</summary>
    [Fact]
    public void AWaterBreathingGrantTooShortForTheBore_FallsBackToTheBreathAwareSearch()
    {
        EffectAwarePlan plan = Plan(SealedBore(), BoreStart, BoreGoal, Holding(WaterBreathing, 100));

        _output.WriteLine($"status={plan.Result.Status} searches={plan.Searches} suspended={plan.BreathSuspended}");
        Assert.False(plan.BreathSuspended);
        Assert.Empty(plan.DependsOnEffects);
        Assert.Equal(2, plan.Searches);
        Assert.NotEqual(PathStatus.Success, plan.Result.Status);
    }

    /// <summary>The polarity guard on the water arm, stated where it is cheapest to read: an unknown remainder never suspends, so a potion the client cannot time is a potion the planner does not spend.</summary>
    [Fact]
    public void AWaterBreathingGrantWithAnUnknownRemainder_NeverSuspends()
    {
        EffectAwarePlan plan = Plan(
            SealedBore(), BoreStart, BoreGoal, Holding(WaterBreathing, CapabilityEffect.UnknownRemaining));

        Assert.False(plan.BreathSuspended);
        Assert.Equal(1, plan.Searches);
        Assert.NotEqual(PathStatus.Success, plan.Result.Status);
    }

    /// <summary>Water breathing held over a dry route is not a dependency because it does not alter the plan; revoking it must not trigger a replan.</summary>
    [Fact]
    public void WaterBreathingOnADryRoute_IsNotADependency()
    {
        EffectAwarePlan plan = Plan(BareCorridor(), LaneStart, LaneGoal, Holding(WaterBreathing, 12000));

        Assert.Equal(PathStatus.Success, plan.Result.Status);
        Assert.False(plan.BreathSuspended);
        Assert.Empty(plan.DependsOnEffects);
        Assert.Equal(1, plan.Searches);
    }
}
