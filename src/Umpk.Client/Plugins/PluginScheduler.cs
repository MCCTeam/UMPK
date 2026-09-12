namespace Umpk.Client.Plugins;

/// <summary>Per-plugin scheduling surface: register per-tick work, delayed callbacks, and off-loop work with marshal-back. Everything registered here is removed automatically when the plugin detaches.</summary>
public sealed class PluginScheduler
{
    private readonly Action<Action> _postToLoop;
    private readonly Func<Func<Task>, CancellationToken, Task> _runOffLoop;
    private readonly List<IDisposable> _registrations = [];
    private readonly Lock _gate = new();

    internal PluginScheduler(
        Action<Action> postToLoop,
        Func<Func<Task>, CancellationToken, Task> runOffLoop)
    {
        _postToLoop = postToLoop;
        _runOffLoop = runOffLoop;
    }

    internal TickRegistry Ticks { get; } = new();

    /// <summary>Registers a callback run on every session-loop tick. Dispose to unregister.</summary>
    public IDisposable OnTick(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        IDisposable reg = Ticks.Add(callback);
        Track(reg);
        return reg;
    }

    /// <summary>Registers a one-shot callback after a number of ticks.</summary>
    public IDisposable Delay(int ticks, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks));

        IDisposable reg = Ticks.AddDelay(ticks, callback);
        Track(reg);
        return reg;
    }

    /// <summary>Registers a one-shot callback after a wall-clock delay (rounded to ticks at 20 TPS).</summary>
    public IDisposable Delay(TimeSpan delay, Action callback)
        => Delay((int)Math.Ceiling(delay.TotalMilliseconds / 50.0), callback);

    /// <summary>Posts work to run on the session loop.</summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _postToLoop(work);
    }

    /// <summary>Runs long work off the session loop; the returned task completes when it finishes.</summary>
    public Task RunOffLoop(Func<Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _runOffLoop(work, ct);
    }

    private void Track(IDisposable reg)
    {
        lock (_gate)
            _registrations.Add(reg);

    }

    internal void DisposeAll()
    {
        IDisposable[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _registrations];
            _registrations.Clear();
        }

        foreach (IDisposable reg in snapshot)
            reg.Dispose();

    }
}
