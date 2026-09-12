namespace Umpk.Pathfinding.Core;

/// <summary>The kind of run-up preparation a search node has partially executed.</summary>
public enum EntryPreparationKind
{
    /// <summary>No preparation in progress.</summary>
    None = 0,

    /// <summary>A sidewall-parkour static run-up (step back, then return to the takeoff block).</summary>
    SidewallRunup = 1,
}
