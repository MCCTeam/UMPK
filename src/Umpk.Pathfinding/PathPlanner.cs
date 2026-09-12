using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;

namespace Umpk.Pathfinding;

/// <summary>The session-free planning entry point: a planning world view plus options in, a <see cref="PathResult"/> out. Because it needs no live session, it is unit-testable against fixture worlds. Deterministic for identical inputs: the same snapshot, start, goal, and options produce the same path (the search is a stable-tie-break A* with a deterministic node budget).</summary>
public static class PathPlanner
{
    /// <summary>Plans a path from <paramref name="start"/> to <paramref name="goal"/> over <paramref name="world"/>, honoring <paramref name="options"/>. Runs the search on the thread pool with cancellation.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static Task<PathResult> FindPathAsync(
        PlanningWorldView world,
        PathfinderOptions options,
        BlockPos start,
        IGoal goal,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(goal);

        TimeProvider clock = timeProvider ?? TimeProvider.System;
        return Task.Run(() => FindPath(world, options, start, goal, clock, ct), ct);
    }

    /// <summary>The synchronous planning core (used by tests and the async wrapper).</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <param name="world">The planning world view.</param>
    /// <param name="options">The caller's options.</param>
    /// <param name="start">The start block.</param>
    /// <param name="goal">The goal.</param>
    /// <param name="timeProvider">The clock the timeout is measured against.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="capabilities">The player's capabilities at capture time.</param>
    /// <param name="fireHazardsCleared">Whether the caller has established that fire resistance covers this route; see <see cref="CalculationContext.FireHazardsCleared"/> for who owns that evidence and why it is not derived here.</param>
    public static PathResult FindPath(
        PlanningWorldView world,
        PathfinderOptions options,
        BlockPos start,
        IGoal goal,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default,
        PathfinderCapabilities? capabilities = null,
        bool fireHazardsCleared = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(goal);

        TimeProvider clock = timeProvider ?? TimeProvider.System;
        var ctx = new CalculationContext(world, options, capabilities, fireHazardsCleared);

        int sx = start.X;
        int sy = start.Y;
        int sz = start.Z;

        // If the feet cell is blocked but the head cell is clear, the player is standing one block higher than the truncated position suggests.
        if (!ctx.CanWalkThrough(sx, sy, sz) && ctx.CanWalkThrough(sx, sy + 1, sz))
            sy++;

        else
            ResolveFallingStart(ctx, sx, ref sy, sz);

        var finder = new AStarPathFinder();
        return finder.Calculate(ctx, sx, sy, sz, goal, ct, options.MaxNodes, options.Timeout, clock);
    }

    /// <summary>How far down a falling start is resolved, in cells: one world-height span.</summary>
    private const int FallingStartScanCells = PathfinderOptions.WorldHeightSpan;

    /// <summary>The downward mirror of the start-cell nudge: a body that is not standing on anything is FALLING, and the cell it happens to be passing through is not a cell any plan can start from.</summary>
    /// <remarks>
    /// <para>An unsupported search root can produce a route from a position the falling body will never occupy. Resolve the start down to the first climbable or water cell, or to the cell resting on the first usable support. The fall itself is already in progress; the plan begins after it.</para>
    /// <para>Scoped to a body that is genuinely unsupported in air. A start in water is a swimmer and is left alone (the whole water move family plans from exactly such cells); a start on a climbable is hanging, not falling; and a start with anything to stand on under it is not falling at all, which is every dry row of the course. A scan that finds nothing to land on leaves the start untouched, so the caller still gets a refusal rather than a silently relocated plan.</para>
    /// </remarks>
    private static void ResolveFallingStart(CalculationContext ctx, int x, ref int y, int z)
    {
        BlockState here = ctx.GetBlock(x, y, z);
        if (Moves.MoveHelper.IsWater(here) || here.IsClimbable || ctx.CanWalkOn(x, y - 1, z))
            return;

        for (int scan = 1; scan <= FallingStartScanCells; scan++)
        {
            int cell = y - scan;
            BlockState state = ctx.GetBlock(x, cell, z);
            if (Moves.MoveHelper.IsWater(state) || state.IsClimbable)
            {
                y = cell;
                return;
            }

            if (ctx.CanWalkThrough(x, cell, z))
                continue;

            if (ctx.CanWalkOn(x, cell, z))
                y = cell + 1;

            return;
        }
    }
}
