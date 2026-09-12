using Umpk.Geometry;

namespace Umpk.Pathfinding.Goals;

/// <summary>
/// A goal at a (possibly moving) entity's position: it targets a moving entity and supports replanning when the target drifts beyond a threshold, as follow-style consumers need.
///
/// <para>The planner is session-free and works on an immutable snapshot, so the entity position is a fixed input to any single search. The moving-target tracking and drift-triggered replan is the Navigator's job: it constructs a fresh <see cref="EntityGoal"/> at the entity's current position for each plan/replan and compares the entity's drift against <see cref="DriftThreshold"/> to decide when to replan. This type exposes that threshold and the target so the Navigator can implement the policy without a bespoke goal shape.</para>
/// </summary>
public sealed class EntityGoal : IGoal
{
    private readonly GoalNear _inner;

    /// <summary>The target block position captured at goal-construction time.</summary>
    public BlockPos Target { get; }

    /// <summary>The acceptance radius (a follow-style goal completes near the target, not exactly on it).</summary>
    public int AcceptanceRadius { get; }

    /// <summary>The drift distance beyond which the Navigator should replan toward the entity's new position.</summary>
    public double DriftThreshold { get; }

    /// <summary>Creates an entity goal at a fixed captured position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A radius or threshold is negative.</exception>
    public EntityGoal(BlockPos target, int acceptanceRadius = 2, double driftThreshold = 3.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(acceptanceRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(driftThreshold);
        Target = target;
        AcceptanceRadius = acceptanceRadius;
        DriftThreshold = driftThreshold;
        _inner = new GoalNear(target.X, target.Y, target.Z, acceptanceRadius);
    }

    /// <summary>True when the entity has drifted far enough from this goal's target to warrant a replan.</summary>
    public bool ShouldReplan(BlockPos currentTarget)
    {
        double dx = currentTarget.X - Target.X;
        double dy = currentTarget.Y - Target.Y;
        double dz = currentTarget.Z - Target.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) > DriftThreshold * DriftThreshold;
    }

    /// <inheritdoc/>
    public bool IsInGoal(BlockPos pos) => _inner.IsInGoal(pos);

    /// <inheritdoc/>
    public double Heuristic(BlockPos pos) => _inner.Heuristic(pos);

    /// <inheritdoc/>
    public bool TryGetTargetHint(out BlockPos hint)
    {
        hint = Target;
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"EntityGoal({Target}, r={AcceptanceRadius}, drift={DriftThreshold})";
}
