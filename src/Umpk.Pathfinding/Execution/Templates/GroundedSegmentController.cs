using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>The grounded-move handoff logic shared by walk/ascend/descend templates. <see cref="Plan"/> returns the desired <see cref="MovementInput"/> and yaw, and <see cref="ShouldComplete"/> reads the snapshot. Adds an ungrounded (swim) completion path so a water segment can complete off the ground on a position envelope. Internal (visible to tests).</summary>
internal static class GroundedSegmentController
{
    private const double FinalStopFastCompleteSpeed = 0.08;

    /// <summary>Computes this tick's grounded input and the yaw to face, given a braking decision.</summary>
    internal static (MovementInput Input, float Yaw) Plan(
        PathExecutionContext ctx, PathSegment segment, in Vec3d pos, in PhysicsState physics, float currentYaw)
    {
        TransitionBrakingDecision decision = ctx.Braking.Plan(segment, pos, physics);

        var input = new MovementInput
        {
            Forward = decision.HoldForward,
            Sprint = decision.HoldSprint,
            Back = decision.HoldBack,
        };

        float segmentYaw = SegmentGeometry.CalculateYaw(segment.HeadingX, segment.HeadingZ);
        if (decision.HoldBack)
        {
            // Stay on the segment heading while braking so the back input pushes straight back, not off the segment line onto a narrow walkway edge.
            return (input, SegmentGeometry.SmoothYaw(currentYaw, segmentYaw));
        }

        // Once the brake releases, bias toward the exit heading when close to the end (unless it is a safe stable-footing turn, where the next segment snaps yaw itself).
        SegmentGeometry.GetExitHeading(segment, out int exitX, out int exitZ);
        bool biasToExit = !IsSafeStableTurn(segment)
            && (exitX != segment.HeadingX || exitZ != segment.HeadingZ)
            && SegmentGeometry.RemainingDistanceAlongSegment(pos, segment) <= 0.35;

        // Anti-stall: a coast decision can stop the bot short of the target block. If it is nearly stationary and its footprint is not yet inside the destination, creep forward along the segment heading so the segment finishes instead of idling until the timeout.
        if (!input.Forward
            && SegmentGeometry.HorizontalSpeed(physics) < 0.03
            && !SegmentGeometry.IsFootprintInsideTargetBlock(pos, segment.End))
            input = input with { Forward = true };

        float targetYaw = biasToExit ? SegmentGeometry.CalculateYaw(exitX, exitZ) : segmentYaw;
        return (input, SegmentGeometry.SmoothYaw(currentYaw, targetYaw));
    }

