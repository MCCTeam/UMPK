using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>Swims through a water volume toward the destination (Swim). Holds forward+sprint (the physics engine enters the swim pose and <c>TravelInWater</c> automatically when sprinting underwater), aims the yaw at the destination with a crab angle that cancels the current's cross-track push, and aims the pitch either at the destination or straight up the escape column while the segment still has to rise. Completion is a non-grounded position envelope: the swimmer is never <c>OnGround</c>, so the segment completes when the center reaches the destination column at the destination Y while still in water.</summary>
public sealed class SwimTemplate : IActionTemplate
{
    /// <summary>The vertical error, in blocks, past which the template treats the segment as still climbing.</summary>
    private const double VerticalIntentThreshold = 0.25;

    /// <summary>The pitch that looks straight up: the escape column's own direction.</summary>
    private const float EscapeColumnPitch = -90f;

    /// <summary>The rise a SEGMENT has to declare before the escape-column aim applies at all, in blocks.</summary>
    private const double RisingSegmentThreshold = 0.5;

    /// <summary>The horizontal acceleration one tick of <c>Forward</c> buys in water, in blocks per tick squared: <see cref="PhysicsConstants.WaterBaseSpeed"/> (0.02) through <c>MapInput</c>'s 0.98, i.e. 0.0196 exactly.</summary>
    /// <remarks>It is the same number with and without <c>Sprint</c>. Sprint does not change the thrust in water; it flips the slow-down from 0.8 to 0.9, which changes the TERMINAL speed and cancels out of the crab angle, because the crab is the ratio of two accelerations that both pass through the same <c>1 / (1 - d)</c>.</remarks>
    private const double WaterInputAcceleration = 0.0196;

    /// <summary>The largest angle the crab will hold off the track, in degrees.</summary>
    private const double MaxCrabDegrees = 60.0;

    /// <summary>How hard the crab leans back onto the segment's own line, in sine of aim angle per block of lateral error.</summary>
    /// <remarks>The feed-forward angle below is exact in DIRECTION and only nominal in MAGNITUDE, because the push a body receives is the cell flows AVERAGED over the cells its box overlaps, with a cell whose fluid surface is near the feet scaled by the depth above them (<c>water-contact processing</c>'s depth term). Measured: a body resting 0.008 blocks below a cell boundary overlaps three water cells instead of two and feels 67% of the nominal 0.014, so a crab built on the nominal over-corrects and walks upstream off the line. This term closes that residual. At 1.0 the cross-track error decays with a time constant of about five ticks, well inside the 35-degree-a-tick yaw rate, and it is zero in still water.</remarks>
    private const double LineHoldGain = 1.0;

    private readonly PathExecutionContext _ctx;
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    /// <summary>The consecutive-stuck-tick limit for that budget.</summary>
    private readonly int _stuckLimit;
    private readonly bool _segmentRises;
    private readonly bool _segmentSinks;
    private int _tickCount;
    private Vec3d _lastPos;
    private int _stuckTicks;

    /// <summary>Creates a swim template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public SwimTemplate(PathExecutionContext ctx, PathSegment segment)
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
        _segmentRises = segment.End.Y - segment.Start.Y > RisingSegmentThreshold;
        _segmentSinks = segment.Start.Y - segment.End.Y > RisingSegmentThreshold;
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
        double dy = ExpectedEnd.Y - pos.Y;
        double dz = ExpectedEnd.Z - pos.Z;
        bool climbing = dy > VerticalIntentThreshold;

        float targetYaw = SegmentGeometry.CalculateYaw(dx, dz) + (float)CrabDegrees(pos, dx, dz);

        // Aim up the escape column, not at the far end of it. On a segment that rises, pointing at the destination costs vertical speed for nothing: MoveRelative is YAW-only (PlayerPhysics.GetInputVector), so pitch never steers the horizontal thrust, and the only thing pitch does in the swim pose is set the target of movement update's look-angle blend (`v += (lookY - v) * 0.06`). Looking at a destination one up and one over asks that blend for 0.707 of the climb rate and buys nothing back. Measured over a ten-segment 1-up-1-over rise in open water: 5.900 ticks a block aiming at the destination.
        float targetPitch = _segmentRises && climbing
            ? EscapeColumnPitch
            : SegmentGeometry.CalculatePitch(dx, dy, dz);

