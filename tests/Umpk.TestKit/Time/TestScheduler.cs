using System.Threading.Channels;
using Umpk.Hosting;

namespace Umpk.TestKit.Time;

/// <summary>A manually pumped <see cref="ISessionScheduler"/> for deterministic session-loop tests. Unlike <c>Umpk.Core</c>'s <see cref="ChannelSessionScheduler"/>, which drains on its own background task, this scheduler executes queued work only when the test calls <see cref="PumpAsync"/> (or <see cref="RunUntilIdleAsync"/>). That lets a test interleave "advance a tick", "deliver a packet", and "run the scheduler" in a fixed, reproducible order.</summary>
/// <remarks>FIFO ordering and the one-item-at-a-time contract of <see cref="ISessionScheduler"/> hold: asynchronous work items are awaited to completion before the next item runs. Errors from <see cref="Post"/>ed work go to the error sink; <c>InvokeAsync</c> failures travel through the returned task.</remarks>
public sealed class TestScheduler : ISessionScheduler
{
    private static readonly AsyncLocal<TestScheduler?> Current = new();

    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly Action<Exception>? _errorSink;
    private bool _disposed;

    /// <summary>Creates a manually pumped scheduler with an optional error sink.</summary>
    public TestScheduler(Action<Exception>? errorSink = null) => _errorSink = errorSink;

    /// <inheritdoc />
    public bool IsCurrent => ReferenceEquals(Current.Value, this);

    /// <inheritdoc />
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Enqueue(() =>
        {
            work();
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(() =>
        {
            work();
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(() => ValueTask.FromResult(work()), cancellationToken);
    }

    /// <inheritdoc />
    public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(async () =>
        {
            await work().ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(async () =>
        {
            if (completion.Task.IsCompleted)
                return;

            try
            {
                completion.TrySetResult(await work().ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(static state =>
                ((TaskCompletionSource<TResult>)state!).TrySetCanceled(), completion);

        return completion.Task;
    }

    /// <summary>Runs up to <paramref name="maxItems"/> queued work items (default: all currently queued), returning the number actually run. Does not wait for new work to arrive.</summary>
    public async Task<int> PumpAsync(int maxItems = int.MaxValue)
    {
        int ran = 0;
        Current.Value = this;
        try
        {
            while (ran < maxItems && _work.Reader.TryRead(out Func<Task>? item))
            {
                await RunOne(item).ConfigureAwait(false);
                ran++;
            }
        }
        finally
        {
            Current.Value = null;
        }

        return ran;
    }

    /// <summary>Pumps repeatedly until the queue is empty and stays empty across a yield, so work that re-posts more work settles. Bounded by <paramref name="maxRounds"/> to avoid a livelock.</summary>
    public async Task RunUntilIdleAsync(int maxRounds = 1000)
    {
        for (int round = 0; round < maxRounds; round++)
        {
            int ran = await PumpAsync().ConfigureAwait(false);
            if (ran == 0)
            {
                await Task.Yield();
                if (_work.Reader.Count == 0)
                    return;

            }
        }

        throw new InvalidOperationException($"TestScheduler did not reach idle within {maxRounds} rounds.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _work.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async Task RunOne(Func<Task> item)
    {
        try
        {
            await item().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _errorSink?.Invoke(exception);
        }
    }

    private void Enqueue(Func<Task> item)
    {
        if (_disposed || !_work.Writer.TryWrite(item))
            throw new ObjectDisposedException(nameof(TestScheduler));

    }
}
