namespace Umpk.Hosting;

/// <summary>The serialized work queue a session runtime executes on. All work posted here runs one item at a time, in FIFO order; asynchronous work items are awaited to completion before the next item starts. Hosts substitute an implementation pumped from their own thread (for example a game engine's update loop).</summary>
public interface ISessionScheduler : IAsyncDisposable
{
    /// <summary>True when the caller is executing on this scheduler's loop. Intended for assertions; note that unawaited work escaping a loop item still observes true (async-local flow).</summary>
    bool IsCurrent { get; }

    /// <summary>Queues fire-and-forget work. Exceptions go to the scheduler's error sink.</summary>
    void Post(Action work);

    /// <summary>Queues work and completes when it has run.</summary>
    Task InvokeAsync(Action work, CancellationToken cancellationToken);

    /// <summary>Queues work and returns its result.</summary>
    Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken);

    /// <summary>Queues asynchronous work; the loop stays blocked until it completes.</summary>
    Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken);

    /// <summary>Queues asynchronous work and returns its result; the loop stays blocked until it completes.</summary>
    Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken);
}
