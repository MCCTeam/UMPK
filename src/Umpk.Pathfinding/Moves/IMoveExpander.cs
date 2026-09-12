using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves;

/// <summary>A feasible neighbor emitted by an <see cref="IMoveExpander"/>: the destination, cost, parkour profile, and move type the A* main loop needs to update its open set.</summary>
public readonly struct MoveNeighbor
{
    /// <summary>Destination X.</summary>
    public readonly int DestX;

    /// <summary>Destination Y.</summary>
    public readonly int DestY;

    /// <summary>Destination Z.</summary>
    public readonly int DestZ;

    /// <summary>The move cost in ticks.</summary>
    public readonly double Cost;

    /// <summary>The parkour profile the move requires.</summary>
    public readonly ParkourProfile ParkourProfile;

    /// <summary>The move classification.</summary>
    public readonly MoveType MoveType;

    /// <summary>Where inside the destination cell the body ends up on X. See <see cref="MoveResult.LateralX"/>.</summary>
    public readonly LateralQuantum LateralX;

    /// <summary>Where inside the destination cell the body ends up on Z. See <see cref="MoveResult.LateralX"/>.</summary>
    public readonly LateralQuantum LateralZ;

    /// <summary>Whether the squeeze arm produced this edge. See <see cref="MoveResult.Squeezed"/>.</summary>
    public readonly bool Squeezed;

    /// <summary>Creates a neighbor from a computed move result.</summary>
    /// <remarks><paramref name="moveType"/> is what the descriptor or the <see cref="IMove"/> expects the move to be; <see cref="MoveResult.Classification"/> is what the evaluator found it to be once it had read the world, and it wins. See that field for why the two can differ.</remarks>
    public MoveNeighbor(in MoveResult result, MoveType moveType)
    {
        DestX = result.DestX;
        DestY = result.DestY;
        DestZ = result.DestZ;
        Cost = result.Cost;
        ParkourProfile = result.ParkourProfile;
        MoveType = result.Classification ?? moveType;
        LateralX = result.LateralX;
        LateralZ = result.LateralZ;
        Squeezed = result.Squeezed;
    }
}

/// <summary>Emits feasible neighbors from a node. A* asks each expander to fill a stack-allocated buffer with all feasible neighbors, and the expander can prune whole categories (e.g. skip sidewall when no wall is near) without instantiating per-direction move objects.</summary>
public interface IMoveExpander
{
    /// <summary>Populates <paramref name="buffer"/> with feasible neighbors and returns the count written.</summary>
    int Expand(CalculationContext ctx, int x, int y, int z, Span<MoveNeighbor> buffer);

    /// <summary>The upper bound on neighbors this expander can emit from a single node.</summary>
    int MaxNeighbors { get; }
}
