using Umpk.Geometry;

namespace Umpk.Pathfinding.Goals;

/// <summary>A pathfinding goal: a membership test plus an admissible heuristic. The heuristic must never overestimate the remaining cost for A* to stay optimal.</summary>
public interface IGoal
{
    /// <summary>True when the block position satisfies the goal.</summary>
    bool IsInGoal(BlockPos pos);

    /// <summary>An admissible lower-bound cost estimate from the block position to the goal.</summary>
    double Heuristic(BlockPos pos);

    /// <summary>A representative block this goal is trying to reach, when it has one.</summary>
    /// <remarks>
    /// <para>A goal is a PREDICATE, so it cannot in general say where "there" is. But a caller that has to size a finite planning region around the route needs a second point to bound it, and asking the predicate by probing outward cannot find a goal satisfied by a single block except by luck.</para>
    /// <para>The default returns false, so an external goal keeps working and simply falls back to whatever the caller does without a hint. Implement it whenever the goal is built from a concrete destination.</para>
    /// </remarks>
    bool TryGetTargetHint(out BlockPos hint)
    {
        hint = default;
        return false;
    }
}
