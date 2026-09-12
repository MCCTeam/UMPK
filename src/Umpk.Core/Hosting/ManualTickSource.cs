using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Umpk.Hosting;

/// <summary>A host-driven tick source: ticks fire only when <see cref="Advance"/> is called. This is the shape a game-engine host uses (advance once per engine frame) and the deterministic clock tests use.</summary>
public sealed class ManualTickSource : ITickSource
{
    private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private long _next;

    public ManualTickSource(TimeSpan? nominalInterval = null)
    {
        TickInterval = nominalInterval ?? PeriodicTimerTickSource.DefaultInterval;
    }

    public TimeSpan TickInterval { get; }

    /// <summary>Enqueues <paramref name="count"/> ticks for the consumer.</summary>
    public void Advance(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        for (int i = 0; i < count; i++)
            _ticks.Writer.TryWrite(Interlocked.Increment(ref _next) - 1);

    }

    /// <summary>Completes the stream; the consumer's enumeration ends after draining.</summary>
    public void Complete() => _ticks.Writer.TryComplete();

    public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (long tick in _ticks.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return tick;

    }
}
