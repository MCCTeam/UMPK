using System.Collections.Concurrent;

namespace Umpk.Realms.Tests.Fakes;

/// <summary>A controllable <see cref="TimeProvider"/> for virtual-time tests. <see cref="GetUtcNow"/> returns the current virtual time; <see cref="Advance"/> moves it forward and fires any timers whose due time has passed. This lets device-code polling run through many <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> waits without real elapsed time. No external test package is used.</summary>
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly ConcurrentDictionary<TestTimer, byte> _timers = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;

    }

    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Moves virtual time forward by <paramref name="delta"/>, firing due timers.</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_gate)
            _now += delta;

        foreach (TestTimer timer in _timers.Keys)
            timer.MaybeFire(GetUtcNow());

    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, callback, state, GetUtcNow() + dueTime);
        _timers[timer] = 0;
        return timer;
    }

    internal void Remove(TestTimer timer) => _timers.TryRemove(timer, out _);

    internal sealed class TestTimer(TestTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
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
