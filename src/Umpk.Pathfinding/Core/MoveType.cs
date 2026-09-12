namespace Umpk.Pathfinding.Core;

/// <summary>The taxonomy of movement actions the planner emits. <see cref="Swim"/> selects the dedicated swim execution template.</summary>
public enum MoveType
{
    /// <summary>A cardinal one-block walk on the same Y.</summary>
    Traverse,

    /// <summary>A diagonal one-block walk on the same Y.</summary>
    Diagonal,

    /// <summary>A one-block ascend (step up).</summary>
    Ascend,

    /// <summary>A descend (step or dynamic-height drop).</summary>
    Descend,

    /// <summary>A straight-down free fall.</summary>
    Fall,

    /// <summary>A ladder/vine climb up or down.</summary>
    Climb,

    /// <summary>A sprint-jump (parkour) across a gap.</summary>
    Parkour,

    /// <summary>Movement through a water volume (surface or submerged).</summary>
    Swim,
}
