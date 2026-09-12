namespace Umpk.Client.Internal;

/// <summary>Tracks the monotonic block-action sequence number (1.19+) the client attaches to dig/place/use packets and that the server acknowledges via BlockChangedAck. Callers can await a specific sequence so a dig/place future completes on protocol acknowledgment; on pre-1.19 versions the sequence is unused and completion falls back to the write. Instance state, owned by the session.</summary>
internal sealed class SequenceTracker
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<int, TaskCompletionSource> _pending = [];
    private int _next;

    /// <summary>Returns the next sequence number to attach to an outbound action.</summary>
    public int Next() => Interlocked.Increment(ref _next);

    /// <summary>The highest sequence the server has acknowledged.</summary>
    public int LastAcknowledged { get; private set; }

    /// <summary>Completes when the server acknowledges <paramref name="sequence"/> (or already has). Cancellation (e.g. a bounded caller timeout) drops the pending wait so it does not leak.</summary>
    public Task WaitForAsync(int sequence, CancellationToken ct)
    {
        TaskCompletionSource tcs;
        lock (_gate)
        {
            if (sequence <= LastAcknowledged)
                return Task.CompletedTask;

            if (!_pending.TryGetValue(sequence, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[sequence] = tcs;
            }
        }

        return ct.CanBeCanceled ? AwaitWithCancellationAsync(tcs, sequence, ct) : tcs.Task;
    }

    /// <summary>Records a server acknowledgment, completing every pending wait up to <paramref name="sequence"/>.</summary>
    public void Acknowledge(int sequence)
    {
        List<TaskCompletionSource>? completed = null;
        lock (_gate)
        {
            if (sequence > LastAcknowledged)
                LastAcknowledged = sequence;

            foreach (int key in _pending.Keys.ToList())
            {
                if (key > sequence)
                    break;

                (completed ??= []).Add(_pending[key]);
                _pending.Remove(key);
            }
        }

        if (completed is null)
            return;

        foreach (TaskCompletionSource tcs in completed)
            tcs.TrySetResult();

    }

    private async Task AwaitWithCancellationAsync(TaskCompletionSource tcs, int sequence, CancellationToken ct)
    {
        using CancellationTokenRegistration registration = ct.Register(() =>
        {
            lock (_gate)
                _pending.Remove(sequence);

            tcs.TrySetCanceled(ct);
        });

        await tcs.Task.ConfigureAwait(false);
    }
}
