using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>Timeout and partial-path coverage for the deterministic planner: the wall-clock budget is evaluated against an injected <see cref="TimeProvider"/>, so a fake clock that reports elapsed time past the deadline forces an early stop with a best-effort partial path rather than a wall-clock read in the loop.</summary>
public sealed class PlannerTimeoutTests
{
    private const int FloorY = 64;

    [Fact]
    public void Timeout_StopsEarlyAgainstInjectedClock()
    {
        var world = new FixtureWorld();
        world.Floor(-32, 32, -32, 32, FloorY);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(30, FloorY + 1, 30);
        PlanningWorldView view = world.Capture(start, goal, margin: 16);

        // A clock that reports a timestamp far past any deadline on the very first check.
        var clock = new JumpingClock();
        var options = PathfinderOptions.Default with { Timeout = TimeSpan.FromMilliseconds(1) };

        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal), clock);

        Assert.NotEqual(PathStatus.Success, result.Status);
        Assert.True(result.Diagnostics.TimedOut);
    }

    [Fact]
    public void Timeout_UsesTheInjectedClocksFrequency_NotTheSystems()
    {
        var world = new FixtureWorld();
        world.Floor(-16, 16, -16, 16, FloorY);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(12, FloorY + 1, 12);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        // Ten injected seconds pass per timestamp read against a one-second budget, so the first in-loop deadline check is already ten budgets late. Converted with the host frequency instead, the same ten seconds read as a fraction of a millisecond and the search runs to completion.
        var clock = new MicrosecondClock(stepTicks: 10 * MicrosecondClock.Frequency);
        var options = PathfinderOptions.Default with { Timeout = TimeSpan.FromSeconds(1) };

        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal), clock);

        Assert.True(result.Diagnostics.TimedOut);
        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The reported planning time must be converted with the injected clock's frequency as well: the deadline and the elapsed report are the same unit mistake in two places.</summary>
    [Fact]
    public void ElapsedMilliseconds_UsesTheInjectedClocksFrequency_NotTheSystems()
    {
        var world = new FixtureWorld();
        world.Floor(-16, 16, -16, 16, FloorY);
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(12, FloorY + 1, 12);
        PlanningWorldView view = world.Capture(start, goal, margin: 8);

        var clock = new MicrosecondClock(stepTicks: 10 * MicrosecondClock.Frequency);
        var options = PathfinderOptions.Default with { Timeout = TimeSpan.FromSeconds(1) };

        PathResult result = PathPlanner.FindPath(view, options, start, new GoalBlock(goal), clock);

        // At least one 10-second step separates the start stamp from the final one, whatever the read count is. Anything under that means the elapsed span was divided by a frequency the clock does not use.
        Assert.True(
            result.Diagnostics.ElapsedMilliseconds >= 10_000,
            $"elapsed was {result.Diagnostics.ElapsedMilliseconds} ms for a span of at least 10 injected seconds");
    }

    /// <summary>A <see cref="TimeProvider"/> whose timestamp leaps forward on every read.</summary>
    private sealed class JumpingClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => System.TimestampFrequency;

        public override long GetTimestamp()
        {
            _ticks += System.TimestampFrequency; // +1 second per read
            return _ticks;
        }
    }

    private sealed class MicrosecondClock(long stepTicks) : TimeProvider
    {
        /// <summary>Ticks per second: microseconds.</summary>
        public const long Frequency = 1_000_000;

        private long _ticks;

        public override long TimestampFrequency => Frequency;

        public override long GetTimestamp()
        {
            _ticks += stepTicks;
            return _ticks;
        }
    }
}
