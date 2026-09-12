using System.Runtime.CompilerServices;
using Umpk.Client.Events;

namespace Umpk.Client.Plugins;

/// <summary>A <see cref="ClientEvents"/> facade whose subscriptions are tracked so a plugin's event handlers are all disposed on detach, and whose streams end when the plugin does. Subscription handles are auto-released; a stream keeps running past whatever token the caller passed in as long as the plugin itself has not detached, and ends the moment either one fires.</summary>
internal sealed class TrackingClientEvents : ClientEvents
{
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationToken _detached;

    public TrackingClientEvents(EventBus bus, CancellationToken detached)
        : base(bus)
    {
        _detached = detached;
    }

    public void DisposeAll()
    {
        IDisposable[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (IDisposable sub in snapshot)
            sub.Dispose();

    }

    private protected override IDisposable Register(IDisposable subscription)
    {
        lock (_gate)
            _subscriptions.Add(subscription);

        return subscription;
    }

    private protected override IAsyncEnumerable<TEvent> StreamCore<TEvent>(
        Func<CancellationToken, IAsyncEnumerable<TEvent>> stream, CancellationToken ct)
        => LinkedStream(stream, ct);

    /// <summary>Runs <paramref name="stream"/> against a token that is cancelled when EITHER the caller's own <paramref name="ct"/> fires OR this plugin detaches, whichever comes first. Without this a stream created with <see cref="CancellationToken.None"/> (or any token the plugin does not itself cancel on unload) would otherwise outlive <see cref="ClientPluginContext.Detached"/>.</summary>
    private async IAsyncEnumerable<TEvent> LinkedStream<TEvent>(
        Func<CancellationToken, IAsyncEnumerable<TEvent>> stream,
        [EnumeratorCancellation] CancellationToken ct)
        where TEvent : IClientEvent
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _detached);
        await foreach (TEvent item in stream(linked.Token).ConfigureAwait(false))
            yield return item;

    }
}
