using Microsoft.Extensions.Logging;
using Umpk.Hosting;
using Umpk.TestKit.Time;
using Xunit;

namespace Umpk.Tests.Hosting;

public class CronSchedulerTests
{
    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_FiresOnTheInterval()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.Every(TimeSpan.FromSeconds(10), () => fireCount++);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, fireCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, fireCount);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fireCount);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, fireCount);
    }

    [Fact]
    public void Every_DoesNotOverlapASlowCallback()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.Every(TimeSpan.FromSeconds(10), () =>
        {
            fireCount++;
            // Simulate a callback slow enough that a full interval elapses before it returns.
            clock.Advance(TimeSpan.FromSeconds(10));
        });

        clock.Advance(TimeSpan.FromSeconds(10));
        // The nested advance inside the callback did not cause a second, overlapping fire.
        Assert.Equal(1, fireCount);

        // Re-arm happens only after the callback returns, timed from the completion time (20s), so the job is next due at 30s, not at 20s (which is when a naive "arm before calling back" scheduler would already be due again).
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(1, fireCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void EveryWithJitter_StaysWithinItsBounds()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.EveryWithJitter(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), () => fireCount++);

        // The delay is never shorter than min.
        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, fireCount);

        // The delay is never longer than max, so by max it must have fired exactly once.
        clock.Advance(TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void DailyAt_FiresAtTheNextLocalOccurrence()
    {
        // 2024-01-01T10:00:00Z, local time zone pinned to UTC so the test is deterministic.
        var start = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new VirtualTimeProvider(start, TimeZoneInfo.Utc);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.DailyAt(new TimeOnly(14, 0), () => fireCount++);

        clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(59));
        Assert.Equal(0, fireCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void DailyAt_SkipsToTomorrowWhenTheTimeHasPassed()
    {
        // 2024-01-01T15:00:00Z; the 10:00 slot for today has already gone by, so the first fire must be tomorrow at 10:00 (19 hours away), not today, and not "never".
        var start = new DateTimeOffset(2024, 1, 1, 15, 0, 0, TimeSpan.Zero);
        var clock = new VirtualTimeProvider(start, TimeZoneInfo.Utc);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.DailyAt(new TimeOnly(10, 0), () => fireCount++);

        clock.Advance(TimeSpan.FromHours(18) + TimeSpan.FromMinutes(59));
        Assert.Equal(0, fireCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void At_InThePast_FiresImmediately()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        bool fired = false;
        using IDisposable handle = scheduler.At(Epoch - TimeSpan.FromHours(1), () => fired = true);

        // No time needs to pass: the delay was clamped to zero rather than left negative (which would mean the timer never comes due).
        clock.Advance(TimeSpan.Zero);
        Assert.True(fired);
    }

    [Fact]
    public void At_IsOneShot()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        using IDisposable handle = scheduler.At(Epoch + TimeSpan.FromSeconds(5), () => fireCount++);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fireCount);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Dispose_CancelsAPendingJob()
    {
        var clock = new VirtualTimeProvider(Epoch);
        using var scheduler = new CronScheduler(clock);
        int fireCount = 0;
        IDisposable handle = scheduler.Every(TimeSpan.FromSeconds(10), () => fireCount++);

        clock.Advance(TimeSpan.FromSeconds(5));
        handle.Dispose();
        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void RegisteringAfterDispose_ReturnsADeadHandle()
    {
        var clock = new VirtualTimeProvider(Epoch);
        var scheduler = new CronScheduler(clock);
        scheduler.DisposeAll();

        int fireCount = 0;
        IDisposable handle = scheduler.Every(TimeSpan.FromSeconds(1), () => fireCount++);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, fireCount);

        // The dead handle disposes cleanly too, with no throw.
        handle.Dispose();
    }

    [Fact]
    public void AThrowingCallback_IsLoggedAndTheCadenceSurvives()
    {
        var clock = new VirtualTimeProvider(Epoch);
        var logger = new CapturingLogger();
        using var scheduler = new CronScheduler(clock, logger);
        int fireCount = 0;
        using IDisposable handle = scheduler.Every(TimeSpan.FromSeconds(10), () =>
        {
            fireCount++;
            throw new InvalidOperationException("boom");
        });

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fireCount);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);

        // The cadence survives the throw: the job is still re-armed for its next fire.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fireCount);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
