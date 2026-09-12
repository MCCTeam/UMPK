using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class EffectCoverageTests
{
    private static readonly Identifier FireResistance = Identifier.Minecraft("fire_resistance");
    private static readonly Identifier WaterBreathing = Identifier.Minecraft("water_breathing");

    private static PathfinderCapabilities With(params CapabilityEffect[] effects)
        => new() { EffectsKnown = true, InventoryKnown = false, VitalsKnown = false, Effects = effects };

    private static CapabilityEffect Effect(
        Identifier id, int remaining, bool infinite = false, bool estimated = true)
        => new(id, NetworkId: 12, Amplifier: 0, remaining, infinite, estimated);

    [Fact]
    public void AnInfiniteEffect_CoversAnyRoute()
    {
        PathfinderCapabilities caps = With(
            Effect(FireResistance, CapabilityEffect.InfiniteRemaining, infinite: true));

        Assert.True(EffectCoverage.Covers(caps, FireResistance, 10_000.0));
    }

    [Fact]
    public void AnUnknownDuration_NeverCovers()
    {
        PathfinderCapabilities caps = With(Effect(FireResistance, CapabilityEffect.UnknownRemaining));

        Assert.False(EffectCoverage.Covers(caps, FireResistance, 1.0));
        Assert.False(EffectCoverage.Covers(caps, FireResistance, 0.0));
    }

    [Fact]
    public void AnUnobservableEffectTable_NeverCovers()
    {
        var blind = new PathfinderCapabilities { EffectsKnown = false, InventoryKnown = false, VitalsKnown = false };

        Assert.False(EffectCoverage.Covers(blind, FireResistance, 1.0));
        Assert.False(EffectCoverage.Covers(PathfinderCapabilities.None, FireResistance, 1.0));
    }

    [Fact]
    public void AnEffectThePlayerDoesNotHave_NeverCovers()
    {
        PathfinderCapabilities caps = With(Effect(WaterBreathing, 6000));

        Assert.False(EffectCoverage.Covers(caps, FireResistance, 1.0));
        Assert.True(EffectCoverage.Covers(caps, WaterBreathing, 1.0));
    }

    /// <summary>The headline case: 200 ticks left does not cover a 300-real-tick route. It does not even cover half of one, because the safety factor is on the route and not on the remainder.</summary>
    [Fact]
    public void TwoHundredTicksLeft_DoesNotCoverAThreeHundredTickRoute()
    {
        PathfinderCapabilities caps = With(Effect(FireResistance, 200));

        Assert.False(EffectCoverage.Covers(caps, FireResistance, 300.0));
        Assert.Equal(
            (300.0 * EffectCoverage.SafetyFactor) + BreathModel.ReactionTicks + EffectCoverage.EstimateReserveTicks,
            EffectCoverage.RequiredTicks(300.0, durationIsEstimated: true),
            9);
    }

    /// <summary>The estimate surcharge, at the one route length that separates the two answers. A derived remainder is charged one extra reaction reserve, so a 124-tick route is covered by 200 ticks read off a packet that landed this tick and is NOT covered by the same 200 derived from a stamp.</summary>
    [Fact]
    public void AnEstimatedDuration_IsChargedOneExtraReserve()
    {
        const double Route = 124.0;

        Assert.True(EffectCoverage.Covers(With(Effect(FireResistance, 200, estimated: false)), FireResistance, Route));
        Assert.False(EffectCoverage.Covers(With(Effect(FireResistance, 200, estimated: true)), FireResistance, Route));

        Assert.Equal(
            EffectCoverage.EstimateReserveTicks,
            EffectCoverage.RequiredTicks(Route, durationIsEstimated: true)
                - EffectCoverage.RequiredTicks(Route, durationIsEstimated: false),
            9);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(100.0, true)]
    [InlineData(126.0, true)]
    [InlineData(127.0, false)]
    [InlineData(300.0, false)]
    public void TheBoundaryIsTheRequirement(double routeRealTicks, bool expected)
    {
        PathfinderCapabilities caps = With(Effect(FireResistance, 200, estimated: false));
        Assert.Equal(expected, EffectCoverage.Covers(caps, FireResistance, routeRealTicks));
    }

    /// <summary>On a dry route the real-tick model IS the planner's charge, so the sum is the plan's own cost. This is the M1/M3 shape: a fire corridor has no water in it anywhere.</summary>
    [Fact]
    public void ADryRoute_CostsWhatThePlannerCharged()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 20, -4, 4, 63);
        PlanningWorldView view = world.Capture(new BlockPos(0, 64, 0), new BlockPos(10, 64, 0));
        IReadOnlyList<PathSegment> segments = Straight(10);

        double real = BreathValidator.RouteRealTicks(segments, view, PhysicsProfile.ForProtocol(770), allowSprint: true);

        Assert.Equal(10 * ActionCosts.SprintOneBlock, real, 6);
    }

    /// <summary>A submerged route is priced at the medium's measured rate, not the charge. The whole point of comparing a duration against REAL ticks is that these two numbers differ by a factor of two.</summary>
    [Fact]
    public void AWetRoute_CostsTheMediumsRateAndNotTheCharge()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 20, -4, 4, 63);
        world.Fill(-4, 64, -4, 20, 66, 4, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(0, 64, 0), new BlockPos(10, 64, 0));
        IReadOnlyList<PathSegment> segments = Straight(10);

        double real = BreathValidator.RouteRealTicks(segments, view, PhysicsProfile.ForProtocol(770), allowSprint: true);
        double charged = 10 * ActionCosts.SprintOneBlock;

        Assert.True(
            real > charged * 1.5,
            $"a submerged walk is measured at {BreathValidator.SprintWadeBlocksPerTick} blocks a tick, so ten "
                + $"blocks of it must cost far more than the {charged:F3} the planner charged; got {real:F3}");
        Assert.Equal(BreathValidator.WadeTicks(10, PhysicsProfile.ForProtocol(770), allowSprint: true), real, 6);
    }

    private static IReadOnlyList<PathSegment> Straight(int blocks)
    {
        var segments = new List<PathSegment>(blocks);
        for (int i = 0; i < blocks; i++)
            segments.Add(new PathSegment
            {
                Start = new Vec3d(i + 0.5, 64, 0.5),
                End = new Vec3d(i + 1.5, 64, 0.5),
                MoveType = MoveType.Traverse,
                PlannedTickCost = ActionCosts.SprintOneBlock,
            });

        return segments;
    }
}
