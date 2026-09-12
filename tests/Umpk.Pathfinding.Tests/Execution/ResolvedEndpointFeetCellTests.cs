using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class ResolvedEndpointFeetCellTests
{
    private const int SupportY = 60;
    private const int FeetY = SupportY + 1;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(772);

    private readonly ITestOutputHelper _output;

    public ResolvedEndpointFeetCellTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<int, double, string> Supports() => new()
    {
        { FixtureWorld.Stone, 1.0, "stone: full cube, floor(61.0)=61, no shift" },
        { FixtureWorld.TopSlab, 1.0, "oak_slab[top]: [0,0.5,0,1,1,1], tops at 1.0, no shift" },
        { FixtureWorld.BottomSlab, 0.5, "oak_slab: [0,0,0,1,0.5,1], floor(60.5)=60, SHIFTED" },
        { FixtureWorld.SnowLayer, 0.375, "snow: [0,0,0,1,0.375,1], floor(60.375)=60, SHIFTED" },
    };

    /// <summary>R3b. A one-deep sheet over the support, dry air above: the head cell is air, so the route is not submerged, and neither spelling of the endpoint may say otherwise.</summary>
    [Theory]
    [MemberData(nameof(Supports))]
    public void OneDeepSheet_ReadsTheSameSubmergedAnswerUnderBothSpellings(int support, double height, string why)
    {
        PlanningWorldView view = Pool(support, waterDepth: 1);

        BreathValidation integer = Validate(view, IntegerRoute(), airTicks: 300);
        BreathValidation resolved = Validate(view, ResolvedRoute(height), airTicks: 300);

        _output.WriteLine($"{why}: integer={Describe(integer)} resolved={Describe(resolved)}");

        Assert.True(integer.IsSurvivable, "the head cell is air, so the integer spelling must be dry");
        Assert.Equal(0, integer.BreathingStops);
        Assert.Equal(integer, resolved);
    }

    /// <summary>R3c. The same support at the bottom of a six-deep pool, with the lung chosen so the arrival reserve is what decides survivability. The shifted read scans from one cell lower and charges 21.72 ticks where the node's own cell charges 19.1, which is enough to flip the verdict.</summary>
    [Theory]
    [MemberData(nameof(Supports))]
    public void ArrivalReserve_ReadsTheSameEscapeUnderBothSpellings(int support, double height, string why)
    {
        PlanningWorldView view = Pool(support, waterDepth: 6);

        for (int airTicks = 30; airTicks <= 120; airTicks++)
        {
            BreathValidation integer = Validate(view, IntegerRoute(), airTicks);
            BreathValidation resolved = Validate(view, ResolvedRoute(height), airTicks);
            if (integer != resolved)
                _output.WriteLine(
                    $"{why}: air={airTicks} integer={Describe(integer)} resolved={Describe(resolved)}");

            Assert.Equal(integer, resolved);
        }
    }

    [Theory]
    [MemberData(nameof(Supports))]
    public void SegmentBudget_IsTheSameUnderBothSpellings(int support, double height, string why)
    {
        for (int depth = 1; depth <= 4; depth++)
        {
            PlanningWorldView view = Pool(support, depth);
            var ctx = new PathExecutionContext(view, Profile);

            IReadOnlyList<PathSegment> integer = IntegerRoute();
            IReadOnlyList<PathSegment> resolved = ResolvedRoute(height);
            for (int i = 0; i < integer.Count; i++)
            {
                int a = ctx.Budget.BudgetFor(integer[i]);
                int b = ctx.Budget.BudgetFor(resolved[i]);
                if (a != b)
                    _output.WriteLine($"{why}: depth={depth} segment {i} integer={a} resolved={b}");

                Assert.Equal(a, b);
            }
        }
    }

    /// <summary>The planner-distance read. A wet ASCEND onto a partial support is where the resolved spelling shortens the segment's own geometry - one logical block up measures 1.118 blocks against 1.414 when the destination is a bottom slab - and both the breath price and the tick budget are re-derivations of what the SEARCH charged on its integer grid.</summary>
    [Theory]
    [MemberData(nameof(Supports))]
    public void AWetAscend_IsPricedByTheLogicalRiseUnderBothSpellings(int support, double height, string why)
    {
        PlanningWorldView view = AscendPool(support);
        var ctx = new PathExecutionContext(view, Profile);

        PathSegment integer = AscendSegment(FeetY + 1);
        PathSegment resolved = AscendSegment(FeetY + height) with { StartFeetY = FeetY, EndFeetY = FeetY + 1 };

        double integerTicks = BreathValidator.RealTicks(integer, submerged: true, Profile, allowSprint: true);
        double resolvedTicks = BreathValidator.RealTicks(resolved, submerged: true, Profile, allowSprint: true);
        _output.WriteLine(
            $"{why}: realTicks integer={integerTicks:F4} resolved={resolvedTicks:F4}, "
            + $"budget integer={ctx.Budget.BudgetFor(integer)} resolved={ctx.Budget.BudgetFor(resolved)}");

        Assert.Equal(integerTicks, resolvedTicks, 9);
        Assert.Equal(ctx.Budget.BudgetFor(integer), ctx.Budget.BudgetFor(resolved));
    }

    /// <summary>A support at <see cref="SupportY"/> over stone, flooded from the feet cell up. <paramref name="waterDepth"/> cells of water, air above them.</summary>
    private static PlanningWorldView Pool(int support, int waterDepth)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 4, SupportY - 1);
        world.Fill(-4, SupportY, -4, 12, SupportY, 4, support);
        world.Fill(-4, FeetY, -4, 12, FeetY + waterDepth - 1, 4, FixtureWorld.Water);
        return world.Capture(new BlockPos(-4, SupportY - 4, -4), new BlockPos(12, SupportY + 16, 4), margin: 2);
    }

    /// <summary>A step up onto the support: bare ground at <see cref="SupportY"/> for x &lt;= 1, the support laid on top of it for x &gt;= 2, and the whole water column above so the move is priced wet.</summary>
    private static PlanningWorldView AscendPool(int support)
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 4, SupportY - 1);
        world.Fill(-4, SupportY, -4, 12, SupportY, 4, FixtureWorld.Stone);
        world.Fill(-4, FeetY, -4, 12, SupportY + 8, 4, FixtureWorld.Water);
        world.Fill(2, FeetY, -4, 12, FeetY, 4, support);
        return world.Capture(new BlockPos(-4, SupportY - 4, -4), new BlockPos(12, SupportY + 16, 4), margin: 2);
    }

    /// <summary>Three one-block traverses along +X at the LOGICAL feet cell, endpoints as integers.</summary>
    private static IReadOnlyList<PathSegment> IntegerRoute() => Route(FeetY);

    /// <summary>The same three traverses with the endpoints carrying the support's resolved top.</summary>
    private static IReadOnlyList<PathSegment> ResolvedRoute(double height) => Route(SupportY + height, FeetY);

    private static IReadOnlyList<PathSegment> Route(double y, int? feetY = null)
    {
        var segments = new List<PathSegment>(3);
        for (int i = 0; i < 3; i++)
        {
            var segment = new PathSegment
            {
                Start = new Vec3d(i + 1.5, y, 0.5),
                End = new Vec3d(i + 2.5, y, 0.5),
                MoveType = MoveType.Traverse,
                PlannedTickCost = ActionCosts.SprintOneBlock,
                ExitTransition = i == 2 ? PathTransitionType.FinalStop : PathTransitionType.ContinueStraight,
            };

            segments.Add(feetY is null ? segment : segment with { StartFeetY = feetY.Value, EndFeetY = feetY.Value });
        }

        return segments;
    }

    private static PathSegment AscendSegment(double endY) => new()
    {
        Start = new Vec3d(1.5, FeetY, 0.5),
        End = new Vec3d(2.5, endY, 0.5),
        MoveType = MoveType.Ascend,
        PlannedTickCost = ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty,
        ExitTransition = PathTransitionType.FinalStop,
    };

    private static BreathValidation Validate(PlanningWorldView view, IReadOnlyList<PathSegment> route, int airTicks)
        => BreathValidator.Validate(route, view, Profile, allowSprint: true, airTicks);

    private static string Describe(in BreathValidation v)
        => $"(survivable={v.IsSurvivable} peak={v.PeakDeficitTicks:F2} first={v.FirstViolationSegment} "
            + $"budget={v.BudgetTicks:F0} stops={v.BreathingStops})";
}
