using Umpk.Client.Navigation;
using Umpk.Geometry;
using Umpk.Pathfinding.Goals;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A navigation whose goal the player ALREADY satisfies is an arrival, not a failure.</summary>
/// <remarks><c>MoveToAsync</c> has always handled this (start block == destination block walks the remaining fraction and returns), but <c>NavigateAsync</c> did not: the planner returns a single-node path, <c>PathSegmentBuilder.FromPath</c> yields no segments, <c>BuildExecutor</c> returns null, and the navigator threw "No path to the goal was found". That is what broke ItemsCollector, which walks to <c>GoalNear(item, 1)</c>: the moment it was within a block of a drop, every further attempt threw and the bot stood next to the item forever.</remarks>
public sealed class NavigateAlreadyInGoalTests
{
    [Fact]
    public void GoalNear_IsSatisfiedByABlockInsideItsRange()
    {
        // The predicate the navigator consults. A bot standing one block from the drop is already "there".
        var goal = new NearGoal(new BlockPos(1023, 64, 1008), 1);

        Assert.True(goal.IsInGoal(new BlockPos(1023, 64, 1008)));
        Assert.True(goal.IsInGoal(new BlockPos(1022, 64, 1008)));
        Assert.True(goal.IsInGoal(new BlockPos(1023, 64, 1009)));
        Assert.False(goal.IsInGoal(new BlockPos(1020, 64, 1008)));
    }
}
