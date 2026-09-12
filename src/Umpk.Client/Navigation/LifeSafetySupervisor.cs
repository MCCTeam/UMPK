using Umpk.Client.State;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>What the life-safety supervisor decided for one navigation tick.</summary>
internal enum LifeSafetyAction
{
    /// <summary>Nothing to do; the tick belongs to the path executor.</summary>
    None,

    /// <summary>The supervisor is driving the engine directly. The executor is dead.</summary>
    Surfacing,

    /// <summary>The surfacing released. The caller must replan from where the player now is.</summary>
    Surfaced,

    /// <summary>The surfacing's climb has reached its air and is now HOLDING at it, exactly as a scheduled breathing pause holds at a bell. The supervisor still owns the engine and the executor is still dead, as under <see cref="Surfacing"/>; only the input changes.</summary>
    /// <remarks>A vertical climb is not sufficient at a one-cell opening: lateral offset can keep the body under the adjacent lid, and sprinting keeps the eye low. Once air is reached, the existing breath-hold input centres the body and stops sprinting while the lung refills.</remarks>
    SurfaceBreathing,

    /// <summary>The supervisor is driving the engine BACK along the route the body has already walked. The executor is dead, exactly as it is under <see cref="Surfacing"/>.</summary>
    Retreating,

    /// <summary>The retreat released. The route the body was on is abandoned: the caller must replan from where the player now is, with the vitals it now has, and a refusal is a legitimate answer.</summary>
    Retreated,
}

/// <summary>What fired one surfacing, and what it is aiming at: the lung it started from, the bar that tripped it, the largest of the three bars its release is sized by, and the lung it releases at.</summary>
/// <remarks>Telemetry only. Nothing in the supervisor reads it back, and no decision is taken on it; it exists so that a transcript can say WHY the backstop fired rather than only that it did. See <see cref="LifeSafetySupervisor.TryTakeSurfacingTrip"/>.</remarks>
internal readonly record struct SurfacingTrip(int Air, double Threshold, double Bar, double ResumeAir, string Why);

/// <summary>What one finished surfacing actually did: the lung it began and ended on, the best it saw, how long it ran, the bar it was aiming at, and which of the three exits ended it.</summary>
/// <remarks>Telemetry only, for the reason <see cref="SurfacingTrip"/> is. The pair <c>EntryAir -> ExitAir</c> against <c>ResumeAir</c> is what separates a hold that reached air from one that climbed into stone, which no other line in the transcript can tell apart.</remarks>
internal readonly record struct SurfacingRelease(
    int EntryAir, int BestAir, int ExitAir, int Ticks, double ResumeAir, string Why);

/// <summary>The life-safety check that runs before every executor tick: it watches breath and health, and when either says the player is about to die it pre-empts the navigation and drives the engine itself - up, to the surface, or back, along the route the body has already walked.</summary>
/// <remarks>
/// <para><b>Why it lives here.</b> <c>PhysicsState</c> carries no air and no health, and <c>Umpk.Pathfinding</c> cannot reference <c>Umpk.Client</c>, where <see cref="SelfState.AirSupply"/> lives, so no template and no executor can see the number this decision needs. The navigation tick in <see cref="PhysicsEngineHolder"/> is the one place that reaches self state, the engine, and the live world at once, and it is also the only place that can step the engine outside the executor.</para>
/// <para><b>Why the trip reads the ROUTE and not the ceiling.</b> The obvious test - scan up for air, panic if there is none - fires the instant the player swims under a lid, at a completely full lung, on exactly the routes this subsystem exists to make possible. The question that actually matters is "how long until the plan puts my head in air", and the plan knows: the next node on the remaining route whose head cell is not water is a breathing node, and the cost of getting there is the sum of the segments in between, priced with the same medium-aware model the planner's own breath validation uses. Only when the remaining route has no breathing node left does this fall back to the vertical climb - and that is the same climb <see cref="BreathValidator"/> already charged as an arrival reserve before it approved the plan, so the two agree by construction. An escape the model cannot see NEVER fires the trip (<see cref="BreathEscape"/>).</para>
/// <para><b>Two conditions, not one.</b> A trip needs the breath arithmetic to say "now" AND a surfacing to be able to achieve something (<see cref="CanSurface"/>). The second was missing, and without it the first is a suicide loop under any closed ceiling. Nothing pre-empts a route whose per-segment breath accounting <see cref="BreathValidator"/> just approved, either: the validator judges the route against the LIVE lung less the same <see cref="BreathModel.ReactionTicks"/> reserve this class charges (<see cref="BreathModel.RouteBudget"/>), so "approved" and "silent" are the same inequality read from the two ends.</para>
/// <para><b>Why the release survives pre-emption.</b> Once the supervisor has stepped the engine even once, <c>PathExecutor._lastExpected</c> is stale, it is private with no setter, and the executor is dead by contract. So the release condition cannot be a question asked of the executor later: it is computed from the remaining route AT THE MOMENT OF THE TRIP and frozen on the hold.</para>
/// <para><b>Two escapes, because the two arms are asking different questions.</b> Breath is answered by going UP: air is above, always, and <see cref="CanSurface"/> is the honest test of whether a climb can reach it. Health is not - a floor that is cooking the body is not fixed by being over it - so where no vertical escape exists the health arm goes BACK instead, along the executed portion of the route (<see cref="TryBeginRetreat"/>). That escape needs no planner and no search, because the body has already walked it and survived every cell of it. Gating the health arm on the BREATH arm's escape is what left a body dying of floor damage in a sealed corridor with no intervention at all; see <see cref="TryBeginRetreat"/> for the live measurement.</para>
/// </remarks>
internal sealed class LifeSafetySupervisor(PhysicsProfile profile)
{
    /// <summary>The health at or below which a crossing pre-empts the navigation: 6.0, three hearts.</summary>
    /// <remarks>Health is a separate and simpler trip than breath, and it cannot be inferred from breath: drowning damage arrives as <c>set_health</c> frames because <c>AirSupplyRule</c> deliberately applies no local damage, so the supervisor has to watch health directly. It is edge-triggered on the CROSSING, not level-triggered, so a player that is simply hurt does not re-fire the trip on every tick for the rest of the session.</remarks>
    public const float HealthFloor = 6.0f;

