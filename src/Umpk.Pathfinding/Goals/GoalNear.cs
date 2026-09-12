using Umpk.Geometry;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Goals;

/// <summary>A goal satisfied within a spherical radius of a target block.</summary>
public sealed class GoalNear : IGoal
{
    /// <summary>The target X.</summary>
    public int X { get; }

    /// <summary>The target Y.</summary>
    public int Y { get; }

    /// <summary>The target Z.</summary>
    public int Z { get; }

    /// <summary>The acceptance radius in blocks.</summary>
    public int Range { get; }

    private readonly int _rangeSq;

    /// <summary>Creates a near goal.</summary>
    public GoalNear(int x, int y, int z, int range)
    {
        X = x;
        Y = y;
        Z = z;
        Range = range;
        _rangeSq = range * range;
    }

    /// <inheritdoc/>
    public bool IsInGoal(BlockPos pos)
    {
        int dx = pos.X - X;
        int dy = pos.Y - Y;
        int dz = pos.Z - Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= _rangeSq;
    }

    /// <inheritdoc/>
    public double Heuristic(BlockPos pos)
    {
        int dx = Math.Abs(pos.X - X);
        int dz = Math.Abs(pos.Z - Z);
        double h = GoalBlock.DistanceHeuristic(dx, dz);
        double reduction = Range * ActionCosts.SprintOneBlock;
        return Math.Max(0, h - reduction);
    }

    /// <inheritdoc/>
    public bool TryGetTargetHint(out BlockPos hint)
    {
        hint = new BlockPos(X, Y, Z);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"GoalNear({X}, {Y}, {Z}, range={Range})";
}
