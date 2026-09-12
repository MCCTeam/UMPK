namespace Umpk.Pathfinding.Execution;

/// <summary>How a segment hands off to the next.</summary>
public enum PathTransitionType
{
    /// <summary>The final segment: brake to a stop.</summary>
    FinalStop,

    /// <summary>The next segment continues in the same heading: carry momentum.</summary>
    ContinueStraight,

    /// <summary>The next segment turns: shed speed and align.</summary>
    Turn,

    /// <summary>The next segment jumps: preserve run-up speed.</summary>
    PrepareJump,

    /// <summary>This segment was a jump/descend/fall: recover on landing.</summary>
    LandingRecovery,
}
