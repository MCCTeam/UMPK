namespace Umpk.Events;

/// <summary>A thread-safe, typed subscription list: the primitive the client event bus builds on. Handlers are invoked in subscription order; a throwing handler never prevents the remaining handlers from running (exception isolation). Unsubscription is the returned <see cref="IDisposable"/>; disposing twice is harmless.</summary>
public sealed class SubscriptionList<T>
{
    private readonly Lock _gate = new();
    private readonly Action<Exception, Delegate>? _errorSink;
    private Subscription[] _subscriptions = [];

    /// <summary>Creates a subscription list.</summary>
    /// <param name="errorSink">Receives exceptions thrown by handlers together with the faulting handler. When null, handler exceptions are swallowed after isolation.</param>
    public SubscriptionList(Action<Exception, Delegate>? errorSink = null)
    {
        _errorSink = errorSink;
    }

    /// <summary>The current number of subscribers.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _subscriptions.Length;

        }
    }

    /// <summary>Adds a handler; dispose the returned token to unsubscribe.</summary>
    public IDisposable Subscribe(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        lock (_gate)
        {
            var next = new Subscription[_subscriptions.Length + 1];
            Array.Copy(_subscriptions, next, _subscriptions.Length);
            next[^1] = subscription;
            _subscriptions = next;
        }

        return subscription;
    }

    /// <summary>Invokes all current handlers in subscription order with exception isolation. Handlers subscribed or disposed during invocation take effect on the next invocation.</summary>
    public void Invoke(T args)
    {
        Subscription[] snapshot;
        lock (_gate)
            snapshot = _subscriptions;

        foreach (var subscription in snapshot)
            try
            {
                subscription.Handler(args);
            }
            catch (Exception exception)
            {
                _errorSink?.Invoke(exception, subscription.Handler);
            }

    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            int index = Array.IndexOf(_subscriptions, subscription);
            if (index < 0)
                return;

            var next = new Subscription[_subscriptions.Length - 1];
            Array.Copy(_subscriptions, next, index);
            Array.Copy(_subscriptions, index + 1, next, index, next.Length - index);
            _subscriptions = next;
        }
    }

    private sealed class Subscription(SubscriptionList<T> owner, Action<T> handler) : IDisposable
    {
        public Action<T> Handler { get; } = handler;

        private SubscriptionList<T>? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Remove(this);
        }
    }
}
