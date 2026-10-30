using Umpk.Client.Internal;
using Xunit;

namespace Umpk.Client.Tests;

// New captured-ticket contract checks. Excluded from the old-source258-case RED compilation because the old owner does not expose this contract.
public sealed class SequenceTrackerLifetimeContractTests
{
    [Fact]
    public async Task TicketCapturedBeforeWorldResetRejectsDelayedWaitRegistration()
    {
        var tracker = new SequenceTracker();
        SequenceTracker.Ticket old = tracker.NextTicket();
        Assert.Equal(1, old.Sequence);
        tracker.ResetLifetime();
        SequenceTracker.Ticket current = tracker.NextTicket();
        Assert.Equal(1, current.Sequence);
        Task currentWait = tracker.WaitForAsync(current, CancellationToken.None);
        await Assert.ThrowsAsync<SequenceTracker.LifetimeEndedException>(() => tracker.WaitForAsync(old, CancellationToken.None));
        Assert.False(currentWait.IsCompleted);
        tracker.Acknowledge(1);
        await currentWait;
    }

    [Fact]
    public async Task OldCancellationCannotRemoveTheNewSameNumberWaiter()
    {
        var tracker = new SequenceTracker();
        using var oldCaller = new CancellationTokenSource();
        SequenceTracker.Ticket old = tracker.NextTicket();
        Task oldWait = tracker.WaitForAsync(old, oldCaller.Token);
        tracker.ResetLifetime();
        SequenceTracker.Ticket current = tracker.NextTicket();
        Assert.Equal(old.Sequence, current.Sequence);
        Task currentWait = tracker.WaitForAsync(current, CancellationToken.None);
        oldCaller.Cancel();
        await Assert.ThrowsAsync<SequenceTracker.LifetimeEndedException>(() => oldWait);
        Assert.False(currentWait.IsCompleted);
        tracker.Acknowledge(current.Sequence);
        await currentWait;
    }
}
