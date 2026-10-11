using Umpk.Client.Bedrock.Actions;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Inventory;

public sealed class BedrockActionCoordinatorTests
{
    [Fact]
    public void StandaloneClaimBlocksEmbeddedEmission()
    {
        var coordinator = new BedrockActionCoordinator();

        Assert.True(coordinator.TryClaim(11, BedrockActionEmission.Standalone));
        Assert.False(coordinator.TryClaim(11, BedrockActionEmission.Embedded));
        Assert.True(coordinator.IsClaimed(11));
    }

    [Fact]
    public void EmbeddedClaimBlocksStandaloneEmission()
    {
        var coordinator = new BedrockActionCoordinator();

        Assert.True(coordinator.TryClaim(12, BedrockActionEmission.Embedded));
        Assert.False(coordinator.TryClaim(12, BedrockActionEmission.Standalone));
    }

    [Fact]
    public void ReleaseAllowsReclaim()
    {
        var coordinator = new BedrockActionCoordinator();

        Assert.True(coordinator.TryClaim(13, BedrockActionEmission.Standalone));
        coordinator.Release(13);

        Assert.False(coordinator.IsClaimed(13));
        Assert.True(coordinator.TryClaim(13, BedrockActionEmission.Embedded));
    }

    [Fact]
    public void ReplayClaimsOnlyUnclaimedIdentities()
    {
        var coordinator = new BedrockActionCoordinator();

        Assert.True(coordinator.TryBeginReplay([21, 22]));
        Assert.Equal(2, coordinator.ClaimedCount);
        Assert.False(coordinator.TryBeginReplay([22, 23]));
        Assert.False(coordinator.IsClaimed(23));
    }

    [Fact]
    public void ReplayNeverDuplicatesAStandaloneAction()
    {
        var coordinator = new BedrockActionCoordinator();

        Assert.True(coordinator.TryClaim(24, BedrockActionEmission.Standalone));
        Assert.False(coordinator.TryBeginReplay([24]));
    }
}
