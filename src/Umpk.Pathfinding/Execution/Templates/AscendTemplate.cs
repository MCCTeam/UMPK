using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>
/// Jumps up one block while moving one block horizontally (Ascend). Faces the destination, sprints forward, and jumps when grounded below the target; completes once back on the ground at the target's elevation with the center inside the target column. It emits <see cref="TemplateOutput"/> and preserves the headroom-clear early jump and heading-ready gate.
/// <para>Once airborne on dry land the jump is steered closed-loop: every airborne tick re-picks the held input from its <see cref="PhysicsSimulator.PredictLanding"/> outcome (see <c>ChooseAirborneInput</c>), which is what keeps a DIAGONAL ascend on the destination column.</para>
/// </summary>
public sealed class AscendTemplate : IActionTemplate
{
    /// <summary>How far a predicted landing may sit from the destination's elevation and still count as having arrived. Matches the completion gate's own <c>|dy| &lt; 0.2</c>, deliberately: the objective must call "arrived" exactly what the gate calls "arrived", or the controller optimises for a landing the gate will refuse.</summary>
    private const double LandingElevationTolerance = 0.2;

    /// <summary>The lookahead for one landing prediction. Fixed and small because a one-block ascend's whole arc is 8-11 ticks (measured over 22 geometries and 32 approach phases); 16 is the ceiling and it only bounds the cost of a candidate that never lands, since <see cref="PhysicsSimulator.PredictLanding"/> returns the moment a candidate touches down. <see cref="DescendTemplate"/> derives its budget from the remaining fall instead, because a fall can be eight blocks; an ascend is always one.</summary>
    private const int LandingPredictionTicks = 16;

    private readonly PathExecutionContext _ctx;
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    /// <summary>The consecutive-stuck-tick limit for that budget.</summary>
    private readonly int _stuckLimit;
    private int _tickCount;
    private Vec3d _lastPos;
    private int _stuckTicks;
    private bool _hasBeenAirborne;