    /// <summary>The air, in ticks, banked above the firing threshold before a surfacing releases. This is the second of the two thresholds: without a band, a player hovering at the boundary trips, rises one block, releases, sinks and trips again. Refill is four ticks a tick, so sixty ticks of bank costs fifteen ticks at the surface.</summary>
    public const double ResumeRefillBonusTicks = 60.0;

    /// <summary>How long a surfacing may go on buying NO air before it gives up, in ticks: the stall window.</summary>
    /// <remarks>
    /// <para>This is the cap's original job, stated as the thing it was actually for. Under a motion-blocking lid a surfacing is a climb into stone: the eye never leaves the water, the lung only falls, and eighty ticks of that buys nothing. Releasing anyway hands control back to the navigator, whose own surfacing budget then bounds the loop, instead of parking the player at the surface forever. A hold that buys nothing still ends on exactly this tick, because a stalled hold never resets the window.</para>
    /// <para>The window is measured against the BEST air the hold has seen and not against the previous tick, because at a one-cell hole the body bobs through the surface and the lung alternates between <c>+4</c> and <c>-1</c>. A last-tick comparison would read that bob as a stall on nearly half its ticks; the running maximum reads it as the progress it is.</para>
    /// </remarks>
    public const int MaxSurfacingTicks = 80;

    /// <summary>The hard ceiling on one surfacing, in ticks, whether or not it is still gaining air.</summary>
    /// <remarks>A clean fill from empty takes 75 ticks, but climbing and bobbing at an opening add delay. The 150-tick ceiling allows a progressing hold to finish while <see cref="MaxSurfacingTicks"/> still ends a hold that gains no air.</remarks>
    public const int MaxSurfacingTicksTotal = 150;

    /// <summary>The hard ceiling on one retreat, in ticks.</summary>
    /// <remarks>
    /// <para>This is NOT what normally ends a retreat. A retreat ends when the body is back at a cell it can sit in without being hurt (<see cref="RetreatSettleTicks"/>), or when the route it has walked runs out - both of which are statements about the world. The ceiling is the guard that stops the supervisor owning the engine indefinitely when neither of those ever arrives, exactly as <see cref="MaxSurfacingTicksTotal"/> is for a hold that is filling and never fills.</para>
    /// <para>Four hundred ticks is twenty seconds, which at the submerged bottom-walk price the planner itself uses - 10.2 ticks a block, the unsafe bound quoted in <see cref="CanSurface"/>'s own remarks, is about thirty-nine blocks of withdrawal, leaving ample room for a long retreat and a failed settle before the safety ceiling ends control.</para>
    /// </remarks>
    public const int MaxRetreatTicks = 400;

    /// <summary>Ticks the body must sit at the cell a retreat was aimed at, taking NO further damage, before the retreat releases: the settle window.</summary>
    /// <remarks>
    /// <para>It runs AFTER the arrival and not instead of it, and that ordering is the whole of it. A window that could release on "nothing has hurt me lately" alone would release on the very first tick of the retreat, because the first thing the retreat does is lift the body off the floor and the bleeding stops there and then - with the body still directly over the hazard, which is where it would then be handed back. So the window's job is not to find the way out; the route history does that. Its job is to let the body settle onto the ground it came back to, and to notice if that ground is hurting it too, in which case it resets and the retreat goes on.</para>
    /// <para>Twenty ticks is one player damage-invulnerability period, so a shorter window could not tell a body that has escaped a hot floor from one standing on it between two hits.</para>
    /// <para>It is measured against the WORST health the retreat has seen and not against the previous tick, for the same reason <see cref="MaxSurfacingTicks"/> is measured against the best air: a regeneration tick arriving mid-retreat would otherwise read as progress and cut the window short.</para>
    /// </remarks>
    public const int RetreatSettleTicks = 20;

    /// <summary>How near a retreat waypoint the body must come, horizontally, to have reached it.</summary>
    /// <remarks>Three quarters of a block: wider than the sub-tick step of a swim (about a fifth of a block) so a waypoint cannot be stepped over, and narrower than the one-block spacing of the cells themselves so two of them can never be claimed by one position.</remarks>
    public const double RetreatWaypointRadius = 0.75;

    private readonly PhysicsProfile _profile = profile;
    private SurfacingHold? _hold;
    private RetreatHold? _retreat;
    private SurfacingTrip? _trip;
    private SurfacingRelease? _release;
    private Vec3d? _arrival;
    private float _lastHealth = 20.0f;
    private float _peakHealth = 20.0f;
    private int _unharmedIndex;

