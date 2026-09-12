namespace Umpk.Pathfinding.Core;

/// <summary>Per-search diagnostics attached to a <see cref="PathResult"/>. Diagnostics live on the result and flow through the per-instance observer seam rather than process-global state.</summary>
public readonly record struct PathDiagnostics
{
    /// <summary>The number of nodes expanded (popped from the open set).</summary>
    public int NodesExplored { get; init; }

    /// <summary>The elapsed planning time in milliseconds.</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>The number of times the search hit an unloaded-chunk boundary.</summary>
    public int UnloadedChunkHits { get; init; }

    /// <summary>True when the search stopped because the timeout deadline passed.</summary>
    public bool TimedOut { get; init; }

    /// <summary>True when the search stopped because the node budget was exhausted.</summary>
    public bool NodeBudgetExhausted { get; init; }

    /// <summary>How many jump-family arms this search refused because the takeoff floor's effective jump factor is under 1.0 - in practice, honey, or anything laid over honey.</summary>
    /// <remarks>
    /// <para>A refusal that leaves no trace is indistinguishable from an unreachable goal, and this one is easy to disbelieve: the block underfoot looks like a floor, the step ahead looks like an ordinary kerb, and the planner simply says no. A non-zero count here names the rule that said it. See <see cref="Moves.MoveHelper.CanTakeOffForAJump"/> for the measurement behind it.</para>
    /// <para>It counts ARMS, not nodes: one node can offer several jump-requiring moves and each is refused separately, so the magnitude means nothing beyond "more shapes were tried". Read it as a flag, not a census.</para>
    /// </remarks>
    public int SlowJumpFloorTakeoffsRefused { get; init; }

    /// <summary>How many jump-family arms this search refused because the takeoff cell is one the body HANGS in - a ladder or a vine rung with nothing underneath it.</summary>
    /// <remarks>
    /// <para>It gets a counter of its own rather than sharing <see cref="SlowJumpFloorTakeoffsRefused"/> because the two say opposite things to whoever reads them: that one means "the floor here is honey", this one means "the bot is on a rung and the way on is a corner". Folded together, a vine shaft would read as a honey field.</para>
    /// <para>A non-zero count at the top of a climbable is the normal answer and not a warning - the diagonal and parkour arms are offered at every rung and refused at every rung. It earns its keep when the search then FAILS, because it names the reason a route the operator can see with their own eyes was not taken. See <see cref="Moves.MoveHelper.IsHangingOnAClimbable"/>.</para>
    /// <para>It counts ARMS, not nodes, exactly as the counter above does.</para>
    /// </remarks>
    public int HangingTakeoffsRefused { get; init; }

    /// <summary>How many jump-family arms this search refused because the body is AFLOAT at the takeoff - water deeper than vanilla's 0.4 fluid-jump threshold, where the 0.42 ground jump does not exist.</summary>
    /// <remarks>
    /// <para>Its own counter for the same reason the one above has one: three refusals that all mean "no jump here" mean three different things to whoever is reading a failed search. This one means "the bot is in the water", and the route it is asking for is a swim.</para>
    /// <para>A non-zero count anywhere near water is the normal answer, not a warning: the ascend, level jump and parkour arms are offered at every wet node and refused at every wet node. It earns its keep when the search then fails at the foot of a fall. See <see cref="Moves.MoveHelper.IsAfloatInWater"/>.</para>
    /// <para>It counts ARMS, not nodes, exactly as the two counters above do.</para>
    /// </remarks>
    public int FloatingTakeoffsRefused { get; init; }
}
