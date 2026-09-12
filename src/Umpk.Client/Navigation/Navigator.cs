using Microsoft.Extensions.Logging;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;

namespace Umpk.Client.Navigation;

/// <summary>Drives physics-based movement and pathfinding for the local player. Runs under a movement lease on the session loop: each tick it reads engine state, ticks the path executor, applies rotation, and steps the engine, replanning on deviation/failure. The client feeds physics conditions to the engine from tracked self state.</summary>
public sealed class Navigator
{
    private readonly ClientSessionServices _services;
    private readonly PhysicsEngineHolder _physics;
    private readonly MovementLeaseManager _leases;
    private readonly ILogger _logger;

    /// <summary>The block-interaction surface, reached lazily because <c>ClientActions</c> is constructed AFTER this navigator - the same knot <c>MovementActions</c> unties from the other side by taking <c>() =&gt; _navigator</c>. Null on a session that has no actions wired, which a hand-built holder-level test is.</summary>
    private readonly Func<Actions.InteractionActions?> _interaction;
    private PendingTick? _pendingTick;

    private abstract class PendingTick
    {
        internal abstract bool Execute(PhysicsEngineHolder physics);
        internal abstract void Cancel();
    }

    private sealed class ApproachTick(Vec3d target, double tolerance, TaskCompletionSource<bool> completion)
        : PendingTick
    {
        internal override bool Execute(PhysicsEngineHolder physics)
        {
            if (completion.Task.IsCompleted)
                return false;

            try
            {
                completion.TrySetResult(physics.TickApproach(target, tolerance));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return true;
        }

        internal override void Cancel() => completion.TrySetCanceled();
    }

    private sealed class RouteTick(PathExecutor executor, TaskCompletionSource<NavigationTickOutcome> completion)
        : PendingTick
    {
        internal override bool Execute(PhysicsEngineHolder physics)
        {
            if (completion.Task.IsCompleted)
                return false;

            try
            {
                completion.TrySetResult(physics.TickNavigation(executor));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return true;
        }

        internal override void Cancel() => completion.TrySetCanceled();
    }

    internal Navigator(
        ClientSessionServices services,
        PhysicsEngineHolder physics,
        MovementLeaseManager leases,
        ILogger logger,
        Func<Actions.InteractionActions?>? interaction = null)
    {
        _services = services;
        _physics = physics;
        _leases = leases;
        _logger = logger;
        _interaction = interaction ?? (static () => null);
    }

    /// <summary>Whether pathfinding is available (feature enabled).</summary>
    public bool PathfindingAvailable => _services.State.Features.Pathfinding;

    /// <summary>Whether the movement lease belongs to a navigator operation, including async waits.</summary>
    internal bool HasActiveOperation => _leases.CurrentOwner?.StartsWith("Navigator.", StringComparison.Ordinal) == true;

    /// <summary>Executes the one controller request assigned to this client tick. Called only by the client's session-tick owner; navigation never owns a timer.</summary>
    internal bool TickOnLoop()
    {
        PendingTick? pending = _pendingTick;
        _pendingTick = null;
        return pending?.Execute(_physics) == true;
    }

    /// <summary>Clears a controller request that belonged to the departing connection.</summary>
    internal void ResetSession()
    {
        _pendingTick?.Cancel();
        _pendingTick = null;
    }

    /// <summary>Terrain-level movement to the block containing <paramref name="target"/>. Completes when the player is in that block, or when the closest block the planner can finish in has been reached; throws when no path exists at all.</summary>
    /// <remarks>The goal is the exact destination block. A radius-1 near goal is only a fallback for a destination the planner cannot finish in, such as a ladder rung, solid block, or unloaded column. Using the near goal unconditionally would allow the start or an adjacent block to count as the requested arrival.</remarks>
    public Task MoveToAsync(Vec3d target, CancellationToken ct)
        => MoveToAsync(target, PathfinderOptions.Default, ct);

    /// <summary><see cref="MoveToAsync(Vec3d, CancellationToken)"/> planned under caller-supplied options, for a caller that needs limits the defaults refuse (see <see cref="PathfinderOptions.UnsafeFalls"/>).</summary>
    public async Task MoveToAsync(Vec3d target, PathfinderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_services.State.Features.Physics)
            throw new FeatureDisabledException("Physics");

        using CancellationTokenSource linked = LinkToCurrentSession(ct);
        ct = linked.Token;
        ct.ThrowIfCancellationRequested();

        using IMovementLease lease = _leases.TryAcquire("Navigator.MoveTo")
            ?? throw new InvalidOperationException(
                $"Movement is held by '{_leases.CurrentOwner}'.");

        BlockPos destination = BlockPos.Containing(target);
        BlockPos start = await _services.InvokeOnLoopResult(
            () => BlockPos.Containing(_services.State.Self.Position), ct).ConfigureAwait(false);
        if (start == destination)
        {
            // The whole request is inside one block, so there is no path to plan: walk the remaining fraction of a block directly. Returning here instead was a no-op reported as an arrival.
            await ApproachPointAsync(target, ct).ConfigureAwait(false);
            return;
        }

        var exact = new GoalBlock(destination);
        PlannedRoute? route = await PlanOnLoopAsync(exact, options, ct).ConfigureAwait(false);
        if (route is not null)
        {
            await RunNavigationAsync(exact, options, route.Value, ct).ConfigureAwait(false);
            return;
        }

        // Nothing can stand in the destination block. Get as close as the planner can and let the caller decide what to report; it is the caller that knows whether "next to it" satisfies the request.
        var near = new NearGoal(destination, 1);

        // Already inside the ball, which is the case the owner actually hits: standing on a face neighbour of a cell no body fits in and asking to step into it. There is nothing to plan and nowhere closer to walk, and planning it anyway is a trap - A* short-circuits goal.IsInGoal at the START node into a ONE-node path, PathSegmentBuilder yields no segments, PhysicsEngineHolder.BuildExecutor returns null on segments.Count == 0, and the `?? throw` below fired "No path to the goal was found." at a body that was already as close as any body
        // can get. RunNavigationAsync has carried the same guard since ItemsCollector stalled on it;
        // this fallback never got it.
        //
        // It does NOT need to also ask whether the destination is unstandable, and the reason is geometric: a radius-1 ball is the six AXIS neighbours, no diagonals. A standable same-Y
        // neighbour is one plain walk edge away, so the exact plan above could not have failed for it;
        // and neither vertical neighbour can be standable while the body occupies the column, because MoveHelper.CanStandAt wants CanWalkOn at the cell below, which for the cell above the body is the body's own passable feet cell. Inside the ball, "the exact plan failed" already means "the destination is unstandable".
        if (near.IsInGoal(start))
        {
            _logger.LogDebug(
                "No body can occupy {Destination} and the player is already in the radius-1 ball around "
                + "it at {Start}; nothing to plan.",
                destination,
                start);
            return;
        }

        PlannedRoute fallback = await PlanOnLoopAsync(near, options, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No path to the goal was found.");
        await RunNavigationAsync(near, options, fallback, ct).ConfigureAwait(false);
    }

    /// <summary>Pathfinding navigation to a goal. Completes on arrival, throws on failure.</summary>
    public Task NavigateAsync(IGoal goal, CancellationToken ct)
        => NavigateAsync(goal, PathfinderOptions.Default, ct);

    /// <summary><see cref="NavigateAsync(IGoal, CancellationToken)"/> planned under caller-supplied options.</summary>
    public async Task NavigateAsync(IGoal goal, PathfinderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(options);
        if (!_services.State.Features.Pathfinding)
            throw new FeatureDisabledException("Pathfinding");

        using CancellationTokenSource linked = LinkToCurrentSession(ct);
        ct = linked.Token;
        ct.ThrowIfCancellationRequested();

        using IMovementLease lease = _leases.TryAcquire("Navigator.Navigate")
            ?? throw new InvalidOperationException(
                $"Movement is held by '{_leases.CurrentOwner}'.");

        await RunNavigationAsync(goal, options, ct).ConfigureAwait(false);
    }

    /// <summary>Links an operation to the live connection without conflating an absent connection with cancellation. Cancellation remains reserved for an explicit caller request or for a connection whose lifetime ended after the operation was admitted.</summary>
    private CancellationTokenSource LinkToCurrentSession(CancellationToken caller)
    {
        // Preserve the caller's cancellation contract even when no connection currently exists.
        caller.ThrowIfCancellationRequested();

        CancellationToken? session = _services.CurrentSessionCancellation();
        if (session is null)
            throw new InvalidOperationException("The client is not connected.");

        return CancellationTokenSource.CreateLinkedTokenSource(caller, session.Value);
    }

    /// <summary>
    /// <see cref="MoveToAsync(Vec3d, CancellationToken)"/>, verified: a completed move TASK is not an arrival, so this re-reads the player's own position afterwards and judges it against the request instead of reporting the task's completion as a success.
    ///
    /// <para>The planner can fall back to the closest block it can finish in when the destination cannot be occupied. Re-reading the position prevents that fallback from being reported as an arrival.</para>
    /// </summary>
    public Task<MoveResult> MoveToVerifiedAsync(Vec3d target, CancellationToken ct)
        => MoveToVerifiedAsync(target, PathfinderOptions.Default, ct);

    /// <summary><see cref="MoveToVerifiedAsync(Vec3d, CancellationToken)"/> planned under caller-supplied options.</summary>
    public async Task<MoveResult> MoveToVerifiedAsync(
        Vec3d target, PathfinderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Vec3d start = await _services.InvokeOnLoopResult(() => _services.State.Self.Position, ct).ConfigureAwait(false);
        bool subBlockRequest = IsSubBlockRequest(start, target);

        await MoveToAsync(target, options, ct).ConfigureAwait(false);

        Vec3d arrival = await _services.InvokeOnLoopResult(() => _services.State.Self.Position, ct).ConfigureAwait(false);
        return await ClassifyAsync(Judge(target, arrival, subBlockRequest), options, ct).ConfigureAwait(false);
    }

    /// <summary>Separates the two failures a caller has to be able to tell apart: "I cannot stand there" from "I cannot get there". <see cref="Judge"/> is pure and answers only the first question a caller asks - did the body end up where it was sent - so the second one is asked here, where there is a session to ask the world with.</summary>
    /// <remarks>One world read, on the miss path only, and it is the SAME predicate the planner's own goal precheck uses (<c>MoveHelper.CanStandAt</c>, through <see cref="PhysicsEngineHolder.CanStandAt"/>), so the sentence a host prints cannot contradict the search that produced it. An unloaded or unavailable destination answers "standable", which keeps the report from asserting anything about terrain the client has never seen.</remarks>
    private async Task<MoveResult> ClassifyAsync(
        MoveResult judged, PathfinderOptions options, CancellationToken ct)
    {
        if (judged.Reached)
            return judged;

        BlockPos destination = BlockPos.Containing(judged.Target);
        bool standable = await _services.InvokeOnLoopResult(
            () => _physics.CanStandAt(destination, options), ct).ConfigureAwait(false);

        return standable ? judged : judged with { DestinationUnstandable = true };
    }

    /// <summary><see cref="NavigateAsync(IGoal, CancellationToken)"/>, verified against an explicit <paramref name="target"/> point rather than the goal itself. An <see cref="IGoal"/> is a PREDICATE (<c>IsInGoal(BlockPos)</c>): it can say whether a block satisfies the goal, but it cannot say WHERE "there" is, so it has no point to measure a sub-block arrival against and no way to floor-compare a block-level one. The caller, who built the goal from a real destination, is the one who knows what "there" means.</summary>
    public Task<MoveResult> NavigateVerifiedAsync(IGoal goal, Vec3d target, CancellationToken ct)
        => NavigateVerifiedAsync(goal, target, PathfinderOptions.Default, ct);

    /// <summary><see cref="NavigateVerifiedAsync(IGoal, Vec3d, CancellationToken)"/> planned under caller-supplied options.</summary>
    public async Task<MoveResult> NavigateVerifiedAsync(
        IGoal goal, Vec3d target, PathfinderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(options);
        Vec3d start = await _services.InvokeOnLoopResult(() => _services.State.Self.Position, ct).ConfigureAwait(false);
        bool subBlockRequest = IsSubBlockRequest(start, target);

        await NavigateAsync(goal, options, ct).ConfigureAwait(false);

        Vec3d arrival = await _services.InvokeOnLoopResult(() => _services.State.Self.Position, ct).ConfigureAwait(false);
        return Judge(target, arrival, subBlockRequest);
    }

    /// <summary>Whether a move from <paramref name="from"/> to <paramref name="target"/> never leaves the block it starts in, which makes it a request for a FRACTION of a block rather than a request to reach another block. A move-to-centre-of-the-current-block is always one; a move to any point still inside the occupied block is too. This selects between <see cref="Judge"/>'s two comparisons.</summary>
    public static bool IsSubBlockRequest(Vec3d from, Vec3d target) => BlockPos.Containing(from) == BlockPos.Containing(target);

    /// <summary>Turns "where the move was asked to go" and "where the player actually is" into a <see cref="MoveResult"/>. Pure and static so the mapping can be tested without a live server.</summary>
    /// <remarks>
    /// The verdict has to match the RESOLUTION OF THE REQUEST, and the two differ:
    /// <list type="bullet">
    /// <item>A move that crosses a block boundary is judged by BLOCK. The planner works in blocks and the
    /// arrival settles somewhere inside the destination block, so demanding the exact point would make every honest arrival a failure.</item>
    /// <item>A move that never leaves one block is judged by DISTANCE, because the block comparison is
    /// TRUE BEFORE THE MOVE EVEN RUNS. A block verdict on a no-op centre request reports arrival at (100.5, 80, 100.5) while the player sits at (100.12, 80, 100.12) both before and after, turning a should-be-loud failure into a silent false success; <see cref="MoveResult.SubBlockRequest"/> exists so a caller does not fall into that hole.</item>
    /// </list>
    /// The distance bound is <see cref="SubBlockArrivalTolerance"/>, the library's own tolerance rather than a caller-side guess at what the approach achieves. It is horizontal only, matching what the walking approach can actually control: it cannot change the player's Y within a block, so judging Y here would fail every honest centre arrival.
    /// </remarks>
    public static MoveResult Judge(Vec3d target, Vec3d arrival, bool subBlockRequest)
    {
        double dx = arrival.X - target.X;
        double dz = arrival.Z - target.Z;
        double horizontalDistance = Math.Sqrt((dx * dx) + (dz * dz));

        bool reached = subBlockRequest
            ? horizontalDistance <= SubBlockArrivalTolerance
            : BlockPos.Containing(arrival) == BlockPos.Containing(target);

        return new MoveResult(target, arrival, subBlockRequest, reached, horizontalDistance);
    }

    /// <summary>The CONTRACT for a sub-block approach, in blocks: a caller may treat an arrival within this of the requested point as reached. Callers that need to know read the position back; this surface makes a bounded best effort and never throws for a fraction of a block.</summary>
    public const double SubBlockArrivalTolerance = 0.1;

    /// <summary>What the approach loop aims for, deliberately TIGHTER than the contract so the contract has headroom. If the two were equal, the loop could stop on the contract boundary and become a miss when immediately remeasured. The tighter aim leaves room for settling drift.</summary>
    private const double ApproachAimTolerance = 0.04;

    /// <summary>Four seconds of ticks, which is far more than the 0.71 blocks a sub-block move can span.</summary>
    private const int MaxApproachTicks = 80;

    /// <summary>Walks the player to an exact point inside the block it already occupies, at 20 ticks a second on the session loop, exactly as a navigation does. Bounded: it returns when the point is reached, when the budget runs out, or when cancelled. It does NOT throw on a near miss, because a fraction of a block is a question of precision rather than reachability and the caller can measure it.</summary>
    private async Task ApproachPointAsync(Vec3d target, CancellationToken ct)
    {
        for (int tick = 0; tick < MaxApproachTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            bool finished = await AwaitApproachTickAsync(target, ct).ConfigureAwait(false);
            if (finished)
                return;
        }
    }

    private async Task RunNavigationAsync(IGoal goal, PathfinderOptions options, CancellationToken ct)
    {
        // Already there: an IGoal is a predicate, and a player standing on a block that satisfies it has arrived. The planner answers such a request with a single-node path, PathSegmentBuilder yields no segments from it, and BuildExecutor returns null, which is indistinguishable here from "the goal is unreachable". Treating that as a failure is what made ItemsCollector stall next to a drop forever: it navigates to GoalNear(item, 1), so arriving within a block of the item turned every subsequent attempt into "No path to the goal was found". MoveToAsync has always had the equivalent guard (start block == destination block).
        BlockPos here = await _services.InvokeOnLoopResult(
            () => BlockPos.Containing(_services.State.Self.Position), ct).ConfigureAwait(false);
        if (goal.IsInGoal(here))
            return;

        // Plan on the loop (it captures a consistent region snapshot from world state); the search itself is a bounded pure computation.
        PlannedRoute? route = await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false);
        if (route is null)
            throw new InvalidOperationException("No path to the goal was found.");

        await RunNavigationAsync(goal, options, route.Value, ct).ConfigureAwait(false);
    }

    private async Task RunNavigationAsync(
        IGoal goal, PathfinderOptions options, PlannedRoute route, CancellationToken ct)
    {
        // Everything navigation-scoped is re-armed here: the station hold the PREVIOUS navigation may have left behind, and the life-safety supervisor's health edge.
        await _services.InvokeOnLoopResult(
            () =>
            {
                _physics.BeginNavigation();
                return true;
            },
            ct).ConfigureAwait(false);

        // Sized from the FIRST plan's own air stops, and not resized on a replan. A replan that reports more stops than the route it replaces would otherwise top the budget back up every time the supervisor fired, which is the one thing this counter must not be able to do.
        var budget = new NavigationBudget(options, route.BreathingStops, CountInteractions(route.Executor));
        PathExecutor executor = route.Executor;
        while (!ct.IsCancellationRequested)
        {
            NavigationTickOutcome outcome = await AwaitRouteTickAsync(executor, ct).ConfigureAwait(false);

            if (outcome.State == PathExecutorState.Complete)
                return;

            // A finished surfacing is not a failure and must not be paid for out of the deviation budget: a route with three legitimate air stops would otherwise starve its own recovery.
            if (outcome.Preemption == NavigationPreemption.Surfaced)
            {
                if (!budget.TrySurface())
                    throw new InvalidOperationException("Navigation failed after exhausting surfacing recoveries.");

                _logger.LogDebug(
                    "Navigation resumed after surfacing; {Surfacings} surfacing recoveries and {Replans} replans left.",
                    budget.SurfacingsLeft,
                    budget.ReplansLeft);

                executor = (await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Replan after surfacing produced no path to the goal.")).Executor;
                continue;
            }

            // A retreat that has released. This is charged to the REPLAN budget and to nothing else, and the difference from a surfacing is the reason: a surfacing is bought by something going right on a route that is still good, and the same route resumes afterwards, while a retreat says the route walked the body into something that was hurting it. That is the same shape as losing a load-bearing effect - the plan was wrong about the world - and the same one replan answers it. The retreat itself costs nothing beyond that: it is not charged to the surfacing budget, because it surfaced nothing.
            if (outcome.Preemption == NavigationPreemption.Retreated)
            {
                if (!budget.TryReplan())
                    throw new InvalidOperationException(
                        "Navigation aborted: the body retreated from damage and there are no replans left.");

                _logger.LogDebug(
                    "Retreated from damage along the executed route; replanning with the current vitals; "
                    + "{Replans} replans left.",
                    budget.ReplansLeft);

                // With the CURRENT vitals and the CURRENT world, which is the whole point. The hazard the original plan never saw is in the capture this time, and the landing and hazard affordability is judged against the health the body actually has left. A refusal here is the honest answer to a crossing that has already proved it kills.
                executor = (await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Navigation aborted after retreating from damage: no safe path to the goal.")).Executor;
                continue;
            }

            // A potion the plan was priced on has been revoked or has expired. This is charged to the REPLAN budget and not to a budget of its own, and the difference from a surfacing or a door is the reason: those are bought by something going right and are expected to happen more than once on a legitimate route, while an effect can only be lost once per plan (the next plan is priced on what is left) and losing it means the plan was wrong about the world. That is exactly what a replan is for.
            if (outcome.Preemption == NavigationPreemption.CapabilityLost)
            {
                if (!budget.TryReplan())
                    throw new InvalidOperationException(
                        "Navigation failed: a status effect the plan depended on was lost and there are "
                        + "no replans left.");

                _logger.LogDebug(
                    "Replanning: a status effect the plan was priced on is no longer held; "
                    + "{Replans} replans left.",
                    budget.ReplansLeft);

                // With the CURRENT capabilities, which is the whole point: the capture is taken fresh on the loop, so the arm that is gone is simply not offered to the planner this time. A refusal here is the honest answer to a crossing the player can no longer survive, and the life-safety supervisor - whose breath arm is live again the moment the effect went
                // - is what covers the body in the meantime.
                executor = (await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Replan after losing a status effect produced no path to the goal.")).Executor;
                continue;
            }

            // A door the executor is holding for. This is a PAUSE, not a pre-emption: the body pressed nothing, the engine was not stepped, and the same executor carries on once the barrier has moved. It gets its own budget for the reason a surfacing does - it is bought by something going right, and charging it to the replans would leave a corridor of doors with nothing to recover a real deviation with.
            if (outcome.Preemption == NavigationPreemption.Interaction)
            {
                if (!budget.TryInteract())
                    throw new InvalidOperationException("Navigation failed after exhausting interaction attempts.");

                InteractionRequirement requirement = outcome.PendingInteraction
                    ?? throw new InvalidOperationException("The executor reported an interaction hold with no requirement.");

                UseBlockOutcome result = await PerformInteractionAsync(requirement, ct).ConfigureAwait(false);
                _logger.LogDebug(
                    "Interaction {Kind} on {Target} witnessed at {Witness}: {Result}; {Left} attempts left.",
                    requirement.Kind,
                    requirement.Target,
                    requirement.Witness,
                    result,
                    budget.InteractionsLeft);

                if (result != UseBlockOutcome.Used)
                {
                    // Not an observed open. Retrying the same press against the same world is what the budget above bounds; when it runs out the honest answer is a different route, so spend a replan rather than pressing forever at a door that is not moving.
                    if (budget.InteractionsLeft > 0)
                        continue;

                    if (!budget.TryReplan())
                        throw new InvalidOperationException(
                            "Navigation failed: a barrier would not open and there are no replans left.");

                    executor = (await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Replan after a refused interaction produced no path.")).Executor;
                    continue;
                }

                PathExecutor held = executor;
                await _services.InvokeOnLoopResult(
                    () =>
                    {
                        _physics.CompleteInteraction(held, requirement.Witness);
                        return true;
                    },
                    ct).ConfigureAwait(false);
                continue;
            }

            if (outcome.State == PathExecutorState.Failed || outcome.DeviationExceeded)
            {
                if (!budget.TryReplan())
                    throw new InvalidOperationException("Navigation failed after exhausting replans.");

                executor = (await PlanOnLoopAsync(goal, options, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Replan produced no path to the goal.")).Executor;
                continue;
            }

        }

        ct.ThrowIfCancellationRequested();
    }

    private async Task<bool> AwaitApproachTickAsync(Vec3d target, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new ApproachTick(target, ApproachAimTolerance, completion);
        using CancellationTokenRegistration registration = ct.Register(
            static state => ((PendingTick)state!).Cancel(),
            pending);
        await InstallTickAsync(pending, ct).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task<NavigationTickOutcome> AwaitRouteTickAsync(PathExecutor executor, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<NavigationTickOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new RouteTick(executor, completion);
        using CancellationTokenRegistration registration = ct.Register(
            static state => ((PendingTick)state!).Cancel(), pending);
        await InstallTickAsync(pending, ct).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    private Task InstallTickAsync(PendingTick pending, CancellationToken ct)
        => _services.InvokeOnLoopResult(
            () =>
            {
                if (_pendingTick is not null)
                    throw new InvalidOperationException("A navigation controller tick is already pending.");

                _pendingTick = pending;
                return true;
            },
            ct);

    /// <summary>Does one interaction and reports what the world afterwards says about it. Every arm but <see cref="UseBlockOutcome.Used"/> is treated as "the barrier did not move", which is the safe polarity: reporting an unconfirmed press as an open walks a body at a door that is still shut.</summary>
    private Task<UseBlockOutcome> PerformInteractionAsync(InteractionRequirement requirement, CancellationToken ct)
    {
        Actions.InteractionActions? actions = _interaction();
        if (actions is null)
            return Task.FromResult(UseBlockOutcome.Unconfirmed);

        if (!requirement.SendsAUse)
        {
            // A pressure plate. The body standing on the cell IS the interaction, so there is nothing to send and the only question is when the server agrees the door moved. See InteractionKind.StandOnPlate: a use_item_on against a plate is not a no-op, it is an attempt to place whatever the bot is holding.
            return actions.ObserveBlockPropertyAsync(
                requirement.Witness,
                OpenProperty,
                requirement.ExpectedOpen ? TrueValue : FalseValue,
                ct: ct);
        }

        return actions.UseBlockVerifiedAsync(
                requirement.Target,
                requirement.Witness,
                OpenProperty,

                // The toggle is one packet in both directions and only the expected answer separates them. Reading `open == true` back after a CLOSE would score every successful close as a refusal. See InteractionRequirement.ExpectedOpen.
                requirement.ExpectedOpen ? TrueValue : FalseValue,
                ct: ct);
    }

    /// <summary>How many barriers this route opens, counted off the segments the planner priced.</summary>
    private static int CountInteractions(PathExecutor executor)
    {
        int count = 0;
        foreach (PathSegment segment in executor.Segments)
            if (segment.Interaction is not null)
                count++;

        return count;
    }

    private const string OpenProperty = "open";

    private const string TrueValue = "true";

    private const string FalseValue = "false";

    private async Task<PlannedRoute?> PlanOnLoopAsync(IGoal goal, PathfinderOptions options, CancellationToken ct)
    {
        // Capture the planning region on the session loop (the only step consistency requires), then run the A* search on the thread pool so a long or failed search never stalls the loop.
        PlanCapture? capture = await _services.InvokeOnLoopResult(() => _physics.CapturePlan(goal), ct).ConfigureAwait(false);
        if (capture is null)
            return null;

        PlanCapture value = capture.Value;
        return await Task.Run(() => _physics.BuildExecutor(value, options, ct), ct).ConfigureAwait(false);
    }
}

/// <summary>A goal that is satisfied within a block radius of a target position.</summary>
internal sealed class NearGoal(BlockPos target, int radius) : IGoal
{
    public bool IsInGoal(BlockPos pos)
    {
        int dx = pos.X - target.X;
        int dy = pos.Y - target.Y;
        int dz = pos.Z - target.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= radius * radius;
    }

    public double Heuristic(BlockPos pos)
    {
        double dx = pos.X - target.X;
        double dy = pos.Y - target.Y;
        double dz = pos.Z - target.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>The ball's own centre, so the region capture sizes off the goal instead of guessing at it.</summary>
    /// <remarks>Register #3. Without this the goal took <c>IGoal</c>'s default "I have no destination", and <c>PhysicsEngineHolder.ApproximateGoal</c> fell through to its lattice probe, which samples offsets in <c>{-r, 0, +r}</c> for r in steps of four and so finds a radius-1 ball only when the destination happens to sit on that lattice in all three axes at once. Every miss returned the START, and the captured region became a margin box around it with everything past the margin reading as air - the exact bug already fixed for the public goals, still live on the one path that reaches this class. <see cref="Umpk.Pathfinding.Goals.GoalNear"/> is the mirror.</remarks>
    /// <param name="hint">The ball's centre.</param>
    /// <returns>Always true.</returns>
    public bool TryGetTargetHint(out BlockPos hint)
    {
        hint = target;
        return true;
    }
}

/// <summary>Whether the life-safety supervisor took this tick away from the path executor.</summary>
internal enum NavigationPreemption
{
    /// <summary>The tick belonged to the executor.</summary>
    None,

    /// <summary>The supervisor drove the engine this tick. The executor is dead; keep ticking.</summary>
    Surfacing,

    /// <summary>The surfacing released. Replan to the same goal from where the player now is.</summary>
    Surfaced,

    /// <summary>The supervisor is walking the body BACK along the route it has already executed, because health crossed the floor somewhere a vertical climb cannot reach air. The executor is dead; keep ticking.</summary>
    Retreating,

    /// <summary>The retreat released. Unlike a surfacing, this is not a pause in a route that is still good: the route led the body into something that was hurting it, so it is abandoned and the navigator replans from where the body now is. A refusal is the correct answer where the way on is a hazard the plan can no longer afford.</summary>
    Retreated,

    /// <summary>The executor is holding in front of a barrier it needs opened. The tick belonged to nobody: the body pressed nothing and stayed put, and the navigator has to do the interaction and then release the hold. The executor is NOT dead - this is a pause, not a pre-emption - so the same executor carries on afterwards.</summary>
    Interaction,

    /// <summary>The executor is standing still at a cell the PLAN scheduled a breathing pause for, refilling its lung before it goes on. The executor is NOT dead - this is a scheduled wait, not a pre-emption - so the same executor carries on afterwards.</summary>
    /// <remarks>Deliberately its own value rather than folded into <see cref="Interaction"/>. A route can arrive because the schedule ran or because <see cref="Surfacing"/> rescued it, and those are opposite outcomes: the first proves the plan, the second proves the plan was wrong and the reactive backstop caught it. Counting them apart is the only way to tell.</remarks>
    Breathing,

    /// <summary>A status effect the running plan was PRICED on is no longer held. The tick belonged to nobody - the body was not stepped - and the plan's premise is gone, so the navigator must replan with the capabilities the player actually has now. The route the replan finds may well be a refusal, which is the correct answer: finishing a crossing whose potion has expired is what kills the bot.</summary>
    CapabilityLost,
}

/// <summary>A planned route ready to execute, and the number of times it surfaces to breathe on the way.</summary>
/// <remarks>
/// The stop count comes from the breath validation that already approved this route, so it costs nothing extra to compute and cannot describe a route other than the one being handed over. It rides with the executor rather than being stashed on the holder because <c>BuildExecutor</c> runs off the session loop: a field there would be written on the thread pool and read on the loop.
/// <para><c>BreathSuspended</c> is zero-stop by construction: a route planned with the breath dimension suspended never validated, so it has no air stops to count and needs no surfacing budget for them. It is surfaced here as well as on the executor's own context so a caller can see what it was handed without reaching through the executor.</para>
/// </remarks>
internal readonly record struct PlannedRoute(
    PathExecutor Executor, int BreathingStops, bool BreathSuspended = false);

/// <summary>The per-tick navigation outcome surfaced to the navigator loop.</summary>
/// <param name="State">The executor's lifecycle state after the tick.</param>
/// <param name="DeviationExceeded">Whether the body drifted past the replan threshold.</param>
/// <param name="Preemption">Who the tick belonged to.</param>
/// <param name="PendingInteraction">What the executor is waiting to have done, set exactly when <paramref name="Preemption"/> is <see cref="NavigationPreemption.Interaction"/>.</param>
internal readonly record struct NavigationTickOutcome(
    PathExecutorState State,
    bool DeviationExceeded,
    NavigationPreemption Preemption,
    InteractionRequirement? PendingInteraction = null);

/// <summary>The two independent recovery budgets one navigation gets.</summary>
/// <remarks>
/// <para>They are separate because they mean different things. A REPLAN is bought by something going wrong: a segment failed, or the player drifted off the plan far enough that the plan no longer describes where it is. A SURFACING is bought by something going right: the supervisor saw the player running out of air and got it to the surface, and the route resumes from there. Charging a surfacing to the replan budget means a route with three legitimate air stops arrives at its first real deviation with nothing left to recover with.</para>
/// <para>The replan count comes from <see cref="PathfinderOptions.MaxReplans"/> so host policy controls recovery from failed or invalidated plans.</para>
/// </remarks>
internal struct NavigationBudget
{
    /// <summary>The FLOOR on surfacing recoveries per navigation. Three: enough for a route with a couple of legitimate air stops plus one surprise, few enough that a bot which cannot make progress between breaths gives up instead of oscillating forever.</summary>
    /// <remarks>
    /// A flat allowance is the wrong shape. The supervisor spends one recovery per surfacing, so a route that legitimately breathes five times would otherwise exhaust a fixed budget. The route knows how many air stops it has (<see cref="Umpk.Pathfinding.Core.BreathValidation.BreathingStops"/>, counted by the same validation that approved the route), so the allowance is that count plus one surprise, never below this floor.
    /// <para>There is deliberately no ceiling. The count is not a free parameter: it comes from a route the validator has already proved survivable on one lung, so it is bounded by that route's own wet/dry alternations, and each recovery is itself bounded by <c>MaxSurfacingTicksTotal</c>. Capping it at some round number would put back exactly the arbitrary limit this replaces.</para>
    /// </remarks>
    public const int MaxSurfacingRecoveries = 3;

    /// <summary>The extra recovery every route gets on top of its own air stops, for one surprise.</summary>
    public const int SurfacingSurpriseAllowance = 1;

    /// <summary>The FLOOR on interaction attempts per navigation. Two: one for the door, and one for the case where the first press raced something else - a redstone pulse, another player, a server that dropped the packet.</summary>
    /// <remarks>Interactions get their OWN budget for the reason surfacings do: a door is bought by something going RIGHT, and a route through four doors that charged them to the replan budget would arrive at its first real deviation with nothing left. The allowance is the plan's own interaction count plus the retry, never below this floor, so a corridor with four doors gets five attempts and a corridor with one gets two.</remarks>
    public const int MaxInteractionAttempts = 2;

    private int _replansLeft;
    private int _surfacingsLeft;
    private int _interactionsLeft;

    /// <summary>Creates the budgets for one navigation.</summary>
    /// <param name="options">The search options the navigation runs under.</param>
    /// <param name="breathingStops">How many times the planned route surfaces to breathe, from its own breath validation.</param>
    /// <param name="plannedInteractions">How many barriers the planned route opens on the way, counted off its own segments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public NavigationBudget(PathfinderOptions options, int breathingStops, int plannedInteractions = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        _replansLeft = options.MaxReplans;
        _surfacingsLeft = Math.Max(MaxSurfacingRecoveries, breathingStops + SurfacingSurpriseAllowance);
        _interactionsLeft = Math.Max(MaxInteractionAttempts, plannedInteractions + 1);
    }

    /// <summary>Replans left before the navigation gives up.</summary>
    public readonly int ReplansLeft => _replansLeft;

    /// <summary>Surfacing recoveries left before the navigation gives up.</summary>
    public readonly int SurfacingsLeft => _surfacingsLeft;

    /// <summary>Takes one replan, or reports the budget is spent.</summary>
    public bool TryReplan()
    {
        if (_replansLeft <= 0)
            return false;

        _replansLeft--;
        return true;
    }

    /// <summary>Interaction attempts left before the navigation gives up.</summary>
    public readonly int InteractionsLeft => _interactionsLeft;

    /// <summary>Takes one surfacing recovery, or reports the budget is spent. Never touches replans.</summary>
    public bool TrySurface()
    {
        if (_surfacingsLeft <= 0)
            return false;

        _surfacingsLeft--;
        return true;
    }

    /// <summary>Takes one interaction attempt, or reports the budget is spent. Never touches replans.</summary>
    public bool TryInteract()
    {
        if (_interactionsLeft <= 0)
            return false;

        _interactionsLeft--;
        return true;
    }
}
