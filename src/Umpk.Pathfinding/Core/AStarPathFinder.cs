using Umpk.Geometry;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Moves.Impl;

namespace Umpk.Pathfinding.Core;

/// <summary>The weighted A* driver over the discretized block-move graph. Determinism: the open-set tie-break is a stable (F, H) comparison, the timeout is a caller-supplied deadline evaluated against an injected <see cref="TimeProvider"/> (no <c>DateTime.Now</c> reads inside the loop), and the node budget is a deterministic bound so identical inputs and options produce identical paths.</summary>
public sealed class AStarPathFinder
{
    /// <summary>The search state: WHERE the body is, what run-up it arrived with, and how much of its lung the route so far has spent, banded. The third field is 0 on every dry search and on every node of a dry region, so a plan that never touches water explores exactly the nodes it always did.</summary>
    /// <remarks>
    /// <para>Equality and hashing are written out rather than left to the compiler, and the reason is the shape of the second field. <see cref="EntryPreparationState"/> is itself a nine-field record struct, so a compiler-generated <c>NodeKey.GetHashCode</c> chains <c>EqualityComparer&lt;T&gt;.Default</c> over every field of both structs, for a key the node map is probed with about ten times per node explored. Measured over 200,000 insert-and-probe pairs with no run-up in progress: 13.4 ns an operation generated, 11.0 ns written out. The margin is modest because the JIT already devirtualises and inlines those comparers; what does NOT work is reaching for <see cref="HashCode"/>, which is 1.79x SLOWER than the generated key here (<c>NodeKeyHashBenchmark</c>).</para>
    /// <para>The hash short-circuits when no run-up is in progress, which is the overwhelmingly common case and the only case a dry search ever produces. That is legal because a hash may collide where equality may not: two keys that agree on position and band but carry different preparation origins under <see cref="EntryPreparationKind.None"/> would land in the same bucket and then compare unequal, which costs a probe and changes no result. Equality itself short-circuits nothing and compares all eleven fields.</para>
    /// </remarks>
    internal readonly struct NodeKey(
        long packedPosition, EntryPreparationState entryPreparation, int airBand, int lateral)
        : IEquatable<NodeKey>
    {
        /// <summary>The odd multiplier the C# compiler itself uses in generated record hashes (-1521134295). A multiply-add chain, not <see cref="HashCode"/>: <see cref="HashCode"/> runs a seeded xxHash round per value, and against a key this small the mixing costs more than the collisions it prevents. Measured over 200,000 insert-and-probe pairs, this chain is 0.82x the compiler-generated key where a <see cref="HashCode"/>-based version of the same equality is 1.79x.</summary>
        private const int HashPrime = -1521134295;

        public readonly long PackedPosition = packedPosition;

        public readonly EntryPreparationState EntryPreparation = entryPreparation;

        public readonly int AirBand = airBand;

        /// <summary>Where inside its cell the body stands, both axes packed into one small int: <c>(lateralX + 1) * 3 + (lateralZ + 1)</c>, so <b>4</b> is the centre and every plan that meets no bamboo carries 4 at every node.</summary>
        /// <remarks>A fourth dimension on the search key, and the cheapest possible one: it is a single int compare in <c>Equals</c> and, like the run-up origin, it is folded into the hash only when it is not the default, so a dry search's hash is the arithmetic it always was. It has to be in the key rather than derived, because two bodies in the same cell at different laterals genuinely have different edges available to them - which is the entire feature.</remarks>
        public readonly int Lateral = lateral;

        /// <summary>The <see cref="Lateral"/> of a centred body, which is every node of every existing plan.</summary>
        public const int CentredLateral = 4;

        /// <summary>Packs a pair of quanta into <see cref="Lateral"/>.</summary>
        public static int PackLateral(LateralQuantum x, LateralQuantum z) => (((sbyte)x + 1) * 3) + (sbyte)z + 1;

        public static bool operator ==(NodeKey left, NodeKey right) => left.Equals(right);

        public static bool operator !=(NodeKey left, NodeKey right) => !left.Equals(right);

        public bool Equals(NodeKey other)
        {
            if (PackedPosition != other.PackedPosition || AirBand != other.AirBand || Lateral != other.Lateral)
                return false;

            EntryPreparationState a = EntryPreparation;
            EntryPreparationState b = other.EntryPreparation;
            return a.Kind == b.Kind
                && a.OriginX == b.OriginX
                && a.OriginY == b.OriginY
                && a.OriginZ == b.OriginZ
                && a.ForwardX == b.ForwardX
                && a.ForwardZ == b.ForwardZ
                && a.RequiredSteps == b.RequiredSteps
                && a.BackwardSteps == b.BackwardSteps
                && a.ReturnSteps == b.ReturnSteps;
        }

        public override bool Equals(object? obj) => obj is NodeKey other && Equals(other);

        public override int GetHashCode()
        {
            long packed = PackedPosition;
            int hash = ((int)packed ^ (int)(packed >> 32)) * HashPrime;
            hash = (hash + AirBand) * HashPrime;

            if (Lateral != CentredLateral)
                hash = (hash + Lateral) * HashPrime;

            EntryPreparationState preparation = EntryPreparation;
            if (preparation.Kind == EntryPreparationKind.None)
                return hash;

            hash = (hash + (int)preparation.Kind) * HashPrime;
            hash = (hash + preparation.OriginX) * HashPrime;
            hash = (hash + preparation.OriginY) * HashPrime;
            hash = (hash + preparation.OriginZ) * HashPrime;
            hash = (hash + preparation.ForwardX) * HashPrime;
            hash = (hash + preparation.ForwardZ) * HashPrime;
            return (hash + ((preparation.RequiredSteps << 16) | (preparation.BackwardSteps << 8) | preparation.ReturnSteps)) * HashPrime;
        }
    }

