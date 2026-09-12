using Umpk.Hosting;
using Umpk.TestKit.Time;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Confirms the session scheduler serializes work in FIFO order (session-loop ordering). The client runs all appliers, events, ticks, and posted work through this single loop.</summary>
public sealed class SchedulerOrderingTests
{
    [Fact]
    public async Task Work_Runs_In_FIFO_Order()
    {
        var scheduler = new TestScheduler();
        var order = new List<int>();

        for (int i = 0; i < 5; i++)
        {
            int captured = i;
            scheduler.Post(() => order.Add(captured));
        }

        await scheduler.PumpAsync();

        Assert.Equal([0, 1, 2, 3, 4], order);
    }

    [Fact]
    public async Task InvokeAsync_Marshals_Result()
    {
        await using var scheduler = new ChannelSessionScheduler();
        int result = await scheduler.InvokeAsync(() => 21 * 2, CancellationToken.None);
        Assert.Equal(42, result);
    }
}
