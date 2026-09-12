using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves;

/// <summary>One type of movement action for path planning. Each implementation defines its spatial check pattern and cost model.</summary>
public interface IMove
{
    /// <summary>The move classification.</summary>
    MoveType Type { get; }

    /// <summary>The fixed X offset (0 for dynamic-landing moves).</summary>
    int XOffset { get; }

    /// <summary>The fixed Z offset (0 for dynamic-landing moves).</summary>
    int ZOffset { get; }

    /// <summary>True when the landing Y is computed dynamically (fall/descend).</summary>
    bool DynamicY { get; }

    /// <summary>Evaluates the move from a start block position, writing feasibility and cost into <paramref name="result"/>.</summary>
    void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result);
}
