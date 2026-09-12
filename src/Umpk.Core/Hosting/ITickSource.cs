namespace Umpk.Hosting;

/// <summary>Produces the game-tick cadence a session runtime consumes (20 TPS by default). Hosts substitute their own source to drive ticks from an engine frame loop or a test clock.</summary>
public interface ITickSource
{
    /// <summary>The nominal interval between ticks (informational; sources may jitter).</summary>
    TimeSpan TickInterval { get; }

    /// <summary>Streams monotonically increasing tick numbers, starting at 0, until cancellation. A source is single-consumer: one enumeration per session.</summary>
    IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default);
}