    /// <summary>Creates an ascend template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public AscendTemplate(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);
        _ctx = ctx;
        _segment = segment;
        _budget = ctx.Budget.BudgetFor(segment);
        _stuckLimit = ctx.Budget.StuckTicksFor(_budget);
        ExpectedStart = segment.Start;
        ExpectedEnd = segment.End;
        _lastPos = segment.Start;
    }

    /// <inheritdoc/>
    public Vec3d ExpectedStart { get; }

    /// <inheritdoc/>
    public Vec3d ExpectedEnd { get; }

    /// <inheritdoc/>
    public TemplateState Tick(in PhysicsState physics, out TemplateOutput output)
    {
        _tickCount++;
        Vec3d pos = physics.Position;

        double dx = ExpectedEnd.X - pos.X;
        double dz = ExpectedEnd.Z - pos.Z;
        double dy = ExpectedEnd.Y - pos.Y;

        // Steer toward the segment's cardinal heading, which is also what headingReady checks. From an off-centre start the endpoint bearing can differ by more than the eight-degree readiness bound.
        float targetYaw = SegmentGeometry.CalculateYaw(_segment.HeadingX, _segment.HeadingZ);
        float targetPitch = SegmentGeometry.CalculatePitch(dx, dy, dz);
        float yaw = _tickCount == 1 ? targetYaw : SegmentGeometry.SmoothYaw(physics.Yaw, targetYaw);
        float pitch = SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch);

        double headingPenalty = SegmentGeometry.HeadingPenaltyDegrees(yaw, _segment.HeadingX, _segment.HeadingZ);
        bool headingReady = headingPenalty <= 8.0;

        var input = new MovementInput
        {
            Forward = headingReady,
            Sprint = headingReady,
        };

        // A fluid jump is not a ground jump. It applies a 0.04 impulse without the ground jump's ten-tick delay. The smaller impulse becomes an ascent only when applied on consecutive ticks, because water drag has eaten it within two; requiring OnGround delivers it only on the ticks the player has already sunk back to the bottom, which is a 0.02-block bob and not a climb. On the ground, an airborne ground-jump input is wasted and the jump delay paces a held input.
        //
        // Keep the 0.1 slack for fluid ascents. Residual upward velocity carries the player over the ledge, so the same stopping tolerance works for fluid and ballistic jumps.
        bool inFluid = physics.InWater || physics.InLava;
        if ((physics.OnGround || inFluid) && dy > 0.1 && headingReady)
        {
            input = input with { Jump = true };
            yaw = targetYaw;
        }

        // Airborne on dry land: stop holding Forward open-loop and steer the arc by its predicted landing. A DIAGONAL one-block ascend cannot be flown open-loop, and the reason is collision resolution rather than the jump: approaching the destination column corner-on, the body's leading faces touch the column during the rise, axis-ordered resolution frees whichever axis it resolves first and pins the other, and the whole 0.2 sprint-jump boost lands on one axis. On the B4 course pad (flush NE corner, yaw 315) the Z half of the boost was consumed at take-off, the trajectory ran down the column's +X edge to a lateral offset of 0.713, and the first grounded tick was x = 1.2043 against a column ending at 1.0 - 0.204 blocks past it, off the pad, and no completion gate can ever fire again. The cardinal case only passes because the block face blocks the TRAVEL axis, so the boost is spent into the wall and the hop is short; it is crippled in the direction that happens to help.
        //
        // The candidate set carries the strafes for exactly that reason. The authority this needs is LATERAL, and Left/Right are the only lateral levers: at yaw 315 the strafe axis IS the axis the column's face stripped, and Forward+Right converts the surviving +X momentum into the missing +Z. Measured over 22 geometries and 32 approach phases: the descend controller's forward-only
        // set (Forward / nothing / Back) fixes 15/22 and 25/32 because it has no lateral authority at all;
        // this set fixes 22/22 and 32/32 with a worst landing error of 0.267 blocks against V0's 11/32. The pure strafes and the two shortening candidates each earn their place on a chained diagonal staircase, where every handoff is the next take-off and there is no room to recover. Ablated in this tree: drop nothing/Back and the controller cannot SHORTEN an arc, so tread 1 hands off at (0.8775, 0.7000) near the tread's far corner and tread 2 dies; drop the pure Left/Right and it cannot correct laterally WHILE shortening, so the drift takes three treads to accumulate and tread 3 hands off at (3.2512, 2.2233), a quarter block outside its own tread, killing tread 4. With the full set all six treads hand off inside their column and the route takes 61 ticks.
        //
        // Not in a fluid. A submerged ascend's input is the fluid jump impulse the arm above emits on consecutive ticks, and the player is !OnGround for most of that climb, so running the controller there would drop Jump and re-break the two captured 1.9 sessions (SubmergedAscendTests). Not on the first airborne tick either: _hasBeenAirborne latches below, after the completion checks, so this arm starts on the SECOND airborne tick. Running it from the first was ablated and is not free - it still fixes 22/22 and 32/32 but at a worst error of 0.290, and it makes the five-candidate set (no pure strafes) pass the chained staircase too, which costs that part of the change its only failing test.
        if (!physics.OnGround && !inFluid && _hasBeenAirborne && headingReady)
            input = ChooseAirborneInput(physics, yaw);

        // Landed at an elevation the target column legitimately rests a body at, with the center inside that column: complete. The band is `[End.Y - 0.2, End.Y + EndElevationSlack + 0.2]` rather than `|dy| < 0.2` because the destination's own elevation is not the only one it can hold: a 0.6-wide body centred in a 1-wide column also stands over its neighbours and settles on the highest of them. Over uniform terrain the slack is 0 and this IS `|dy| < 0.2`.
        if (physics.OnGround
            && SegmentGeometry.IsAtEndElevation(pos.Y, ExpectedEnd, _segment.EndElevationSlack, LandingElevationTolerance)
            && _hasBeenAirborne
            && SegmentGeometry.IsCenterInsideTargetBlock(pos, _segment.End))
        {
            output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);
            return TemplateState.Complete;
        }

        if (!physics.OnGround)
            _hasBeenAirborne = true;

        output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);

        // The same band as the arm above: the two must call "arrived" the same thing, or the second one refuses what the first accepts.
        if (physics.OnGround
            && SegmentGeometry.IsAtEndElevation(pos.Y, ExpectedEnd, _segment.EndElevationSlack, LandingElevationTolerance)
            && GroundedSegmentController.ShouldComplete(_segment, pos, physics))
            return TemplateState.Complete;

        double movedSq = HorizontalDistanceSq(pos, _lastPos);
        double movedY = Math.Abs(pos.Y - _lastPos.Y);
        _stuckTicks = movedSq < _ctx.Budget.StuckMovedSquared(physics.InWater) && movedY < 0.001 ? _stuckTicks + 1 : 0;
        _lastPos = pos;

        if (_stuckTicks > _stuckLimit || _tickCount > _budget)
            return TemplateState.Failed;

        return TemplateState.InProgress;
    }

    /// <summary>Whether <paramref name="candidate"/>'s predicted landing serves the ascend better than <paramref name="incumbent"/>'s, against a destination of <paramref name="end"/>.</summary>
    /// <remarks>
    /// Two keys, in this order, and the order is the whole point. A candidate that does not land inside the prediction budget, or lands more than <see cref="LandingElevationTolerance"/> from the destination's elevation, is a FAILED CLIMB and loses to any candidate that reached the right elevation, however far off horizontally it landed. Without that key the objective would happily "fix" a lateral overshoot by picking the input that drops the bot straight back onto the pad it took off from, because a fall-back can be horizontally nearer the destination than a real landing is. This is how an ascend refuses a jump that fell back down WITHOUT loosening a completion gate: the refusal lives in the objective, not in the gate.
    /// <para>Honest boundary: on a ONE-BLOCK ascend the elevation key never actually changes a decision, and that is geometry rather than luck. A predicted landing within ~0.3 of the destination column's centre is by construction ON that column and therefore at its elevation, while a landing at the wrong elevation has to clear the column's half-width plus the player's, so it is at least 0.8 away; the winning right-elevation candidate is never worse than 0.3. Instrumented over the 22 named geometries and the 32 approach phases, the key flipped the winner 0 times. It is kept because it states what the objective means, because the bound above is a property of a one-block step and not of this method, and because it is what makes the loop safe to reuse.</para>
    /// </remarks>
    internal static bool IsBetterLanding(
        in LandingPrediction candidate, in LandingPrediction incumbent, in Vec3d end, double slack = 0.0)
    {
        bool candidateFailed = IsFailedClimb(candidate, end, slack);
        bool incumbentFailed = IsFailedClimb(incumbent, end, slack);
        if (candidateFailed != incumbentFailed)
            return !candidateFailed;

        return HorizontalDistanceSq(candidate.LandingPosition, end) < HorizontalDistanceSq(incumbent.LandingPosition, end);
    }

    /// <summary>Whether a predicted landing missed every elevation the destination column can rest a body at, or never landed at all.</summary>
    /// <remarks><paramref name="slack"/> is <c>PathSegment.EndElevationSlack</c>: how far above its own elevation the column's coplanar neighbours reach, and therefore how far above <paramref name="end"/> a body whose centre is inside the column can legitimately settle. It defaults to zero so a caller with no straddle to model, including every uniform-terrain caller, gets <c>|dy| &lt;= LandingElevationTolerance</c> exactly. The objective's ordering is unaffected either way: a landing outside the band still has to clear the column's half-width plus the player's, so the elevation key keeps discriminating what it always did.</remarks>
    internal static bool IsFailedClimb(in LandingPrediction prediction, in Vec3d end, double slack = 0.0)
        => !prediction.Landed
            || !SegmentGeometry.IsAtEndElevation(
                prediction.LandingPosition.Y, end, slack, LandingElevationTolerance);

    /// <summary>Picks this airborne tick's input by forward-simulating where each candidate would land and keeping the one that best serves <see cref="IsBetterLanding"/>.</summary>
    /// <remarks>Simulates from the pose the executor is ABOUT to apply, not the one it currently holds: the driver writes the emitted yaw before it steps, and air control is applied along that yaw. Sprint is on in every forward candidate because this arm only runs while the heading gate is open, which is the same condition the open-loop input sprints under, so the ascend never quietly contradicts <see cref="PathSegmentBuilder"/>'s sprint taxonomy.</remarks>
    private MovementInput ChooseAirborneInput(in PhysicsState physics, float plannedYaw)
    {
        PhysicsState start = physics with { Yaw = plannedYaw };

        ReadOnlySpan<MovementInput> candidates =
        [
            new MovementInput { Forward = true, Sprint = true },
            new MovementInput { Forward = true, Left = true, Sprint = true },
            new MovementInput { Forward = true, Right = true, Sprint = true },
            new MovementInput { Left = true },
            new MovementInput { Right = true },
            MovementInput.None,
            new MovementInput { Back = true },
        ];

        MovementInput best = candidates[0];
        LandingPrediction bestLanding = PhysicsSimulator.PredictLanding(
            start, _ctx.Conditions, _ctx.World, _ctx.Profile, candidates[0], LandingPredictionTicks);

        for (int i = 1; i < candidates.Length; i++)
        {
            LandingPrediction prediction = PhysicsSimulator.PredictLanding(
                start, _ctx.Conditions, _ctx.World, _ctx.Profile, candidates[i], LandingPredictionTicks);
            if (IsBetterLanding(prediction, bestLanding, ExpectedEnd, _segment.EndElevationSlack))
            {
                bestLanding = prediction;
                best = candidates[i];
            }
        }

        return best;
    }

    private static double HorizontalDistanceSq(in Vec3d a, in Vec3d b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
