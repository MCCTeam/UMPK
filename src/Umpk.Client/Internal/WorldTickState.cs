namespace Umpk.Client.Internal;

/// <summary>Level-owned native tick control. The session loop writes immutable snapshots; the default tick source may read the target interval from its own continuation.</summary>
internal sealed class WorldTickState
{
    private sealed record Snapshot(float Rate, long NanosecondsPerTick, bool Frozen, int Steps, bool RunsNormally);

    private static readonly Snapshot Default = new(20f, 50_000_000, false, 0, true);
    private Snapshot _snapshot = Default;

    internal float TickRate => Current.Rate;
    internal long NanosecondsPerTick => Current.NanosecondsPerTick;
    internal float MillisecondsPerTick => (float)Current.NanosecondsPerTick / 1_000_000f;
    internal bool IsFrozen => Current.Frozen;
    internal int FrozenTicksToRun => Current.Steps;
    internal bool IsSteppingForward => Current.Steps > 0;
    internal bool RunsNormally => Current.RunsNormally;

    internal float TargetMilliseconds
    {
        get
        {
            Snapshot state = Current;
            return state.RunsNormally ? MathF.Max(50f, (float)state.NanosecondsPerTick / 1_000_000f) : 50f;
        }
    }

    private Snapshot Current => Volatile.Read(ref _snapshot);

    internal void ApplyState(float tickRate, bool frozen)
    {
        float rate = MathF.Max(tickRate, 1f);
        // Java's narrowing conversion maps NaN to zero; the minimum rate bounds every finite quotient to at most one billion.
        long nanoseconds = float.IsNaN(rate) ? 0 : (long)(1_000_000_000d / rate);
        Volatile.Write(ref _snapshot, Current with { Rate = rate, NanosecondsPerTick = nanoseconds, Frozen = frozen });
    }

    internal void SetSteps(int steps) => Volatile.Write(ref _snapshot, Current with { Steps = steps });

    internal void BeginTick()
    {
        Snapshot state = Current;
        bool runsNormally = !state.Frozen || state.Steps > 0;
        int remaining = state.Steps > 0 ? state.Steps - 1 : state.Steps;
        if (runsNormally != state.RunsNormally || remaining != state.Steps)
            Volatile.Write(ref _snapshot, state with { RunsNormally = runsNormally, Steps = remaining });
    }

    internal void Reset() => Volatile.Write(ref _snapshot, Default);
}
