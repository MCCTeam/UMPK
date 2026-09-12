namespace Umpk.Pathfinding.Core;

/// <summary>The run-up-preparation state carried alongside a search node's position key: packed position node keys are extended by an entry-preparation state, so run-up sequences for sidewall parkour widen the search space deliberately.</summary>
public readonly record struct EntryPreparationState(
    EntryPreparationKind Kind,
    int OriginX,
    int OriginY,
    int OriginZ,
    int ForwardX,
    int ForwardZ,
    byte RequiredSteps,
    byte BackwardSteps,
    byte ReturnSteps)
{
    /// <summary>The empty preparation state.</summary>
    public static EntryPreparationState None => default;

    /// <summary>True when no preparation is in progress.</summary>
    public bool IsNone => Kind == EntryPreparationKind.None;

    /// <summary>True when both the backward and return legs of the run-up are complete.</summary>
    public bool IsPrepared =>
        Kind != EntryPreparationKind.None &&
        BackwardSteps == RequiredSteps &&
        ReturnSteps == RequiredSteps;

    /// <summary>Advances the backward-leg step counter.</summary>
    public EntryPreparationState AdvanceBackward() =>
        this with { BackwardSteps = (byte)(BackwardSteps + 1) };

    /// <summary>Advances the return-leg step counter.</summary>
    public EntryPreparationState AdvanceReturn() =>
        this with { ReturnSteps = (byte)(ReturnSteps + 1) };
}
