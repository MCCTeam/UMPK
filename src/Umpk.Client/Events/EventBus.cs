using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Umpk.Client.Events;

/// <summary>The internal typed event dispatcher behind <see cref="ClientEvents"/>. Delivery is synchronous on the session loop, in subscription order, exception-isolated, and async subscribers are awaited inline so state updates stay ordered with event handling. Streams are bounded per subscription with a full-policy so a slow consumer never stalls the loop.</summary>
internal sealed class EventBus
{
    private readonly ConcurrentDictionary<Type, object> _channels = new();
    private readonly ILogger _logger;
    private readonly TimeSpan _watchdogThreshold;
    private readonly int _defaultStreamCapacity;

    // Set after construction to avoid a cycle: lagging streams publish a StreamLagged event.
    private Action<StreamLagged>? _lagPublisher;

    public EventBus(ILogger logger, TimeSpan watchdogThreshold, int defaultStreamCapacity)
    {
        _logger = logger;
        _watchdogThreshold = watchdogThreshold;
        _defaultStreamCapacity = defaultStreamCapacity <= 0 ? 256 : defaultStreamCapacity;
    }

    /// <summary>Wires the diagnostic publisher used to report stream lag. Set once after construction.</summary>
    public void SetLagPublisher(Action<StreamLagged> publisher) => _lagPublisher = publisher;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IClientEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Channel<TEvent>().AddSync(handler);
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : IClientEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Channel<TEvent>().AddAsync(handler);
    }

    public IAsyncEnumerable<TEvent> Stream<TEvent>(CancellationToken ct)
        where TEvent : IClientEvent
        => Stream<TEvent>(_defaultStreamCapacity, StreamFullPolicy.DropOldest, ct);

    public IAsyncEnumerable<TEvent> Stream<TEvent>(int capacity, StreamFullPolicy policy, CancellationToken ct)
        where TEvent : IClientEvent
        => Channel<TEvent>().Stream(capacity <= 0 ? _defaultStreamCapacity : capacity, policy, ct);

    /// <summary>Publishes an event to all subscribers. Must be called on the session loop. Async subscribers are awaited inline to preserve ordering; each handler is exception-isolated and timed by the watchdog.</summary>
    public ValueTask PublishAsync<TEvent>(TEvent evt)
        where TEvent : IClientEvent
    {
        if (_channels.TryGetValue(typeof(TEvent), out object? channel))
            return ((TypedChannel<TEvent>)channel).PublishAsync(evt);

        return ValueTask.CompletedTask;
    }

    private TypedChannel<TEvent> Channel<TEvent>()
        where TEvent : IClientEvent
        => (TypedChannel<TEvent>)_channels.GetOrAdd(
            typeof(TEvent),
            static (_, state) => new TypedChannel<TEvent>(state._logger, state._watchdogThreshold, state.Self),
            (this, _logger, _watchdogThreshold, Self: this));

    private void ReportLag(Type eventType, int dropped) =>
        _lagPublisher?.Invoke(new StreamLagged(eventType, dropped));

    private sealed class TypedChannel<TEvent>
        where TEvent : IClientEvent
    {
        private readonly Lock _gate = new();
        private readonly ILogger _logger;
        private readonly TimeSpan _watchdog;
        private readonly EventBus _owner;
        private Handler[] _handlers = [];
        private StreamSink[] _streams = [];

        public TypedChannel(ILogger logger, TimeSpan watchdog, EventBus owner)
        {
            _logger = logger;
            _watchdog = watchdog;
            _owner = owner;
        }

        public IDisposable AddSync(Action<TEvent> handler)
        {
            var h = new Handler(this) { Sync = handler };
            AddHandler(h);
            return h;
        }

        public IDisposable AddAsync(Func<TEvent, ValueTask> handler)
        {
            var h = new Handler(this) { Async = handler };
            AddHandler(h);
            return h;
        }

        public IAsyncEnumerable<TEvent> Stream(int capacity, StreamFullPolicy policy, CancellationToken ct)
        {
            var sink = new StreamSink(this, capacity, policy);
            lock (_gate)
                _streams = [.. _streams, sink];

            return sink.Consume(ct);
        }

        public async ValueTask PublishAsync(TEvent evt)
        {
            Handler[] handlers;
            StreamSink[] streams;
            lock (_gate)
            {
                handlers = _handlers;
                streams = _streams;
            }

            foreach (Handler h in handlers)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    if (h.Sync is { } sync)
                        sync(evt);

                    else if (h.Async is { } async)
                        await async(evt).ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event handler for {EventType} threw.", typeof(TEvent).Name);
                }
                finally
                {
                    if (_watchdog > TimeSpan.Zero)
                    {
                        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
                        if (elapsed > _watchdog)
                            _logger.LogWarning(
                                "Session-loop handler for {EventType} took {Elapsed} (watchdog {Threshold}).",
                                typeof(TEvent).Name, elapsed, _watchdog);

                    }
                }
            }

            foreach (StreamSink sink in streams)
                sink.Offer(evt);

        }

        private void AddHandler(Handler h)
        {
            lock (_gate)
                _handlers = [.. _handlers, h];

        }

        private void RemoveHandler(Handler h)
        {
            lock (_gate)
                _handlers = [.. _handlers.Where(x => !ReferenceEquals(x, h))];

        }

        private void RemoveStream(StreamSink s)
        {
            lock (_gate)
                _streams = [.. _streams.Where(x => !ReferenceEquals(x, s))];

        }

        private sealed class Handler(TypedChannel<TEvent> owner) : IDisposable
        {
            public Action<TEvent>? Sync { get; init; }

            public Func<TEvent, ValueTask>? Async { get; init; }

            private TypedChannel<TEvent>? _owner = owner;

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.RemoveHandler(this);
        }

        private sealed class StreamSink
        {
            private readonly TypedChannel<TEvent> _owner;
            private readonly Channel<TEvent> _channel;
            private readonly StreamFullPolicy _policy;
            private readonly int _capacity;
            private int _dropped;
            private int _count;

            public StreamSink(TypedChannel<TEvent> owner, int capacity, StreamFullPolicy policy)
            {
                _owner = owner;
                _policy = policy;
                // The channel is unbounded internally; capacity/eviction is enforced here so overflow is detectable (BCL bounded channels either drop silently or block, neither of which lets us count drops for the lag diagnostic).
                _capacity = capacity;
                _channel = System.Threading.Channels.Channel.CreateUnbounded<TEvent>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            }

            public void Offer(TEvent evt)
            {
                if (Volatile.Read(ref _count) < _capacity)
                {
                    if (_channel.Writer.TryWrite(evt))
                        Interlocked.Increment(ref _count);

                    return;
                }

                // Full.
                if (_policy == StreamFullPolicy.Throw)
                {
                    _channel.Writer.TryComplete(new InvalidOperationException(
                        $"Event stream for {typeof(TEvent).Name} overflowed and the Throw policy is set."));
                    return;
                }

                // DropOldest: evict the oldest buffered item (if present) to make room for the newest, counting the drop for the lag diagnostic.
                if (_channel.Reader.TryRead(out _))
                    Interlocked.Decrement(ref _count);

                int dropped = Interlocked.Increment(ref _dropped);
                _owner._owner.ReportLag(typeof(TEvent), dropped);
                if (_channel.Writer.TryWrite(evt))
                    Interlocked.Increment(ref _count);

            }

            public async IAsyncEnumerable<TEvent> Consume(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                try
                {
                    while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                        while (_channel.Reader.TryRead(out TEvent? item))
                        {
                            Interlocked.Decrement(ref _count);
                            if (_policy == StreamFullPolicy.DropOldest && _dropped > 0)
                            {
                                int lost = Interlocked.Exchange(ref _dropped, 0);
                                if (lost > 0)
                                    _owner._owner.ReportLag(typeof(TEvent), lost);

                            }

                            yield return item;
                        }

                }
                finally
                {
                    _owner.RemoveStream(this);
                }
            }
        }
    }
}
