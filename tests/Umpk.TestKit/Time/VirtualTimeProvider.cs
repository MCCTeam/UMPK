using System.Collections.Concurrent;

namespace Umpk.TestKit.Time;

/// <summary>A controllable <see cref="TimeProvider"/> for virtual-time tests. <see cref="GetUtcNow"/> returns the current virtual time; <see cref="Advance"/> moves it forward and fires any timers whose due time has passed. <see cref="LocalTimeZone"/> is fixed at construction (default UTC) so time-of-day scheduling that reads <see cref="TimeProvider.GetLocalNow"/> (for example <c>Umpk.Hosting.CronScheduler.DailyAt</c>) is deterministic regardless of the machine running the test. No external test package is used.</summary>
public sealed class VirtualTimeProvider(DateTimeOffset start, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    private readonly ConcurrentDictionary<VirtualTimer, byte> _timers = new();
    private readonly Lock _gate = new();
    private readonly TimeZoneInfo _localTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    private DateTimeOffset _now = start;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;

    }

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    /// <inheritdoc/>
    public override long GetTimestamp() => GetUtcNow().Ticks;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Moves virtual time forward by <paramref name="delta"/>, firing due timers.</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_gate)
            _now += delta;

        foreach (VirtualTimer timer in _timers.Keys)
            timer.MaybeFire(GetUtcNow());

    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new VirtualTimer(this, callback, state, GetUtcNow() + dueTime);
        _timers[timer] = 0;
        return timer;
    }

    internal void Remove(VirtualTimer timer) => _timers.TryRemove(timer, out _);

    internal sealed class VirtualTimer(VirtualTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
    {
        private int _fired;
        private DateTimeOffset _dueAt = dueAt;

        public void MaybeFire(DateTimeOffset now)
        {
            if (now >= _dueAt && Interlocked.Exchange(ref _fired, 1) == 0)
                callback(state);

        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = owner.GetUtcNow() + dueTime;
            Interlocked.Exchange(ref _fired, 0);
            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            owner.Remove(this);
            return ValueTask.CompletedTask;
        }
    }
}
