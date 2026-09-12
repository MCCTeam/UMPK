namespace Umpk.Pathfinding.Core;

/// <summary>Which parkour execution profile a jump-family move requires.</summary>
public enum ParkourProfile
{
    /// <summary>Not a parkour move.</summary>
    None = 0,

    /// <summary>A standard sprint-jump.</summary>
    Default = 1,

    /// <summary>A dominant-axis sprint jump that leans on an inner wall.</summary>
    Sidewall = 2,
}
