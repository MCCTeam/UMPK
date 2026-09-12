using Umpk.Geometry;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Goals;

/// <summary>A goal satisfied only at one exact block position.</summary>
public sealed class GoalBlock : IGoal
{
    /// <summary>The goal X.</summary>
    public int X { get; }

    /// <summary>The goal Y.</summary>
    public int Y { get; }

    /// <summary>The goal Z.</summary>
    public int Z { get; }

    /// <summary>Creates a block goal.</summary>
    public GoalBlock(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Creates a block goal from a position.</summary>
    public GoalBlock(BlockPos pos) : this(pos.X, pos.Y, pos.Z)
    {
    }

    /// <inheritdoc/>
    public bool IsInGoal(BlockPos pos) => pos.X == X && pos.Y == Y && pos.Z == Z;

    /// <inheritdoc/>
    public double Heuristic(BlockPos pos)
    {
        int dx = Math.Abs(pos.X - X);
        int dz = Math.Abs(pos.Z - Z);
        return DistanceHeuristic(dx, dz);
    }

    /// <summary>The octile ground distance to the goal column, priced at the sprint rate: the cheapest any sequence of moves can cover that much X/Z, and therefore an admissible lower bound on what is left to pay.</summary>
    /// <remarks>
    /// <para><b>There is deliberately no vertical term.</b> A cardinal step-ascend moves one block across and one block up for 5.5638 ticks, while pricing that height as another sprint block would produce 7.1276 and overstate the remaining cost. An overstated heuristic can make A* close a node before finding the cheapest route.</para>
    /// <para><b>Why the term cannot be rescaled instead of dropped.</b> An admissible coefficient has to be at most the cheapest extra cost one block of height can add to a move, and that number is zero: a step whose two support elevations differ by no more than <c>BlockSupport.PlayerStepHeight</c> is walked, and is charged the flat walk rate for a displacement that still carries a node-Y of one. Any positive coefficient over-states that edge. The same holds downward, and worse - vanilla's fall table integrates gravity against drag, so <c>ActionCosts.FallCost</c> is 7 ticks where the removed term charged 7.1276, and the table's cheapest ratio over its whole 256-block range is 0.426 ticks a block.</para>
    /// <para><b>What it costs, measured.</b> Nodes explored before and after, over the recorded shapes, with the route and its cost IDENTICAL in every row (the search buys the same answer, it just no longer asserts it without proof): dry plain 41 -&gt; 41, plain with a wall detour 4007 -&gt; 4007, ladder shaft 466 -&gt; 466, spiral well 371 -&gt; 373, slab street 302 -&gt; 323, ten-tread staircase 18 -&gt; 413, ten-block descent 16 -&gt; 521, twenty-four-block ascent 32 -&gt; 1044. The price is nil wherever the horizontal term already carries the route and is up to 33x on a shape that is almost pure climb, against a node budget of 800,000.</para>
    /// <para><b>A vertical term taken as a MAX rather than a sum</b> - <c>rise * SprintOneBlock</c>, which IS admissible because every upward move raises Y by exactly one and none is cheaper than a walked step - was built and measured against these same shapes and moved not one of them (spiral well 373 either way, every other row identical), because a walkable route gains at most one block of height per block travelled, so the horizontal term dominates the max almost everywhere. The chosen coefficient is therefore measurement-backed.</para>
    /// </remarks>
    internal static double DistanceHeuristic(int dx, int dz)
    {
        int horizontal = Math.Max(dx, dz);
        int diagonal = Math.Min(dx, dz);
        int straight = horizontal - diagonal;
        return (diagonal * ActionCosts.SprintOneBlock * ActionCosts.DiagonalMultiplier)
            + (straight * ActionCosts.SprintOneBlock);
    }

    /// <inheritdoc/>
    public bool TryGetTargetHint(out BlockPos hint)
    {
        hint = new BlockPos(X, Y, Z);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"GoalBlock({X}, {Y}, {Z})";
}
