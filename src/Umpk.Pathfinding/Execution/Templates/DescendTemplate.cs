using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>
/// Walks off a ledge and drops one or more blocks to a landing (Descend). Faces the target, walks (sprinting on long drops), and lets gravity resolve the fall; completes on a solid landing at the target or on a water landing. An airborne-yaw lock on multi-block drops prevents air control from drifting the bot off the landing column) and the water-landing completion.
/// <para>Drops of two or more steer the fall closed-loop: every airborne tick re-picks the held input from its <see cref="PhysicsSimulator.PredictLanding"/> outcome (see <c>ChooseAirborneInput</c>). A single step keeps the open-loop input, because there is barely an arc to steer.</para>
/// <para>A descent down a CLIMBABLE is neither: the planner's ladder-grab descent arrives here as an ordinary <c>Descend</c>, and on a ladder any horizontal input is a climb rather than a fall, so that arm releases the stick outright. See the comment on it.</para>
/// </summary>
public sealed class DescendTemplate : IActionTemplate
{
    private readonly PathExecutionContext _ctx;
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    private readonly bool _needsSprint;
    private int _tickCount;
    private bool _hasFallen;

    /// <summary>Creates a descend template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public DescendTemplate(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);
        _ctx = ctx;
        _segment = segment;
        _budget = ctx.Budget.BudgetFor(segment);
        ExpectedStart = segment.Start;
        ExpectedEnd = segment.End;
        double hdx = segment.End.X - segment.Start.X;
        double hdz = segment.End.Z - segment.Start.Z;
        _needsSprint = (hdx * hdx) + (hdz * hdz) > 2.25;
        _bouncyLanding = FallTemplate.LandsOnABouncyBlock(ctx, segment.End);
    }

    private readonly bool _bouncyLanding;

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
        double horizDistSq = (dx * dx) + (dz * dz);

        if (!physics.OnGround)
            _hasFallen = true;

        // Water landing near the destination.
        if (_hasFallen && physics.InWater && horizDistSq < 0.5 && Math.Abs(dy) < 2.0)
        {
            output = default;
            return TemplateState.Complete;
        }

        // Climbing back up rather than descending, or timed out.
        if (pos.Y > ExpectedStart.Y + 2.0 || _tickCount > _budget)
        {
            output = default;
            return TemplateState.Failed;
        }

        double segmentYDrop = _segment.Start.Y - _segment.End.Y;
        bool isSingleStep = segmentYDrop <= 1.0;

        float targetPitch = SegmentGeometry.CalculatePitch(dx, dy, dz);
        float yaw;

        // Sneak while airborne over a bouncy pad: vanilla's own bounce cancel (isSuppressingBounce), and it costs nothing but the approach. Released the moment the body is on the pad, so the grounded controller below plans an ordinary walk-out. It is applied as a final composition below rather than as this seed, because every airborne arm ASSIGNS input rather than amending it - and the landing predictor is given the same bit, so what it simulates is what the executor emits.
        bool sneakOffTheBounce = _bouncyLanding && !physics.OnGround;
        var input = new MovementInput();

        if (physics.OnGround && Math.Abs(dy) < (_hasFallen ? 1.0 : 0.6))
        {
            // On/at landing elevation: hand off through the grounded controller.
            (MovementInput grounded, float plannedYaw) = GroundedSegmentController.Plan(_ctx, _segment, pos, physics, physics.Yaw);
            input = grounded;
            yaw = plannedYaw;

            output = TemplateOutput.From(input, yaw, SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch), _ctx, physics);

            // A bouncy pad is not a landing until the body has stopped being thrown off it. See FallTemplate.LandsOnABouncyBlock for the measurement: ten inversions and 152 ticks without the sneak, 18 with it.
            bool settled = !_bouncyLanding || Math.Abs(physics.Velocity.Y) <= FallTemplate.BounceSettleSpeed;
            if (settled && GroundedSegmentController.ShouldComplete(_segment, pos, physics))
                return TemplateState.Complete;

            return TemplateState.InProgress;
        }

        // Airborne (or still on the launch ledge): keep yaw aligned so air control stays on the planned trajectory. Multi-block drops lock to the segment heading to avoid drifting off the landing. A single-step drop aims at the end point for lateral correction only until the body crosses the end plane. Beyond it, the point-relative direction reverses, while heading remains stable.
        //
        // The heading lock on a MULTI-block drop is an AIRBORNE device, and it is applied only while the body is airborne. It exists so air control does not drift the arc off the landing column, and on the launch surface there is no arc: there the heading is the one reference that cannot correct an overshoot, because it points the same way whether the body is short of the landing column or already past it.
        //
        // While grounded, aiming at the end point corrects overshoot toward the column centre. A centred 0.6-wide body clears a climbable's 3/16 wall plate and can enter the shaft.
        float airborneYaw = (isSingleStep ? !SegmentGeometry.HasReachedSegmentEndPlane(pos, _segment) : !_hasFallen)
            ? SegmentGeometry.CalculateYaw(dx, dz)
            : SegmentGeometry.CalculateYaw(_segment.HeadingX, _segment.HeadingZ);
        // Snap rather than smooth when the body is still grounded and has already reached the end plane, which is only ever the overshoot correction above. SmoothYaw turns at 35 degrees a tick, so a 180-degree reversal takes six ticks, and Forward is held through every one of them: the body is driven sideways for the whole sweep. Measured on the plated C11 shaft with smoothing left in, the correction converged in X and pushed the body 0.236 out in Z, onto the shaft's far rim at (714.4217, 100.0000, 138.7361) - it stopped walking off the mass and started standing on the other lip instead. There is no arc to protect here and no air control to keep continuous, so the smoothing buys nothing and costs the axis it is not steering.
        bool correctingOnTheLedge = !_hasFallen && SegmentGeometry.HasReachedSegmentEndPlane(pos, _segment);
        yaw = _tickCount == 1 || correctingOnTheLedge
            ? airborneYaw
            : SegmentGeometry.SmoothYaw(physics.Yaw, airborneYaw);

        if (physics.OnClimbable && dy < -0.1)
        {
            // A descend whose column is a LADDER: let go of the stick and let the clamp do the work.
            //
            // Climbable motion caps descent at -0.15 a tick, but horizontal collision or jumping on the same state replaces post-move Y velocity with 0.2. This is how a player climbs a ladder by walking into it. Inside a 1x1 shaft the body rests against a wall whatever it presses, so the lift fires as often as the clamp and the descent becomes a limit cycle. Measured on the course's C11 shaft: pinned at x = 714.7000, the bot bobbed between y = 97.5139 and 97.7860 for the segment's whole 200-tick budget, a net +0.0246 over the last 173 ticks, with the shaft floor 5.6 blocks below at y = 92.
            //
            // This is not a matter of picking a better horizontal candidate, which is why the arm sits ABOVE the predicted-landing branch rather than inside it: probed directly from (714.7, 99.0, 138.5), Forward pins against the +x wall and CLIMBS 0.98 blocks in ten ticks, Back crosses the shaft and climbs the same way from tick 7, and only MovementInput.None descends - 1.58 blocks in the same ten, exactly the clamp. (LadderDescentTests.PressingIntoAShaftWall_ClimbsTheLadderInsteadOfDescendingIt.) Two of ChooseAirborneInput's three candidates are lifts, so it cannot choose its way out.
            //
            // Gate this on a lower destination because a climb upward needs the lift.
            input = MovementInput.None;
        }
        else if (_hasFallen && segmentYDrop >= 2.0)
            input = ChooseAirborneInput(physics, yaw, sneakOffTheBounce);

        else if (horizDistSq > 0.01)
            input = new MovementInput { Forward = true, Sprint = _needsSprint };

        if (sneakOffTheBounce)
            input = input with { Sneak = true };

        output = TemplateOutput.From(input, yaw, SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch), _ctx, physics);
        return TemplateState.InProgress;
    }

    /// <summary>Picks this airborne tick's input by forward-simulating where each candidate would land and keeping the one that lands nearest the landing column's centre.</summary>
    /// <remarks>Holding Forward until the footprint is over the landing column and then pressing Back cannot hit the target on a drop of three or more. Landing quantises to roughly 0.22 blocks at walk speed while the footprint-inside acceptance window is 0.4 blocks wide, so on the long drops the bot arrived at or past the far rim of the column and completion could never fire again. Choosing the input by its predicted landing closes the loop instead of guessing: it reads the same engine the executor is about to step, so drag, the fall profile and the terrain under the arc are all accounted for.</remarks>
    private MovementInput ChooseAirborneInput(in PhysicsState physics, float plannedYaw, bool sneak)
    {
        // Simulate from the pose the executor is ABOUT to apply, not the one it currently holds: the driver writes the emitted yaw before it steps, and air control is applied along that yaw.
        PhysicsState start = physics with { Yaw = plannedYaw };
        int budget = PredictionTicks(physics.Position.Y - _segment.End.Y);

        // The sneak bit rides every candidate, because it changes what each of them DOES: sneaking scales the air-control input to 0.3 as well as suppressing the bounce, so simulating a candidate without it and then emitting it with it would pick the input by the wrong arc.
        ReadOnlySpan<MovementInput> candidates =
        [
            new MovementInput { Forward = true, Sprint = _needsSprint, Sneak = sneak },
            new MovementInput { Sneak = sneak },
            new MovementInput { Back = true, Sneak = sneak },
        ];

        MovementInput best = candidates[0];
        double bestError = double.PositiveInfinity;
        foreach (MovementInput candidate in candidates)
        {
            LandingPrediction prediction = PhysicsSimulator.PredictLanding(
                start, _ctx.Conditions, _ctx.World, _ctx.Profile, candidate, budget);
            double errX = prediction.LandingPosition.X - ExpectedEnd.X;
            double errZ = prediction.LandingPosition.Z - ExpectedEnd.Z;
            double error = (errX * errX) + (errZ * errZ);
            if (error < bestError)
            {
                bestError = error;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The lookahead budget for one landing prediction, derived from the fall still to come.</summary>
    /// <remarks>Vanilla free fall accumulates about 0.04*n^2 blocks after n ticks (gravity 0.08 damped by 0.98), so a fall of <paramref name="remainingFallY"/> takes roughly 5*sqrt(dy) ticks; half again covers drag and the ticks a still-rising player spends before it starts descending. The cap only bounds the cost of a candidate that never lands (an arc that misses the platform and falls past it), because <see cref="PhysicsSimulator.PredictLanding"/> returns the moment a candidate touches down.</remarks>
    private static int PredictionTicks(double remainingFallY)
    {
        double estimate = (5.0 * Math.Sqrt(Math.Max(0.0, remainingFallY)) * 1.5) + 4.0;
        return (int)Math.Clamp(estimate, 4.0, 40.0);
    }
}
