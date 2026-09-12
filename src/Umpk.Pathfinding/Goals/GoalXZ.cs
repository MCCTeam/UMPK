using Umpk.Geometry;

namespace Umpk.Pathfinding.Goals;

/// <summary>A goal satisfied at any Y once the X/Z column is reached.</summary>
public sealed class GoalXZ : IGoal
{
    /// <summary>The goal X.</summary>
    public int X { get; }

    /// <summary>The goal Z.</summary>
    public int Z { get; }

    /// <summary>Creates an XZ goal.</summary>
    public GoalXZ(int x, int z)
    {
        X = x;
        Z = z;
    }

    /// <inheritdoc/>
    public bool IsInGoal(BlockPos pos) => pos.X == X && pos.Z == Z;

    /// <inheritdoc/>
    public double Heuristic(BlockPos pos)
    {
        int dx = Math.Abs(pos.X - X);
        int dz = Math.Abs(pos.Z - Z);
        return GoalBlock.DistanceHeuristic(dx, dz);
    }

    /// <inheritdoc/>
    public override string ToString() => $"GoalXZ({X}, {Z})";
}
