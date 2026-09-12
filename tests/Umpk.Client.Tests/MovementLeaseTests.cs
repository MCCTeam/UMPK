using Umpk.Client.Movement;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class MovementLeaseTests
{
    [Fact]
    public void Acquire_Grants_WhenFree()
    {
        var manager = new MovementLeaseManager();
        IMovementLease? lease = manager.TryAcquire("a");
        Assert.NotNull(lease);
        Assert.True(lease!.IsHeld);
        Assert.Equal("a", manager.CurrentOwner);
    }

    [Fact]
    public void Acquire_Fails_WhenHeld()
    {
        var manager = new MovementLeaseManager();
        using IMovementLease? first = manager.TryAcquire("a");
        IMovementLease? second = manager.TryAcquire("b");
        Assert.Null(second);
    }

    [Fact]
    public void Dispose_Releases_ForNextAcquirer()
    {
        var manager = new MovementLeaseManager();
        IMovementLease? first = manager.TryAcquire("a");
        first!.Dispose();
        Assert.False(first.IsHeld);
        IMovementLease? second = manager.TryAcquire("b");
        Assert.NotNull(second);
        Assert.Equal("b", manager.CurrentOwner);
    }
}