    private readonly IMoveExpander[] _expanders;
    private readonly int _totalExpanderCapacity;
    private readonly int _maxChunkBorderFetch;

    /// <summary>Creates a finder with the default move set, or a caller-supplied move array wrapped as a legacy expander.</summary>
    public AStarPathFinder(IMove[]? moves = null, int maxChunkBorderFetch = 64)
        : this(BuildExpanders(moves), maxChunkBorderFetch)
    {
    }

    /// <summary>Creates a finder over explicit expanders.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="expanders"/> is null.</exception>
    internal AStarPathFinder(IMoveExpander[] expanders, int maxChunkBorderFetch)
    {
        ArgumentNullException.ThrowIfNull(expanders);
        _expanders = expanders;
        _maxChunkBorderFetch = maxChunkBorderFetch;

        int total = 0;
        for (int i = 0; i < expanders.Length; i++)
            total += expanders[i].MaxNeighbors;

        _totalExpanderCapacity = total;
    }

    private static IMoveExpander[] BuildExpanders(IMove[]? explicitMoves)
    {
        if (explicitMoves is null)
            return BuildDefaultExpanders();

        return [new LegacyMoveExpander(explicitMoves)];
    }

    /// <summary>The default expander set: jump family plus dynamic-landing, vertical, and swim moves.</summary>
    public static IMoveExpander[] BuildDefaultExpanders()
    {
        IMove[] legacyMoves =
        [
            new MoveDescend(1, 0),
            new MoveDescend(-1, 0),
            new MoveDescend(0, 1),
            new MoveDescend(0, -1),
            new MoveSprintDescend(2, 0),
            new MoveSprintDescend(-2, 0),
            new MoveSprintDescend(0, 2),
            new MoveSprintDescend(0, -2),
            new MoveSprintDescend(1, 1),
            new MoveSprintDescend(1, -1),
            new MoveSprintDescend(-1, 1),
            new MoveSprintDescend(-1, -1),
            new MoveClimb(true),
            new MoveClimb(false),
            new MoveFall(),
            new MoveSwim(1, 0),
            new MoveSwim(-1, 0),
            new MoveSwim(0, 1),
            new MoveSwim(0, -1),
            new MoveSwimVertical(true),
            new MoveSwimVertical(false),
            new MoveSwimExit(1, 0),
            new MoveSwimExit(-1, 0),
            new MoveSwimExit(0, 1),
            new MoveSwimExit(0, -1),
        ];

        return [new JumpExpander(), new LegacyMoveExpander(legacyMoves)];
    }

