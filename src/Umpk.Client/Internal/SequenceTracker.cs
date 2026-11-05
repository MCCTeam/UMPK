namespace Umpk.Client.Internal;

/// <summary>Owns the native world's block-action sequence and acknowledgments. A captured ticket cannot bind an earlier world's action to a later world that reuses its number.</summary>
internal sealed class SequenceTracker
{
    private readonly Lock _gate = new();
    private Lifetime _lifetime = new();

    internal readonly record struct Ticket(int Sequence, Lifetime Owner);

    internal sealed class Lifetime
    {
        internal int Next;
        internal int Acknowledged;
        internal int LastTeleport = -1;
        internal readonly SortedDictionary<int, TaskCompletionSource> Pending = [];
    }

    internal sealed class LifetimeEndedException() : OperationCanceledException("The block action's world lifetime has ended.", new CancellationToken(true));

    public int Next() => NextTicket().Sequence;

    internal Ticket NextTicket()
    {
        lock (_gate)
            return new Ticket(_lifetime.Next = unchecked(_lifetime.Next + 1), _lifetime);
    }

    internal Lifetime CaptureLifetime()
    {
        lock (_gate)
            return _lifetime;
    }

    internal void ObserveTeleport()
    {
        lock (_gate)
            _lifetime.LastTeleport = _lifetime.Next;
    }

    internal bool CanRestoreCapturedPosition(int acknowledged)
    {
        lock (_gate)
            return _lifetime.LastTeleport < acknowledged;
    }

    public int LastAcknowledged
    {
        get
        {
            lock (_gate)
                return _lifetime.Acknowledged;
        }
    }

    public Task WaitForAsync(int sequence, CancellationToken ct)
        => WaitForAsync(new Ticket(sequence, CaptureLifetime()), ct);

    internal Task WaitForAsync(Ticket ticket, CancellationToken ct)
    {
        TaskCompletionSource pending;
        lock (_gate)
        {
            if (!ReferenceEquals(ticket.Owner, _lifetime))
                return Task.FromException(new LifetimeEndedException());
            if (ticket.Sequence <= ticket.Owner.Acknowledged)
                return Task.CompletedTask;
            if (!ticket.Owner.Pending.TryGetValue(ticket.Sequence, out pending!))
            {
                pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ticket.Owner.Pending.Add(ticket.Sequence, pending);
            }
        }
        return ct.CanBeCanceled ? AwaitWithCancellationAsync(ticket, pending, ct) : pending.Task;
    }

    public void Acknowledge(int sequence)
    {
        List<TaskCompletionSource>? completed = null;
        lock (_gate)
        {
            if (sequence > _lifetime.Acknowledged)
                _lifetime.Acknowledged = sequence;
            foreach (int key in _lifetime.Pending.Keys.ToList())
            {
                if (key > sequence)
                    break;
                (completed ??= []).Add(_lifetime.Pending[key]);
                _lifetime.Pending.Remove(key);
            }
        }
        if (completed is not null)
            foreach (TaskCompletionSource pending in completed)
                pending.TrySetResult();
    }

    /// <summary>The owner calls this after installing or clearing native world state, before releasing old action waiters.</summary>
    internal void ResetLifetime()
    {
        TaskCompletionSource[] pending;
        lock (_gate)
        {
            pending = _lifetime.Pending.Values.ToArray();
            _lifetime.Pending.Clear();
            _lifetime = new Lifetime();
        }
        foreach (TaskCompletionSource wait in pending)
            wait.TrySetException(new LifetimeEndedException());
    }

    private async Task AwaitWithCancellationAsync(Ticket ticket, TaskCompletionSource pending, CancellationToken ct)
    {
        using CancellationTokenRegistration registration = ct.Register(() =>
        {
            lock (_gate)
                if (ticket.Owner.Pending.TryGetValue(ticket.Sequence, out TaskCompletionSource? current) && ReferenceEquals(current, pending))
                    ticket.Owner.Pending.Remove(ticket.Sequence);
            pending.TrySetCanceled(ct);
        });
        await pending.Task.ConfigureAwait(false);
    }
}
