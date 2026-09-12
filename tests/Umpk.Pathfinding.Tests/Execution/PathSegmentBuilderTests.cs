using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class PathSegmentBuilderTests
{
    private static PathNode Node(int x, int y, int z, MoveType move)
        => new(x, y, z) { MoveUsed = move };

    [Fact]
    public void FromPath_EmptyForSingleNode()
    {
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath([Node(0, 64, 0, MoveType.Traverse)]);
        Assert.Empty(segments);
    }

    [Fact]
    public void FromPath_UsesBlockCenterPoints()
    {
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(
        [
            Node(0, 64, 0, MoveType.Traverse),
            Node(1, 64, 0, MoveType.Traverse),
        ]);

        PathSegment seg = Assert.Single(segments);
        Assert.Equal(0.5, seg.Start.X, 6);
        Assert.Equal(0.5, seg.Start.Z, 6);
        Assert.Equal(1.5, seg.End.X, 6);
        Assert.Equal(1, seg.HeadingX);
    }

    [Fact]
    public void FromPath_LastSegmentIsFinalStop()
    {
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(
        [
            Node(0, 64, 0, MoveType.Traverse),
            Node(1, 64, 0, MoveType.Traverse),
            Node(2, 64, 0, MoveType.Traverse),
        ]);

        Assert.Equal(PathTransitionType.ContinueStraight, segments[0].ExitTransition);
        Assert.Equal(PathTransitionType.FinalStop, segments[^1].ExitTransition);
        Assert.True(segments[^1].ExitHints.RequireGrounded);
    }

    [Fact]
    public void FromPath_JumpAheadPreparesJump()
    {
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(
        [
            Node(0, 64, 0, MoveType.Traverse),
            Node(1, 64, 0, MoveType.Traverse),
            Node(3, 64, 0, MoveType.Parkour),
        ]);

        Assert.Equal(PathTransitionType.PrepareJump, segments[0].ExitTransition);
        Assert.True(segments[0].ExitHints.RequireJumpReady);
        Assert.True(segments[0].PreserveSprint);
    }

    [Fact]
    public void FromPath_SwimSegmentAllowsUngroundedExit()
    {
        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(
        [
            Node(0, 64, 0, MoveType.Traverse),
            Node(1, 64, 0, MoveType.Swim),
            Node(2, 64, 0, MoveType.Swim),
        ]);

        // The swim segment (index 1) must not require grounding to complete.
        PathSegment swim = segments[1];
        Assert.Equal(MoveType.Swim, swim.MoveType);
        Assert.True(swim.ExitHints.AllowUngrounded);
        Assert.False(swim.ExitHints.RequireGrounded);
    }
}