        float yaw = _tickCount == 1 ? targetYaw : SegmentGeometry.SmoothYaw(physics.Yaw, targetYaw);
        float pitch = SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch);

        // Sprint underwater triggers the swim pose and TravelInWater buoyancy handling. There is deliberately NO Sneak arm: sneaking gives ZERO downward impulse (goDownInWater is unimplemented and PhysicsConstants.WaterDescendImpulse is dead), and it cuts the thrust to 30% of 0.0196, which raises the current-to-thrust ratio from 0.714 to 2.381. Above 1.0 a current wins outright: the swimmer travels BACKWARDS at up to 0.081 blocks a tick, which is four times the stuck detector's floor, so the segment burns its whole budget going the wrong way and then reports Failed rather than stuck. The dive it was meant to drive is the pitch's job, and CalculatePitch already points down whenever the destination is below.
        //
        // ...except that "underwater" is a real precondition and not a figure of speech, and a dive that has to START at the surface does not meet it. See SinkingIn.
        bool sinking = SinkingIn(physics, dy);
        var input = new MovementInput { Forward = true, Sprint = !sinking };
        if (climbing)
        {
            // Ascend/surface: add upward intent (fluid jump impulse when in water).
            input = input with { Jump = true };
        }

        output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);

        // Non-grounded completion: reached the destination column at the destination Y, still in water.
        bool reached = SegmentGeometry.IsCenterInsideTargetBlock(pos, _segment.End) && Math.Abs(dy) < 0.6;
        if (reached)
            return TemplateState.Complete;

        double movedSq = HorizontalDistanceSq(pos, _lastPos);
        double movedY = Math.Abs(pos.Y - _lastPos.Y);
        _stuckTicks = movedSq < _ctx.Budget.StuckMovedSquared(inWater: true) && movedY < 0.002 ? _stuckTicks + 1 : 0;
        _lastPos = pos;

        if (_stuckTicks > _stuckLimit || _tickCount > _budget)
            return TemplateState.Failed;

        return TemplateState.InProgress;
    }

    /// <summary>Whether this tick has to get the body UNDER the surface before the swim pose can do anything, in which case <c>Sprint</c> must be released.</summary>
    /// <remarks>
    /// <para><b>Sprinting in water cancels gravity.</b> Fluid movement leaves velocity unchanged for a sprinting entity, as <c>PlayerPhysics.GetFluidFallingAdjustedMovement</c> implements: <c>if (baseGravity == 0.0 || _sprinting) return movement;</c>), so a sprinting body in water neither sinks nor is pulled down at all.</para>
    /// <para><b>And the swim pose needs the EYE under water.</b> <c>PlayerPhysics.IsSwimmingState</c> requires both sprinting and an eye below the surface. A body floating at the surface - dropped into a shaft mouth, or arriving at a lid hole - has its eye in air, so <c>Sprint</c> earns it no pose, no <c>PlayerPhysics.ApplySwimSteer</c> look-angle blend, and no downward authority whatsoever, while simultaneously deleting the passive sink that WOULD have taken it under. The two clauses together pin a surface float in place permanently.</para>
    /// <para>Measured in course row E4's own entry shaft, body at y=103.86 with the water surface at y=105.0. Holding <c>Forward+Sprint</c> with the pitch straight down: <c>vy</c> decays -0.0040, -0.0032, -0.00256 and then reads exactly <c>0.00000</c> from tick 3 onward, with <c>y = 103.8478</c> on all 200 samples of a 200-tick budget. Releasing <c>Sprint</c>: the body sinks at 0.025 blocks a tick, its eye submerges on tick 28, and from there the pose engages and the ordinary dive runs at 2.6 ticks a block. That is the whole of course row E4's <c>Segment 2/91 failed (Swim) after 41 ticks</c>, and it is why the tick budget was never the thing to raise.</para>
    /// <para>Scoped by the SEGMENT's own declared drop (<c>_segmentSinks</c>, the mirror of <c>_segmentRises</c>) and not by the live vertical error alone, for the same reason the escape-column pitch is. On a level or rising swim <c>Sprint</c> is what doubles the terminal horizontal speed (the 0.8-to-0.9 slow-down flip), and a body that merely ENTERED a level segment a block high still reads a negative <c>dy</c>: gating on that alone releases <c>Sprint</c> for the whole of an ordinary seven-block crossing and halves it. Measured on <c>SwimUpstreamFromAboveTheLine_CompletesInsteadOfBurningItsBudget</c>, whose top water layer is a level-7 film one ninth of a block deep so the swimmer's eye rides above it: the live-error gate covered 5.3768 of its 7 blocks before its budget expired, and the segment gate completes it unchanged.</para>
    /// </remarks>
    private bool SinkingIn(in PhysicsState physics, double dy)
        => _segmentSinks && dy < -VerticalIntentThreshold && !physics.IsUnderWater;

    /// <summary>The angle to hold off the track so that thrust plus current runs ALONG the track, in degrees.</summary>
    /// <remarks>
    /// <para>Pure pursuit re-aims at the destination every tick, which corrects a cross-current only after it has already pushed the swimmer off the line; over a ten-block crossing of a sheet that is a measured 1.6062 blocks of lateral excursion, and it is what walks a bot off a walkway.</para>
    /// <para>The angle is exact rather than a gain. Write the track unit vector <c>t</c> and the cell's unit flow <c>f</c>; the cross-track components of thrust and push are <c>a * sin(theta)</c> and <c>c * cross(t, f)</c>, and both pass through the same <c>1/(1-d)</c> slow-down, so the damping (and therefore <c>Sprint</c>, and therefore the era) cancels and the balance is <c>sin(theta) = -(c / a) * cross(t, f)</c>. With <c>c = 0.014</c> and <c>a = 0.0196</c> the ratio is 0.714, so a dead-90-degree sheet asks for 45.6 degrees of crab and the 60-degree clamp is a guard against a stacked flow rather than a working limit.</para>
    /// <para>The flow is sampled at the swimmer's own feet cell: one <c>GetBlock</c> plus its four neighbours, once a tick. The engine averages the flow over every cell the body overlaps, so this is the direction rather than the exact impulse; it is the same approximation the planner's cost model makes, deliberately, so the two cannot disagree.</para>
    /// </remarks>
    private double CrabDegrees(in Vec3d pos, double dx, double dz)
    {
        double trackLength = Math.Sqrt((dx * dx) + (dz * dz));
        if (trackLength < 1.0E-4)
            return 0.0;

        Vec3d flow = PlayerPhysics.GetWaterFlow(
            _ctx.World, BlockPos.Containing(pos.X, pos.Y, pos.Z));
        if (flow.X == 0.0 && flow.Z == 0.0)
            return 0.0;

        double tx = dx / trackLength;
        double tz = dz / trackLength;
        double crossFlow = (tx * flow.Z) - (tz * flow.X);
        double sinTheta = -(PhysicsConstants.WaterPushScale * crossFlow) / WaterInputAcceleration;
        sinTheta -= LineHoldGain * LateralOffset(pos);

        double clamp = Math.Sin(MaxCrabDegrees * Math.PI / 180.0);
        sinTheta = Math.Clamp(sinTheta, -clamp, clamp);
        return Math.Asin(sinTheta) * 180.0 / Math.PI;
    }

    /// <summary>How far the swimmer has been pushed off the segment's own line, signed the same way the crab angle is (positive when the body sits to the left of the line looking along it).</summary>
    private double LateralOffset(in Vec3d pos)
    {
        double lineX = _segment.End.X - _segment.Start.X;
        double lineZ = _segment.End.Z - _segment.Start.Z;
        double lineLength = Math.Sqrt((lineX * lineX) + (lineZ * lineZ));
        if (lineLength < 1.0E-4)
            return 0.0;

        double ux = lineX / lineLength;
        double uz = lineZ / lineLength;
        double wx = pos.X - _segment.Start.X;
        double wz = pos.Z - _segment.Start.Z;
        return (ux * wz) - (uz * wx);
    }

    private static double HorizontalDistanceSq(in Vec3d a, in Vec3d b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
