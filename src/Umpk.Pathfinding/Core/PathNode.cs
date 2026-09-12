namespace Umpk.Pathfinding.Core;

/// <summary>An A* search node. Stored in the open/closed sets during a search. The packed position uses 26 bits per horizontal axis and 12 bits for Y, matching the world's coordinate range.</summary>
public sealed class PathNode
{
    /// <summary>Block X.</summary>
    public readonly int X;

    /// <summary>Block Y.</summary>
    public readonly int Y;

    /// <summary>Block Z.</summary>
    public readonly int Z;

    /// <summary>The accumulated cost from the start node.</summary>
    public double GCost;

    /// <summary>The heuristic cost to the goal.</summary>
    public double HCost;

    /// <summary>The total estimated cost (G + H).</summary>
    public double FCost => GCost + HCost;

    /// <summary>The parent node on the best-known path.</summary>
    public PathNode? Parent;

    /// <summary>The move used to reach this node from its parent.</summary>
    public MoveType MoveUsed;

    /// <summary>The parkour profile of the move used to reach this node.</summary>
    public ParkourProfile ParkourProfile;

    /// <summary>The run-up preparation state at this node.</summary>
    public EntryPreparationState EntryPreparation;

    /// <summary>Where inside its own cell the body stands at this node, on X and on Z, quantised to <see cref="Moves.LateralQuantum"/>.</summary>
    /// <remarks>
    /// <para><see cref="Moves.LateralQuantum.Centre"/> on every node of every plan that meets no bamboo, which is what keeps every existing route bit-identical. It is real node STATE and not a derived function of the position - two bodies in the same cell with different laterals can take different edges out of it - so it is part of the search key, in the same way the run-up preparation and the breath band are.</para>
    /// <para><c>PathSegmentBuilder</c> turns it into the segment's actual endpoint: the hardcoded <c>+ 0.5</c> becomes <c>+ 0.5 + lateral</c>, which is the whole executor-side change.</para>
    /// </remarks>
    public Moves.LateralQuantum LateralX;

    /// <inheritdoc cref="LateralX"/>
    public Moves.LateralQuantum LateralZ;

    /// <summary>The air DEFICIT on arrival at this node, in real ticks: how much of the 300-tick lung the route so far has spent. Zero everywhere when the search is not breath-aware, and zero at the start node regardless, so a plan never has to know the bot's actual lung.</summary>
    /// <remarks>A deficit rather than air remaining, so the start state is 0 whatever the bot is carrying and so a route is judged on what it COSTS rather than on what the bot happened to have when it was planned. It is monotone under a submerged move and decays four times as fast out of the water, at +4 air per tick.</remarks>
    public double AirDeficit;

    /// <summary>Ticks the body must STAND STILL at this node, refilling its lung, before the next move may begin. Zero everywhere except at a breathing cell the route arrives at with a deficit to clear.</summary>
    /// <remarks><c>AStarPathFinder.NextAirDeficit</c> has always computed this - it is what lets it zero the deficit at a cell whose head is dry - and has always charged it to the g-cost. It simply had nowhere to live: it was a local in the expansion loop, so the plan the search handed back carried no trace of a wait its own feasibility depended on. Storing it here is what lets <c>PathSegmentBuilder</c> put it on the segment that ends at this node.</remarks>
    public double BreathHoldTicks;

    /// <summary>The node's index in the open-set heap.</summary>
    public int HeapIndex;

    /// <summary>True while the node is in the open set.</summary>
    public bool IsOpen;

    /// <summary>True once the node is finalized.</summary>
    public bool IsClosed;

    /// <summary>Creates a node at a block position.</summary>
    public PathNode(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The packed position key for this node.</summary>
    public long PackedPosition => Pack(X, Y, Z);

    /// <summary>Packs a block position into a single long key.</summary>
    public static long Pack(int x, int y, int z)
    {
        long px = (long)(x + 30_000_000) & 0x3FFFFFF;
        long pz = (long)(z + 30_000_000) & 0x3FFFFFF;
        long py = (long)(y + 2048) & 0xFFF;
        return (px << 38) | (pz << 12) | py;
    }
}
