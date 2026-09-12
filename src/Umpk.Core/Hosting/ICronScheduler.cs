namespace Umpk.Hosting;

/// <summary>Wall-clock (cron-style) scheduling. This is the third member of the hosting family: <see cref="ISessionScheduler"/> is the serialized loop, <see cref="ITickSource"/> is the 20 TPS cadence, and this is wall-clock time, which keeps running while there is no session at all. Every registration returns an <see cref="IDisposable"/>; dispose it to cancel that job alone.</summary>
public interface ICronScheduler
{
    /// <summary>Runs <paramref name="callback"/> every <paramref name="interval"/>.</summary>
    IDisposable Every(TimeSpan interval, Action callback);

    /// <summary>Runs <paramref name="callback"/> on a random interval between <paramref name="min"/> and <paramref name="max"/> (interval + jitter), re-rolled after every fire.</summary>
    IDisposable EveryWithJitter(TimeSpan min, TimeSpan max, Action callback);

    /// <summary>Runs <paramref name="callback"/> once per day at the given local time of day (per <see cref="TimeProvider.LocalTimeZone"/>). If that time of day has already passed today, the first run is tomorrow.</summary>
    IDisposable DailyAt(TimeOnly timeOfDay, Action callback);

    /// <summary>Runs <paramref name="callback"/> once at the given absolute time. A <paramref name="when"/> already in the past fires as soon as possible instead of being skipped.</summary>
    IDisposable At(DateTimeOffset when, Action callback);
}