    /// <summary>Whether the supervisor currently owns the engine.</summary>
    public bool IsSurfacing => _hold is not null;

    /// <summary>Whether the supervisor is currently walking the body back along its own route.</summary>
    public bool IsRetreating => _retreat is not null;

    /// <summary>The air level this surfacing releases at, or 0 when none is running. For tests.</summary>
    public double ResumeAir => _hold?.ResumeAir ?? 0.0;

    /// <summary>The cell centre a surfacing that has reached its air is holding at, or null while it is still climbing. The driver reads this on the tick <see cref="Evaluate"/> answers <see cref="LifeSafetyAction.SurfaceBreathing"/>, exactly as it reads <see cref="RetreatTarget"/> under <see cref="LifeSafetyAction.Retreating"/>.</summary>
    /// <remarks>The anchor is FROZEN at the arrival rather than recomputed per tick. The hold's whole job is to stop the body drifting off the one cell whose column has the air, and an anchor that followed the body would chase the drift instead of correcting it - the same reason the station hold anchors on the arrival CELL rather than on the arrival position.</remarks>
    public Vec3d? SurfacingAnchor => _hold?.Anchor;

    /// <summary>Takes the position a surfacing reached its air at, if one did so on the tick just evaluated.</summary>
    /// <inheritdoc cref="TryTakeSurfacingTrip"/>
    public bool TryTakeSurfacingArrival(out Vec3d anchor)
    {
        if (_arrival is { } reached)
        {
            _arrival = null;
            anchor = reached;
            return true;
        }

        anchor = default;
        return false;
    }

    /// <summary>Takes the report of a surfacing that STARTED on the tick just evaluated, if one did.</summary>
    /// <remarks>
    /// The report records what fired the hold, how long it ran, and whether it gained air. It matches the report produced by <c>PhysicsEngineHolder.TickBreathHold</c>.
    /// <para>Taken rather than read, so one event produces one line however often the driver asks.</para>
    /// </remarks>
    public bool TryTakeSurfacingTrip(out SurfacingTrip trip)
    {
        if (_trip is { } begun)
        {
            _trip = null;
            trip = begun;
            return true;
        }

        trip = default;
        return false;
    }

    /// <summary>Takes the report of a surfacing that RELEASED on the tick just evaluated, if one did.</summary>
    /// <inheritdoc cref="TryTakeSurfacingTrip"/>
    public bool TryTakeSurfacingRelease(out SurfacingRelease release)
    {
        if (_release is { } finished)
        {
            _release = null;
            release = finished;
            return true;
        }

        release = default;
        return false;
    }

    /// <summary>The cell the running retreat is steering for, or null when it has got there and is settling onto it. The driver reads this on the tick <see cref="Evaluate"/> answers <see cref="LifeSafetyAction.Retreating"/>.</summary>
    public Vec3d? RetreatTarget
        => _retreat is { } retreat
            && retreat.Index < retreat.SettleFrom
            && retreat.Index < retreat.Waypoints.Count
                ? retreat.Waypoints[retreat.Index]
                : null;

    /// <summary>Clears any hold and re-arms the health edge at the player's current health.</summary>
    /// <remarks>Re-arming at the CURRENT health rather than at full is what makes the trip a crossing: a navigation started by an already-hurt player is not a health event, and firing there would refuse to move a player that is merely below three hearts.</remarks>
    public void Reset(float health)
    {
        _hold = null;
        _retreat = null;
        _trip = null;
        _release = null;
        _arrival = null;
        _lastHealth = health;
        _peakHealth = health;
        _unharmedIndex = 0;
    }

