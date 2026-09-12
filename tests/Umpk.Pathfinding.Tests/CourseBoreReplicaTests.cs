using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class CourseBoreReplicaTests
{
    private readonly ITestOutputHelper _output;

    public CourseBoreReplicaTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void E30_PauseDive_PlansWithAtLeastFourScheduledPauses()
    {
        Plan plan = PlanBore([(18, 8), (20, 12), (4, 12), (12, 16), (18, 16)]);

        _output.WriteLine($"E30 {plan}");

        Assert.Equal(PathStatus.Success, plan.Status);
        Assert.True(plan.Holds >= 4, $"E30 must schedule at least four pauses: {plan}");
    }

    [Fact]
    public void E31_PauseBell_PlansRatherThanRefusing()
    {
        Plan plan = PlanBore([(22, 8), (12, 14), (8, 16)], spur: true);

        _output.WriteLine($"E31 {plan}");

        Assert.Equal(PathStatus.Success, plan.Status);
        Assert.True(plan.Holds >= 2, $"E31 must schedule at least two pauses: {plan}");

        // The plan DETOURS INTO THE DEAD-END SPUR to breathe, which is the structural fact the pause count cannot show and which decides whether this row can ever pass. Pinned so a later edit to the bells cannot silently remove it.
        Assert.Contains(plan.Pauses, p => (int)p.Cell.Z == 14);

        // And the price of the longest leg with no breath in it, which is where this row dies live. Measured on the course, the executor spends 326 engine ticks on the leg the validator prices here at 179.3 - a factor of 1.8 - because a submerged Diagonal costs a measured 46.5 ticks (n=24, max 53) against a submerged Traverse's 8.1, and this leg carries three of them. The number is pinned rather than asserted against a bound: the bound that matters is live, and this is the offline half of the comparison.
        Assert.InRange(plan.LongestDryLegTicks, 170.0, 190.0);
    }

    /// <summary>E32 deepwork: only two bells, so each leg spends most of a lung and each pause has to refill most of one.</summary>
    /// <remarks>
    /// <para>This is the row most at risk of being built wrong, and it is why the file exists. It was FIRST WRITTEN WITH TWO BELLS and this test refused it: measured, no two-bell placement plans at all. Three legs of about 28 blocks is 286 ticks apiece at the search's own 10.204 ticks a block, and against a 300-tick lung the escape reserve closes the gap, so the whole bore becomes a refusal. Live, that would have burned a course slot and looked like a finding. See <see cref="TwoBellsIsBeyondThePlannersCeiling"/>, which pins it.</para>
    /// <para>Three bells at (24,8) (8,12) (14,16) is the measured answer: four pauses, the longest 71.8 ticks, which is most of the 75 a full lung takes to refill. The assertion on pause LENGTH is the row's actual subject: E30 would still arrive if every pause were cut short, because its legs are short enough to absorb the error.</para>
    /// </remarks>
    [Fact]
    public void E32_DeepWork_PlansAndItsPausesAreLong()
    {
        Plan plan = PlanBore([(24, 8), (8, 12), (14, 16)]);

        _output.WriteLine($"E32 {plan}");

        Assert.Equal(PathStatus.Success, plan.Status);
        Assert.True(plan.Holds >= 3, $"E32 must schedule at least three pauses: {plan}");
        Assert.True(
            plan.LongestHold >= 60.0,
            $"E32's pauses must be long ones, or the row is not about hold duration: {plan}");
    }

    /// <summary>E33 noairdeep: the control. No air anywhere, so no plan and no invented pause.</summary>
    /// <remarks>The three rows above all say "the pause works". None of them can say "and it is not invented". A scheduler that treats a cell as breathable on geometry it has not checked turns this bore into a plan, and the bot drowns in it.</remarks>
    [Fact]
    public void E33_NoAirDeep_IsRefused()
    {
        Plan plan = PlanBore([]);

        _output.WriteLine($"E33 {plan}");

        Assert.NotEqual(PathStatus.Success, plan.Status);
        Assert.Equal(0, plan.Holds);
    }

    [Fact]
    public void TheBellsAreWhatMakesTheBoreCrossable()
    {
        Plan withBells = PlanBore([(18, 8), (20, 12), (4, 12), (12, 16), (18, 16)]);
        Plan without = PlanBore([]);

        _output.WriteLine($"  with bells: {withBells}");
        _output.WriteLine($"  none      : {without}");

        Assert.Equal(PathStatus.Success, withBells.Status);
        Assert.NotEqual(PathStatus.Success, without.Status);
    }

    [Theory]
    [InlineData(25, 10, 2, 15)]
    [InlineData(25, 11, 2, 14)]
    [InlineData(22, 8, 6, 12)]
    public void TwoBellsIsBeyondThePlannersCeiling(int ax, int az, int bx, int bz)
    {
        Plan plan = PlanBore([(ax, az), (bx, bz)]);

        _output.WriteLine($"two bells @ ({ax},{az}) ({bx},{bz}): {plan}");

        Assert.NotEqual(PathStatus.Success, plan.Status);
    }

    private readonly record struct Plan(
        PathStatus Status, int Segments, int Holds, double TotalHold, double LongestHold,
        IReadOnlyList<(Vec3d Cell, double Ticks)> Pauses, double LongestDryLegTicks)
    {
        public override string ToString()
            => $"{Status}, {Segments} segments, {Holds} pauses, {TotalHold:F1} ticks held "
                + $"(longest {LongestHold:F1}), longest unbroken submerged leg {LongestDryLegTicks:F1} "
                + $"ticks; pauses at [{string.Join(" ", Pauses.Select(p => $"({p.Cell.X:F0},{p.Cell.Y:F0},{p.Cell.Z:F0})={p.Ticks:F0}t"))}]";
    }

    private static Plan PlanBore(IReadOnlyList<(int X, int Z)> bells, bool spur = false)
    {
        var world = new FixtureWorld();

        // The casing, y=99..104.  p.fill(0, 0, 6, 27, 5, 18, "stone")
        world.Fill(0, 99, 6, 27, 104, 18, FixtureWorld.Stone);

        // Bore legs, 1 wide and 2 tall, y=100..101.  p.fill(2, 1, dz, 25, 2, dz, "water")
        foreach (int dz in new[] { 8, 12, 16 })
            world.Fill(2, 100, dz, 25, 101, dz, FixtureWorld.Water);

        world.Fill(25, 100, 8, 25, 101, 12, FixtureWorld.Water);   // cross at the +x end
        world.Fill(2, 100, 12, 2, 101, 16, FixtureWorld.Water);    // cross at the -x end
        world.Fill(2, 102, 8, 2, 104, 8, FixtureWorld.Water);      // entry shaft, y=102..104
        world.Fill(25, 102, 16, 25, 104, 16, FixtureWorld.Water);  // exit shaft, y=102..104

        if (spur)
            world.Fill(12, 100, 13, 12, 101, 14, FixtureWorld.Water);

        // The one-cell head bells: air at y=102 over a leg whose top water cell is y=101, sealed by the casing at y=103. The feet cell stays water and only the HEAD cell is air, so the only way to breathe at one is to stop.
        foreach ((int bx, int bz) in bells)
            world.Fill(bx, 102, bz, bx, 102, bz, FixtureWorld.Air);

        // The roof divider, y=105..107, so the two roofs do not connect and the bore is the only route.
        world.Fill(5, 105, 6, 5, 107, 18, FixtureWorld.Stone);

        var start = new BlockPos(0, 105, 8);
        var goal = new BlockPos(27, 105, 16);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        if (result.Status != PathStatus.Success)
            return new Plan(result.Status, 0, 0, 0.0, 0.0, [], 0.0);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        double total = segments.Sum(s => s.BreathHoldTicks);
        double longest = segments.Count == 0 ? 0.0 : segments.Max(s => s.BreathHoldTicks);
        var pauses = segments
            .Where(s => s.BreathHoldTicks > 0.0)
            .Select(s => (s.End, s.BreathHoldTicks))
            .ToList();

        // The number the pause COUNT cannot see: the longest run of submerged travel with no pause in it, priced the way BreathValidator prices it. A bore can schedule four pauses and still be unsurvivable if one of the legs between them is longer than a lung, which is exactly how E31 died live - two of its three bells sit on stretches the route never breathes at.
        double worst = 0.0;
        double running = 0.0;
        foreach (PathSegment segment in segments)
        {
            bool submerged = MoveHelper.IsWater(view.GetBlock(new BlockPos(
                (int)Math.Floor(segment.End.X),
                (int)Math.Floor(segment.End.Y) + 1,
                (int)Math.Floor(segment.End.Z))));
            running = submerged
                ? running + BreathValidator.RealTicks(segment, submerged: true, PhysicsProfile.ForProtocol(772), allowSprint: true)
                : 0.0;
            if (segment.BreathHoldTicks > 0.0)
                running = 0.0;

            worst = Math.Max(worst, running);
        }

        return new Plan(
            result.Status, segments.Count, segments.Count(s => s.BreathHoldTicks > 0.0), total, longest,
            pauses, worst);
    }
}
