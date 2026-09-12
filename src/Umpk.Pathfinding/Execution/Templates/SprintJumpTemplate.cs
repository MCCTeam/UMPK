using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>
/// Sprint-jumps across a gap (Parkour). A phased state machine: Approach (build sprint speed, jump at the block edge once yaw is aligned, or hand off if the gap turned out to be walkable), Airborne (steer the arc by its predicted landing), and Landing (settle on the destination). Sidewall-profile jumps use the same straight sprint-jump controller.
/// <para>The arc is steered closed-loop: from the SECOND airborne tick every tick re-picks the held input from its <see cref="PhysicsSimulator.PredictLanding"/> outcome (see <c>ChooseAirborneInput</c>), which is what puts a leap onto a pad narrower than the overshoot it would otherwise carry.</para>
/// </summary>
public sealed class SprintJumpTemplate : IActionTemplate
{
    private enum Phase
    {
        Approach,
        Airborne,
        Landing,
    }

    private const float YawToleranceDeg = 5f;

    /// <summary>How far from its declared elevation a landing may settle and still count as arrived, the same 0.2 the walked-gap arm has always used and the same <c>AscendTemplate.LandingElevationTolerance</c> carries. The band it forms is one-sided; see <see cref="SegmentGeometry.IsAtEndElevation"/>.</summary>
    private const double LandingElevationTolerance = 0.2;

    private readonly PathExecutionContext _ctx;
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    private readonly double _horizDist;
    private int _tickCount;
    private Phase _phase = Phase.Approach;
    private bool _leftGround;

    /// <summary>Creates a sprint-jump template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public SprintJumpTemplate(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);
        _ctx = ctx;
        _segment = segment;
        _budget = ctx.Budget.BudgetFor(segment);
        ExpectedStart = segment.Start;
        ExpectedEnd = segment.End;
        double dx = segment.End.X - segment.Start.X;
        double dz = segment.End.Z - segment.Start.Z;
        _horizDist = Math.Sqrt((dx * dx) + (dz * dz));
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

