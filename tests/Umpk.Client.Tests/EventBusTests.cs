using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Events;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EventBusTests
{
    private sealed record Ping(int N) : IClientEvent;

    private static EventBus NewBus(TimeSpan? watchdog = null)
        => new(NullLogger.Instance, watchdog ?? TimeSpan.Zero, 8);

    [Fact]
    public async Task Publishes_InSubscriptionOrder()
    {
        EventBus bus = NewBus();
        var order = new List<int>();
        bus.Subscribe<Ping>(_ => order.Add(1));
        bus.Subscribe<Ping>(_ => order.Add(2));
        bus.Subscribe<Ping>(_ => order.Add(3));

        await bus.PublishAsync(new Ping(0));

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task ThrowingHandler_IsIsolated_OthersStillRun()
    {
        EventBus bus = NewBus();
        bool ran = false;
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<Ping>(_ => ran = true);

        await bus.PublishAsync(new Ping(0));

        Assert.True(ran);
    }

    [Fact]
    public async Task AsyncSubscriber_IsAwaitedInline()
    {
        EventBus bus = NewBus();
        var log = new List<string>();
        bus.Subscribe<Ping>(async _ =>
        {
            await Task.Delay(10);
            log.Add("async-done");
        });
        bus.Subscribe<Ping>(_ => log.Add("sync"));

        await bus.PublishAsync(new Ping(0));

        Assert.Equal(["async-done", "sync"], log);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        EventBus bus = NewBus();
        int count = 0;
        IDisposable sub = bus.Subscribe<Ping>(_ => count++);
        sub.Dispose();

        _ = bus.PublishAsync(new Ping(0));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Stream_DropOldest_DoesNotStall_AndReportsLag()
    {
        EventBus bus = NewBus();
        int lagged = 0;
        bus.SetLagPublisher(_ => Interlocked.Increment(ref lagged));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IAsyncEnumerable<Ping> stream = bus.Stream<Ping>(2, StreamFullPolicy.DropOldest, cts.Token);

        // Publish more than the buffer holds before anyone reads: DropOldest must never block.
        for (int i = 0; i < 20; i++)
            await bus.PublishAsync(new Ping(i));

        // DropOldest reports lag on the publish path (never stalling the loop), so the diagnostic is observable without consuming. Drain the two survivors to prove the stream still yields.
        int seen = 0;
        await using IAsyncEnumerator<Ping> e = stream.GetAsyncEnumerator(cts.Token);
        while (seen < 2)
        {
            Task<bool> moveTask = e.MoveNextAsync().AsTask();
            Task done = await Task.WhenAny(moveTask, Task.Delay(500));
            if (done != moveTask || !await moveTask)
                break;

            seen++;
        }

        Assert.True(seen > 0, "expected to drain at least one surviving item");
        Assert.True(lagged > 0, "expected a StreamLagged diagnostic after overflow");
    }

    [Fact]
    public async Task Stream_Throw_FaultsConsumer_OnOverflow()
    {
        EventBus bus = NewBus();
        using var cts = new CancellationTokenSource();
        IAsyncEnumerable<Ping> stream = bus.Stream<Ping>(1, StreamFullPolicy.Throw, cts.Token);

        for (int i = 0; i < 10; i++)
            await bus.PublishAsync(new Ping(i));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (Ping _ in stream.WithCancellation(cts.Token))
            {
            }
        });
    }
}
