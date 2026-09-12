using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SegmentBudgetPolicyTests
{
    private static readonly PhysicsProfile Modern = PhysicsProfile.ForProtocol(772);
    private static readonly PhysicsProfile Legacy = PhysicsProfile.ForProtocol(340);

    private static Vec3d Center(int x, int y, int z) => new(x + 0.5, y, z + 0.5);

    /// <summary>Dry stone floor at y=63, open air above.</summary>
    private static PathExecutionContext DryContext(bool allowSprint = true, PhysicsProfile? profile = null)
    {
        var world = new FixtureWorld();
        world.Fill(-8, 60, -8, 24, 63, 8, FixtureWorld.Stone);
        PlanningWorldView view = world.Capture(new BlockPos(-8, 58, -8), new BlockPos(24, 76, 8));
        return new PathExecutionContext(view, profile ?? Modern, PhysicsConditions.Default, allowSprint);
    }

    /// <summary>The same floor, flooded two cells deep: a submerged bottom-walk lane, the E15/E16 shape.</summary>
    private static PathExecutionContext FloodedContext(bool allowSprint = true, PhysicsProfile? profile = null)
    {
        var world = new FixtureWorld();
        world.Fill(-8, 60, -8, 24, 70, 8, FixtureWorld.Stone);
        world.Fill(-7, 64, -7, 23, 69, 7, FixtureWorld.Air);
        world.Fill(-7, 64, -7, 23, 66, 7, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(-8, 58, -8), new BlockPos(24, 76, 8));
        return new PathExecutionContext(view, profile ?? Modern, PhysicsConditions.Default, allowSprint);
    }

    private static PathSegment Walk(int y = 64) => new()
    {
        Start = Center(0, y, 0),
        End = Center(1, y, 0),
        MoveType = MoveType.Traverse,
        PlannedTickCost = ActionCosts.SprintOneBlock,
        ExitTransition = PathTransitionType.ContinueStraight,
    };

    private static PathSegment Swim(int y = 64) => new()
    {
        Start = Center(0, y, 0),
        End = Center(1, y, 0),
        MoveType = MoveType.Swim,
        PlannedTickCost = ActionCosts.SwimOneBlock,
        ExitTransition = PathTransitionType.ContinueStraight,
    };

    /// <summary>The same one-block walk, dry and flooded, is not the same move. The plan cannot tell them apart - both are a <c>Traverse</c> charged <c>SprintOneBlock</c> - so only the world can.</summary>
    [Fact]
    public void ASubmergedWalk_GetsMoreBudgetThanADryOne()
    {
        int dry = DryContext().Budget.BudgetFor(Walk());
        int flooded = FloodedContext().Budget.BudgetFor(Walk());

        Assert.True(
            flooded > dry * 1.3,
            $"a flooded bottom-walk got {flooded} ticks against a dry one's {dry}");
    }

    /// <summary>A flooded walk's budget must clear the worst LEGITIMATE wade, which is a dead-upstream no-sprint 0.028 blocks a tick, i.e. 35.7 ticks a block. That is the E6/E9 live shape: 22.2 and 33.6 ticks a block measured on the course.</summary>
    [Fact]
    public void ASubmergedWalkBudget_ClearsADeadUpstreamWade()
    {
        int flooded = FloodedContext().Budget.BudgetFor(Walk());
        const double upstreamTicksPerBlock = 1.0 / 0.028;

        Assert.True(
            flooded > upstreamTicksPerBlock * 1.4,
            $"a flooded bottom-walk got {flooded} ticks against the {upstreamTicksPerBlock:F1} a dead "
            + "upstream wade of one block really takes");
    }

    /// <summary>The same floor with ONE cell of water on it: the body's feet are wet and its head is not. This is what a sheet spilling across a walking surface makes, and it is course row E10's shell top.</summary>
    private static PathExecutionContext ShallowContext(bool allowSprint = true, PhysicsProfile? profile = null)
    {
        var world = new FixtureWorld();
        world.Fill(-8, 60, -8, 24, 70, 8, FixtureWorld.Stone);
        world.Fill(-7, 64, -7, 23, 69, 7, FixtureWorld.Air);
        world.Fill(-7, 64, -7, 23, 64, 7, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(-8, 58, -8), new BlockPos(24, 76, 8));
        return new PathExecutionContext(view, profile ?? Modern, PhysicsConditions.Default, allowSprint);
    }

    [Fact]
    public void AShallowWadeBudget_ClearsADeadUpstreamWade()
    {
        int shallow = ShallowContext().Budget.BudgetFor(Walk());
        const double upstreamTicksPerBlock = 1.0 / 0.028;

        Assert.True(
            shallow > upstreamTicksPerBlock * 1.4,
            $"a one-deep wade got {shallow} ticks against the {upstreamTicksPerBlock:F1} a dead upstream "
            + "wade of one block really takes");
    }

    /// <summary>GUARD, and the reason the medium test reads the world rather than the move type: dry terrain must be bit-identical. Nothing on the 99 non-water rows of the course may move.</summary>
    [Fact]
    public void ADryWalkBudget_IsUnchangedByTheMediumTest()
    {
        // ceil(LandSlack * SprintOneBlock) + FixedOverheadTicks = ceil(6.026) + 20 = 27, floored at 40.
        Assert.Equal(SegmentBudgetPolicy.MinBudgetTicks, DryContext().Budget.BudgetFor(Walk()));

        // ceil(1.691 * 28.5103) + 20 = ceil(48.211) + 20 = 49 + 20 = 69, clear of both bounds, so this row reads the slack itself rather than a clamp.
        Assert.Equal(69, DryContext().Budget.BudgetFor(LongDryWalk()));
    }

    [Fact]
    public void LandSlack_IsTheMeasuredSlowFloorRatio_NotOneOverTheSpeedFactor()
    {
        Assert.Equal(ActionCosts.MeasuredSlowFloorCostMultiplier, SegmentBudgetPolicy.LandSlack);
        Assert.Equal(1.691, SegmentBudgetPolicy.LandSlack, 6);
        Assert.NotEqual(1.0 / ActionCosts.MeasuredSlowFloorSpeedFactor, SegmentBudgetPolicy.LandSlack, 3);
    }

    /// <summary>What is left for the slack to cover once the CHARGE carries the slowdown: the SOURCE floor.</summary>
    /// <remarks>A ground move is charged at the DESTINATION floor's factor, so a move onto soul sand is charged 1.691x and executed 1.691x and the ratio is one. What the charge cannot see is the cell the body leaves: <c>ApplyBlockSpeedFactor</c> scales the velocity by whichever cell the body is in on the tick, so a move OFF a slow floor onto a fast one is charged the open-ground rate and spends its first ticks at the slow one. Its bound is the whole-move 1.691, which is exactly this slack - and the same bound covers a hand-built segment, whose <c>ExpectedTicks</c> fallback is the nominal rate with no floor term at all.</remarks>
    [Fact]
    public void ALandBudget_ClearsAWholeMoveSpentOnASlowFloor()
    {
        PathSegment longDry = LongDryWalk();
        double worstLegitimate = longDry.PlannedTickCost * ActionCosts.MeasuredSlowFloorCostMultiplier;

        Assert.True(
            DryContext().Budget.BudgetFor(longDry) >= worstLegitimate,
            $"a {longDry.PlannedTickCost:F2}-tick charge got "
            + $"{DryContext().Budget.BudgetFor(longDry)} ticks against the {worstLegitimate:F2} a whole "
            + "move over soul sand really takes");
    }

    [Theory]
    [InlineData(1, 40, 40)]
    [InlineData(2, 40, 40)]
    [InlineData(3, 47, 40)]
    [InlineData(8, 92, 69)]
    public void TheLandBudget_TightensOnlyPastItsFloor(int blocks, int atTheRetiredSlack, int expected)
    {
        const double retiredSlack = 2.50;
        var segment = new PathSegment
        {
            Start = Center(0, 64, 0),
            End = Center(blocks, 64, 0),
            MoveType = MoveType.Traverse,
            PlannedTickCost = blocks * ActionCosts.SprintOneBlock,
            ExitTransition = PathTransitionType.ContinueStraight,
        };

        Assert.Equal(
            atTheRetiredSlack,
            (int)Math.Clamp(
                Math.Ceiling(retiredSlack * segment.PlannedTickCost) + SegmentBudgetPolicy.FixedOverheadTicks,
                SegmentBudgetPolicy.MinBudgetTicks,
                SegmentBudgetPolicy.MaxBudgetTicks));

        Assert.Equal(expected, DryContext().Budget.BudgetFor(segment));
    }

    private static PathSegment LongDryWalk() => new()
    {
        Start = Center(0, 64, 0),
        End = Center(8, 64, 0),
        MoveType = MoveType.Traverse,
        PlannedTickCost = 8 * ActionCosts.SprintOneBlock,
        ExitTransition = PathTransitionType.ContinueStraight,
    };

    /// <summary>Without the swim pose a dive has no downward authority at all and becomes the passive sink, measured at 37.6 ticks a block against a charge of 9.09. The budget has to carry that.</summary>
    [Fact]
    public void ASwimBudget_IsLargerWithoutTheSwimPose()
    {
        int posed = FloodedContext(allowSprint: true).Budget.BudgetFor(Swim());
        int unposed = FloodedContext(allowSprint: false).Budget.BudgetFor(Swim());

        Assert.True(
            unposed > posed,
            $"a swim with no swim pose got {unposed} ticks, no more than the {posed} a sprint swim gets");
        Assert.True(unposed > 37.6, $"a no-pose swim got {unposed} ticks against a 37.6-tick passive sink");
    }

    /// <summary>The legacy era has no swim pose either, so it takes the same row.</summary>
    [Fact]
    public void ASwimBudget_TreatsLegacyAsPoseless()
    {
        int legacy = FloodedContext(allowSprint: true, profile: Legacy).Budget.BudgetFor(Swim());
        int modern = FloodedContext(allowSprint: true, profile: Modern).Budget.BudgetFor(Swim());

        Assert.True(legacy > modern, $"legacy got {legacy} ticks and modern {modern}");
    }

    [Fact]
    public void StuckSpeed_OnLand_IsAQuarterOfTheSprintRate()
    {
        double land = DryContext().Budget.StuckSpeed(inWater: false);

        Assert.Equal(0.25 * 0.28062, land, 5);
    }

    [Fact]
    public void StuckSpeed_InWater_LeavesAnUpstreamWadeFourfoldClear()
    {
        double water = FloodedContext().Budget.StuckSpeed(inWater: true);
        const double upstreamWade = 0.028;

        Assert.True(
            upstreamWade > water * 3.9,
            $"a 0.028 blocks-a-tick upstream wade sits only {upstreamWade / water:F2}x over the "
            + $"{water:F5} stuck floor");
    }

    /// <summary>A hand-built segment carries no charge. It must fall back to its own geometry at the medium's nominal rate rather than being believed to be free; this is what guards <see cref="SubmergedAscendTests"/>'s two hand-built segments.</summary>
    [Fact]
    public void AHandBuiltSegment_FallsBackToLengthTimesNominal()
    {
        PathExecutionContext ctx = DryContext();
        var handBuilt = new PathSegment
        {
            Start = Center(0, 64, 0),
            End = Center(6, 64, 0),
            MoveType = MoveType.Traverse,
            ExitTransition = PathTransitionType.ContinueStraight,
        };

        Assert.Equal(0.0, handBuilt.PlannedTickCost);
        int budget = ctx.Budget.BudgetFor(handBuilt);
        int oneBlock = ctx.Budget.BudgetFor(Walk());

        Assert.True(
            budget > oneBlock,
            $"a six-block hand-built segment got {budget} ticks, no more than a one-block one's {oneBlock}");
    }

    [Theory]
    [InlineData(4, 164.10)]
    [InlineData(5, 205.13)]
    [InlineData(8, 328.21)]
    [InlineData(9, 369.23)]
    public void ADeepWaterSinkGetsABudgetItCanMeet(int blocks, double measuredTicks)
    {
        int budget = FloodedContext().Budget.BudgetFor(WaterSink(blocks));

        Assert.True(
            budget >= measuredTicks,
            $"a {blocks}-block still-water sink was budgeted {budget} ticks against a measured "
            + $"{measuredTicks:F2}");
    }

    [Theory]
    [InlineData(10, 410.26)]
    [InlineData(14, 574.36)]
    public void TheBudgetCeilingIsStillFinite(int blocks, double measuredTicks)
    {
        int budget = FloodedContext().Budget.BudgetFor(WaterSink(blocks));

        Assert.Equal(SegmentBudgetPolicy.MaxBudgetTicks, budget);
        Assert.True(
            budget < measuredTicks,
            $"a {blocks}-block still-water sink was budgeted {budget} ticks against a measured "
            + $"{measuredTicks:F2}, so it now fits and the ceiling has stopped being a backstop");
    }

    /// <summary>A fall through <paramref name="blocks"/> blocks of still water, as the builder emits it.</summary>
    private static PathSegment WaterSink(int blocks) => new()
    {
        Start = Center(0, 64 + blocks, 0),
        End = Center(0, 64, 0),
        MoveType = MoveType.Fall,
        PlannedTickCost = blocks * ActionCosts.WaterSinkOneBlock,
        ExitTransition = PathTransitionType.FinalStop,
    };

    /// <summary>Both bounds hold, for every segment kind, in both media. The ceiling matters because a charge can be pessimistic: the flow-aware swim cost prices a climb up a falling column at 3.5x where the executor measures 1.13x.</summary>
    [Theory]
    [InlineData(MoveType.Traverse, 0.0)]
    [InlineData(MoveType.Traverse, 4000.0)]
    [InlineData(MoveType.Swim, 0.0)]
    [InlineData(MoveType.Swim, 4000.0)]
    [InlineData(MoveType.Fall, 4000.0)]
    [InlineData(MoveType.Climb, 4000.0)]
    public void EveryBudget_StaysInsideItsBounds(MoveType moveType, double charge)
    {
        var segment = new PathSegment
        {
            Start = Center(0, 64, 0),
            End = Center(1, 64, 0),
            MoveType = moveType,
            PlannedTickCost = charge,
            ExitTransition = PathTransitionType.FinalStop,
        };

        foreach (PathExecutionContext ctx in new[] { DryContext(), FloodedContext() })
        {
            int budget = ctx.Budget.BudgetFor(segment);
            Assert.InRange(budget, SegmentBudgetPolicy.MinBudgetTicks, SegmentBudgetPolicy.MaxBudgetTicks);
        }
    }

    /// <summary>The E15/E16 shape, executed: a fully submerged bottom-walk lane, planned and run end to end against the real engine. These segments are <c>Traverse</c>, executed by <c>WalkTemplate</c>, and they are the reason the budget and the stuck threshold have to read the world rather than the move type.</summary>
    [Fact]
    public void ASubmergedLane_StillCompletesUnderTheNewBudgets()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 60, -4, 28, 70, 4, FixtureWorld.Stone);
        world.Fill(-3, 64, -3, 27, 69, 3, FixtureWorld.Air);
        world.Fill(-3, 64, -3, 27, 66, 3, FixtureWorld.Water);
        PlanningWorldView view = world.Capture(new BlockPos(-4, 58, -4), new BlockPos(28, 76, 4));
        var ctx = new PathExecutionContext(view, Modern, PhysicsConditions.Default);

        var segments = new List<PathSegment>();
        for (int i = 0; i < 12; i++)
            segments.Add(new PathSegment
            {
                Start = Center(i, 64, 0),
                End = Center(i + 1, 64, 0),
                MoveType = MoveType.Traverse,
                PlannedTickCost = ActionCosts.SprintOneBlock,
                ExitTransition = i == 11 ? PathTransitionType.FinalStop : PathTransitionType.ContinueStraight,
            });

        var driver = new ExecutionDriver(ctx, segments, Center(0, 64, 0), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(2000);

        Assert.Equal(PathExecutorState.Complete, state);
    }

    [Fact]
    public void AnUpstreamSubmergedLane_IsNotDeclaredStuck()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 60, -4, 28, 70, 4, FixtureWorld.Stone);
        world.Fill(-3, 64, -3, 27, 69, 3, FixtureWorld.Air);
        for (int z = -3; z <= 3; z++)
            world.FlowingRun(0, 66, z, 12, 1, 0, layers: 3);

        PlanningWorldView view = world.Capture(new BlockPos(-4, 58, -4), new BlockPos(28, 76, 4));
        var ctx = new PathExecutionContext(view, Modern, PhysicsConditions.Default);

        // Travel -x, against a current running +x.
        var segments = new List<PathSegment>();
        for (int i = 0; i < 8; i++)
            segments.Add(new PathSegment
            {
                Start = Center(10 - i, 64, 0),
                End = Center(9 - i, 64, 0),
                MoveType = MoveType.Traverse,
                PlannedTickCost = ActionCosts.SprintOneBlock,
                ExitTransition = i == 7 ? PathTransitionType.FinalStop : PathTransitionType.ContinueStraight,
            });

        var driver = new ExecutionDriver(ctx, segments, Center(10, 64, 0), 0f, seedAtStartPos: true);
        PathExecutorState state = driver.Run(4000);

        Assert.Equal(PathExecutorState.Complete, state);
    }

    /// <summary>The stuck-tick count is derived from the budget, and never exceeds it: a segment that has to spend more consecutive ticks stuck than it has ticks at all can never trip on stuck.</summary>
    [Theory]
    [InlineData(40)]
    [InlineData(56)]
    [InlineData(200)]
    public void StuckTicks_NeverOutlivesTheBudget(int budget)
    {
        int stuck = DryContext().Budget.StuckTicksFor(budget);
        Assert.InRange(stuck, SegmentBudgetPolicy.MinStuckTicks, budget);
    }
}
