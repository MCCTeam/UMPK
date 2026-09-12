namespace Umpk.Pathfinding.Moves;

/// <summary>The kind of jump-family move. Each flavor selects a different evaluator path inside <see cref="JumpFeasibility"/> while sharing low-level primitives.</summary>
public enum JumpFlavor
{
    /// <summary>A single-block cardinal or diagonal walk at the same Y.</summary>
    Walk,

    /// <summary>A single-block vertical step (ascend or descend), cardinal or diagonal.</summary>
    Step,

    /// <summary>A multi-block sprint jump, cardinal or diagonal.</summary>
    SprintJump,

    /// <summary>A dominant-axis sprint jump that leans on an inner wall.</summary>
    Sidewall,
}

/// <summary>Fully describes a single jump-family move. All geometry that downstream planners or templates need derives from this value, so A* enumerates descriptors rather than hard-coded move subclasses.</summary>
public readonly record struct JumpDescriptor(
    int XOffset,
    int ZOffset,
    int YDelta,
    JumpFlavor Flavor)
{
    /// <summary>True when exactly one horizontal axis is non-zero.</summary>
    public bool IsCardinal => (XOffset == 0) != (ZOffset == 0);

    /// <summary>True when both horizontal axes are non-zero.</summary>
    public bool IsDiagonal => XOffset != 0 && ZOffset != 0;

    /// <summary>The larger absolute horizontal component.</summary>
    public int HorizontalMajor => Math.Max(Math.Abs(XOffset), Math.Abs(ZOffset));

    /// <summary>The smaller absolute horizontal component.</summary>
    public int HorizontalMinor => Math.Min(Math.Abs(XOffset), Math.Abs(ZOffset));
}
