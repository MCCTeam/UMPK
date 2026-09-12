using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class FireResistancePlanningTests
{
    private const int FloorY = 99;
    private const int BodyY = 100;
    private const int CourseMargin = 24;

    private static readonly Identifier FireResistance = Identifier.Minecraft("fire_resistance");

    private static readonly BlockPos LaneStart = new(0, BodyY, 1);
    private static readonly BlockPos LaneGoal = new(13, BodyY, 1);

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    /// <summary>M1's plot: fire at x 4-5, campfires at x 7-8, magma floor at x 10-11.</summary>
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

    /// <summary>The polarity control: the same corridor with cactus where the fire was.</summary>
    private static FixtureWorld CactusCorridor()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 13, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 13, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 13, BodyY + 1, 2, FixtureWorld.Stone);
        world.Fill(4, BodyY, 1, 5, BodyY, 1, FixtureWorld.Cactus);
        return world;
    }

    private static PathResult Plan(FixtureWorld world, bool fireHazardsCleared)
    {
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);
        return PathPlanner.FindPath(
            view,
            PathfinderOptions.Default,
            LaneStart,
            new GoalBlock(LaneGoal),
            timeProvider: null,
            ct: default,
            capabilities: null,
            fireHazardsCleared: fireHazardsCleared);
    }

    private static CalculationContext Context(FixtureWorld world, bool fireHazardsCleared)
        => new(
            world.Capture(LaneStart, LaneGoal, CourseMargin),
            PathfinderOptions.Default,
            capabilities: null,
            fireHazardsCleared: fireHazardsCleared);

    [Theory]
    [InlineData(FixtureWorld.Lava, true)]
    [InlineData(FixtureWorld.Fire, true)]
    [InlineData(FixtureWorld.SoulFire, true)]
    [InlineData(FixtureWorld.MagmaBlock, true)]
    [InlineData(FixtureWorld.Campfire, true)]
    [InlineData(FixtureWorld.Cactus, false)]
    [InlineData(FixtureWorld.PowderSnow, false)]
    public void FireResistanceClearsExactlyTheFireDamageHazards(int stateId, bool clearedByFireResistance)
    {
        var world = new FixtureWorld();
        world.Set(6, BodyY, 1, stateId);

        Assert.True(
            MoveHelper.IsHazardAt(Context(world, fireHazardsCleared: false), 6, BodyY, 1),
            "every state in this theory is a curated hazard without the effect");
        Assert.Equal(
            !clearedByFireResistance,
            MoveHelper.IsHazardAt(Context(world, fireHazardsCleared: true), 6, BodyY, 1));
    }

    /// <summary><c>IsOpenGap</c> is <c>!CanWalkOn &amp;&amp; !IsHazardAt</c> and the two must agree, in both modes. The last time they disagreed, course rows F1, F4, F7 and F10 all sprint-jumped across the hazard they exist to route around, because <c>!CanWalkOn</c> alone read a magma block as "there is nothing here".</summary>
    [Theory]
    [InlineData(FixtureWorld.MagmaBlock)]
    [InlineData(FixtureWorld.Campfire)]
    [InlineData(FixtureWorld.Lava)]
    [InlineData(FixtureWorld.Cactus)]
    public void IsOpenGapAgreesWithIsHazardAt_InBothModes(int stateId)
    {
        var world = new FixtureWorld();
        world.Set(6, FloorY, 1, stateId);

        foreach (bool cleared in new[] { false, true })
        {
            CalculationContext ctx = Context(world, cleared);
            Assert.Equal(
                !MoveHelper.CanWalkOn(ctx, 6, FloorY, 1) && !MoveHelper.IsHazardAt(ctx, 6, FloorY, 1),
                MoveHelper.IsOpenGap(ctx, 6, FloorY, 1));
        }
    }

    [Fact]
    public void LavaStopsBeingAHazard_AndStaysImpassable()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 13, FloorY, 2, FixtureWorld.Stone);
        world.Set(6, BodyY, 1, FixtureWorld.Lava);
        CalculationContext ctx = Context(world, fireHazardsCleared: true);

        Assert.False(MoveHelper.IsHazardAt(ctx, 6, BodyY, 1));
        Assert.False(MoveHelper.CanWalkThrough(ctx, 6, BodyY, 1));
        Assert.False(MoveHelper.CanWalkOn(ctx, 6, BodyY, 1));
    }

    [Fact]
    public void ACampfireBecomesStandableOnceTheHazardClears()
    {
        var world = new FixtureWorld();
        world.Set(6, BodyY, 1, FixtureWorld.Campfire);

        Assert.False(MoveHelper.CanWalkOn(Context(world, fireHazardsCleared: false), 6, BodyY, 1));
        Assert.True(MoveHelper.CanWalkOn(Context(world, fireHazardsCleared: true), 6, BodyY, 1));
    }

    [Fact]
    public void TheM1Corridor_RefusesWithoutTheEffect()
    {
        Assert.NotEqual(PathStatus.Success, Plan(FireCorridor(), fireHazardsCleared: false).Status);
    }

    [Fact]
    public void TheM1Corridor_PlansWithTheEffect()
    {
        PathResult result = Plan(FireCorridor(), fireHazardsCleared: true);

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(LaneGoal.X, result.Path[^1].X);

        // The route crosses every strip: there is no gap to thread in a 1-wide lane.
        Assert.Contains(result.Path, n => n.X is 4 or 5);
        Assert.Contains(result.Path, n => n.X is 7 or 8);
        Assert.Contains(result.Path, n => n.X is 10 or 11);

        // The campfires are floors, so the route stands ON them one cell up.
        Assert.Contains(result.Path, n => n.X is 7 or 8 && n.Y == BodyY + 1);
    }

    /// <summary>The polarity: cactus is not fire damage, so the same corridor shape refuses in BOTH modes.</summary>
    [Fact]
    public void TheCactusCorridor_RefusesInBothModes()
    {
        Assert.NotEqual(PathStatus.Success, Plan(CactusCorridor(), fireHazardsCleared: false).Status);
        Assert.NotEqual(PathStatus.Success, Plan(CactusCorridor(), fireHazardsCleared: true).Status);
    }

    /// <summary>M3: M1's corridor with a two-second grant. The route is planned once with the hazards cleared, measured in REAL ticks, and the duration re-checked against it -- the two-pass shape, run here by hand because the planner does not yet run it for itself. A 120-second grant covers the route and a 2-second one does not, and the second answer sends the plan back through with the hazards intact, where it refuses.</summary>
    [Theory]
    [InlineData(2400, true)]
    [InlineData(40, false)]
    public void TheDurationGateIsArithmetic(int remainingTicks, bool expectedCovered)
    {
        FixtureWorld world = FireCorridor();
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, CourseMargin);

        PathResult cleared = Plan(world, fireHazardsCleared: true);
        Assert.Equal(PathStatus.Success, cleared.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(cleared.Path, view);
        double routeRealTicks = BreathValidator.RouteRealTicks(segments, view, Profile, allowSprint: true);

        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = true,
            InventoryKnown = false,
            VitalsKnown = false,
            Effects = [new CapabilityEffect(
                FireResistance, NetworkId: 12, Amplifier: 0, remainingTicks, IsInfinite: false, DurationIsEstimated: true)],
        };

        bool covered = EffectCoverage.Covers(capabilities, FireResistance, routeRealTicks);
        Assert.Equal(expectedCovered, covered);

        // What the caller does with the answer: a route the potion does not cover is re-planned with the hazards intact, and this corridor has no such route.
        if (!covered)
            Assert.NotEqual(PathStatus.Success, Plan(world, fireHazardsCleared: false).Status);

    }

    /// <summary>The default is unchanged: nobody who does not ask for the clearance gets it, so every existing planner call and every hazard row prices exactly as it did.</summary>
    [Fact]
    public void TheClearanceIsOffByDefault()
    {
        PlanningWorldView view = FireCorridor().Capture(LaneStart, LaneGoal, CourseMargin);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.False(ctx.FireHazardsCleared);
        Assert.True(MoveHelper.IsHazardAt(ctx, 4, BodyY, 1));
        Assert.NotEqual(
            PathStatus.Success,
            PathPlanner.FindPath(view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal)).Status);
    }
}
