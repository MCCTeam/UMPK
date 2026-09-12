using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class RoofedLaneBreathPricingTests
{
    private const int Protocol = 772;
    private const int FloorY = 100;

    /// <summary>The lung a row that STARTS submerged can physically be handed, from the live pass: 261 measured, against a ceiling of about 270, because the settle at a submerged start spends the difference.</summary>
    private const int SubmergedStartLung = 261;

    private readonly ITestOutputHelper _output;

    public RoofedLaneBreathPricingTests(ITestOutputHelper output) => _output = output;

    /// <summary>E15's lane at the distance that reproduces the row: a route the planner approves on a full lung, the executor walks end to end, and the validator refused on the lung the row really starts with.</summary>
    /// <remarks>The shelf span is what sets the covered run, so it is what sets the peak. At 23 the modelled peak was 280.62 against E15's live 275.51 - the same row, one block long - and the failure was verbatim <c>the executor walked the whole 33,24-block route in 204 ticks, and the model refuses it: peak 280,62 ticks against the 251-tick budget a 261-tick lung buys, first violation at segment 24 of 32</c>.</remarks>
    [Fact]
    public void RoofedLane_TheExecutorFinishesInsideTheLungTheModelRefused()
    {
        FixtureWorld world = Lane(length: 34, shelfFrom: 5, shelfSpan: 23);
        var start = new BlockPos(1, FloorY, 0);
        var goal = new BlockPos(33, FloorY, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);

        // and not a routing one: the same route, judged on the lung the row really holds, was refused.
        Assert.True(
            BreathValidator.Validate(segments, view, profile, allowSprint: true, (int)BreathModel.FullLungTicks)
                .IsSurvivable,
            "the fixture must plan a route that is affordable on a full lung");

        var ctx = new PathExecutionContext(view, profile, PhysicsConditions.Default);
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5));
        PathExecutorState state = driver.Run(4000);
        double blocks = TotalBlocks(segments);

        Assert.Equal(PathExecutorState.Complete, state);

        BreathValidation onTheRealLung = BreathValidator.Validate(
            segments, view, profile, allowSprint: true, SubmergedStartLung);

        _output.WriteLine(
            $"executed {driver.Trace.Count} ticks over {blocks:F2} blocks; "
            + $"peak={onTheRealLung.PeakDeficitTicks:F2} budget={onTheRealLung.BudgetTicks:F0}");

        Assert.True(
            onTheRealLung.IsSurvivable,
            $"the executor walked the whole {blocks:F2}-block route in {driver.Trace.Count} ticks, and the "
            + $"model refuses it: peak {onTheRealLung.PeakDeficitTicks:F2} ticks against the "
            + $"{onTheRealLung.BudgetTicks:F0}-tick budget a {SubmergedStartLung}-tick lung buys, first "
            + $"violation at segment {onTheRealLung.FirstViolationSegment} of {segments.Count}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SubmergedWade_IsPricedAtWhatTheExecutorMeasures(bool allowSprint)
    {
        FixtureWorld world = Lane(length: 34, shelfFrom: 5, shelfSpan: 23);
        var start = new BlockPos(1, FloorY, 0);
        var goal = new BlockPos(33, FloorY, 0);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        PathfinderOptions options = PathfinderOptions.Default with { AllowSprint = allowSprint };
        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);
        var ctx = new PathExecutionContext(view, profile, PhysicsConditions.Default, allowSprint);
        var driver = new ExecutionDriver(ctx, segments, new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5));

        Assert.Equal(PathExecutorState.Complete, driver.Run(4000));

        (double wadeTicks, double wadeBlocks) = WadeTicks(driver, segments, view);
        Assert.True(wadeBlocks > 20.0, $"the fixture must produce a long wade; it produced {wadeBlocks:F2} blocks");

        double measured = wadeTicks / wadeBlocks;

        // What the model charges for exactly that walk, read through the public entry point rather than from the constant, so the test measures the price the validator really applies.
        var oneBlock = new PathSegment
        {
            Start = new Vec3d(0.5, FloorY, 0.5),
            End = new Vec3d(1.5, FloorY, 0.5),
            MoveType = MoveType.Traverse,
        };
        double charged = BreathValidator.RealTicks(oneBlock, submerged: true, profile, allowSprint);

        _output.WriteLine(
            $"sprint={allowSprint}: executor {wadeTicks:F0} ticks over {wadeBlocks:F2} wade blocks = "
            + $"{measured:F4} ticks/block; model charges {charged:F4}; ratio {charged / measured:F4}");

        Assert.True(
            charged >= measured,
            $"the model charges {charged:F4} ticks a block for a wade the executor really takes "
            + $"{measured:F4} on (sprint={allowSprint}), so it under-states the walk and would approve a "
            + "route the player drowns on");
        Assert.True(
            charged <= measured * 1.25,
            $"the model charges {charged:F4} ticks a block for a wade the executor really takes "
            + $"{measured:F4} on (sprint={allowSprint}), a factor of {charged / measured:F2}, so it "
            + "refuses routes the executor completes");
    }

    /// <summary>The ticks the executor spent on the segments the model prices as a submerged bottom-walk, and their blocks.</summary>
    private static (double Ticks, double Blocks) WadeTicks(
        ExecutionDriver driver, IReadOnlyList<PathSegment> segments, PlanningWorldView view)
    {
        double ticks = 0;
        double blocks = 0;
        int segmentStart = 0;
        int index = 0;
        for (int t = 0; t <= driver.Trace.Count; t++)
        {
            int at = t < driver.Trace.Count ? driver.Trace[t].SegmentIndex : segments.Count;
            if (at == index)
                continue;

            if (index < segments.Count && IsWade(segments[index], view))
            {
                ticks += t - segmentStart;
                blocks += (segments[index].End - segments[index].Start).Length();
            }

            segmentStart = t;
            index = at;
        }

        return (ticks, blocks);
    }

    private static bool IsWade(PathSegment segment, PlanningWorldView view)
        => segment.MoveType is MoveType.Traverse or MoveType.Diagonal
            && (Submerged(view, segment.Start) || Submerged(view, segment.End));

    private static bool Submerged(PlanningWorldView view, in Vec3d point)
        => BreathModel.IsSubmerged(
            view, (int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z));

    private static double TotalBlocks(IReadOnlyList<PathSegment> segments)
    {
        double blocks = 0;
        foreach (PathSegment segment in segments)
            blocks += (segment.End - segment.Start).Length();

        return blocks;
    }

    /// <summary>E15's lane: two cells of water in a stone casing, open to the air at both ends and roofed by a two-thick shelf over the middle.</summary>
    /// <remarks>The shelf is TWO cells thick for the same reason the course builds it that way: one cell thick leaves a walkable surface flush above the open end, and the planner climbs out and walks the roof instead of crossing under it, which voids the row. Two cells also means a body can never stand in the top water cell under the shelf, so the covered span has exactly one node level - the bed - and its head cell is water. That is what makes the covered run one unbroken submerged walk.</remarks>
    private static FixtureWorld Lane(int length, int shelfFrom, int shelfSpan)
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY - 2, -4, length + 4, FloorY + 6, 4, FixtureWorld.Stone);
        world.Fill(-1, FloorY, -1, length + 1, FloorY + 3, 1, FixtureWorld.Air);
        world.Fill(0, FloorY, -1, length, FloorY + 1, 1, FixtureWorld.Water);
        world.Fill(shelfFrom, FloorY + 2, -1, shelfFrom + shelfSpan - 1, FloorY + 3, 1, FixtureWorld.Stone);
        return world;
    }
}