    /// <summary>Runs one supervision tick.</summary>
    /// <param name="physics">The engine state this tick.</param>
    /// <param name="self">The live self state (air, health).</param>
    /// <param name="view">The live world view.</param>
    /// <param name="executor">The executor being supervised.</param>
    /// <param name="waterBreathingHeld">Whether the player holds water breathing or conduit power right now. Together with the plan's own <see cref="PathExecutionContext.BreathSuspended"/> this is what suspends the breath arm; see <see cref="IsBreathArmSuspended"/> for why it takes both and why the health arm takes neither. The default is the safe polarity: no suspension.</param>
    public LifeSafetyAction Evaluate(
        in PhysicsState physics,
        SelfState self,
        IPhysicsWorldView view,
        PathExecutor executor,
        bool waterBreathingHeld = false)
    {
        if (_hold is { } running)
        {
            // Rising, stalled, or out of time. A hold that is still gaining air is doing the one thing it exists to do, so it gets the whole of MaxSurfacingTicksTotal; one that has stopped gaining is cut MaxSurfacingTicks after its best tick, which for a climb into a lid is its first.
            bool rising = self.AirSupply > running.BestAir;

            // Has the climb arrived? Latched, and latched for the reason the climb is latched: a body at a one-cell hole bobs across the boundary, and a phase that could be re-decided every tick would swap the input under it and accumulate neither. Once a surfacing has reached air it holds at it for the rest of the hold, and the two clocks below still bound the hold if the cell turns out not to work - which is exactly the protection a scheduled pause relies on.
            Vec3d? anchor = running.Anchor
                ?? (HasClimbedIntoAir(view, physics) ? ArrivalCellCentre(physics.Position) : null);
            SurfacingHold next = running with
            {
                TotalTicksLeft = running.TotalTicksLeft - 1,
                StallTicksLeft = rising ? MaxSurfacingTicks : running.StallTicksLeft - 1,
                BestAir = rising ? self.AirSupply : running.BestAir,
                Ticks = running.Ticks + 1,
                Anchor = anchor,
            };
            if (running.Anchor is null && anchor is { } reached)
                _arrival = reached;

            if (self.AirSupply >= next.ResumeAir || next.StallTicksLeft <= 0 || next.TotalTicksLeft <= 0)
            {
                _hold = null;
                _release = new SurfacingRelease(
                    next.EntryAir,
                    next.BestAir,
                    self.AirSupply,
                    next.Ticks,
                    next.ResumeAir,
                    self.AirSupply >= next.ResumeAir
                        ? "the lung is back over the bar"
                        : next.StallTicksLeft <= 0
                            ? "the climb stopped buying air"
                            : "the total ceiling");
                return LifeSafetyAction.Surfaced;
            }

            _hold = next;
            return next.Anchor is null ? LifeSafetyAction.Surfacing : LifeSafetyAction.SurfaceBreathing;
        }

        if (_retreat is { } retreating)
        {
            // Two phases and one clock. WITHDRAWING walks the frozen history backwards a waypoint at a time; SETTLING lets go at the cell the anchor pointed at and watches whether that cell is hurting the body too. A hit while settling is the answer "no", and the retreat resumes one cell further back rather than handing a body back onto a floor that is still cooking it. The total clock bounds the pair the way MaxSurfacingTicksTotal bounds a filling hold.
            bool hurting = self.Health < retreating.WorstHealth;
            int index = retreating.Index;
            int settleFrom = retreating.SettleFrom;
            int settleTicks = retreating.SettleTicksLeft;

            if (index < settleFrom
                && index < retreating.Waypoints.Count
                && HasReached(physics.Position, retreating.Waypoints[index]))
                index++;

            if (hurting)
            {
                settleFrom = Math.Max(settleFrom, index + 1);
                settleTicks = RetreatSettleTicks;
            }
            else if (index >= settleFrom)
                settleTicks--;

            RetreatHold nextRetreat = retreating with
            {
                Index = index,
                SettleFrom = settleFrom,
                TotalTicksLeft = retreating.TotalTicksLeft - 1,
                SettleTicksLeft = settleTicks,
                WorstHealth = Math.Min(retreating.WorstHealth, self.Health),
            };

            if (nextRetreat.TotalTicksLeft <= 0 || nextRetreat.SettleTicksLeft <= 0)
            {
                _retreat = null;
                return LifeSafetyAction.Retreated;
            }

            _retreat = nextRetreat;
            return LifeSafetyAction.Retreating;
        }

        float previousHealth = _lastHealth;
        _lastHealth = self.Health;

        // Where the body was standing the last time nothing had yet hurt it. This is the ONE piece of bookkeeping the retreat needs and it costs a compare a tick: a retreat has to know which cell to go back TO, and by the time health crosses the floor the damage has been running for a
        // while and the answer is several segments behind. Recording it as it happens is the only way to
        // have it; asking afterwards would mean searching for a hazard the plan never saw.
        //
        // Against the PEAK health and not against last tick, and that is the difference between a working anchor and a useless one. Damage arrives as set_health frames on a server-side timer - a hot floor lands one every few ticks - so on most ticks of a crossing that is killing the body health is simply unchanged, and an anchor that moved on "not hurt this tick" would follow the body straight into the hazard and answer "here" when asked where to run. Health at or above the best this navigation has seen means nothing has hurt the body up to this cell, which is the actual question. Regeneration back to the peak re-arms it, which is right: a body that is whole again is a body nothing is hurting.
        if (self.Health >= _peakHealth)
        {
            _peakHealth = self.Health;
            _unharmedIndex = executor.CurrentIndex;
        }

        if (previousHealth > HealthFloor && self.Health <= HealthFloor)
        {
            if (CanSurface(view, physics))
            {
                _hold = BeginHold(view, executor, tripThreshold: 0.0, self.AirSupply, "health crossed the floor");
                return LifeSafetyAction.Surfacing;
            }

            if (TryBeginRetreat(executor, _unharmedIndex, self.Health, out RetreatHold begun))
            {
                _retreat = begun;
                return LifeSafetyAction.Retreating;
            }

            return LifeSafetyAction.None;
        }

        // Out of the water the lung is refilling, so there is no breath trip to make.
        if (!physics.IsUnderWater)
            return LifeSafetyAction.None;

        if (IsBreathArmSuspended(executor, waterBreathingHeld))
            return LifeSafetyAction.None;

        // The body is standing in the air the plan sent it to. Surfacing it now would take it OFF that air, and this arm is the only thing in the system that would do so. See the remarks on IsBreathArmSuspended's sibling below for why standing down here is not a weakening.
        if (executor.IsAwaitingBreath)
            return LifeSafetyAction.None;

        (BreathEscape breath, bool fromRoute) = TicksToNextBreath(view, physics, executor);
        if (!breath.IsKnown)
            return LifeSafetyAction.None;

        double threshold = fromRoute
            ? BreathModel.RouteThreshold(breath.Ticks)
            : BreathModel.Threshold(breath.Ticks);
        if (self.AirSupply >= threshold)
            return LifeSafetyAction.None;

        if (!CanSurface(view, physics))
            return LifeSafetyAction.None;

        _hold = BeginHold(
            view,
            executor,
            threshold,
            self.AirSupply,
            fromRoute
                ? $"the route's next breath is {Math.Ceiling(breath.Ticks)} ticks away"
                : $"the climb out of this column is {Math.Ceiling(breath.Ticks)} ticks");
        return LifeSafetyAction.Surfacing;
    }

