using Umpk.Hosting;
using Xunit;

namespace Umpk.Tests.Hosting;

public class ManualTickSourceTests
{
    [Fact]
    public async Task Advance_ProducesExactlyThatManyTicks_Deterministically()
    {
        var source = new ManualTickSource();
        source.Advance(3);
        source.Complete();

        var ticks = new List<long>();
        await foreach (long tick in source.Ticks())
            ticks.Add(tick);

        Assert.Equal([0L, 1L, 2L], ticks);
    }

    [Fact]
    public async Task Consumer_WaitsUntilAdvanced()
    {
        var source = new ManualTickSource();
        var received = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            await foreach (long tick in source.Ticks())
            {
                received.TrySetResult(tick);
                return;
            }
        });

        await Task.Delay(50);
        Assert.False(received.Task.IsCompleted);

        source.Advance();
        long value = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, value);
    }

    [Fact]
    public void Advance_RejectsNonPositiveCounts()
    {
        var source = new ManualTickSource();
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Advance(0));
    }

    [Fact]
    public void TickInterval_DefaultsToVanilla50Ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(50), new ManualTickSource().TickInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(25), new ManualTickSource(TimeSpan.FromMilliseconds(25)).TickInterval);
    }
}

public class PeriodicTimerTickSourceTests
{
    [Fact]
    public void DefaultInterval_IsTwentyTps()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(50), new PeriodicTimerTickSource().TickInterval);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicTimerTickSource(TimeSpan.Zero));
    }

    [Fact]
    public async Task Ticks_YieldIncreasingNumbers()
    {
        var source = new PeriodicTimerTickSource(TimeSpan.FromMilliseconds(1));
        var ticks = new List<long>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (long tick in source.Ticks(cts.Token))
        {
            ticks.Add(tick);
            if (ticks.Count == 3)
                break;

        }

        Assert.Equal([0L, 1L, 2L], ticks);
    }

    [Fact]
    public async Task Ticks_StopOnCancellation()
    {
        var source = new PeriodicTimerTickSource(TimeSpan.FromMilliseconds(1));
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (long _ in source.Ticks(cts.Token))
                cts.Cancel();

        });
    }
}
