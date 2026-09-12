using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umpk.Hosting;

/// <summary>The default <see cref="ICronScheduler"/>, backed by <see cref="TimeProvider.CreateTimer"/>.</summary>
/// <remarks>
/// Callbacks run on the thread pool, off every session or tick loop; marshal onto a session loop via <see cref="ISessionScheduler.Post"/> / <see cref="ISessionScheduler.InvokeAsync(Action, CancellationToken)"/>, or onto the tick cadence via whichever consumer drives an <see cref="ITickSource"/>, before touching state that is not itself thread-safe.
///
/// A job re-arms for its next fire only after its own callback returns, so a slow callback delays that job's next fire; a job never overlaps itself, and other jobs are unaffected.
///
/// A callback that throws is logged as a warning and swallowed; the job's cadence survives the throw and keeps re-arming normally.
///
/// Registering a job after <see cref="DisposeAll"/> does not throw: it returns a handle that is already canceled and will never fire, matching the handle returned by disposing a live job.
///
/// Tracks every live handle so <see cref="DisposeAll"/> can cancel them all at once, for example on shutdown or plugin unload.
/// </remarks>
public sealed class CronScheduler : ICronScheduler, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly HashSet<CronJob> _jobs = [];
    private bool _disposed;

    /// <summary>Creates a scheduler on <paramref name="timeProvider"/> (default: <see cref="TimeProvider.System"/>), reporting throwing callbacks through <paramref name="logger"/> (default: a no-op logger).</summary>
    public CronScheduler(TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public IDisposable Every(TimeSpan interval, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        return Add(callback, interval, () => interval);
    }

    /// <inheritdoc/>
    public IDisposable EveryWithJitter(TimeSpan min, TimeSpan max, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(min, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        TimeSpan Next() => min + (max - min) * Random.Shared.NextDouble();
        return Add(callback, Next(), Next);
    }

    /// <inheritdoc/>
    public IDisposable DailyAt(TimeOnly timeOfDay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TimeSpan Until()
        {
            DateTimeOffset now = _timeProvider.GetLocalNow();
            DateTimeOffset next = new DateTimeOffset(now.Date, now.Offset) + timeOfDay.ToTimeSpan();
            if (next <= now)
                next = next.AddDays(1);

            return next - now;
        }

        return Add(callback, Until(), () => TimeSpan.FromDays(1), recomputeFirst: Until);
    }

    /// <inheritdoc/>
    public IDisposable At(DateTimeOffset when, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TimeSpan delay = when - _timeProvider.GetUtcNow();
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return Add(callback, delay, next: null);
    }

    /// <summary>Cancels and disposes every currently registered job.</summary>
    public void DisposeAll()
    {
        CronJob[] jobs;
        lock (_gate)
        {
            _disposed = true;
            jobs = [.. _jobs];
            _jobs.Clear();
        }

        foreach (CronJob job in jobs)
            job.Dispose();

    }

    /// <inheritdoc/>
    void IDisposable.Dispose() => DisposeAll();

    private IDisposable Add(Action callback, TimeSpan first, Func<TimeSpan>? next, Func<TimeSpan>? recomputeFirst = null)
    {
        var job = new CronJob(this, callback, next, recomputeFirst, _timeProvider, _logger);
        lock (_gate)
        {
            if (_disposed)
            {
                job.Dispose();
                return job;
            }

            _jobs.Add(job);
        }

        job.Arm(first);
        return job;
    }

    private void Remove(CronJob job)
    {
        lock (_gate)
            _jobs.Remove(job);

    }

    private sealed class CronJob : IDisposable
    {
        private readonly CronScheduler _owner;
        private readonly Action _callback;
        private readonly Func<TimeSpan>? _next;
        private readonly Func<TimeSpan>? _recomputeFirst;
        private readonly ILogger _logger;
        private readonly ITimer _timer;
        private int _disposed;

        internal CronJob(
            CronScheduler owner,
            Action callback,
            Func<TimeSpan>? next,
            Func<TimeSpan>? recomputeFirst,
            TimeProvider timeProvider,
            ILogger logger)
        {
            _owner = owner;
            _callback = callback;
            _next = next;
            _recomputeFirst = recomputeFirst;
            _logger = logger;
            _timer = timeProvider.CreateTimer(OnTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        internal void Arm(TimeSpan due) => _timer.Change(Clamp(due), Timeout.InfiniteTimeSpan);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _timer.Dispose();
            _owner.Remove(this);
        }

        private void OnTick(object? state)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            try
            {
                _callback();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A CronScheduler job's callback threw; its cadence continues.");
            }

            if (Volatile.Read(ref _disposed) != 0)
                return;

            // Re-arm from here, after the callback returned, not from when it was originally due: a slow callback delays this job's own next fire instead of the job overlapping itself.
            TimeSpan? reArm = _recomputeFirst is not null ? _recomputeFirst() : _next?.Invoke();
            if (reArm is { } due)
                _timer.Change(Clamp(due), Timeout.InfiniteTimeSpan);

            else
                Dispose();

        }

        private static TimeSpan Clamp(TimeSpan due) => due < TimeSpan.Zero ? TimeSpan.Zero : due;
    }
}