    /// <summary>Whether the segment has reached its completion criteria this tick.</summary>
    internal static bool ShouldComplete(PathSegment segment, in Vec3d pos, in PhysicsState physics)
    {
        // Swim/ungrounded completion: complete on a position envelope, never requiring OnGround. Also applies to any grounded move whose execution is currently in water (a submerged Traverse the planner picked at a pool bottom), since the buoyant player is never OnGround there.
        if (segment.ExitHints.AllowUngrounded || segment.IsWaterSegment || (physics.InWater && !physics.OnGround))
            return SegmentGeometry.IsCenterInsideTargetBlock(pos, segment.End)
                && Math.Abs(segment.End.Y - pos.Y) < 1.0;

        if (segment.ExitHints.RequireGrounded && !physics.OnGround)
            return false;

        if (segment.ExitTransition == PathTransitionType.ContinueStraight && physics.OnGround)
        {
            if (SegmentGeometry.IsFootprintInsideTargetBlock(pos, segment.End)
                && !SegmentGeometry.WillLeaveTargetBlockNextTick(pos, physics, segment.End))
                return true;

            if (SegmentGeometry.IsSettledAtEnd(pos, segment.End, physics))
                return true;

        }

        if (segment.ExitTransition == PathTransitionType.FinalStop
            && physics.OnGround
            && SegmentGeometry.IsFootprintInsideTargetBlock(pos, segment.End)
            && !SegmentGeometry.WillLeaveTargetBlockNextTick(pos, physics, segment.End))
            return SegmentGeometry.HorizontalSpeed(physics) <= FinalStopFastCompleteSpeed;

        // LandingRecovery accepts a decelerated handoff the moment the bot reaches the target column.
        if (segment.ExitTransition == PathTransitionType.LandingRecovery
            && physics.OnGround
            && !segment.ExitHints.RequireStableFooting)
        {
            if (SegmentGeometry.IsFootprintInsideTargetBlock(pos, segment.End))
                return true;

            // The footprint test alone is one-sided: it is a 0.4-block-wide window (a 0.6-wide footprint inside a 1-wide column) that the bot gets exactly one landing to hit, and a landing that arrives past it can never satisfy it again. Worse, nothing after that pushes the bot back: the braking planner's speed envelope prefers carrying forward, and the anti-stall creep in Plan forces Forward precisely BECAUSE the footprint is outside the destination. So an overshoot drove the whole platform and failed on the tick budget.
            bool continuesOnTheSameHeading = segment.ExitHints.DesiredHeadingX == segment.HeadingX
                && segment.ExitHints.DesiredHeadingZ == segment.HeadingZ;

            // Overshooting into a walk that carries on along the same heading IS the handoff: the bot is past the end plane, still on the segment's line, and the next template steers from here. The lateral gate is what keeps this honest, so a landing that slid sideways off the line (onto a rim, or off a narrow walkway) is not accepted as arrival.
            if (continuesOnTheSameHeading
                && SegmentGeometry.HasReachedSegmentEndPlane(pos, segment)
                && SegmentGeometry.LateralOffsetFromSegmentLine(pos, segment) <= 0.5)
                return true;

            // A turn cannot use the end plane: past it the bot is no longer heading anywhere the next segment wants, and "steer back toward the end point" is not available either, because the braking decision is taken independently of the yaw and inverts the correction whenever it asks for Back. Accept the weaker center-inside test instead, which still puts the bot on the destination block, just not with the whole footprint clear of the rim.
            if (!continuesOnTheSameHeading && SegmentGeometry.IsCenterInsideTargetBlock(pos, segment.End))
                return true;

        }

        double exitSpeed = SegmentGeometry.ProjectSpeedAlongHeading(physics, DesiredHeadingX(segment), DesiredHeadingZ(segment));
        double headingPenalty = SegmentGeometry.HeadingPenaltyDegrees(physics.Yaw, DesiredHeadingX(segment), DesiredHeadingZ(segment));
        bool headingReady = headingPenalty <= (segment.ExitHints.RequireJumpReady ? 8.0 : 15.0);
        if (!headingReady
            && !IsSafeStableTurn(segment)
            && segment.ExitTransition is PathTransitionType.Turn or PathTransitionType.PrepareJump)
            return false;

        if (segment.ExitTransition == PathTransitionType.PrepareJump
            && physics.OnGround
            && segment.ExitHints.RequireJumpReady
            && SegmentGeometry.IsCenterInsideTargetBlock(pos, segment.End))
            return true;

        if (exitSpeed < segment.ExitHints.MinExitSpeed || exitSpeed > segment.ExitHints.MaxExitSpeed)
            return false;

        if (segment.ExitHints.RequireStableFooting)
            return physics.OnGround
                && (segment.ExitTransition == PathTransitionType.FinalStop
                    ? SegmentGeometry.IsSettledAtEnd(pos, segment.End, physics)
                    : SegmentGeometry.IsSettledOnTargetBlock(pos, segment.End, physics));

        return segment.ExitTransition switch
        {
            PathTransitionType.ContinueStraight => SegmentGeometry.IsNear(pos, segment.End, horizThresholdSq: 0.09),
            PathTransitionType.PrepareJump => SegmentGeometry.HasReachedSegmentEndPlane(pos, segment) && exitSpeed > 0.02,
            PathTransitionType.FinalStop => physics.OnGround && SegmentGeometry.IsSettledAtEnd(pos, segment.End, physics),

            // A Turn that reaches here is a ROLLING turn: the stable-footing turns were answered by the RequireStableFooting block above, so the only Turn left is the jump-ready one, the corner whose second move away is an Ascend or a Parkour and which is meant to carry momentum through rather than stop on it. Asking it to SETTLE was a contradiction, and a provable one: PathSegmentBuilder gives it MinExitSpeed 0.05 along the exit heading, the exitSpeed gate a few lines up enforces that, and IsSettledOnTargetBlock caps the horizontal speed at sqrt(0.0016) = 0.04. A projection along a heading can never exceed the horizontal speed, so the two gates had no common solution at any speed, position, or geometry. The segment therefore consumed its full tick budget every time.
            //
            // Position is the right test for it, exactly as it is for PrepareJump. Everything that makes the handoff safe has already been asked: the body is grounded, its yaw is within 8 degrees of the exit heading (RequireJumpReady tightens the heading gate above), and its speed along that heading is inside [MinExitSpeed, MaxExitSpeed]. What is left is "is it on the block", and the centre test is the one a rolling corner can satisfy - the footprint test is a 0.4-block window that a body already steering onto the exit heading leaves before it enters, which is how the fixture row ends up pressed into the riser at z = 2.0 - halfWidth.
            PathTransitionType.Turn => physics.OnGround && SegmentGeometry.IsCenterInsideTargetBlock(pos, segment.End),

            _ => physics.OnGround && SegmentGeometry.IsSettledOnTargetBlock(pos, segment.End, physics),
        };
    }

    /// <summary>A turn handed off from a full stop on the target block: the bot brakes to stable footing and the NEXT template snaps its own yaw on its first tick, so this segment neither steers toward the exit heading nor waits for it.</summary>
    /// <remarks>Both halves of that contract must read the same predicate. <see cref="Plan"/> suppresses the exit-heading bias here, so the yaw provably stays on the CURRENT segment heading (the walk template pins it there once the bot's center is inside the target block). While <see cref="ShouldComplete"/> still demanded a yaw within 15 degrees of the NEXT heading for every Turn, the two contradicted each other and a turn of more than 15 degrees could never complete: the bot arrived, braked, sat on the target block and burned the segment's tick budget. A 90-degree diagonal-to-diagonal turn deadlocked exactly that way.</remarks>
    private static bool IsSafeStableTurn(PathSegment segment)
        => segment.ExitTransition == PathTransitionType.Turn
            && segment.ExitHints.RequireStableFooting
            && !segment.ExitHints.RequireJumpReady;

    private static int DesiredHeadingX(PathSegment segment)
    {
        SegmentGeometry.GetExitHeading(segment, out int headingX, out _);
        return headingX;
    }

    private static int DesiredHeadingZ(PathSegment segment)
    {
        SegmentGeometry.GetExitHeading(segment, out _, out int headingZ);
        return headingZ;
    }
}
