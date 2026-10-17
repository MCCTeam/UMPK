using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Hosting;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class NativeClientTickSourceTests
{
    [Fact]
    public async Task RateChangesKeepTheAccumulatedFraction()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var state = new WorldTickState();
        state.ApplyState(10, false);
        var time = new ManualTimeProvider();
        var frames = new Frames(time);
        var source = new NativeClientTickSource(state, time, frames);
        Assert.Equal(TimeSpan.FromMilliseconds(100), source.TickInterval);
        await using IAsyncEnumerator<long> ticks = source.Ticks(budget.Token).GetAsyncEnumerator();
        Task<bool> pending = ticks.MoveNextAsync().AsTask();
        await frames.Push(50).WaitAsync(budget.Token);
        Assert.False(pending.IsCompleted);

        state.ApplyState(20, false);
        Task processed = frames.Push(75);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(0, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);
        processed = frames.Push(125);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(1, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task FrozenClientKeepsTwentyTicksWhileGrantedWorldStepsUseTheSlowRate()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var state = new WorldTickState();
        state.ApplyState(1, true);
        state.BeginTick();
        var time = new ManualTimeProvider();
        var frames = new Frames(time);
        var source = new NativeClientTickSource(state, time, frames);
        await using IAsyncEnumerator<long> ticks = source.Ticks(budget.Token).GetAsyncEnumerator();
        Task<bool> pending = ticks.MoveNextAsync().AsTask();
        Task processed = frames.Push(50);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(0, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);

        state.SetSteps(1);
        state.BeginTick();
        Assert.Equal(TimeSpan.FromSeconds(1), source.TickInterval);
        await frames.Push(100).WaitAsync(budget.Token);
        Assert.False(pending.IsCompleted);
        processed = frames.Push(1050);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(1, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);

        state.BeginTick();
        Assert.Equal(TimeSpan.FromMilliseconds(50), source.TickInterval);
        processed = frames.Push(1100);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(2, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task LongFrameRunsTenTicksAndDiscardsTheRemainingWholeTicks()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var time = new ManualTimeProvider();
        var frames = new Frames(time);
        var source = new NativeClientTickSource(new WorldTickState(), time, frames);
        await using IAsyncEnumerator<long> ticks = source.Ticks(budget.Token).GetAsyncEnumerator();
        Task<bool> pending = ticks.MoveNextAsync().AsTask();
        Task processed = frames.Push(5000);
        for (int index = 0; index < 10; index++)
        {
            Assert.True(await pending.WaitAsync(budget.Token));
            Assert.Equal(index, ticks.Current);
            pending = ticks.MoveNextAsync().AsTask();
        }
        await processed.WaitAsync(budget.Token);
        Assert.False(pending.IsCompleted);
        processed = frames.Push(5050);
        Assert.True(await pending.WaitAsync(budget.Token));
        Assert.Equal(10, ticks.Current);
        pending = ticks.MoveNextAsync().AsTask();
        await processed.WaitAsync(budget.Token);
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, frames.Disposals);
    }

    [Fact]
    public async Task DefaultFramePumpOwnsAndDisposesOneFixedTimer()
    {
        using var budget = new CancellationTokenSource();
        var state = new WorldTickState();
        var time = new TimerProvider();
        var source = new NativeClientTickSource(state, time);
        await using IAsyncEnumerator<long> ticks = source.Ticks(budget.Token).GetAsyncEnumerator();
        Task<bool> pending = ticks.MoveNextAsync().AsTask();
        Assert.Equal(1, time.Created);
        Assert.Equal(TimeSpan.FromMilliseconds(50), time.Period);
        state.ApplyState(1, false);
        Assert.Equal(1, time.Created);
        Assert.Equal(TimeSpan.FromSeconds(1), source.TickInterval);
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, time.Disposals);
    }

    private sealed record Frame(long Milliseconds, TaskCompletionSource Processed);

    private sealed class Frames(ManualTimeProvider time) : ITickSource
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
        private long _milliseconds;
        internal int Disposals { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        internal Task Push(long milliseconds)
        {
            var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(_frames.Writer.TryWrite(new Frame(milliseconds, processed)));
            return processed.Task;
        }

        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            long sequence = 0;
            try
            {
                await foreach (Frame frame in _frames.Reader.ReadAllAsync(cancellationToken))
                {
                    time.Advance(TimeSpan.FromMilliseconds(frame.Milliseconds - _milliseconds));
                    _milliseconds = frame.Milliseconds;
                    yield return sequence++;
                    frame.Processed.TrySetResult();
                }
            }
            finally
            {
                Disposals++;
            }
        }
    }

    private sealed class TimerProvider : TimeProvider
    {
        internal int Created { get; private set; }
        internal int Disposals { get; private set; }
        internal TimeSpan Period { get; private set; }
        public override long GetTimestamp() => 0;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++;
            Period = period;
            return new OwnedTimer(this);
        }

        private sealed class OwnedTimer(TimerProvider owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => owner.Disposals++;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