    /// <summary>Whether the BREATH arm is suspended this tick, which needs the plan and the world to agree.</summary>
    /// <remarks>
    /// <para>The plan's <see cref="PathExecutionContext.BreathSuspended"/> is a statement about the CAPTURE: this route was chosen, and its breath validation skipped, because the player held an effect that covered it. <paramref name="waterBreathingHeld"/> is a statement about NOW. A suspension carried only by the plan would leave the arm off for the whole remaining route after a potion is revoked, while one carried only by the live effect would suspend the arm on routes nothing ever priced against it.</para>
    /// <para><b>What it deliberately does NOT gate.</b> The health crossing above, which stays live in every case. It is the backstop for hazards the breath model cannot see and can retreat when a vertical escape is unavailable.</para>
    /// <para>The air value itself is already correct without this gate because a water-breathing player's lung does not drain. This suspends only the model's opinion about the route.</para>
    /// </remarks>
    private static bool IsBreathArmSuspended(PathExecutor executor, bool waterBreathingHeld)
        => waterBreathingHeld && executor.Context.BreathSuspended;

    /* The breath arm's SECOND stand-down, <see cref="PathExecutor.IsAwaitingBreath"/>, is asserted inline above rather than through a predicate here, because unlike the suspension it needs no agreement between two sources: the executor either is spending a scheduled pause or it is not.

       Why it is not a weakening, which is the only interesting question about switching a life-safety arm off. A breathing pause is bounded by three AIR clocks of its own, in PhysicsEngineHolder.TickBreathHold: the hard floor at air 0, a lung that rose and started falling again, and no gain at all inside BreathHoldGraceTicks. A pause at a cell that turns out not to be an air source therefore abandons itself in about two dozen ticks and hands the navigator a replan from where the body actually is, on the lung it actually has - which is TIGHTER than MaxSurfacingTicks, so the backstop was not the faster of the two even where it was right.

       And the health arm is untouched. It runs before this point and is not gated on either stand-down, so a hold taken in a hazard is still pre-empted; only the model's opinion about BREATH is suspended, and only while the plan's own answer to breath is in progress. */

    /// <summary>Whether a surfacing from where the player is standing could reach air at all.</summary>
    /// <remarks>
    /// <para>A surfacing is a vertical climb: <see cref="SurfacingController"/> latches <c>Jump</c> and <c>Sprint</c> with the pitch straight up, and deliberately does not press <c>Forward</c>. So under a motion-blocking lid the hold buys nothing at all - it runs to <see cref="MaxSurfacingTicks"/>, releases, and hands the navigator a replan that returns the identical route, which trips again. Three of those exhaust <c>NavigationBudget.MaxSurfacingRecoveries</c> and the navigation throws with the player still submerged.</para>
    /// <para>The two-valued <see cref="BreathEscape"/> prevents an unknown escape from firing the trip. Out of the water <see cref="BreathModel.EscapeTicks"/> answers <c>Known</c>, so a health crossing on dry land is unaffected.</para>
    /// <para><b>On the BREATH arm, staying silent is the right answer</b> and not merely the harmless one: when the ceiling is closed, the PLANNED ROUTE is the only escape the player has, and the route's own real-tick cost is priced at the unsafe bound (a submerged bottom-walk at 10.2 ticks a block, roughly twice what the executor actually manages), so continuing to execute it is strictly better than spending eighty ticks pinned against stone.</para>
    /// <para><b>On the health arm it is not.</b> The argument above turns on the planned route being an escape, and a health crossing is the case where it has stopped being one: something the plan never saw is on it, hurting the body, and "carry on executing" is the move that killed the bot. So a false answer here sends the health arm to <see cref="TryBeginRetreat"/> rather than to silence.</para>
    /// </remarks>
    private bool CanSurface(IPhysicsWorldView view, in PhysicsState physics)
        => BreathModel.EscapeTicks(
            view,
            (int)Math.Floor(physics.Position.X),
            (int)Math.Floor(physics.Position.Y),
            (int)Math.Floor(physics.Position.Z),
            _profile).IsKnown;

    /// <summary>Ticks until the PLANNED route next puts the player's head in air, or the vertical escape from where it is standing when the route has no breathing node left.</summary>
    private (BreathEscape Breath, bool FromRoute) TicksToNextBreath(
        IPhysicsWorldView view, in PhysicsState physics, PathExecutor executor)
    {
        IReadOnlyList<PathSegment> route = executor.Segments;
        double ticks = 0;
        for (int i = executor.CurrentIndex; i < route.Count; i++)
        {
            PathSegment segment = route[i];
            bool submerged = IsSubmerged(view, segment.Start) || IsSubmerged(view, segment.End);
            ticks += BreathValidator.RealTicks(segment, submerged, _profile, executor.AllowSprint);
            if (!IsSubmerged(view, segment.End))
                return (BreathEscape.Known(ticks), true);

        }

        BreathEscape vertical = BreathModel.EscapeTicks(
            view,
            (int)Math.Floor(physics.Position.X),
            (int)Math.Floor(physics.Position.Y),
            (int)Math.Floor(physics.Position.Z),
            _profile);
        return (vertical, false);
    }

