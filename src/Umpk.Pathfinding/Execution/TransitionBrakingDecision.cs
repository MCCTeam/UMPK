namespace Umpk.Pathfinding.Execution;

/// <summary>The per-tick input the braking planner chooses at a segment handoff: whether to hold forward/sprint to carry momentum, or hold back to shed it. A value type.</summary>
internal readonly record struct TransitionBrakingDecision(bool HoldForward, bool HoldSprint, bool HoldBack)
{
    /// <summary>Carry momentum forward (optionally sprinting).</summary>
    internal static TransitionBrakingDecision CarryMomentum(bool preserveSprint) => new(true, preserveSprint, false);

    /// <summary>Release all input and let drag decelerate.</summary>
    internal static TransitionBrakingDecision Coast => new(false, false, false);

    /// <summary>Actively brake by holding back.</summary>
    internal static TransitionBrakingDecision Brake => new(false, false, true);
}