    /// <summary>Computes a path from a start block to the goal against a planning context.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public PathResult Calculate(
        CalculationContext ctx,
        int startX, int startY, int startZ,
        IGoal goal,
        CancellationToken ct,
        long nodeBudget,
        TimeSpan timeout,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long startTimestamp = timeProvider.GetTimestamp();

        // The deadline is compared against timestamps from the INJECTED provider, so it has to be expressed in THAT provider's tick unit. Reading TimeProvider.System.TimestampFrequency here instead scaled the budget by the ratio between the two frequencies: against a microsecond-based provider on Linux (host frequency 1e9) a one-second budget became a thousand seconds, and the timeout never fired. It went unnoticed because the only fake in the suite borrowed System.TimestampFrequency, which makes the two units coincide.
        long deadlineTicks = startTimestamp + (long)(timeout.TotalSeconds * timeProvider.TimestampFrequency);

        if (goal.IsInGoal(new BlockPos(startX, startY, startZ)))
            return new PathResult(
                PathStatus.Success,
                [new PathNode(startX, startY, startZ)],
                0.0,
                new PathDiagnostics { NodesExplored = 0, ElapsedMilliseconds = 0 });

        if (!IsGoalReachableFootPosition(ctx, goal))
            return PathResult.Fail(new PathDiagnostics { NodesExplored = 0, ElapsedMilliseconds = 0 });

        var openSet = new BinaryHeapOpenSet(4096);
        var nodeMap = new Dictionary<NodeKey, PathNode>(4096);

        var startNode = new PathNode(startX, startY, startZ)
        {
            GCost = 0,
            HCost = goal.Heuristic(new BlockPos(startX, startY, startZ)),
            IsOpen = true,
        };
        openSet.Insert(startNode);
        nodeMap[new NodeKey(startNode.PackedPosition, startNode.EntryPreparation, 0, NodeKey.CentredLateral)] = startNode;

        // The breath dimension is skipped outright on terrain that has no water in it, and that is sound rather than a heuristic: the arm's only inputs are IsSubmerged at two cells and the escape scan above one of them, all three of which read this region. MayContainWater is false only when no section overlapping the box can produce a water state at all. With no water reachable, NextAirDeficit returns 0 at every node, every deficit bands to 0, EscapeTicks short-circuits to Known, and IsBreathFeasible passes unconditionally - so the arm is three block reads per emitted neighbour that compute a constant. Measured on dry shapes that is 17% of the search's block reads and about 7% of its wall time.
        bool breathAware = ctx.Options.BreathAware && ctx.World.MayContainWater;
        int nodesExplored = 0;
        int unloadedChunkHits = 0;
        bool timedOut = false;
        bool budgetExhausted = false;
        bool cancelled = false;
        PathNode bestPartialNode = startNode;
        double bestPartialScore = startNode.HCost + (startNode.GCost * 0.5);

        Span<MoveNeighbor> neighborBuffer = _totalExpanderCapacity <= 512
            ? stackalloc MoveNeighbor[_totalExpanderCapacity]
            : new MoveNeighbor[_totalExpanderCapacity];

        while (openSet.Count > 0)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (nodesExplored >= nodeBudget)
            {
                budgetExhausted = true;
                break;
            }

            // Check the wall-clock deadline every 1024 nodes so the injected time source is read rarely and never dominates the hot path (determinism: the node budget is the primary bound).
            if ((nodesExplored & 1023) == 0 && timeProvider.GetTimestamp() > deadlineTicks)
            {
                timedOut = true;
                break;
            }

            PathNode current = openSet.RemoveMin();
            current.IsClosed = true;
            nodesExplored++;

