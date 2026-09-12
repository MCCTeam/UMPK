using Umpk.Geometry;

namespace Umpk.Pathfinding.Goals;

/// <summary>A goal satisfied when any of its sub-goals is satisfied.</summary>
public sealed class GoalComposite : IGoal
{
    private readonly IGoal[] _goals;

    /// <summary>Creates a composite from an array of goals.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="goals"/> is null.</exception>
    public GoalComposite(params IGoal[] goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        _goals = goals;
    }

    /// <summary>Creates a composite from a sequence of goals.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="goals"/> is null.</exception>
    public GoalComposite(IEnumerable<IGoal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        _goals = goals is IGoal[] arr ? arr : [.. goals];
    }

    /// <inheritdoc/>
    public bool IsInGoal(BlockPos pos)
    {
        foreach (IGoal g in _goals)
            if (g.IsInGoal(pos))
                return true;

        return false;
    }

    /// <inheritdoc/>
    public double Heuristic(BlockPos pos)
    {
        double min = double.MaxValue;
        foreach (IGoal g in _goals)
        {
            double h = g.Heuristic(pos);
            if (h < min)
                min = h;

        }

        return min;
    }

    /// <summary>The first hint any child offers. A composite is satisfied by ANY of its goals, so any child's destination is a legitimate point to size a region against.</summary>
    public bool TryGetTargetHint(out BlockPos hint)
    {
        foreach (IGoal goal in _goals)
            if (goal.TryGetTargetHint(out hint))
                return true;

        hint = default;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => $"GoalComposite({_goals.Length} goals)";
}
