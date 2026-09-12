using Umpk.Geometry;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>The lifecycle state of a path executor.</summary>
public enum PathExecutorState
{
    /// <summary>The path is still executing.</summary>
    InProgress,

    /// <summary>The whole path completed.</summary>
    Complete,

    /// <summary>A segment failed; the driver should replan.</summary>
    Failed,
}

/// <summary>A breathing pause the plan scheduled at the cell the body has just reached: stand still here, with the head in the air, until the lung is back.</summary>
/// <remarks>
/// <para>Surfaced to the driver rather than acted on, for the same layering reason a door is: <c>Umpk.Pathfinding</c> does not reference <c>Umpk.Client</c>, so the executor cannot see <c>SelfState.AirSupply</c> and cannot tell whether the lung is actually filling. The executor owns WHERE the pause is and HOW LONG the plan priced it at; the driver owns WHETHER IT IS WORKING, and may release early with <see cref="PathExecutor.NotifyBreathSatisfied"/> or abandon the navigation outright.</para>
/// <para>Absent a driver that says otherwise the executor simply serves the planned time, so a route executed with no breath-aware driver above it still performs the pause it was certified with.</para>
/// </remarks>
/// <param name="Cell">The feet cell the pause is taken at: the destination of the segment that carried it.</param>
/// <param name="PlannedTicks">Ticks the plan priced the pause at, which is the deficit at that cell divided by vanilla's four-a-tick refill.</param>
/// <param name="TicksHeld">Ticks already spent in this hold, counting the tick being reported.</param>
public readonly record struct BreathHoldRequirement(Vec3d Cell, double PlannedTicks, int TicksHeld);

/// <summary>One tick's result from <see cref="PathExecutor.Tick"/>: the lifecycle state, the <see cref="TemplateOutput"/> to apply, and whether a deviation warrants a replan.</summary>
public readonly record struct PathExecutorTick
{
    /// <summary>The executor lifecycle state after this tick.</summary>
    public PathExecutorState State { get; init; }

    /// <summary>The movement output the driver should apply this tick.</summary>
    public TemplateOutput Output { get; init; }

    /// <summary>Whether the real state deviated from the expected state past the replan threshold.</summary>
    public bool DeviationExceeded { get; init; }

    /// <summary>What the driver above the executor has to make happen in the world before the body may go on, or null on every ordinary tick.</summary>
    /// <remarks>The executor cannot send packets - <c>Umpk.Pathfinding</c> does not reference <c>Umpk.Client</c> - so a door is a REQUEST surfaced here and satisfied by whoever is driving, exactly as the life safety supervisor's surfacing is a pre-emption the navigator handles rather than something the executor does. While this is set the executor holds: it presses nothing, it does not advance, and it emits the same requirement every tick until <see cref="PathExecutor.NotifyInteractionSatisfied"/> is called.</remarks>
    public InteractionRequirement? PendingInteraction { get; init; }

    /// <summary>The breathing pause the plan scheduled at the cell the body has just reached, or null on every ordinary tick.</summary>
    /// <remarks>Set on every tick of the hold, not only its first, so a driver that starts watching mid-hold sees it. While it is set the executor presses nothing and does not advance. See <see cref="BreathHoldRequirement"/> for why this is a request rather than something the executor does itself.</remarks>
    public BreathHoldRequirement? PendingBreathHold { get; init; }
}

/// <summary>The session-free execution stack. It instantiates the correct <see cref="IActionTemplate"/> per segment, ticks it against the current <see cref="PhysicsState"/>, advances on completion, and reports failure for the caller (the future client/navigator layer) to replan. It also runs the deviation check: it remembers the last tick's expected state and compares it to the state fed on the next tick, so an execution that drifts from the plan surfaces a replan trigger. The executor never blocks and never reads a live engine; the driver owns the engine handoff and rotation.</summary>
public sealed class PathExecutor
{
    /// <summary>How much farther from a segment's END than the best it has managed the body may sit before a tick counts as backtracking, in blocks.</summary>
    /// <remarks>Half a block, which is below the length of the shortest move any segment can be and above the sub-tick jitter of a settle. A body half a block worse off than the best this segment ever achieved is not executing it.</remarks>
    public const double RegressionMarginBlocks = 0.5;

