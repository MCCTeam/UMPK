using Umpk.Hosting;

namespace Umpk.TestKit.Time;

/// <summary>A deterministic, host-driven tick source for tests: ticks fire only when <see cref="Advance"/> is called. This is a thin wrapper over <c>Umpk.Core</c>'s <see cref="ManualTickSource"/> (which already provides on-demand ticks); it exists so test code has a clearly named virtual-time tick source and a single import surface with the rest of the TestKit time helpers.</summary>
public sealed class TestTickSource : ITickSource
{
    private readonly ManualTickSource _inner;

    /// <summary>Creates a virtual tick source with an optional nominal interval.</summary>
    public TestTickSource(TimeSpan? nominalInterval = null) => _inner = new ManualTickSource(nominalInterval);

    /// <inheritdoc />
    public TimeSpan TickInterval => _inner.TickInterval;

    /// <summary>Fires <paramref name="count"/> ticks for the consumer.</summary>
    public void Advance(int count = 1) => _inner.Advance(count);

    /// <summary>Completes the tick stream.</summary>
    public void Complete() => _inner.Complete();

    /// <inheritdoc />
    public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
        _inner.Ticks(cancellationToken);
}
