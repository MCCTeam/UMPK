using Umpk.Geometry;
using Umpk.Pathfinding.Goals;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class GoalTargetHintTests
{
    [Fact]
    public void GoalBlock_NamesItsBlock()
    {
        Assert.True(new GoalBlock(140, 78, 368).TryGetTargetHint(out BlockPos hint));
        Assert.Equal(new BlockPos(140, 78, 368), hint);
    }

    [Fact]
    public void GoalNear_NamesItsCentre()
    {
        Assert.True(new GoalNear(1023, 64, 1008, 1).TryGetTargetHint(out BlockPos hint));
        Assert.Equal(new BlockPos(1023, 64, 1008), hint);
    }

    [Fact]
    public void EntityGoal_NamesItsTarget()
    {
        Assert.True(new EntityGoal(new BlockPos(-40, 12, 900)).TryGetTargetHint(out BlockPos hint));
        Assert.Equal(new BlockPos(-40, 12, 900), hint);
    }

    [Fact]
    public void GoalComposite_NamesTheFirstHintItsChildrenOffer()
    {
        var composite = new GoalComposite(new GoalXZ(5, 9), new GoalBlock(140, 78, 368));

        Assert.True(composite.TryGetTargetHint(out BlockPos hint));
        Assert.Equal(new BlockPos(140, 78, 368), hint);
    }

    /// <summary>GoalXZ names no block: it has no Y, and inventing one would size the region around a height the caller never asked for. It falls back to the probe, which is correct for a column goal.</summary>
    [Fact]
    public void GoalXZ_OffersNoHint()
    {
        // Through IGoal: GoalXZ does not implement the member, so it takes the interface's default, which is reachable only through the interface. That is exactly the shape an external goal gets.
        IGoal goal = new GoalXZ(5, 9);
        Assert.False(goal.TryGetTargetHint(out _));
    }
}
