using Umpk.Pathfinding.Moves;

namespace Umpk.Pathfinding.Core;

/// <summary>The result of an <c>IMove.Calculate</c> call. A mutable struct passed by ref for a zero-alloc hot path.</summary>
public struct MoveResult
{
    /// <summary>Destination X.</summary>
    public int DestX;

    /// <summary>Destination Y.</summary>
    public int DestY;

    /// <summary>Destination Z.</summary>
    public int DestZ;

    /// <summary>The tick cost of the move.</summary>
    public double Cost;

    /// <summary>The parkour profile the move requires.</summary>
    public ParkourProfile ParkourProfile;

    /// <summary>Where inside the DESTINATION cell the body ends up, on the axis perpendicular to a cardinal heading. <see cref="LateralQuantum.Centre"/> for every move family except a squeezed traverse.</summary>
    /// <remarks>It travels with the result rather than being re-derived for the same reason <see cref="Classification"/> does: only the evaluator has the world and the boxes, and everything downstream - the node key's dimension, the segment's endpoint - keys on the answer.</remarks>
    public LateralQuantum LateralX;

    /// <inheritdoc cref="LateralX"/>
    public LateralQuantum LateralZ;

    /// <summary>Whether this edge came from the squeeze arm, which is the one evaluator that has reasoned about where inside its cells the body stands.</summary>
    /// <remarks>
    /// <para>The driver needs this and cannot derive it. A squeeze that RE-CENTRES the body carries <see cref="LateralQuantum.Centre"/> on both axes and is otherwise indistinguishable from an ordinary level walk, and <see cref="MoveType"/> cannot tell them apart either because a squeeze IS a traverse - it is walked by the same template over the same geometry, only aimed elsewhere.</para>
    /// <para>What it gates: a node standing off centre may leave only by an edge that carries this, because every other move family's feasibility - the jump arcs, the descends, the climbs, the swims - was derived for a body at its cell's centre and none of it has been re-derived for one that is not.</para>
    /// </remarks>
    public bool Squeezed;

    /// <summary>The classification the evaluator resolved, when it differs from the one the descriptor or the <see cref="Moves.IMove"/> would imply. Null means "whatever the caller was going to say".</summary>
    /// <remarks>This exists because the node-Y delta is not the move's kind. A step onto a slab kerb and a step onto a full block are both <c>+1</c>, and only the evaluator - which has the world, the shapes and the two resolved elevations - can tell the walk from the jump. Everything downstream keys on <see cref="MoveType"/>: the transition taxonomy, the execution template, the tick budget. So the answer has to travel with the result rather than be re-derived from the geometry that could not answer it in the first place.</remarks>
    internal MoveType? Classification;

    /// <summary>Records a feasible move.</summary>
    public void Set(int x, int y, int z, double cost, ParkourProfile parkourProfile = ParkourProfile.None)
    {
        DestX = x;
        DestY = y;
        DestZ = z;
        Cost = cost;
        ParkourProfile = parkourProfile;
        Classification = null;
        LateralX = LateralQuantum.Centre;
        LateralZ = LateralQuantum.Centre;
        Squeezed = false;
    }

    /// <summary>Records a feasible move whose kind the evaluator resolved for itself.</summary>
    internal void Set(int x, int y, int z, double cost, MoveType classification)
    {
        Set(x, y, z, cost);
        Classification = classification;
    }

    /// <summary>Marks the move infeasible.</summary>
    public void SetImpossible()
    {
        Cost = ActionCosts.CostInf;
        ParkourProfile = ParkourProfile.None;
        Classification = null;
        LateralX = LateralQuantum.Centre;
        LateralZ = LateralQuantum.Centre;
        Squeezed = false;
    }

    /// <summary>True when the move is infeasible.</summary>
    public readonly bool IsImpossible => Cost >= ActionCosts.CostInf;
}
