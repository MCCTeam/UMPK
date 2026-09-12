namespace Umpk.Pathfinding.Core;

/// <summary>The result of a path search: the node sequence, the move sequence, the total cost, and per-search diagnostics. Deterministic for identical inputs; session-free and immutable.</summary>
public sealed class PathResult
{
    /// <summary>The search outcome.</summary>
    public PathStatus Status { get; }

    /// <summary>The path nodes from start to end (a single-element list when the start is already in the goal).</summary>
    public IReadOnlyList<PathNode> Path { get; }

    /// <summary>The move sequence corresponding to <see cref="Path"/> transitions (length <c>Path.Count - 1</c>).</summary>
    public IReadOnlyList<MoveType> Moves { get; }

    /// <summary>The total tick cost of the path (the G-cost of the final node).</summary>
    public double Cost { get; }

    /// <summary>Per-search diagnostics.</summary>
    public PathDiagnostics Diagnostics { get; }

    /// <summary>The number of nodes expanded during the search.</summary>
    public int NodesExplored => Diagnostics.NodesExplored;

    /// <summary>The elapsed planning time in milliseconds.</summary>
    public long ElapsedMs => Diagnostics.ElapsedMilliseconds;

    /// <summary>Creates a path result.</summary>
    public PathResult(PathStatus status, IReadOnlyList<PathNode> path, double cost, PathDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(path);
        Status = status;
        Path = path;
        Cost = cost;
        Diagnostics = diagnostics;
        Moves = BuildMoves(path);
    }

    private static MoveType[] BuildMoves(IReadOnlyList<PathNode> path)
    {
        if (path.Count < 2)
            return [];

        var moves = new MoveType[path.Count - 1];
        for (int i = 1; i < path.Count; i++)
            moves[i - 1] = path[i].MoveUsed;

        return moves;
    }

    /// <summary>Creates a failed result with diagnostics.</summary>
    public static PathResult Fail(PathDiagnostics diagnostics)
        => new(PathStatus.Failed, [], 0.0, diagnostics);
}
