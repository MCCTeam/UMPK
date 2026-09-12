namespace Umpk.Client.Plugins;

/// <summary>Holds recurring per-tick callbacks and one-shot delayed callbacks. Driven by the session loop's tick. All callbacks run on the loop.</summary>
internal sealed class TickRegistry
{
    private readonly Lock _gate = new();
    private TickSubscription[] _recurring = [];
    private readonly List<DelayedCallback> _delayed = [];

    public IDisposable Add(Action callback)
    {
        var sub = new TickSubscription(this, callback);
        lock (_gate)
            _recurring = [.. _recurring, sub];

        return sub;
    }

    public IDisposable AddDelay(int ticks, Action callback)
    {
        var delayed = new DelayedCallback(ticks, callback);
        lock (_gate)
            _delayed.Add(delayed);

        return delayed;
    }

    /// <summary>Runs one tick: fires recurring callbacks and any due one-shots. Call on the session loop.</summary>
    public void Tick(Action<Exception> onError)
    {
        TickSubscription[] recurring;
        DelayedCallback[] due;
        lock (_gate)
        {
            recurring = _recurring;
            for (int i = _delayed.Count - 1; i >= 0; i--)
                if (_delayed[i].Cancelled)
                    _delayed.RemoveAt(i);

            due = [.. _delayed.Where(d => d.Decrement())];
            _delayed.RemoveAll(d => d.Fired || d.Cancelled);
        }

        foreach (TickSubscription sub in recurring)
        {
            if (sub.Cancelled)
                continue;

            try
            {
                sub.Callback();
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }

        foreach (DelayedCallback d in due)
            try
            {
                d.Invoke();
            }
            catch (Exception ex)
            {
                onError(ex);
            }

    }

    private void Remove(TickSubscription sub)
    {
        lock (_gate)
            _recurring = [.. _recurring.Where(x => !ReferenceEquals(x, sub))];

    }

    private sealed class TickSubscription(TickRegistry owner, Action callback) : IDisposable
    {
        private TickRegistry? _owner = owner;

        public Action Callback { get; } = callback;

        public bool Cancelled => _owner is null;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(this);
    }

    private sealed class DelayedCallback(int ticks, Action callback) : IDisposable
    {
        private int _remaining = ticks;

        public bool Fired { get; private set; }

        public bool Cancelled { get; private set; }

        /// <summary>Decrements the countdown; returns true when it becomes due this tick.</summary>
        public bool Decrement()
        {
            if (Fired || Cancelled)
                return false;

            if (_remaining <= 0)
            {
                Fired = true;
                return true;
            }

            _remaining--;
            if (_remaining <= 0)
            {
                Fired = true;
                return true;
            }

            return false;
        }

        public void Invoke()
        {
            if (!Cancelled)
                callback();

        }

        public void Dispose() => Cancelled = true;
    }
}
