using Umpk.Pathfinding;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class PathfinderOptionsPresetTests
{
    [Fact]
    public void Default_KeepsTheVanillaCalibratedFallLimits()
    {
        Assert.Equal(3, PathfinderOptions.Default.MaxFallHeight);
        Assert.Equal(20, PathfinderOptions.Default.MaxFallHeightIntoWater);
    }

    [Fact]
    public void UnsafeFalls_LiftsBothFallGuardsToTheWorldHeightSpan()
    {
        Assert.Equal(384, PathfinderOptions.WorldHeightSpan);
        Assert.Equal(384, PathfinderOptions.UnsafeFalls.MaxFallHeight);
        Assert.Equal(384, PathfinderOptions.UnsafeFalls.MaxFallHeightIntoWater);
    }

    /// <summary>The limit stays FINITE on purpose: the descend moves scan downward one block at a time up to it, so an unbounded value would not terminate in any practical time.</summary>
    [Fact]
    public void UnsafeFalls_StaysFinite()
    {
        Assert.True(PathfinderOptions.UnsafeFalls.MaxFallHeight < int.MaxValue);
        Assert.True(PathfinderOptions.UnsafeFalls.MaxFallHeight >= 384);
    }

    /// <summary>Only the fall guards move; every other capability is the default.</summary>
    [Fact]
    public void UnsafeFalls_ChangesNothingElse()
    {
        PathfinderOptions unsafeFalls = PathfinderOptions.UnsafeFalls;
        PathfinderOptions expected = PathfinderOptions.Default with
        {
            MaxFallHeight = PathfinderOptions.WorldHeightSpan,
            MaxFallHeightIntoWater = PathfinderOptions.WorldHeightSpan,
        };

        Assert.Equal(expected, unsafeFalls);
    }
}
