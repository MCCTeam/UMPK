using Umpk.Client.Movement;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class MovementReadinessTests
{
    [Fact]
    public void PlacementAndTerrainAreRequiredBeforeModernAnnouncement()
    {
        var readiness = new MovementReadiness();

        Assert.Equal(new ReadinessTick(false, false), readiness.Advance(774, placed: false, localColumnLoaded: true));
        Assert.Equal(new ReadinessTick(false, false), readiness.Advance(774, placed: true, localColumnLoaded: false));
        Assert.Equal(new ReadinessTick(true, true), readiness.Advance(774, placed: true, localColumnLoaded: true));
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(774, placed: true, localColumnLoaded: true));
    }

    [Fact]
    public void ModernReadinessStaysOpenAfterTheInitialColumnUnload()
    {
        var readiness = new MovementReadiness();

        _ = readiness.Advance(774, placed: true, localColumnLoaded: true);

        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(774, placed: true, localColumnLoaded: false));
    }

    [Fact]
    public void LegacyMovementContinuesToFollowTheCurrentColumn()
    {
        var readiness = new MovementReadiness();

        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(768, placed: true, localColumnLoaded: true));
        Assert.Equal(new ReadinessTick(false, false), readiness.Advance(768, placed: true, localColumnLoaded: false));
    }

    [Fact]
    public void OneTwentyOneFiveFallbackOpensOnTickSixtyAndSuppressesLateAnnouncement()
    {
        var readiness = new MovementReadiness();

        for (int tick = 1; tick < 60; tick++)
            Assert.Equal(new ReadinessTick(false, false), readiness.Advance(770, placed: true, localColumnLoaded: false));

        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(770, placed: true, localColumnLoaded: false));
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(770, placed: true, localColumnLoaded: true));
    }

    [Fact]
    public void ResetRequiresReadinessAgainAndAllowsOneNewAnnouncement()
    {
        var readiness = new MovementReadiness();
        Assert.True(readiness.Advance(774, placed: true, localColumnLoaded: true).AnnouncePlayerLoaded);

        readiness.Reset();

        Assert.True(readiness.Advance(774, placed: true, localColumnLoaded: true).AnnouncePlayerLoaded);
        Assert.False(readiness.Advance(774, placed: true, localColumnLoaded: true).AnnouncePlayerLoaded);
    }
}