            if (goal.IsInGoal(new BlockPos(current.X, current.Y, current.Z)))
            {
                List<PathNode> path = ReconstructPath(current);
                long elapsedMs = ElapsedMilliseconds(timeProvider, startTimestamp);
                return new PathResult(PathStatus.Success, path, current.GCost, new PathDiagnostics
                {
                    NodesExplored = nodesExplored,
                    ElapsedMilliseconds = elapsedMs,
                    UnloadedChunkHits = unloadedChunkHits,
                    SlowJumpFloorTakeoffsRefused = ctx.SlowJumpFloorTakeoffsRefused,
                    HangingTakeoffsRefused = ctx.HangingTakeoffsRefused,
                    FloatingTakeoffsRefused = ctx.FloatingTakeoffsRefused,
                });
            }

            ctx.PreviousMoveType = current.MoveUsed;
            ctx.CurrentEntryPreparation = current.EntryPreparation;
            ctx.CurrentLateralX = current.LateralX;
            ctx.CurrentLateralZ = current.LateralZ;

            int bufferOffset = 0;
            for (int ex = 0; ex < _expanders.Length; ex++)
            {
                IMoveExpander expander = _expanders[ex];
                Span<MoveNeighbor> slot = neighborBuffer.Slice(bufferOffset, expander.MaxNeighbors);
                int produced = expander.Expand(ctx, current.X, current.Y, current.Z, slot);
                bufferOffset += expander.MaxNeighbors;

                for (int i = 0; i < produced; i++)
                {
                    MoveNeighbor emitted = slot[i];
                    int nx = emitted.DestX;
                    int ny = emitted.DestY;
                    int nz = emitted.DestZ;

                    // A body standing off centre may leave only by the arm that reasoned about where it stands. Every other family - the jump arcs, the descends, the climbs, the swims - derived its feasibility for a CENTRED body and none of it has been re-derived for one that is not, so admitting them here would be a route the executor cannot walk rather than a conservative refusal. Free on every existing plan: nothing is ever off centre without a bamboo post beside it.
                    if (!emitted.Squeezed
                        && (current.LateralX != LateralQuantum.Centre || current.LateralZ != LateralQuantum.Centre))
                        continue;

                    // A cell holding an open door or trapdoor PANEL may be entered, and left, only by a squared-up cardinal Traverse running along the panel: the crossing has 0.0125 blocks of clearance on the panel side and no other move can be aimed into that band. See MoveHelper.IsBarrierCrossingLegal. Free on terrain with no such block.
                    if (!Moves.MoveHelper.IsBarrierCrossingLegal(
                            ctx, current.X, current.Y, current.Z, nx, ny, nz, emitted.MoveType))
                        continue;

                    if (!ctx.IsChunkLoaded(nx, nz))
                    {
                        unloadedChunkHits++;
                        if (unloadedChunkHits > _maxChunkBorderFetch)
                            continue;

                    }

                    // A crossing that needs a door opened first pays for the door here, at the one edge that needs it, rather than inside a move evaluator: the move families do not know about each other and every one of them can end up in a doorway, and a surcharge applied in N places is a surcharge applied twice sooner or later. Charged on ENTRY only - MoveHelper.TryGetInteraction never looks at the source column - so walking back OUT of a doorway is free, and a two-block door is one interaction because its upper half adopts the lower half's OPEN through updateShape.
                    double interaction = 0.0;
                    if (ctx.MayContainBarrier
                        && Moves.MoveHelper.TryGetInteraction(
                            ctx, current.X, current.Y, current.Z, nx, ny, nz, out _))
                        interaction = ActionCosts.InteractLatency;

                    double tentativeG = current.GCost + emitted.Cost + interaction;
                    EntryPreparationState nextPreparation = ResolveEntryPreparation(current, emitted.MoveType, emitted.DestX, emitted.DestY, emitted.DestZ);

                    long packed = PathNode.Pack(nx, ny, nz);
                    double nextDeficit = 0.0;
                    int band = 0;

                    // Store the breathing pause on the node so route materialization can preserve it. See PathSegment.BreathHoldTicks.
                    double breatheTicks = 0.0;
                    if (breathAware)
                    {
                        nextDeficit = NextAirDeficit(ctx, current, emitted, nx, ny, nz, out breatheTicks);
                        if (!IsBreathFeasible(ctx, nextDeficit, nx, ny, nz))
                            continue;

                        // The breathing stop is paid for in the g-cost, which is what makes an air detour a decision the search can weigh instead of a free lunch. PathSegmentBuilder excludes it from the travel cost using the copy on the node.
                        tentativeG += breatheTicks;

                        band = BreathModel.Band(nextDeficit);
                        if (band > 0 && IsDominated(
                                nodeMap, packed, nextPreparation, band,
                                NodeKey.PackLateral(emitted.LateralX, emitted.LateralZ), tentativeG))
                            continue;

                    }

                    var key = new NodeKey(
                        packed, nextPreparation, band, NodeKey.PackLateral(emitted.LateralX, emitted.LateralZ));

                    if (nodeMap.TryGetValue(key, out PathNode? neighbor))
                    {
                        if (neighbor.IsClosed)
                            continue;

                        if (tentativeG >= neighbor.GCost)
                            continue;

                        neighbor.GCost = tentativeG;
                        neighbor.AirDeficit = nextDeficit;
                        neighbor.BreathHoldTicks = breatheTicks;
                        neighbor.Parent = current;
                        neighbor.MoveUsed = emitted.MoveType;
                        neighbor.ParkourProfile = emitted.ParkourProfile;
                        neighbor.EntryPreparation = nextPreparation;
                        neighbor.LateralX = emitted.LateralX;
                        neighbor.LateralZ = emitted.LateralZ;
                        if (neighbor.IsOpen)
                            openSet.Update(neighbor);

                    }
                    else
                    {
                        neighbor = new PathNode(nx, ny, nz)
                        {
                            GCost = tentativeG,
                            AirDeficit = nextDeficit,
                            BreathHoldTicks = breatheTicks,
                            HCost = goal.Heuristic(new BlockPos(nx, ny, nz)),
                            Parent = current,
                            MoveUsed = emitted.MoveType,
                            ParkourProfile = emitted.ParkourProfile,
                            EntryPreparation = nextPreparation,
                            LateralX = emitted.LateralX,
                            LateralZ = emitted.LateralZ,
                            IsOpen = true,
                        };
                        nodeMap[key] = neighbor;
                        openSet.Insert(neighbor);
                    }

                    double partialScore = neighbor.HCost + (neighbor.GCost * 0.5);
                    if (partialScore < bestPartialScore)
                    {
                        bestPartialScore = partialScore;
                        bestPartialNode = neighbor;
                    }
                }
            }
        }

        long finalElapsedMs = ElapsedMilliseconds(timeProvider, startTimestamp);
        bool searchAborted = timedOut || budgetExhausted || cancelled;

        if (bestPartialNode != startNode && (searchAborted || unloadedChunkHits > 0))
        {
            List<PathNode> path = ReconstructPath(bestPartialNode);
            return new PathResult(PathStatus.Partial, path, bestPartialNode.GCost, new PathDiagnostics
            {
                NodesExplored = nodesExplored,
                ElapsedMilliseconds = finalElapsedMs,
                UnloadedChunkHits = unloadedChunkHits,
                TimedOut = timedOut,
                NodeBudgetExhausted = budgetExhausted,
                SlowJumpFloorTakeoffsRefused = ctx.SlowJumpFloorTakeoffsRefused,
                HangingTakeoffsRefused = ctx.HangingTakeoffsRefused,
                FloatingTakeoffsRefused = ctx.FloatingTakeoffsRefused,
            });
        }

        return PathResult.Fail(new PathDiagnostics
        {
            NodesExplored = nodesExplored,
            ElapsedMilliseconds = finalElapsedMs,
            UnloadedChunkHits = unloadedChunkHits,
            TimedOut = timedOut,
            NodeBudgetExhausted = budgetExhausted,
            SlowJumpFloorTakeoffsRefused = ctx.SlowJumpFloorTakeoffsRefused,
            HangingTakeoffsRefused = ctx.HangingTakeoffsRefused,
            FloatingTakeoffsRefused = ctx.FloatingTakeoffsRefused,
        });
    }

    /// <summary>The air deficit on arrival at a neighbour, in REAL ticks.</summary>
    /// <remarks>
    /// <para>Real ticks, not the planner's charge, and priced at the SLOWEST water rate on every era: 10.204 ticks a block, the no-sprint <c>0.0196 / (1 - 0.8)</c>. The search has no <c>PhysicsProfile</c> and no <c>AllowSprint</c> to key a faster rate on, and it must not need one, because the number has to be a bound rather than an estimate. It is deliberately at least what <see cref="BreathValidator"/> charges - the validator prices a sprint-aware swim at 5.106 - so any route this search approves the validator approves too. That inequality is what makes the validator a useful runtime assertion instead of a second opinion.</para>
    /// <para>Out of the water the deficit decays four times as fast as it accumulated: +4 air per tick against -1 while submerged.</para>
    /// <para><b>The breathing stop</b> (<paramref name="breatheTicks"/>). Arriving at a cell whose head is out of the water, the player may simply stay there until the lung is full: air refills four ticks per tick while the eye is outside water, with no upper bound on how long that is. The search had no way to say so, and the only refill it could express was the decay term above - four times the cost of a move whose BOTH endpoints are breathing - so a breathing node banked air in proportion to how many moves could be made while breathing, and a pocket one cell across banked exactly nothing. Measured on the E4 replica, a bore of 80 with bells at 20/42/60: bells six or sixteen cells long plan the whole bore (1,204 and 1,470 nodes), bells ONE cell long refuse everything past x=29, which is where a single lung runs out. Same terrain, same bands, same escape reserve; only the number of breathing moves differs.</para>
    /// <para>The wait is real time, so it is returned to the caller and charged to the g-cost rather than being free. This also prevents adjacent breathing cells from generating unbounded lung time. The first cell already restores the full deficit.</para>
    /// </remarks>
    private static double NextAirDeficit(
        CalculationContext ctx, PathNode current, in MoveNeighbor emitted, int nx, int ny, int nz, out double breatheTicks)
    {
        breatheTicks = 0.0;

        // EITHER endpoint submerged makes the move a wet one, which is exactly BreathValidator's rule. Reading the destination alone let a move OUT of the water refill instead of costing, and a route that hopped out and back in every other node could underprice repeated immersion by 300 ticks and make the validator reject the search's own output.
        bool destBreathing = !BreathModel.IsSubmerged(ctx.World, nx, ny, nz);
        bool submerged = !destBreathing
            || BreathModel.IsSubmerged(ctx.World, current.X, current.Y, current.Z);

        double deficit;
        if (!submerged)
            deficit = Math.Max(0.0, current.AirDeficit - (BreathValidator.RefillPerTick * emitted.Cost));

        else
        {
            double blocks = Math.Sqrt(
                (double)(((nx - current.X) * (nx - current.X))
                    + ((ny - current.Y) * (ny - current.Y))
                    + ((nz - current.Z) * (nz - current.Z))));
            // Deliberately the no-sprint terminal velocity and NOT BreathValidator's measured wade rate (SprintWadeBlocksPerTick, 7.299 ticks a block), even though this is meant to be one model. The two caps are not the same cap: this arm refuses at a FULL lung (IsBreathFeasible reads BreathModel.FullLungTicks) where the validator refuses at `air - ReactionTicks`, so priced identically the search would be up to 10 + (300 - air) ticks LOOSER than the validator and would hand it routes it refuses. Measured: pricing this arm at the wade rate makes the search skip E4's third bell and the validator then refuses the plan at `peak 363,3 ticks, budget 290, first violation at segment 88 of 91`. The 1.4x this number stands above the validator's walk is what keeps the implication "the search approved it" => "the validator approves it" true, which BreathRegressionQuartetTests.PlannedRoute_AlsoPassesTheValidator pins. Threading the live lung into the search is the honest fix and is an API change, not a constant.
            double real = Math.Max(emitted.Cost, blocks / BreathValidator.SubmergedBlocksPerTick);
            deficit = current.AirDeficit + real;
        }

        if (destBreathing && deficit > 0.0)
        {
            breatheTicks = deficit / BreathValidator.RefillPerTick;
            deficit = 0.0;
        }

        return deficit;
    }

    /// <summary>Whether some state already reached this cell with no more deficit AND no more cost.</summary>
    /// <remarks>The dimension multiplies states only where they are submerged, and in open water almost every pair of states at a cell is comparable, because both the deficit and the g-cost grow with the length of the route that produced them. Dropping the dominated one collapses the effective branching from "one state per band" to "one per distinct air pocket on the frontier", which in a featureless ocean is one. The scan is at most eight lookups, and on dry terrain the band is always 0 so it never runs at all.</remarks>
    private static bool IsDominated(
        Dictionary<NodeKey, PathNode> nodeMap, long packed, EntryPreparationState preparation, int band,
        int lateral, double gCost)
    {
        for (int lower = 0; lower < band; lower++)
            if (nodeMap.TryGetValue(new NodeKey(packed, preparation, lower, lateral), out PathNode? better)
                && better.GCost <= gCost)
                return true;

        return false;
    }

    /// <summary>Whether a body that has spent <paramref name="deficit"/> of its lung can be at a cell at all.</summary>
    /// <remarks>Two clauses, and the second one's silence is deliberate. The deficit itself may never exceed a lung - that is the clause E3's sealed bore and E18's one-way dive fail. The escape reserve on top of it only refuses when the escape is KNOWN: an unknown escape means the vertical scan found a ceiling or ran out of scan, and refusing on that would refuse every node under E15's twenty-block shelf, a lane the bot swims comfortably inside one breath. Under a lid the deficit clause is the whole test, which is exactly right - under a lid the route IS the escape.</remarks>
    private static bool IsBreathFeasible(CalculationContext ctx, double deficit, int nx, int ny, int nz)
    {
        double max = BreathModel.FullLungTicks;
        if (deficit > max)
            return false;

        BreathEscape escape = BreathModel.EscapeTicks(ctx.World, nx, ny, nz, EscapeProfile);
        return !escape.IsKnown || deficit + escape.Ticks + BreathModel.ReactionTicks <= max;
    }

    /// <summary>The profile the escape scan is priced at: the SPRINT-AWARE climb, 2.62 ticks a block.</summary>
    /// <remarks>
    /// <para>Deliberately the FAST rate, where the deficit above is deliberately the SLOW one, and the two are not inconsistent because they do different jobs in opposite directions. The deficit is a safety bound and has to dominate <see cref="BreathValidator"/>'s, so it takes the slowest water rate on any era. The escape reserve is a bail-out margin, and over-stating THAT does not make anything safer - it refuses routes the bot swims comfortably. Priced at the legacy 10 ticks a block it refuses a sixteen-block climb out of a still shaft that the executor really makes in 43 ticks, which is not caution, it is a 3.8x modelling error pointed at the answer.</para>
    /// <para>The residual: on a legacy server the reserve under-states the climb. The deficit clause still carries the legacy rate, and the life-safety supervisor reads the REAL profile at runtime and pre-empts, so the exposure is a route that is tighter than planned rather than one nobody is watching. Giving the search a <c>PhysicsProfile</c> is the honest fix and is an API change, not a constant.</para>
    /// </remarks>
    private static readonly Umpk.Physics.PhysicsProfile EscapeProfile = Umpk.Physics.PhysicsProfile.ForProtocol(772);

    /// <summary>The span since <paramref name="startTimestamp"/> in milliseconds, converted with the injected provider's own frequency. <see cref="TimeProvider.GetElapsedTime(long)"/> performs this conversion correctly even when the provider and host stopwatch use different tick units.</summary>
    private static long ElapsedMilliseconds(TimeProvider timeProvider, long startTimestamp)
        => (long)timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private static EntryPreparationState ResolveEntryPreparation(PathNode current, MoveType moveType, int destX, int destY, int destZ)
    {
        EntryPreparationState advanced = AdvanceExistingPreparation(current, moveType, destX, destY, destZ);
        if (!advanced.IsNone)
            return advanced;

        if (TryStartSidewallRunupPreparation(current, moveType, destX, destY, destZ, out EntryPreparationState started))
            return started;

        return EntryPreparationState.None;
    }

    private static EntryPreparationState AdvanceExistingPreparation(PathNode current, MoveType moveType, int destX, int destY, int destZ)
    {
        EntryPreparationState state = current.EntryPreparation;
        if (state.IsNone)
            return EntryPreparationState.None;

        if (moveType != MoveType.Traverse || destY != current.Y)
            return EntryPreparationState.None;

        int stepX = destX - current.X;
        int stepZ = destZ - current.Z;

        if (state.BackwardSteps < state.RequiredSteps && stepX == -state.ForwardX && stepZ == -state.ForwardZ)
            return state.AdvanceBackward();

        if (state.BackwardSteps == state.RequiredSteps
            && state.ReturnSteps < state.RequiredSteps
            && stepX == state.ForwardX
            && stepZ == state.ForwardZ)
        {
            EntryPreparationState nextState = state.AdvanceReturn();
            if (nextState.IsPrepared && (destX != state.OriginX || destY != state.OriginY || destZ != state.OriginZ))
                return EntryPreparationState.None;

            return nextState;
        }

        return EntryPreparationState.None;
    }

    private static bool TryStartSidewallRunupPreparation(PathNode current, MoveType moveType, int destX, int destY, int destZ, out EntryPreparationState state)
    {
        state = EntryPreparationState.None;

        if (!current.EntryPreparation.IsNone || moveType != MoveType.Traverse || destY != current.Y)
            return false;

        int stepX = destX - current.X;
        int stepZ = destZ - current.Z;

        ReadOnlySpan<(int fx, int fz)> forwards =
        [
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        ];

        for (int i = 0; i < forwards.Length; i++)
        {
            (int forwardX, int forwardZ) = forwards[i];
            if (stepX != -forwardX || stepZ != -forwardZ)
                continue;

            int xOffset, zOffset;
            if (forwardX != 0)
            {
                xOffset = forwardX * 5;
                zOffset = 1;
            }
            else
            {
                xOffset = 1;
                zOffset = forwardZ * 5;
            }

            if (!ParkourFeasibility.TryGetRequiredStaticEntryRunupSteps(current.MoveUsed, xOffset, zOffset, yDelta: -1, out int requiredSteps))
                continue;

            state = new EntryPreparationState(
                EntryPreparationKind.SidewallRunup,
                current.X,
                current.Y,
                current.Z,
                forwardX,
                forwardZ,
                (byte)requiredSteps,
                BackwardSteps: 1,
                ReturnSteps: 0);
            return true;
        }

        return false;
    }

    private static List<PathNode> ReconstructPath(PathNode end)
    {
        var path = new List<PathNode>();
        PathNode? current = end;
        while (current is not null)
        {
            path.Add(current);
            current = current.Parent;
        }

        path.Reverse();
        return path;
    }

    private static bool IsGoalReachableFootPosition(CalculationContext ctx, IGoal goal)
    {
        if (goal is not GoalBlock blockGoal)
            return true;

        if (!ctx.IsChunkLoaded(blockGoal.X, blockGoal.Z))
            return true;

        if (blockGoal.Y == int.MinValue)
            return false;

        // MoveHelper.CanStandAt covers the solid-footing case plus a goal standing in a water column. PhysicsEngineHolder.CanStandAt asks the same question for reporting, and a report that disagrees with the precheck that produced the refusal would be worse than no report.
        return MoveHelper.CanStandAt(ctx, blockGoal.X, blockGoal.Y, blockGoal.Z);
    }
}
