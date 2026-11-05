using Umpk.Client.Movement;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PlayerTickReadinessBoundaryTests
{
    [Fact]
    public void OneTwentyOneTwoDoesNotGatePlayerMovementOnTerrain()
    {
        var readiness = new MovementReadiness();
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(768, true, false));
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    public void TheNativeLoadTimeoutStartsThePlayerOnTickSixty(int protocol)
    {
        var readiness = new MovementReadiness();
        for (int tick = 1; tick < 60; tick++)
            Assert.Equal(new ReadinessTick(false, false), readiness.Advance(protocol, true, false));
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(protocol, true, false));
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(protocol, true, true));
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    public void TheNativeLoadedPlayerStaysEligibleAfterUnload(int protocol)
    {
        var readiness = new MovementReadiness();
        Assert.Equal(new ReadinessTick(true, true), readiness.Advance(protocol, true, true));
        Assert.Equal(new ReadinessTick(true, false), readiness.Advance(protocol, true, false));
    }
    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public void ConnectionLoadTrackingDoesNotHaveTheOldPlayerTimeout(int protocol)
    {
        var readiness = new MovementReadiness();
        for (int tick = 0; tick < 65; tick++)
            Assert.Equal(new ReadinessTick(false, false), readiness.Advance(protocol, true, false));
    }

}
