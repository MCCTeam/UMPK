using Umpk.Hosting;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>the Navigator captures its planning region on the session loop, then runs the A* search off the loop (the navigation threading model). This exercises the exact primitives the Navigator uses (<see cref="ISessionScheduler.InvokeAsync{TResult}(System.Func{TResult}, System.Threading.CancellationToken)"/> for the on-loop capture and <see cref="System.Threading.Tasks.Task.Run(System.Func{int})"/> for the off-loop search) and asserts the loop stays responsive with tick ordering preserved while a long search is in flight. If the search runs on the loop, the subsequent tick calls queue behind it and this test would deadlock until the search released.</summary>
public sealed class NavigationThreadingTests
{
    [Fact]
    public async Task Planning_CapturesOnLoop_SearchesOffLoop_LoopStaysResponsive()
    {
        await using var loop = new ChannelSessionScheduler();

        bool captureRanOnLoop = false;
        bool searchRanOnLoop = true;
        using var searchGate = new ManualResetEventSlim(false);
        var tickOrder = new List<int>();

        // Mirror Navigator.PlanOnLoopAsync: cheap region capture on the loop, A* search off the loop.
        async Task<int> PlanAsync()
        {
            int captured = await loop.InvokeAsync(
                () =>
                {
                    captureRanOnLoop = loop.IsCurrent; // region capture must run on the loop
                    return 7;
                },
                CancellationToken.None).ConfigureAwait(false);

            return await Task.Run(() =>
            {
                searchRanOnLoop = loop.IsCurrent;      // the A* search must run off the loop
                searchGate.Wait(5000);                 // a long search
                return captured * 6;
            }).ConfigureAwait(false);
        }

        Task<int> planning = PlanAsync();

        // While the search is blocked off-loop, the session loop keeps processing tick work in order.
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            await loop.InvokeAsync(() => tickOrder.Add(n), CancellationToken.None);
        }

        Assert.False(planning.IsCompleted);          // still searching
        Assert.Equal([0, 1, 2, 3, 4], tickOrder);    // loop responsive + FIFO tick ordering preserved

        searchGate.Set();
        Assert.Equal(42, await planning);
        Assert.True(captureRanOnLoop);
        Assert.False(searchRanOnLoop);
    }
}
