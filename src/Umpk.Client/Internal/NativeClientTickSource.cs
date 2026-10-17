using System.Runtime.CompilerServices;
using Umpk.Hosting;

namespace Umpk.Client.Internal;

/// <summary>Owns the default session's frame pump and native game-time accumulator. A supplied host tick source already produces game ticks and is not wrapped here.</summary>
internal sealed class NativeClientTickSource : ITickSource
{
    private readonly WorldTickState _state;
    private readonly TimeProvider _time;
    private readonly ITickSource _frames;

    internal NativeClientTickSource(WorldTickState state, TimeProvider? time = null, ITickSource? frames = null)
    {
        _state = state;
        _time = time ?? TimeProvider.System;
        _frames = frames ?? new PeriodicTimerTickSource(PeriodicTimerTickSource.DefaultInterval, _time);
    }

    public TimeSpan TickInterval => TimeSpan.FromMilliseconds(_state.TargetMilliseconds);

    public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var clock = new ClientTickClock(Milliseconds());
        long sequence = 0;
        await foreach (long _ in _frames.Ticks(cancellationToken).ConfigureAwait(false))
        {
            // 1.20.4-client-decompiled net/minecraft/client/Minecraft.java and 26.3-client-decompiled net/minecraft/client/Minecraft.java: runTick limits one frame to ten game ticks after the timer consumes its whole-tick accumulation.
            int ticks = Math.Min(10, clock.Advance(Milliseconds(), _state.TargetMilliseconds));
            for (int index = 0; index < ticks; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return sequence++;
            }
        }
    }

    private long Milliseconds() => _time.GetElapsedTime(0, _time.GetTimestamp()).Ticks / TimeSpan.TicksPerMillisecond;
}
