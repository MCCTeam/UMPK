using Umpk.Client.Internal;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task UnhandledPacketsDoNotAllocateAnApplierEnumerator()
    {
        var dispatcher = new Dispatcher(new List<IApplier> { new UnhandledApplier(), new UnhandledApplier() });
        var packet = new object();
        for (int i = 0; i < 1000; i++)
            await dispatcher.DispatchAsync(packet, default!, CancellationToken.None);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            await dispatcher.DispatchAsync(packet, default!, CancellationToken.None);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated <= 8000, $"Dispatch allocated {allocated / 1000} bytes per packet.");
    }

    [Fact]
    public async Task DispatchAwaitsEachApplierAndStopsAtTheFirstOwner()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<int>();
        using var cancellation = new CancellationTokenSource();
        var dispatcher = new Dispatcher(new List<IApplier>
        {
            new CallbackApplier(async ct =>
            {
                Assert.Equal(cancellation.Token, ct);
                calls.Add(1);
                return await release.Task;
            }),
            new CallbackApplier(ct =>
            {
                Assert.Equal(cancellation.Token, ct);
                calls.Add(2);
                return ValueTask.FromResult(true);
            }),
            new CallbackApplier(_ => throw new InvalidOperationException("Must not run after an owner.")),
        });

        Task dispatch = dispatcher.DispatchAsync(new object(), default!, cancellation.Token).AsTask();
        Assert.False(dispatch.IsCompleted);
        Assert.Equal([1], calls);
        release.SetResult(false);
        await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public async Task DispatchPropagatesAnApplierFailureWithoutCallingTheNextApplier()
    {
        var error = new InvalidOperationException("Applier failure.");
        var dispatcher = new Dispatcher(new List<IApplier>
        {
            new CallbackApplier(_ => ValueTask.FromException<bool>(error)),
            new CallbackApplier(_ => throw new InvalidOperationException("Must not run after a failure.")),
        });

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(new object(), default!, CancellationToken.None).AsTask());
        Assert.Same(error, observed);
    }

    private sealed class UnhandledApplier : IApplier
    {
        public ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct) => ValueTask.FromResult(false);
    }

    private sealed class CallbackApplier(Func<CancellationToken, ValueTask<bool>> apply) : IApplier
    {
        public ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct) => apply(ct);
    }
}