    /// <summary>Freezes the release condition off the REMAINING route, so it stays computable once the executor has been discarded.</summary>
    /// <remarks>
    /// <para>Three bars, and the largest of them wins. The TRIP threshold is what fired the hold, so releasing below it would re-fire immediately. The deepest vertical ESCAPE on the route is what a climb out of its worst cell costs, which is the reserve <see cref="BreathValidator"/> charges at the destination. And the route's own PEAK DEFICIT is what the continuation actually spends under water.</para>
    /// <para>The first two bars are local: the trip threshold prices the leg to the NEXT breathing node, which at an air pocket is a handful of ticks, and the escape prices a climb out of a trench three blocks deep, which is 8.62. Neither says anything about the submerged route after that breath. The peak-deficit bar prevents a successful surfacing from releasing with too little air to continue.</para>
    /// <para>The reaction reserve is added to the peak for the same reason <see cref="BreathModel.RouteBudget"/> subtracts it: the validator judges a continuation at <c>air - ReactionTicks</c>, so <c>peak + ReactionTicks</c> is exactly the lung at which it stops refusing. The band on top is the same anti-chatter allowance the other two bars carry, and it is what makes the release a comfortable margin rather than a boundary. Where the sum runs past a full lung it is clamped to one, so a route whose worst leg is long simply fills up; a hold that cannot get there is bounded by <see cref="MaxSurfacingTicks"/> once it stops gaining and by <see cref="MaxSurfacingTicksTotal"/> regardless, and hands the navigator whatever it managed.</para>
    /// </remarks>
    private SurfacingHold BeginHold(
        IPhysicsWorldView view, PathExecutor executor, double tripThreshold, int airNow, string why)
    {
        IReadOnlyList<PathSegment> route = executor.Segments;
        double deepest = 0;
        for (int i = executor.CurrentIndex; i < route.Count; i++)
        {
            Vec3d end = route[i].End;
            BreathEscape escape = BreathModel.EscapeTicks(
                view, (int)Math.Floor(end.X), (int)Math.Floor(end.Y), (int)Math.Floor(end.Z), _profile);
            if (escape.IsKnown && escape.Ticks > deepest)
                deepest = escape.Ticks;

        }

        double continuation = BreathValidator.PeakDeficit(
            route, executor.CurrentIndex, view, _profile, executor.AllowSprint) + BreathModel.ReactionTicks;

        double bar = Math.Max(
            Math.Max(tripThreshold, BreathModel.Threshold(deepest)),
            continuation);
        double resume = Math.Min(BreathModel.FullLungTicks, bar + ResumeRefillBonusTicks);
        _trip = new SurfacingTrip(airNow, tripThreshold, bar, resume, why);
        return new SurfacingHold(
            resume, MaxSurfacingTicksTotal, MaxSurfacingTicks, airNow, airNow, 0, Anchor: null);
    }

    /// <summary>Freezes the way OUT OF a health crossing that no vertical climb can answer: the route the body has already walked, in reverse.</summary>
    /// <remarks>
    /// <para>A health crossing needs an escape even when a sealed ceiling prevents surfacing. The route behind the body is the only observed safe path available.</para>
    /// <para><b>Why the escape is the route and not the ceiling.</b> The supervisor cannot see how far the hazard extends AHEAD - the hazard is, by construction, something the plan never saw - but it knows every cell BEHIND it was survivable, because the body just stood in each of them and was not killed. The completed segments are therefore the only escape the supervisor can reason about rather than guess at, and they need no planner: they are already a list of points.</para>
    /// <para><b>How far back it goes.</b> To <paramref name="unharmedIndex"/>: the segment the executor was on the last time a tick passed without the body losing health. This is an observation rather than a guess. It needs no constant to be tuned and no search for a hazard whose extent nothing here can see. The trip fires at <see cref="HealthFloor"/>, which by then is a long way past the first hit, so the answer is typically several segments behind and never ahead.</para>
    /// <para><b>Why the settle window cannot be the thing that ends it.</b> The first tick of the retreat lifts the body off the floor and the bleeding stops immediately, directly above the hazard, so a release on "damage stopped" alone would hand the body back exactly where it was dying. The route history is what ends the retreat; the window only runs once the body is back, and resets if the cell it came back to turns out to be hurting it too. See <see cref="RetreatSettleTicks"/>.</para>
    /// <para><b>Why a vertical escape still wins where one exists.</b> The ordering in <see cref="Evaluate"/> is surfacing first. Leaving the floor stops contact damage, and where a climb can reach air it also buys the lung. The retreat is what is left when it cannot.</para>
    /// </remarks>
    private static bool TryBeginRetreat(
        PathExecutor executor, int unharmedIndex, float health, out RetreatHold retreat)
    {
        IReadOnlyList<PathSegment> route = executor.Segments;
        int from = Math.Min(executor.CurrentIndex, route.Count - 1);
        if (from < 0)
        {
            retreat = default;
            return false;
        }

        // The WHOLE executed portion, newest first: the start of the segment being walked is the last cell the body was certainly standing in, and every start before it is one it left in one piece. The anchor picks where to stop and try, not where the list ends, so a settle that finds the anchor cell still hot has somewhere left to go.
        var waypoints = new Vec3d[from + 1];
        for (int i = 0; i < waypoints.Length; i++)
            waypoints[i] = route[from - i].Start;

        // One past the anchor, because reaching a waypoint advances the index past it. Clamped, so a bookkeeping value from a previous executor can never point outside this one's history.
        int settleFrom = from - Math.Clamp(unharmedIndex, 0, from) + 1;
        retreat = new RetreatHold(
            waypoints,
            Index: 0,
            SettleFrom: settleFrom,
            TotalTicksLeft: MaxRetreatTicks,
            SettleTicksLeft: RetreatSettleTicks,
            WorstHealth: health);
        return true;
    }

