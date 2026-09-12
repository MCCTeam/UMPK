using Umpk.Hosting;
using Xunit;

namespace Umpk.Tests.Hosting;

public class ChannelSessionSchedulerTests
{
    [Fact]
    public async Task Work_RunsInFifoOrder()
    {
        await using var scheduler = new ChannelSessionScheduler();
        var order = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            int captured = i;
            scheduler.Post(() => order.Add(captured));
        }

        await scheduler.InvokeAsync(() => { }, CancellationToken.None);
        Assert.Equal(Enumerable.Range(0, 100), order);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsResult()
    {
        await using var scheduler = new ChannelSessionScheduler();
        int result = await scheduler.InvokeAsync(() => 21 * 2, CancellationToken.None);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesExceptions()
    {
        await using var scheduler = new ChannelSessionScheduler();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.InvokeAsync(() => throw new InvalidOperationException("boom"), CancellationToken.None));
    }

    [Fact]
    public async Task Post_ExceptionsGoToErrorSink_AndLoopSurvives()
    {
        var errors = new List<Exception>();
        await using var scheduler = new ChannelSessionScheduler(errors.Add);
        scheduler.Post(() => throw new InvalidOperationException("posted"));
        int result = await scheduler.InvokeAsync(() => 7, CancellationToken.None);

        Assert.Equal(7, result);
        var error = Assert.Single(errors);
        Assert.Equal("posted", error.Message);
    }

    [Fact]
    public async Task AsyncWork_IsSerialized_NoInterleaving()
    {
        await using var scheduler = new ChannelSessionScheduler();
        var log = new List<string>();
        var first = scheduler.InvokeAsync(async () =>
        {
            log.Add("first-start");
            await Task.Delay(50);
            log.Add("first-end");
        }, CancellationToken.None);
        var second = scheduler.InvokeAsync(() => log.Add("second"), CancellationToken.None);

        await Task.WhenAll(first, second);
        Assert.Equal(["first-start", "first-end", "second"], log);
    }

    [Fact]
    public async Task IsCurrent_TrueOnLoop_FalseOutside()
    {
        await using var scheduler = new ChannelSessionScheduler();
        Assert.False(scheduler.IsCurrent);
        bool onLoop = await scheduler.InvokeAsync(() => scheduler.IsCurrent, CancellationToken.None);
        Assert.True(onLoop);
    }

    [Fact]
    public async Task TwoSchedulers_DoNotObserveEachOtherAsCurrent()
    {
        await using var a = new ChannelSessionScheduler();
        await using var b = new ChannelSessionScheduler();
        (bool aIsA, bool aIsB) = await a.InvokeAsync(() => (a.IsCurrent, b.IsCurrent), CancellationToken.None);
        Assert.True(aIsA);
        Assert.False(aIsB);
    }

    [Fact]
    public async Task CancelledInvoke_DoesNotRunWork()
    {
        await using var scheduler = new ChannelSessionScheduler();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool ran = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduler.InvokeAsync(() => { ran = true; }, cts.Token));
        await scheduler.InvokeAsync(() => { }, CancellationToken.None);
        Assert.False(ran);
    }

    [Fact]
    public async Task DisposeAsync_DrainsQueuedWork_ThenRejectsNew()
    {
        var scheduler = new ChannelSessionScheduler();
        int completed = 0;
        for (int i = 0; i < 10; i++)
            scheduler.Post(() => Interlocked.Increment(ref completed));

        await scheduler.DisposeAsync();
        Assert.Equal(10, completed);
        Assert.Throws<ObjectDisposedException>(() => scheduler.Post(() => { }));
    }
}
