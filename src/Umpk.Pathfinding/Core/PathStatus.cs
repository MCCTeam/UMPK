namespace Umpk.Pathfinding.Core;

/// <summary>The outcome of a path search.</summary>
public enum PathStatus
{
    /// <summary>A complete path to the goal was found.</summary>
    Success,

    /// <summary>The search stopped early (timeout, node budget, or unloaded terrain) with a best-effort partial path.</summary>
    Partial,

    /// <summary>No path was found.</summary>
    Failed,
}
