namespace Umpk.Client.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> whose timestamp only moves when a test moves it, so state derived from elapsed time (the server tick-rate estimate, keep-alive timing) is exactly reproducible.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    /// <summary>One tick per <see cref="TimeSpan"/> tick, so advancing by a TimeSpan is exact.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan delta) => _timestamp += delta.Ticks;
}
