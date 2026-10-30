namespace Umpk.Client.Internal;

/// <summary>Native game-time accumulator. Rate changes preserve the fractional tick; a frame's whole ticks are consumed before its runtime catch-up limit is applied.</summary>
internal sealed class ClientTickClock(long initialMilliseconds)
{
    private long _lastMilliseconds = initialMilliseconds;

    internal float DeltaTicks { get; private set; }
    internal float ResidualTicks { get; private set; }

    // 1.20.4-client-decompiled net/minecraft/client/Timer.java and 26.3-client-decompiled net/minecraft/client/DeltaTracker.java: advanceGameTime retains float arithmetic and truncates the accumulated residual.
    internal int Advance(long milliseconds, float targetMilliseconds)
    {
        DeltaTicks = (float)(milliseconds - _lastMilliseconds) / targetMilliseconds;
        _lastMilliseconds = milliseconds;
        ResidualTicks += DeltaTicks;
        // Java float-to-int narrowing saturates and maps NaN to zero, including after a very long frame.
        int wholeTicks = float.IsNaN(ResidualTicks) ? 0
            : ResidualTicks >= int.MaxValue ? int.MaxValue
            : ResidualTicks <= int.MinValue ? int.MinValue
            : (int)ResidualTicks;
        ResidualTicks -= wholeTicks;
        return wholeTicks;
    }
}
