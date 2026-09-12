using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class BambooSqueezeLaneTests
{
    /// <summary>The Y of the lane floor.</summary>
    private const int FloorY = 60;

    /// <summary>The Y of the body's feet.</summary>
    private const int FeetY = 61;

    /// <summary>A one-wide walled lane running along X at <c>z = 0</c>, with a three-tall bamboo stalk at <paramref name="postX"/>. Walls at <c>z = -1</c> and <c>z = +1</c> so the ONLY way past the post is a lateral lane inside the post's own cell.</summary>
    private static FixtureWorld Lane(int postX, int x0 = 0, int x1 = 10)
    {
        var world = new FixtureWorld();
        world.Fill(x0 - 1, FloorY, -1, x1 + 1, FloorY, 1, FixtureWorld.Stone);
        world.Fill(x0 - 1, FeetY, -1, x1 + 1, FeetY + 2, -1, FixtureWorld.Stone);
        world.Fill(x0 - 1, FeetY, 1, x1 + 1, FeetY + 2, 1, FixtureWorld.Stone);
        world.Fill(x0 - 1, FeetY, 0, x0 - 1, FeetY + 2, 0, FixtureWorld.Stone);
        world.Fill(x1 + 1, FeetY, 0, x1 + 1, FeetY + 2, 0, FixtureWorld.Stone);
        world.Fill(postX, FeetY, 0, postX, FeetY + 2, 0, FixtureWorld.Bamboo);
        return world;
    }

    /// <summary><b>The row this feature exists for.</b> The post at <c>(5, 0)</c> hashes to a Z offset of <c>+0.216667</c> (<c>k = 14</c>), so its box spans <c>Z [0.622917, 0.810417]</c> and a body flush with the cell's low Z face - spanning <c>[0.0, 0.6]</c> - clears it by 0.0229. The planner must find that lane and route through it.</summary>
    [Fact]
    public void ABodyFlushWithTheCellFace_WalksPastAnOffsetBambooPost()
    {
        FixtureWorld world = Lane(5);
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));

        PathResult result = new AStarPathFinder().Calculate(
            ctx, 0, FeetY, 0, new GoalBlock(10, FeetY, 0), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.Equal(PathStatus.Success, result.Status);
    }

    /// <summary>The premise the row above rests on, asserted separately so a failure says WHICH half broke: the post really is where the offset model says it is, and a centred body really does not fit.</summary>
    [Fact]
    public void ThePostAt5_0_SitsWhereTheOffsetModelSaysAndBlocksACentredBody()
    {
        Vec3d offset = BlockShapeOffset.For(
            new BlockState(new FixtureBlockData(), FixtureWorld.Bamboo), new BlockPos(5, FeetY, 0));

        Assert.Equal(0.21666666865348816, offset.Z, 12);

        double boxMin = 0.40625 + offset.Z;
        double boxMax = 0.59375 + offset.Z;
        Assert.True(boxMin > 0.6, $"post min {boxMin} must clear a flush body's 0.6 face");
        Assert.True(boxMin < 0.8, $"post min {boxMin} must still block a CENTRED body's [0.2, 0.8]");
        Assert.True(boxMax < 1.0, $"post max {boxMax} must stay inside its own cell");
    }

    /// <summary><b>The control that must keep refusing.</b> The post at <c>(4, 0)</c> hashes to <c>+0.183333</c> (<c>k = 13</c>), one nibble short: its box spans <c>Z [0.589583, 0.777083]</c>, which overlaps a flush body at BOTH faces (<c>[0, 0.6]</c> by 0.0104 and <c>[0.4, 1.0]</c> by 0.1875) as well as a centred one. No lane exists and the planner must still refuse. A squeeze implementation that widened the cell gate rather than measuring the box would pass the row above and fail this one.</summary>
    [Fact]
    public void ABodyFlushWithTheCellFace_StillCannotPassAPostThatIsOneNibbleShort()
    {
        FixtureWorld world = Lane(4);
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));

        PathResult result = new AStarPathFinder().Calculate(
            ctx, 0, FeetY, 0, new GoalBlock(10, FeetY, 0), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The control's premise, for the same reason the threadable row has one.</summary>
    [Fact]
    public void ThePostAt4_0_IsOneNibbleShortOfALane()
    {
        Vec3d offset = BlockShapeOffset.For(
            new BlockState(new FixtureBlockData(), FixtureWorld.Bamboo), new BlockPos(4, FeetY, 0));

        double boxMin = 0.40625 + offset.Z;
        double boxMax = 0.59375 + offset.Z;
        Assert.True(boxMin < 0.6, $"post min {boxMin} must overlap a low-flush body");
        Assert.True(boxMax > 0.4, $"post max {boxMax} must overlap a high-flush body");
    }

    /// <summary>The empty lane, so a failure of the two rows above cannot be blamed on the lane.</summary>
    [Fact]
    public void TheSameLaneWithNoPost_IsWalkedToday()
    {
        var world = new FixtureWorld();
        world.Fill(-1, FloorY, -1, 11, FloorY, 1, FixtureWorld.Stone);
        world.Fill(-1, FeetY, -1, 11, FeetY + 2, -1, FixtureWorld.Stone);
        world.Fill(-1, FeetY, 1, 11, FeetY + 2, 1, FixtureWorld.Stone);

        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));

        PathResult result = new AStarPathFinder().Calculate(
            ctx, 0, FeetY, 0, new GoalBlock(10, FeetY, 0), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.Equal(PathStatus.Success, result.Status);
    }
}
