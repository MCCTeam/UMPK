using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves;

/// <summary>A thin adapter wrapping an array of <see cref="IMove"/> instances as an <see cref="IMoveExpander"/>. Used for the dynamic-landing, vertical, and swim move families that do not fit the <see cref="JumpDescriptor"/> model.</summary>
public sealed class LegacyMoveExpander : IMoveExpander
{
    private readonly IMove[] _moves;

    /// <summary>Wraps a move array.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="moves"/> is null.</exception>
    public LegacyMoveExpander(IMove[] moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        _moves = moves;
    }

    /// <inheritdoc/>
    public int MaxNeighbors => _moves.Length;

    /// <inheritdoc/>
    public int Expand(CalculationContext ctx, int x, int y, int z, Span<MoveNeighbor> buffer)
    {
        int count = 0;
        MoveResult result = default;
        for (int i = 0; i < _moves.Length; i++)
        {
            IMove move = _moves[i];
            result.Cost = 0;
            move.Calculate(ctx, x, y, z, ref result);
            if (result.IsImpossible)
                continue;

            if (count < buffer.Length)
                buffer[count++] = new MoveNeighbor(result, move.Type);

        }

        return count;
    }
}