    /// <summary>Consecutive regressing ticks that abandon a segment, whatever its own tick budget says.</summary>
    /// <remarks>
    /// <para>Twenty, which is twice the whole airborne arc of a one-block ascend (8 to 11 ticks, measured over 22 geometries in <c>AscendTemplate</c>), so no single move can sit inside it while doing what it was planned to do. A braking overshoot and an airborne <c>Back</c> shortening both move away from the end, and both are over in well under it.</para>
    /// <para>This is a SEPARATE bound from the stuck detector each template carries, and the two catch opposite failures. Stuck is "the body is not moving"; this is "the body is moving, and away". Neither implies the other: a body being carried by a current at a quarter of a block a tick is never stuck, and a body pinned against a lid never regresses.</para>
    /// </remarks>
    public const int RegressionTicks = 20;

    private readonly PathExecutionContext _ctx;
    private readonly IReadOnlyList<PathSegment> _segments;
    private readonly IPathExecutionObserver? _observer;
    private readonly double _deviationThreshold;
    private readonly StationController _station;

    private int _currentIndex;
    private IActionTemplate? _currentTemplate;
    private int _segmentTicks;
    private int _totalTicks;
    private PhysicsState? _lastExpected;
    private double _bestDistanceToEnd = double.PositiveInfinity;
    private int _regressionTicks;
    private Phase _phase = Phase.Executing;
    private BarrierCrossing? _observedCrossing;
    private PathSegment? _activeSegment;
    private int _alignTicks;
    private int _breathHoldTicksPerformed;
    private int _breathHoldTicksLeft;
    private Vec3d _breathHoldCell;
    private double _breathHoldPlanned;
    private int _breathHoldElapsed;
    private Umpk.Geometry.BlockPos? _crossingDoorway;

    /// <summary>What the executor is doing between segments, which is not always "executing one".</summary>
    private enum Phase
    {
        /// <summary>Driving the current segment's template. The ordinary state.</summary>
        Executing,

        /// <summary>Steering the body into the doorway's lateral band BEFORE anything is opened. Only entered when the upcoming segment already knows which side its panel will be on, which is exactly the case a <see cref="Templates.FallTemplate"/> cannot fix afterwards.</summary>
        Aligning,

        /// <summary>Holding while the driver above opens the door the next segment crosses.</summary>
        AwaitingInteraction,

        /// <summary>Standing still at the cell the plan scheduled a breathing pause for, refilling the lung before the next segment begins.</summary>
        /// <remarks>Modelled on <see cref="AwaitingInteraction"/> and entered on the same terms: the tick returns before <c>_segmentTicks++</c>, so the wait accrues no segment budget and <c>SegmentBudgetPolicy</c> never sees it. That matters more here than it does for a door, because a scheduled pause can be a full lung's worth of refill - longer than any segment budget in the tree - so charging it to the segment would abandon every route that used one.</remarks>
        AwaitingBreath,
    }

    /// <summary>Creates an executor for a segment list.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deviationThreshold"/> is not positive.</exception>
    public PathExecutor(
        PathExecutionContext ctx,
        IReadOnlyList<PathSegment> segments,
        IPathExecutionObserver? observer = null,
        double deviationThreshold = 1.5)
        : this(ctx, segments, StationControllerOptions.Default, observer, deviationThreshold)
    {
    }