        float targetYaw = SegmentGeometry.CalculateYaw(dx, dz);
        float targetPitch = SegmentGeometry.CalculatePitch(dx, dy, dz);
        float yaw = _tickCount == 1 ? targetYaw : SegmentGeometry.SmoothYaw(physics.Yaw, targetYaw);
        float pitch = SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch);

        var input = new MovementInput();
        double yawDelta = SegmentGeometry.HeadingPenaltyDegrees(yaw, _segment.HeadingX, _segment.HeadingZ);

        // A parkour whose gap the body simply WALKED, which is not a hypothetical: a 0.6-wide collider spans a 1-wide gap in two ticks and never loses support, so the course's D1 row (a one-column gap the planner offers as a Parkour) is crossed on foot. Without this arm the template then deadlocked outright - the jump gate can no longer fire, because the bot is standing on a wide platform and HasSupportUnderFootprint is true forever; Phase.Landing is only reachable through _leftGround; and ShouldComplete is only consulted in Phase.Landing. Measured on D1: the parkour segment ran its full 200-tick budget and held (7.9582, 100.0000, 193.9033) for the last 186 of them, standing motionless ON its own destination block.
        //
        // Reaching the destination column at the destination elevation IS the arrival this segment was asked for, so hand it off through the grounded controller exactly as a real landing does. The check runs before the switch so the controller drives this tick rather than the next one.
        //
        // _leftGround latches with it. It is what arms the fall-abort below, and a body standing on the destination has the same business aborting on a subsequent fall as one that landed there. The elevation band is `[End.Y - 0.2, End.Y + EndElevationSlack + 0.2]`, not `|dy| < 0.2`: the destination column also rests a body at whatever its COPLANAR neighbours reach, because a 0.6-wide body standing in a 1-wide column is over them too. The slack is 0 over uniform terrain, which is every parkour pad this arm was written for.
        if (_phase == Phase.Approach
            && physics.OnGround
            && SegmentGeometry.IsAtEndElevation(pos.Y, ExpectedEnd, _segment.EndElevationSlack, LandingElevationTolerance)
            && SegmentGeometry.IsFootprintInsideTargetBlock(pos, ExpectedEnd))
        {
            _leftGround = true;
            _phase = Phase.Landing;
        }

        switch (_phase)
        {
            case Phase.Approach:
                if (physics.OnGround)
                {
                    bool turnInPlace = yawDelta > 35.0;
                    input = new MovementInput { Forward = !turnInPlace, Sprint = !turnInPlace };

                    // Jump once aligned and out of run-up. Two arms, and they cover different jumps.
                    //
                    // The short arm leaps immediately: over one or two blocks the target is already within reach from the launch block, and waiting for the ledge would only shorten the arc. Measured over 41 run-up phases, gap-1 and gap-2 land from every one of them on this arm and from none of them without it.
                    //
                    // The long arm is a coyote tick: go on the first grounded tick whose footprint is no longer fully supported, which is the last tick a jump can be taken at all and therefore the furthest into the gap the arc can start. Across 41 starting phases, this gate crossed the four-block gap in every case.
                    bool nearEdge = _horizDist <= 2.0
                        || !SegmentGeometry.HasSupportUnderFootprint(_ctx.World, pos);
                    if (yawDelta <= YawToleranceDeg && nearEdge)
                    {
                        input = input with { Jump = true, Forward = true, Sprint = true };
                        yaw = targetYaw;
                        _phase = Phase.Airborne;
                    }
                }
                else
                    _phase = Phase.Airborne;

                break;

            case Phase.Airborne:
                if (!physics.OnGround)
                {
                    // Steer the arc by where it is predicted to land, from the second airborne tick on (_leftGround latches at the end of the first one). Releasing Forward only after reaching the end plane or entering the target footprint fires too late: on the course's D7 pillar the first release tick was the APEX, t26 at y = 101.2522 with the pad's surface at y = 100, and the bot sailed over the 1x1 pad and fell past its far face. Measured over gap 1-4 x pad depth 1/2/6 x 41 run-up phases, plus a chained pillar-pillar-platform family: 325/656 open loop against 593/656 steered, and every 1x1 and 2x1 pad column at gap 1 and 2 goes 0-or-8-or-23 of 41 to 41 of 41.
                    //
                    // Second tick rather than first, exactly as AscendTemplate latches its own. Running from the first airborne tick is a wash on the total (594 against 593) and costs the perfect gap-1 depth-1 column, 39/41 against 41/41: on a short hop the first tick is still the take-off impulse and steering it re-decides a jump that has not started.
                    bool steer = _leftGround;
                    _leftGround = true;
                    if (steer)
                        input = ChooseAirborneInput(physics, yaw);

                    else
                    {
                        bool release = SegmentGeometry.HasReachedSegmentEndPlane(pos, _segment)
                            || SegmentGeometry.IsFootprintInsideTargetBlock(pos, ExpectedEnd);
                        input = release ? MovementInput.None : new MovementInput { Forward = true, Sprint = true };
                    }
                }
                else if (_leftGround)
                    _phase = Phase.Landing;

                break;

            case Phase.Landing:
            default:
                (MovementInput grounded, float plannedYaw) = GroundedSegmentController.Plan(_ctx, _segment, pos, physics, physics.Yaw);
                input = grounded;
                yaw = plannedYaw;
                break;
        }

        output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);

        // Apply the walked-gap elevation gate to the arm used after the body leaves the ground. This arm otherwise completes on ShouldComplete alone, and ShouldComplete is a POSITION test: every one of its arms asks where the body is in the destination COLUMN and not one of them asks how high it is. So a landing that reaches the right column at the wrong height must not report success. A pointed-dripstone side face can stop the body 0.6875 blocks below the planned endpoint. The same elevation band is applied by AscendTemplate so both movement families agree on acceptable landings. coplanar neighbour is accepted here exactly as it is there. A miss is a FAILURE and not a hold, because the body has already landed and the destination is up a face taller than vanilla's 0.6 step: no input this template can still issue reaches it, and a replan one tick after touchdown is strictly better than one forty ticks later from inside the wedge.
        //
        // Scoped to a GROUNDED, dry completion on purpose. ShouldComplete's first arm completes a swim or a submerged traverse off the ground on a `|dy| < 1.0` envelope, and a buoyant body has no support elevation for this band to mean anything about; those keep the gate they had.
        if (_phase == Phase.Landing && GroundedSegmentController.ShouldComplete(_segment, pos, physics))
        {
            bool restingOnAFloor = physics.OnGround && !physics.InWater;
            if (!restingOnAFloor
                || SegmentGeometry.IsAtEndElevation(
                    pos.Y, ExpectedEnd, _segment.EndElevationSlack, LandingElevationTolerance))
                return TemplateState.Complete;

            return TemplateState.Failed;
        }

        // Fell below the target without landing: abort.
        if (_leftGround && pos.Y < _segment.End.Y - 2.0 && !physics.OnGround)
            return TemplateState.Failed;

        if (_tickCount > _budget)
            return TemplateState.Failed;

        return TemplateState.InProgress;
    }

    /// <summary>Picks this airborne tick's input by forward-simulating where each candidate would land and keeping the one that best serves <see cref="AscendTemplate.IsBetterLanding"/>.</summary>
    /// <remarks>
    /// <para>FORWARD-ONLY, and that is a measured result rather than a saving. Swept side by side against <see cref="AscendTemplate"/>'s seven-candidate set (which adds Forward+Left, Forward+Right and the two pure strafes) over the same 656 runs, the two are byte-identical: 593/656 each, and the same count in every one of the sixteen gap x depth cells. That is the honest opposite of the ascend package's finding, and the reason is structural rather than lucky. The ascend needed lateral authority because axis-ordered collision resolution against the DESTINATION COLUMN'S FACE strips one axis of the sprint-jump boost during the rise; a parkour arc flies over a gap and touches nothing, so it is along-axis by construction and there is no lateral deficit to correct. Carrying four candidates that never win would be four <see cref="PhysicsSimulator.PredictLanding"/> calls a tick bought with nothing.</para>
    /// <para>Sprint is on in the forward candidate because a parkour segment is a sprint segment by construction (<c>PathSegmentBuilder</c> sets <c>PreserveSprint</c> on it and the take-off arm above emits Forward+Sprint), so the controller never quietly contradicts the plan's own taxonomy.</para>
    /// </remarks>
    private MovementInput ChooseAirborneInput(in PhysicsState physics, float plannedYaw)
    {
        // Simulate from the pose the executor is ABOUT to apply, not the one it currently holds: the driver writes the emitted yaw before it steps, and air control is applied along that yaw.
        PhysicsState start = physics with { Yaw = plannedYaw };
        int budget = PredictionTicks(physics.Position.Y - _segment.End.Y);

        ReadOnlySpan<MovementInput> candidates =
        [
            new MovementInput { Forward = true, Sprint = true },
            MovementInput.None,
            new MovementInput { Back = true },
        ];

        MovementInput best = candidates[0];
        LandingPrediction bestLanding = PhysicsSimulator.PredictLanding(
            start, _ctx.Conditions, _ctx.World, _ctx.Profile, candidates[0], budget);

        for (int i = 1; i < candidates.Length; i++)
        {
            LandingPrediction prediction = PhysicsSimulator.PredictLanding(
                start, _ctx.Conditions, _ctx.World, _ctx.Profile, candidates[i], budget);
            if (AscendTemplate.IsBetterLanding(prediction, bestLanding, ExpectedEnd, _segment.EndElevationSlack))
            {
                bestLanding = prediction;
                best = candidates[i];
            }
        }

        return best;
    }

    /// <summary>The lookahead budget for one landing prediction, derived from the arc still to come.</summary>
    /// <remarks>The same shape <see cref="DescendTemplate"/> uses - vanilla free fall accumulates about 0.04*n^2 blocks, so a drop of <paramref name="remainingDropY"/> takes roughly 5*sqrt(dy) ticks and half again covers drag - but with a floor of 8 rather than 4 ticks. A descend is already falling when its controller starts; a jump is still RISING, and on a flat parkour <paramref name="remainingDropY"/> is near zero (or negative, below the apex) for the whole first half of the arc, so a floor of 4 would time out every candidate before it touched anything and score three non-landings against each other. Eight covers the rise plus the fall of a flat one-block-high arc. The cap only bounds the cost of a candidate that never lands, because <see cref="PhysicsSimulator.PredictLanding"/> returns the moment a candidate touches down.</remarks>
    private static int PredictionTicks(double remainingDropY)
    {
        double estimate = (5.0 * Math.Sqrt(Math.Max(0.0, remainingDropY)) * 1.5) + 8.0;
        return (int)Math.Clamp(estimate, 8.0, 40.0);
    }
}
