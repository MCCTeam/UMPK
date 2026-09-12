using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Pathfinding.Goals;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Distinguishes an unavailable session from cancellation of an active navigation request.</summary>
public sealed class NavigationSessionLifetimeTests
{
    private const string NotConnected = "The client is not connected.";

    [Fact]
    public async Task IdleMove_FailsAsNotConnectedRatherThanCancelled()
    {
        await using UmpkClient client = IdleClient();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Navigation.MoveToAsync(new Vec3d(1.5, 64, 1.5), CancellationToken.None));

        Assert.Equal(NotConnected, error.Message);
    }

    [Fact]
    public async Task IdleVerifiedMove_FailsAsNotConnectedRatherThanCancelled()
    {
        await using UmpkClient client = IdleClient();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Actions.Movement.MoveToVerifiedAsync(
                new Vec3d(1.5, 64, 1.5), CancellationToken.None));

        Assert.Equal(NotConnected, error.Message);
    }

    [Fact]
    public async Task IdleNavigate_FailsAsNotConnectedRatherThanCancelled()
    {
        await using UmpkClient client = IdleClient();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Navigation.NavigateAsync(
                new GoalBlock(new BlockPos(1, 64, 1)), CancellationToken.None));

        Assert.Equal(NotConnected, error.Message);
    }

    [Fact]
    public async Task CallerCancellation_WinsEvenWhenTheClientIsIdle()
    {
        await using UmpkClient client = IdleClient();
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.Navigation.MoveToAsync(new Vec3d(1.5, 64, 1.5), caller.Token));
    }

    private static UmpkClient IdleClient() => new UmpkClientBuilder()
        .UseVersion(JavaVersions.V1_21_5)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .ConfigureFeatures(features =>
        {
            features.Terrain = true;
            features.Physics = true;
            features.Pathfinding = true;
        })
        .Build();
}
