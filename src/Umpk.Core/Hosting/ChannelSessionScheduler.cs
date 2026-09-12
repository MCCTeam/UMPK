using System.Threading.Channels;

namespace Umpk.Hosting;

/// <summary>The default <see cref="ISessionScheduler"/>: an unbounded channel drained by one long-running task. Work runs strictly serialized in FIFO order. Disposal stops accepting new work, drains what was already queued, and awaits the loop.</summary>
public sealed class ChannelSessionScheduler : ISessionScheduler
{
    private static readonly AsyncLocal<ChannelSessionScheduler?> CurrentScheduler = new();

    private readonly Channel<WorkItem> _work = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly Action<Exception>? _errorSink;
    private readonly Task _loop;
    private bool _disposed;

    /// <summary>Creates the scheduler and starts its drain loop.</summary>
    /// <param name="errorSink">Receives exceptions from <see cref="Post"/>ed work (InvokeAsync failures travel through the returned task instead). When null, such exceptions are swallowed.</param>
    public ChannelSessionScheduler(Action<Exception>? errorSink = null)
    {
        _errorSink = errorSink;
        _loop = Task.Run(DrainAsync);
    }

    public bool IsCurrent => ReferenceEquals(CurrentScheduler.Value, this);

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Enqueue(new WorkItem(work, null, CancellationToken.None));
    }

    public Task InvokeAsync(Action work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(() =>
        {
            work();
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(() => ValueTask.FromResult(work()), cancellationToken);
    }

    public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(async () =>
        {
            await work().ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public async Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(null, async () =>
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
        }, cancellationToken));

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
                ((TaskCompletionSource<TResult>)state!).TrySetCanceled(), completion)
            : default;
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _work.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    private void Enqueue(WorkItem item)
    {
        if (!_work.Writer.TryWrite(item))
            throw new ObjectDisposedException(nameof(ChannelSessionScheduler));

    }

    private async Task DrainAsync()
    {
        CurrentScheduler.Value = this;
        await foreach (var item in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.CancellationToken.IsCancellationRequested)
                continue;

            try
            {
                if (item.Synchronous is not null)
                    item.Synchronous();

                else
                    await item.Asynchronous!().ConfigureAwait(false);

            }
            catch (Exception exception)
            {
                _errorSink?.Invoke(exception);
            }
        }
    }

    private readonly record struct WorkItem(Action? Synchronous, Func<Task>? Asynchronous, CancellationToken CancellationToken);
}