    /// <summary>Creates an executor whose station controller carries an explicit tuning. Internal, and it exists for exactly one reason: every part of <see cref="StationController"/> has to be ABLATABLE, so a test can show that removing it brings the drift back rather than merely asserting that it is there.</summary>
    internal PathExecutor(
        PathExecutionContext ctx,
        IReadOnlyList<PathSegment> segments,
        StationControllerOptions station,
        IPathExecutionObserver? observer = null,
        double deviationThreshold = 1.5)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviationThreshold);
        _ctx = ctx;
        _segments = segments;
        _observer = observer;
        _deviationThreshold = deviationThreshold;
        _station = new StationController(station);
        _observer?.OnNavigationStarted(segments);
        AdvanceToNextSegment();
    }

    /// <summary>The frozen context this executor and every template it drives run against: the captured terrain, the physics profile, the conditions, and the player capabilities as of the plan's capture. Exposed so a driver above the executor can see what the plan was priced under without re-deriving it.</summary>
    public PathExecutionContext Context => _ctx;

    /// <summary>Whether the whole path has completed.</summary>
    public bool IsComplete => _currentIndex >= _segments.Count && _currentTemplate is null;

    /// <summary>The index of the segment currently executing.</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>The total number of segments.</summary>
    public int TotalSegments => _segments.Count;

    /// <summary>Ticks the CURRENT segment has been executing for, excluding any hold.</summary>
    public int CurrentSegmentTicks => _segmentTicks;

    /// <summary>Ticks this executor has actually spent standing still in scheduled breathing pauses.</summary>
    /// <remarks>The acceptance criterion for the whole breathing feature is that this is greater than zero on a route the plan scheduled pauses for. Without it there is no way to tell a schedule that RAN from a life-safety supervisor that rescued the bot, and a route that arrives by rescue has not proven anything about the schedule.</remarks>
    public int BreathHoldTicksPerformed => _breathHoldTicksPerformed;

    /// <summary>Whether the executor is holding at a cell the plan scheduled a breathing pause for.</summary>
    /// <remarks>
    /// <para>Exposed for exactly one caller: the life-safety supervisor's breath arm, which must stand down while this is true. The order the two run in makes the question unanswerable any other way. <c>PhysicsEngineHolder</c> evaluates the supervisor BEFORE it ticks the executor - correctly, because the point of the supervisor is that the executor is about to do something that kills the player - and <see cref="AdvanceToNextSegment"/> arms the pause on the tick the segment carrying it completes. So by the supervisor's next look <see cref="CurrentIndex"/> has already moved PAST the breathing cell, and a route arm walking forward from there prices the leg to the NEXT breathing node against a lung that has just finished a submerged leg and is at its lowest. It fires at the one cell where the plan was about to refill it.</para>
    /// </remarks>
    public bool IsAwaitingBreath => _phase == Phase.AwaitingBreath;

    /// <summary>The planned route. Exposed so a driver above the executor can reason about what is still ahead - the life-safety supervisor asks it where the next breathing node is, and holds the reference across a pre-emption that kills the executor.</summary>
    public IReadOnlyList<PathSegment> Segments => _segments;

    /// <summary>Whether this plan was priced and is executed with sprinting allowed. It decides real swim and walk speeds, so anything estimating how long the rest of the route takes needs it.</summary>
    public bool AllowSprint => _ctx.AllowSprint;

    /// <summary>The total number of ticks executed so far.</summary>
    public int TotalTicks => _totalTicks;

    /// <summary>The segment currently executing, or null if finished.</summary>
    public PathSegment? CurrentSegment => _currentIndex < _segments.Count ? _segments[_currentIndex] : null;

    /// <summary>How far off the panel side of a doorway the body has to be, relative to the free-side aim, before the alignment phase lets go: 0.05 blocks either way of <see cref="BarrierCrossing.FreeSideBias"/>.</summary>
    /// <remarks>That is a body-centre offset of 0.55 to 0.65 from the panel-side cell face, inside the 0.5-to-0.7 band <c>DoorwayCrossingTests</c> measured against the real engine and comfortably clear of the 0.4875 the panel itself reaches. Tighter would have the alignment hunting; looser would let it finish on the band's edge, which is the state E1b existed to get away from.</remarks>
    public const double AlignToleranceBlocks = 0.05;

    /// <summary>Ticks the alignment phase is allowed before the executor gives up and crosses anyway: two seconds.</summary>
    /// <remarks>A body that cannot reach the band in two seconds of sneaking is not going to, and hanging forever is worse than trying: the crossing may still work (the aim is a bias, not a requirement) and if it does not, the segment fails and the navigator replans, which is a recoverable outcome. Hanging is not.</remarks>
    public const int MaxAlignTicks = 40;

    /// <summary>Whether the body has passed the current crossing's commit point - centre at least <see cref="BarrierCrossing.CommitOffset"/> past the doorway's entry face along the direction of travel - so that a door observed closing now is a failure rather than a nuisance.</summary>
    /// <remarks>False whenever there is no crossing to be committed to, which is every segment on terrain with no door in it.</remarks>
    public bool HasCommittedToCrossing { get; private set; }

    /// <summary>Tells the executor that the interaction it is holding for has been done, optionally with the panel side the driver OBSERVED once the barrier moved.</summary>
    /// <remarks>
    /// <para>The observed side is the honest one. The executor's own world is the frozen plan capture, in which the door is still shut, so the shape it can read is the closed panel's - ninety degrees off the one the body is about to squeeze past. The driver has the live world and can simply look.</para>
    /// <para>Calling this when nothing is pending is a no-op rather than an error: a driver that retries an interaction it already satisfied has not done anything wrong.</para>
    /// </remarks>
    /// <param name="observedCrossing">The panel side read from the live world, or null if unknown.</param>
    public void NotifyInteractionSatisfied(BarrierCrossing? observedCrossing = null)
    {
        if (_phase != Phase.AwaitingInteraction)
            return;

        if (observedCrossing is not null)
            _observedCrossing = observedCrossing;

        // The doorway the commit point is measured against. It is the barrier the interaction was VERIFIED on rather than the segment's end cell, because those are not the same cell on the segment that walks back OUT of a doorway, and because a lid is under the body rather than in front of it.
        _crossingDoorway = _segments[_currentIndex].Interaction?.Witness;
        HasCommittedToCrossing = false;
        _phase = Phase.Executing;
        BeginCurrentSegment();
    }

    /// <summary>Advances execution by one tick against the current physics state.</summary>
    public PathExecutorTick Tick(in PhysicsState physics)
    {
        _totalTicks++;

        bool deviation = CheckDeviation(physics) | CheckCrossTrack(physics);
        UpdateCommitment(physics);

        if (_phase == Phase.Aligning)
        {
            if (TryAlign(physics, out TemplateOutput alignOutput))
                return new PathExecutorTick
                {
                    State = PathExecutorState.InProgress,
                    Output = alignOutput,
                    DeviationExceeded = deviation,
                };

            EnterInteractionHoldOrExecute();
        }

        if (_phase == Phase.AwaitingInteraction)
            return new PathExecutorTick
            {
                State = PathExecutorState.InProgress,
                Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch },
                PendingInteraction = _segments[_currentIndex].Interaction,
                DeviationExceeded = deviation,
            };

        if (_phase == Phase.AwaitingBreath)
        {
            // Counted BEFORE the early return, so the tick being reported is included in TicksHeld and the two counters cannot disagree with the number of ticks the driver actually saw.
            _breathHoldTicksLeft--;
            _breathHoldTicksPerformed++;
            _breathHoldElapsed++;

            PathExecutorTick holding = new()
            {
                State = PathExecutorState.InProgress,
                Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch },
                PendingBreathHold = new BreathHoldRequirement(_breathHoldCell, _breathHoldPlanned, _breathHoldElapsed),
                DeviationExceeded = deviation,
            };

            if (_breathHoldTicksLeft <= 0)
                ReleaseBreathHold();

            return holding;
        }

        if (_currentTemplate is null)
            return new PathExecutorTick
            {
                State = PathExecutorState.Complete,
                Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch },
                DeviationExceeded = deviation,
            };

        // A segment whose body is being carried away from its end is not going to arrive. Its tick budget prices duration, so displacement is the independent safety bound for this condition.
        if (CheckRegression(physics))
        {
            _observer?.OnSegmentFailed(
                _currentIndex, _segments.Count, _segments[_currentIndex], _segmentTicks, physics.Position);
            return new PathExecutorTick
            {
                State = PathExecutorState.Failed,
                Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch },
                DeviationExceeded = deviation,
            };
        }

        int sameTickAdvances = 0;
        while (_currentTemplate is not null)
        {
            _segmentTicks++;
            TemplateState state = _currentTemplate.Tick(physics, out TemplateOutput output);

            // The lateral half of the segment, added here and nowhere else. It runs BEFORE _lastExpected is taken, so the deviation baseline is a prediction of the input actually pressed; see StationController.Correct.
            output = _station.Correct(_activeSegment, physics, output, _ctx);
            _lastExpected = output.ExpectedState;

            switch (state)
            {
                case TemplateState.Complete:
                    _observer?.OnSegmentCompleted(_currentIndex, _segments.Count, _segments[_currentIndex], _segmentTicks, physics.Position);
                    _currentIndex++;
                    _segmentTicks = 0;
                    if (_currentIndex >= _segments.Count)
                    {
                        _currentTemplate = null;
                        _observer?.OnNavigationCompleted(_totalTicks);
                        return new PathExecutorTick
                        {
                            State = PathExecutorState.Complete,
                            Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = output.TargetYaw, TargetPitch = output.TargetPitch },
                            DeviationExceeded = deviation,
                        };
                    }

                    AdvanceToNextSegment();
                    sameTickAdvances++;
                    if (sameTickAdvances > _segments.Count)
                        return new PathExecutorTick { State = PathExecutorState.Failed, Output = output, DeviationExceeded = deviation };

                    if (_phase != Phase.Executing)
                    {
                        // The next segment has a door in front of it. Stop here rather than ticking a template that does not exist yet; the hold begins on the NEXT tick, which keeps one tick of the executor to one decision.
                        return new PathExecutorTick
                        {
                            State = PathExecutorState.InProgress,
                            Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = output.TargetYaw, TargetPitch = output.TargetPitch },
                            PendingInteraction = _phase == Phase.AwaitingInteraction ? _segments[_currentIndex].Interaction : null,
                            DeviationExceeded = deviation,
                        };
                    }

                    continue;

                case TemplateState.Failed:
                    _observer?.OnSegmentFailed(_currentIndex, _segments.Count, _segments[_currentIndex], _segmentTicks, physics.Position);
                    return new PathExecutorTick { State = PathExecutorState.Failed, Output = output, DeviationExceeded = deviation };

                default:
                    return new PathExecutorTick { State = PathExecutorState.InProgress, Output = output, DeviationExceeded = deviation };
            }
        }

        return new PathExecutorTick
        {
            State = PathExecutorState.Complete,
            Output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch },
            DeviationExceeded = deviation,
        };
    }

    private bool CheckDeviation(in PhysicsState physics)
    {
        if (_lastExpected is not { } expected)
            return false;

        double distance = physics.Position.Subtract(expected.Position).Length();
        if (distance > _deviationThreshold)
        {
            _observer?.OnDeviationDetected(_currentIndex, expected.Position, physics.Position, distance);
            return true;
        }

        return false;
    }

    /// <summary>The second, correctly-dimensioned replan trigger: the body has been outside its segment's lane for long enough that the station controller is not going to pull it back.</summary>
    /// <remarks><see cref="CheckDeviation"/> cannot cover this and no value of its threshold could. <see cref="_lastExpected"/> is a ONE-TICK forward simulation by the same <c>PlayerPhysics</c> that applies <c>ApplyFluidPushing</c>, so the prediction already contains the current's push and the body is washed off its lane exactly as predicted. Measured on the offline crossing in <c>CrossTrackStationTests</c>: worst per-tick prediction error <c>0.0000</c> against <c>1.0037</c> blocks of cross-track drift.</remarks>
    private bool CheckCrossTrack(in PhysicsState physics)
    {
        if (!_station.CheckCrossTrack(_activeSegment, physics, out double offset))
            return false;

        Templates.SegmentGeometry.GetNormalizedSegmentDirection(_activeSegment!, out double dirX, out double dirZ);
        var onLine = new Vec3d(
            physics.Position.X + (offset * dirZ), physics.Position.Y, physics.Position.Z - (offset * dirX));
        _observer?.OnDeviationDetected(_currentIndex, onLine, physics.Position, Math.Abs(offset));
        return true;
    }

    /// <summary>Whether the body has been getting FARTHER from the current segment's end, past the best it managed on this segment, for <see cref="RegressionTicks"/> consecutive ticks.</summary>
    /// <remarks>Measured against the segment's own BEST rather than against its start, so a move that legitimately leaves its start behind (every one of them) is judged on whether it is still making the ground it already made, and a move that has never been closer than it is now simply re-bases. Full 3D, because the two failures this exists for are one horizontal (carried downstream) and one vertical (falling out of the world).</remarks>
    private bool CheckRegression(in PhysicsState physics)
    {
        if (_currentTemplate is null || _currentIndex >= _segments.Count)
            return false;

        double distance = physics.Position.Subtract(_segments[_currentIndex].End).Length();
        if (distance < _bestDistanceToEnd)
        {
            _bestDistanceToEnd = distance;
            _regressionTicks = 0;
            return false;
        }

        _regressionTicks = distance > _bestDistanceToEnd + RegressionMarginBlocks ? _regressionTicks + 1 : 0;
        return _regressionTicks > RegressionTicks;
    }

    private void AdvanceToNextSegment()
    {
        _bestDistanceToEnd = double.PositiveInfinity;
        _regressionTicks = 0;
        _station.Reset();
        _observedCrossing = null;
        _alignTicks = 0;
        if (_currentIndex < _segments.Count && _segments[_currentIndex].Interaction is not null)
        {
            // A NEW barrier: the previous one's commitment says nothing about this one.
            _crossingDoorway = null;
            HasCommittedToCrossing = false;
        }

        if (_currentIndex >= _segments.Count)
        {
            _currentTemplate = null;
            _activeSegment = null;
            _phase = Phase.Executing;
            return;
        }

        // The breathing pause the segment just finished carried. It is taken BEFORE any door in front of the next segment, because it belongs to the cell the body is standing on now.
        if (TryEnterBreathHold())
        {
            _currentTemplate = null;
            return;
        }

        PathSegment seg = _segments[_currentIndex];
        if (seg.Interaction is null)
        {
            _phase = Phase.Executing;
            BeginCurrentSegment();
            return;
        }

        // A door in front of this segment. Where the panel will stand is already known - the segment carries a predicted crossing - the body squares up FIRST, because the move that needs the band may be a fall, and a fall cannot steer.
        _currentTemplate = null;
        _phase = seg.Crossing is not null ? Phase.Aligning : Phase.AwaitingInteraction;
    }

    /// <summary>Releases a breathing pause early, because the driver above can see the lung and the executor cannot.</summary>
    /// <remarks>
    /// <para>The planned tick count is a PRICE, not a measurement: it is the deficit the search believed the route had run up, divided by vanilla's four-a-tick refill. The body's actual lung may be fuller than the plan assumed (it started the route with more air than the plan's zero deficit implies) or the refill may be faster than the duty cycle at a constrained opening allows. Only the driver can tell, so only the driver may cut the wait short.</para>
    /// <para>Calling this when no hold is running is a no-op rather than an error, exactly as <see cref="NotifyInteractionSatisfied"/> is.</para>
    /// </remarks>
    public void NotifyBreathSatisfied()
    {
        if (_phase != Phase.AwaitingBreath)
            return;

        ReleaseBreathHold();
    }

    private void ReleaseBreathHold()
    {
        _breathHoldTicksLeft = 0;
        _breathHoldElapsed = 0;
        _phase = Phase.Executing;
        BeginCurrentSegment();
    }

    /// <summary>Whether the segment just finished carried a scheduled pause, and if so arms it.</summary>
    /// <remarks>The pause hangs on the segment that ENDS at the breathing cell, so the one to read is the one just completed - <c>_currentIndex - 1</c> - and it is armed after that segment's template is done and before the next one's begins.</remarks>
    private bool TryEnterBreathHold()
    {
        if (_currentIndex <= 0 || _currentIndex > _segments.Count)
            return false;

        PathSegment finished = _segments[_currentIndex - 1];
        if (finished.BreathHoldTicks <= 0.0)
            return false;

        // Rounded UP: a pause priced at 40.2 ticks that serves 40 leaves the body departing with less air than the plan was certified on, and the whole point of the schedule is that the departure bar is met.
        _breathHoldTicksLeft = (int)Math.Ceiling(finished.BreathHoldTicks);
        _breathHoldPlanned = finished.BreathHoldTicks;
        _breathHoldCell = finished.End;
        _breathHoldElapsed = 0;
        _phase = Phase.AwaitingBreath;
        return true;
    }

    private void EnterInteractionHoldOrExecute()
    {
        if (_currentIndex < _segments.Count && _segments[_currentIndex].Interaction is not null)
        {
            _phase = Phase.AwaitingInteraction;
            return;
        }

        _phase = Phase.Executing;
        BeginCurrentSegment();
    }

    private void BeginCurrentSegment()
    {
        if (_currentIndex >= _segments.Count)
        {
            _currentTemplate = null;
            _activeSegment = null;
            return;
        }

        PathSegment seg = _segments[_currentIndex];
        if (_observedCrossing is { } observed)
            seg = seg with { Crossing = observed };

        _activeSegment = seg;
        _currentTemplate = ActionTemplateFactory.Create(_ctx, seg);
        _lastExpected = null;
        _observer?.OnSegmentStarted(_currentIndex, _segments.Count, seg);
    }

    /// <summary>Walks the body onto the doorway's free side before anything is opened, and reports whether it is still doing so.</summary>
    /// <remarks>
    /// The aim is the segment's own start point pushed <see cref="BarrierCrossing.FreeSideBias"/> toward the free side, and the input is a SNEAKING forward press: the whole correction is 0.1 of a block, which one walking tick overshoots and one sneaking tick is worth about 0.065 of - the same reasoning <c>PhysicsEngineHolder.TickApproach</c> records for a sub-block approach.
    /// <para>The sneak is dropped when the body's feet cell is scaffolding. There the bit does not mean "cross carefully", it means "fall": vanilla's climb clamp exempts scaffolding and its plates are absent while descending, so a body aligning to a doorway from inside a scaffold would sink 0.15 a tick for up to <see cref="MaxAlignTicks"/>. Nothing else changes: for every crossing whose feet cell is not scaffolding the emitted input is bit-identical to what it was.</para>
    /// </remarks>
    private bool TryAlign(in PhysicsState physics, out TemplateOutput output)
    {
        output = new TemplateOutput { Input = MovementInput.None, TargetYaw = physics.Yaw, TargetPitch = physics.Pitch };
        if (_currentIndex >= _segments.Count || _segments[_currentIndex].Crossing is not { } crossing)
            return false;

        PathSegment seg = _segments[_currentIndex];
        double targetX = seg.Start.X + (crossing.FreeX * BarrierCrossing.FreeSideBias);
        double targetZ = seg.Start.Z + (crossing.FreeZ * BarrierCrossing.FreeSideBias);
        double dx = targetX - physics.Position.X;
        double dz = targetZ - physics.Position.Z;

        // Only the component ACROSS the panel matters; drift along it is what the segment itself steers.
        double error = (dx * crossing.FreeX) + (dz * crossing.FreeZ);
        if (Math.Abs(error) <= AlignToleranceBlocks || ++_alignTicks > MaxAlignTicks)
            return false;

        double aimX = error > 0 ? crossing.FreeX : -crossing.FreeX;
        double aimZ = error > 0 ? crossing.FreeZ : -crossing.FreeZ;
        bool scaffold = Templates.FallTemplate.IsScaffoldingAt(
            _ctx, physics.Position.X, physics.Position.Y, physics.Position.Z);
        output = new TemplateOutput
        {
            Input = new MovementInput { Forward = true, Sneak = !scaffold },
            TargetYaw = (float)(Math.Atan2(-aimX, aimZ) * 180.0 / Math.PI),
            TargetPitch = physics.Pitch,
        };
        return true;
    }

    /// <summary>Latches <see cref="HasCommittedToCrossing"/> once the body's centre is <see cref="BarrierCrossing.CommitOffset"/> past the doorway's entry face along the travel direction. Latched rather than sampled, because the question a reclose asks is "has this body ever been past the point of no return", not "is it there this instant".</summary>
    private void UpdateCommitment(in PhysicsState physics)
    {
        if (HasCommittedToCrossing || _crossingDoorway is not { } doorway || _activeSegment is not { } seg)
            return;

        int headingX = seg.HeadingX;
        int headingZ = seg.HeadingZ;
        if (headingX == 0 && headingZ == 0)
        {
            // A vertical move - a fall through a lid - has no entry face to be past. What is committed there is leaving the ground, which the fall itself reports, and the reclose question does not arise: nothing shuts a trapdoor under a body already in the air.
            return;
        }

        // The doorway cell's ENTRY face along the direction of travel: its low edge when travelling in the positive direction, its high edge otherwise.
        int heading = headingX != 0 ? headingX : headingZ;
        double face = headingX != 0
            ? (headingX > 0 ? doorway.X : doorway.X + 1)
            : (headingZ > 0 ? doorway.Z : doorway.Z + 1);
        double centre = headingX != 0 ? physics.Position.X : physics.Position.Z;

        HasCommittedToCrossing = heading * (centre - face) >= BarrierCrossing.CommitOffset;
    }
}