    /// <summary>Whether the body is close enough, horizontally, to count a retreat waypoint as reached.</summary>
    /// <remarks>Horizontal only. A retreat back along a leg that stepped down a block would otherwise never claim the cell it is standing in, and the elevation is the completed segment's own, not something the retreat has to reproduce.</remarks>
    private static bool HasReached(in Vec3d position, in Vec3d waypoint)
    {
        double dx = waypoint.X - position.X;
        double dz = waypoint.Z - position.Z;
        return (dx * dx) + (dz * dz) <= RetreatWaypointRadius * RetreatWaypointRadius;
    }

    private static bool IsSubmerged(IPhysicsWorldView view, Vec3d point)
        => BreathModel.IsSubmerged(
            view, (int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z));

    /// <summary>Whether the climb has arrived: the body is in water, and the cell over its feet cell is one it could breathe in - not water, and not something it is merely pressed against.</summary>
    /// <remarks>
    /// <para>Both halves are load bearing. <b>Not water</b> is <see cref="BreathModel.IsSubmerged"/>, the same head-cell predicate <see cref="BreathModel.EscapeTicks"/> stops its scan on and <c>BreathValidator</c> calls a breathing node, so the cell the climb aims at and the cell it stops at are decided by one piece of code. <b>Not motion-blocking</b> is the other half of that same scan and it is what stops the phase latching under stone: a body that has drifted a tenth of a block sideways has a lid cell over its feet cell, which is not water either, and holding there would park it under the very thing it needs to get out from under.</para>
    /// <para><b>In water</b> is what keeps the HEALTH arm's surfacing unchanged. That arm fires on dry land too - a body standing on a hot floor - where the answer is to climb off it, and where <see cref="BreathModel.EscapeTicks"/> already answers <c>Known</c>. Without this clause a health surfacing on land would hold station on the floor that is cooking it.</para>
    /// </remarks>
    private static bool HasClimbedIntoAir(IPhysicsWorldView view, in PhysicsState physics)
    {
        if (!physics.InWater || IsSubmerged(view, physics.Position))
            return false;

        var head = new BlockPos(
            (int)Math.Floor(physics.Position.X),
            (int)Math.Floor(physics.Position.Y) + 1,
            (int)Math.Floor(physics.Position.Z));
        return !view.GetBlock(head).BlocksMotion;
    }

    /// <summary>The horizontal centre of the cell a position sits in, keeping the position's own Y: the anchor a surfacing that has reached its air holds at.</summary>
    /// <remarks>The Y is carried through rather than floored because the hold is purely horizontal - only X and Z are read - which is the same convention <c>PhysicsEngineHolder.ArrivalCellCentre</c> follows for the station hold and for a scheduled breathing pause.</remarks>
    private static Vec3d ArrivalCellCentre(in Vec3d position)
        => new(Math.Floor(position.X) + 0.5, position.Y, Math.Floor(position.Z) + 0.5);

    /// <summary>A running surfacing: the air level it releases at, the two clocks that bound it, and the best lung it has seen (which is what makes "still rising" a question about the hold and not about one tick).</summary>
    /// <remarks><c>Anchor</c> is null while the hold is still climbing, and the cell centre it holds at once the climb has reached air. Latched: see the phase note in <see cref="LifeSafetyAction.SurfaceBreathing"/>.</remarks>
    private readonly record struct SurfacingHold(
        double ResumeAir,
        int TotalTicksLeft,
        int StallTicksLeft,
        int BestAir,
        int EntryAir,
        int Ticks,
        Vec3d? Anchor);

    /// <summary>A running retreat: the executed route in reverse, how far back along it the body has got, the two clocks that bound it, and the worst health it has seen (which is what makes "still being hurt" a question about the retreat and not about one tick).</summary>
    /// <remarks>The waypoints are frozen at the trip for the same reason <see cref="BeginHold"/> freezes the release condition: once the supervisor has stepped the engine even once the executor is dead by contract, so nothing the retreat needs may be a question asked of it later.</remarks>
    private readonly record struct RetreatHold(
        IReadOnlyList<Vec3d> Waypoints,
        int Index,
        int SettleFrom,
        int TotalTicksLeft,
        int SettleTicksLeft,
        float WorstHealth);
}

