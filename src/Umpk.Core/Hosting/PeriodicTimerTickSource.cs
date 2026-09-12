using System.Runtime.CompilerServices;

namespace Umpk.Hosting;

/// <summary>The default wall-clock tick source: a <see cref="PeriodicTimer"/> at 20 TPS (50 ms) unless configured otherwise. Pass a <see cref="TimeProvider"/> to control time in tests.</summary>
public sealed class PeriodicTimerTickSource : ITickSource
{
    /// <summary>The vanilla tick interval: 50 ms (20 TPS).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    private readonly TimeProvider _timeProvider;

    public PeriodicTimerTickSource()
        : this(DefaultInterval, TimeProvider.System)
    {
    }

    public PeriodicTimerTickSource(TimeSpan interval)
        : this(interval, TimeProvider.System)
    {
    }

    public PeriodicTimerTickSource(TimeSpan interval, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        TickInterval = interval;
        _timeProvider = timeProvider;
    }

    public TimeSpan TickInterval { get; }

    public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(TickInterval, _timeProvider);
        long tick = 0;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            yield return tick++;

    }
}
