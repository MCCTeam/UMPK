using Umpk.Client.Events;

namespace Umpk.Client;

/// <summary>The typed event bus surface of a client session. Handlers run on the session loop in subscription order with exception isolation; async subscribers are awaited inline. Streams are bounded per subscription so slow consumers never stall the loop. Plugin contexts hand out a tracking subclass so a plugin's subscriptions are auto-disposed on detach.</summary>
public class ClientEvents
{
    private readonly EventBus _bus;

    internal ClientEvents(EventBus bus)
    {
        _bus = bus;
    }

    /// <summary>Subscribes a synchronous handler for an event type.</summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IClientEvent
        => Register(_bus.Subscribe(handler));

    /// <summary>Subscribes an async handler; it is awaited inline to preserve event ordering.</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : IClientEvent
        => Register(_bus.Subscribe(handler));

    /// <summary>Consumes events of a type through a bounded channel with the default full-policy.</summary>
    public IAsyncEnumerable<TEvent> Stream<TEvent>(CancellationToken ct)
        where TEvent : IClientEvent
        => StreamCore<TEvent>(token => _bus.Stream<TEvent>(token), ct);

    /// <summary>Consumes events of a type through a bounded channel with an explicit capacity and policy.</summary>
    public IAsyncEnumerable<TEvent> Stream<TEvent>(int capacity, StreamFullPolicy policy, CancellationToken ct)
        where TEvent : IClientEvent
        => StreamCore<TEvent>(token => _bus.Stream<TEvent>(capacity, policy, token), ct);

    /// <summary>Hook for subclasses to track subscription handles (plugin auto-teardown).</summary>
    private protected virtual IDisposable Register(IDisposable subscription) => subscription;

    /// <summary>Hook for subclasses to bind a stream's lifetime to something beyond the caller's own token (plugin auto-teardown). <paramref name="stream"/> builds the underlying stream from whatever token this hook decides to run it with; the base implementation runs it with the caller's token, unchanged.</summary>
    private protected virtual IAsyncEnumerable<TEvent> StreamCore<TEvent>(
        Func<CancellationToken, IAsyncEnumerable<TEvent>> stream, CancellationToken ct)
        where TEvent : IClientEvent
        => stream(ct);
}