/// <summary>The emergency ascent: hold <c>Sprint</c> and <c>Jump</c> and look straight up.</summary>
/// <remarks>
/// <para>Measured, the controller choice is worth a factor of 2.2: <c>Sprint + Jump</c> with the pitch up the column climbs at 2.62 ticks a block where the naive "hold Jump" climbs at 5.71. Adding <c>Forward</c> does not climb any faster and adds a quarter of a block a tick of lateral drift, which in a shaft is a wall.</para>
/// <para>The inputs are LATCHED, not varied per tick. The fluid impulse is <c>+0.04</c> applied to velocity each tick and water drag eats it in two, so a controller that swaps its held input per tick never accumulates a climb at all.</para>
/// </remarks>
internal static class SurfacingController
{
    /// <summary>Straight up.</summary>
    public const float TargetPitch = -90.0f;

    /// <summary>The per-tick rotation limit the execution templates use, in degrees (<c>SegmentGeometry.MaxPitchStepPerTick</c>). A 90-degree pitch swing therefore takes four ticks, which is most of what <c>BreathModel.ReactionTicks</c> is reserved for.</summary>
    public const float MaxPitchStepPerTick = 25.0f;

    /// <summary>The engine inputs for one tick of emergency ascent.</summary>
    public static (MovementInput Input, float Yaw, float Pitch) Next(in PhysicsState physics)
        => (new MovementInput { Jump = true, Sprint = true }, physics.Yaw, StepPitch(physics.Pitch));

    private static float StepPitch(float current)
    {
        float delta = TargetPitch - current;
        if (delta > MaxPitchStepPerTick)
            return current + MaxPitchStepPerTick;

        return delta < -MaxPitchStepPerTick ? current - MaxPitchStepPerTick : TargetPitch;
    }
}

/// <summary>The withdrawal: face the cell the body came from, hold <c>Forward</c>, and in water hold <c>Jump</c> as well.</summary>
/// <remarks>
/// <para><b>Two latched inputs, and each does one job.</b> <c>Forward</c> is the retreat itself, aimed at a cell the body has already occupied. <c>Jump</c> is the half that stops contact damage: a body that leaves the floor stops being hurt by that floor on the tick it leaves it, and in water leaving it costs nothing - <c>jumpInLiquid</c> is a <c>+0.04</c> impulse a swimmer can hold indefinitely. On dry land it is deliberately NOT pressed: a jump there lands the body back on the same hazard and buys a hop, so walking off is the whole escape.</para>
/// <para>The inputs are LATCHED for the reason <see cref="SurfacingController"/> latches its own: the fluid impulse is applied to velocity each tick and drag eats it in two, so a controller that swaps its held input per tick never accumulates anything.</para>
/// <para>The pitch is levelled rather than aimed. The retreat is a horizontal withdrawal along a leg the body already walked, and while <c>Jump</c> is providing the lift a downward pitch would swim the body straight back onto the floor it is trying to leave.</para>
/// </remarks>
internal static class RetreatController
{
    /// <summary>Level.</summary>
    public const float TargetPitch = 0.0f;

    /// <summary>The per-tick rotation limits the execution templates use, in degrees (<c>SegmentGeometry.MaxYawStepPerTick</c> and <c>MaxPitchStepPerTick</c>).</summary>
    public const float MaxYawStepPerTick = 35.0f;

    /// <inheritdoc cref="MaxYawStepPerTick"/>
    public const float MaxPitchStepPerTick = 25.0f;

    /// <summary>The engine inputs for one tick of withdrawal toward <paramref name="target"/>, or of settling where the body already is once there is no target left.</summary>
    /// <remarks>A null target is the arrival, and letting go of BOTH inputs there is what makes the settle window mean anything: the body sinks back onto the cell it retreated to, and if that cell is hurting it too the window resets and the retreat carries on. Holding the lift through the settle would test nothing, because a body that is off the floor is never hurt by the floor.</remarks>
    public static (MovementInput Input, float Yaw, float Pitch) Next(in PhysicsState physics, Vec3d? target)
    {
        if (target is not { } waypoint)
            return (MovementInput.None, physics.Yaw, StepPitch(physics.Pitch));

        double dx = waypoint.X - physics.Position.X;
        double dz = waypoint.Z - physics.Position.Z;
        float yaw = dx == 0.0 && dz == 0.0 ? physics.Yaw : StepYaw(physics.Yaw, FacingYaw(dx, dz));
        return (
            new MovementInput { Forward = true, Jump = physics.InWater },
            yaw,
            StepPitch(physics.Pitch));
    }

    /// <summary>The yaw that faces a horizontal direction, in vanilla's convention.</summary>
    private static float FacingYaw(double dx, double dz)
    {
        float yaw = (float)(-Math.Atan2(dx, dz) / Math.PI * 180.0);
        return yaw < 0f ? yaw + 360f : yaw;
    }

    private static float StepYaw(float current, float target)
    {
        float delta = target - current;
        while (delta > 180f)
            delta -= 360f;

        while (delta < -180f)
            delta += 360f;

        float next = Math.Abs(delta) <= MaxYawStepPerTick
            ? target
            : current + (Math.Sign(delta) * MaxYawStepPerTick);
        while (next < 0f)
            next += 360f;

        while (next >= 360f)
            next -= 360f;

        return next;
    }

    private static float StepPitch(float current)
    {
        float delta = TargetPitch - current;
        if (delta > MaxPitchStepPerTick)
            return current + MaxPitchStepPerTick;

        return delta < -MaxPitchStepPerTick ? current - MaxPitchStepPerTick : TargetPitch;
    }
}
