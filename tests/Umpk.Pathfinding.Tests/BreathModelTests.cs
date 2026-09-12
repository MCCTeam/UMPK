using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>The shared breath model: how long it takes to reach air from a cell and the air level at which a player at that distance has to stop and surface.</summary>
public sealed class BreathModelTests
{
    private const int FloorY = 64;

    private static readonly PhysicsProfile Modern = PhysicsProfile.ForProtocol(772);
    private static readonly PhysicsProfile LegacyEra = PhysicsProfile.ForProtocol(47);

    /// <summary>A water column with <paramref name="waterAbove"/> water cells over the node at <c>(0, FloorY, 0)</c>, capped either with air or with stone.</summary>
    private static PlanningWorldView Column(int waterAbove, bool lid)
    {
        var world = new FixtureWorld();
        world.Floor(-2, 2, -2, 2, FloorY - 1);
        world.Fill(0, FloorY, 0, 0, FloorY + waterAbove, 0, FixtureWorld.Water);
        if (lid)
            world.Set(0, FloorY + waterAbove + 1, 0, FixtureWorld.Stone);

        return world.Capture(new BlockPos(0, FloorY, 0), new BlockPos(0, FloorY + waterAbove + 2, 0), margin: 4);
    }

    [Theory]
    [InlineData(5, 19.1)]
    [InlineData(10, 32.2)]
    [InlineData(20, 58.4)]
    [InlineData(40, 110.8)]
    public void EscapeTicks_ScalesWithDepth(int depth, double expected)
    {
        BreathEscape escape = BreathModel.EscapeTicks(Column(depth, lid: false), 0, FloorY, 0, Modern);

        Assert.True(escape.IsKnown);
        Assert.Equal(expected, escape.Ticks, 6);
    }

    [Fact]
    public void EscapeTicks_IsUnknownUnderACeiling()
    {
        BreathEscape escape = BreathModel.EscapeTicks(Column(10, lid: true), 0, FloorY, 0, Modern);

        // An unknown must stay silent for the supervisor. Treating it as an infinite escape cost would fire the trip at a full lung and make the bot abandon its route under an overhang.
        Assert.False(
            BreathModel.ShouldSurface(escape, 300),
            $"a full lung under a lid fired the trip: escape={escape.Ticks}, threshold={BreathModel.Threshold(escape.Ticks)}");
        Assert.False(escape.IsKnown);
        Assert.False(BreathModel.ShouldSurface(escape, 1));
    }

    [Fact]
    public void EscapeTicks_IsUnknownBeyondTheScan()
    {
        BreathEscape escape = BreathModel.EscapeTicks(Column(60, lid: false), 0, FloorY, 0, Modern);

        Assert.False(
            BreathModel.ShouldSurface(escape, 300),
            $"a full lung under sixty blocks of water fired the trip: escape={escape.Ticks}, threshold={BreathModel.Threshold(escape.Ticks)}");
        Assert.False(escape.IsKnown);
    }

    [Fact]
    public void EscapeTicks_IsLargerOnLegacyProtocols()
    {
        PlanningWorldView view = Column(10, lid: false);

        BreathEscape modern = BreathModel.EscapeTicks(view, 0, FloorY, 0, Modern);
        BreathEscape legacy = BreathModel.EscapeTicks(view, 0, FloorY, 0, LegacyEra);

        Assert.Equal(32.2, modern.Ticks, 6);
        Assert.Equal(106.0, legacy.Ticks, 6);
    }

    [Fact]
    public void EscapeTicks_IsZeroWhenTheHeadCellIsNotWater()
    {
        // Standing on the pool floor with the head out: nothing to escape from.
        BreathEscape escape = BreathModel.EscapeTicks(Column(0, lid: false), 0, FloorY, 0, Modern);

        Assert.True(escape.IsKnown);
        Assert.Equal(0.0, escape.Ticks);
        Assert.False(BreathModel.ShouldSurface(escape, 300));
    }

    /// <summary>A modern bot may be up to 71 blocks under a ceiling and still hold enough air to get out. At 72 blocks the threshold exceeds a full lung, so the planner must refuse the position.</summary>
    [Fact]
    public void Threshold_ExceedsAFullLungBeyondSeventyOneBlocks()
    {
        Assert.Equal(299.0, BreathModel.Threshold(BreathModel.AscendTicks(71, Modern)));
        Assert.Equal(302.0, BreathModel.Threshold(BreathModel.AscendTicks(72, Modern)));

        Assert.True(BreathModel.Threshold(BreathModel.AscendTicks(71, Modern)) <= BreathModel.FullLungTicks);
        Assert.True(BreathModel.Threshold(BreathModel.AscendTicks(72, Modern)) > BreathModel.FullLungTicks);
    }

    /// <summary>The same table on protocols 47-340, where the climb is a flat ten ticks a block: the crossing is at nineteen blocks, so a legacy bot has less than a third of the modern depth budget.</summary>
    [Fact]
    public void Threshold_ExceedsAFullLungAtNineteenBlocksOnLegacy()
    {
        Assert.Equal(289.0, BreathModel.Threshold(BreathModel.AscendTicks(18, LegacyEra)));
        Assert.Equal(304.0, BreathModel.Threshold(BreathModel.AscendTicks(19, LegacyEra)));

        Assert.True(BreathModel.Threshold(BreathModel.AscendTicks(18, LegacyEra)) <= BreathModel.FullLungTicks);
        Assert.True(BreathModel.Threshold(BreathModel.AscendTicks(19, LegacyEra)) > BreathModel.FullLungTicks);
    }

    [Theory]
    [InlineData(5, 39.0)]
    [InlineData(10, 59.0)]
    [InlineData(20, 98.0)]
    [InlineData(40, 177.0)]
    [InlineData(60, 255.0)]
    public void Threshold_MatchesRepresentativeModernDepths(int depth, double expected)
        => Assert.Equal(expected, BreathModel.Threshold(BreathModel.AscendTicks(depth, Modern)));

    [Fact]
    public void ShouldSurface_FiresExactlyAtTheThreshold()
    {
        BreathEscape escape = BreathModel.EscapeTicks(Column(20, lid: false), 0, FloorY, 0, Modern);

        Assert.Equal(58.4, escape.Ticks, 6);
        Assert.Equal(98.0, BreathModel.Threshold(escape.Ticks));
        Assert.False(BreathModel.ShouldSurface(escape, 98));
        Assert.True(BreathModel.ShouldSurface(escape, 97));
    }

    [Fact]
    public void MaxDeficit_IsAFullLung()
    {
        Assert.Equal(300.0, BreathModel.MaxDeficit(Modern));
        Assert.Equal(300.0, BreathModel.MaxDeficit(LegacyEra));
    }

    [Fact]
    public void IsSubmerged_ReadsTheHeadCell()
    {
        PlanningWorldView view = Column(3, lid: false);

        Assert.True(BreathModel.IsSubmerged(view, 0, FloorY, 0));
        Assert.False(BreathModel.IsSubmerged(view, 0, FloorY + 3, 0));
    }
}
