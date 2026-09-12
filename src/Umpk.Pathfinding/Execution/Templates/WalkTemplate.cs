using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>
/// Walks (or sprints) toward a destination on the same Y level: Traverse and Diagonal moves. Faces the target, holds forward/sprint via the grounded controller, and completes when settled on the destination block. It emits a <see cref="TemplateOutput"/> (input + yaw/pitch + a one-tick forward-simulated expected state) instead of mutating a live engine.
/// <para>One exception to "faces the target": on the last block of an approach into a jump that leaves on a different heading, the yaw steers to the EXIT heading instead, because that is the only place the turn the jump-ready handoff demands can still be made. See the comment on <c>targetYaw</c>.</para>
/// </summary>
public sealed class WalkTemplate : IActionTemplate
{
    private readonly PathExecutionContext _ctx;
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    /// <summary>The consecutive-stuck-tick limit for that budget.</summary>
    private readonly int _stuckLimit;
    private int _tickCount;
    private Vec3d _lastPos;
    private int _stuckTicks;
    private int _airborneTicks;

    /// <summary>Consecutive stuck ticks after which a SUBMERGED walk gives up <c>Sprint</c> so the body can sink onto the floor it is supposed to be walking on.</summary>
    /// <remarks>
    /// <para><b>Sprinting in water suppresses passive sinking.</b> A submerged bottom-walk handed over above its floor therefore holds its height for ever, keeps its head inside the ceiling band, and burns the segment in place.</para>
    /// <para>Course row E30 measures only 0.0025 blocks of sinking per tick while wedged. Releasing <c>Sprint</c> restores a 0.025-block passive sink, so half a block of settling costs about twenty ticks instead of never finishing.</para>
    /// <para><b>Gated on being stuck.</b> A body being carried down a current is above its floor on most ticks and needs its sprint; a body that is WEDGED is not moving at all. Making no horizontal progress is the difference between them, and it is a condition this template already tracks.</para>
    /// <para>Four ticks. Long enough that a body merely bobbing over its floor while it travels never trips it, short enough that the twenty-odd ticks of sinking still fit inside the segment budget that the wedge would otherwise burn.</para>
    /// </remarks>
    private const int SubmergedSettleStuckTicks = 4;

    /// <summary>How far above its own floor a submerged walk has to be for the sink to be worth taking.</summary>
    /// <remarks>A tenth of a block: a settled submerged walk reads the floor Y exactly, so this never fires on a body that is already down, and a body handed over half a block high is caught.</remarks>
    private const double SubmergedSettleThreshold = 0.1;

    /// <summary>Creates a walk template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public WalkTemplate(PathExecutionContext ctx, PathSegment segment)
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

        // Steer at the destination until the whole FOOTPRINT is inside its column, then pin the yaw to the segment heading and coast out along the path.
        //
        // The gate uses the whole footprint. An off-axis body can have its centre inside the destination
        // while its footprint still straddles the previous block; switching headings there can drive it
        // back out and oscillate until the segment budget expires.
        float targetYaw = SegmentGeometry.IsFootprintInsideTargetBlock(pos, _segment.End)
            ? SegmentGeometry.CalculateYaw(_segment.HeadingX, _segment.HeadingZ)
            : SegmentGeometry.CalculateYaw(dx, dz);

        // ...except on the last block before a jump, where the segment heading is the one thing the yaw must NOT be on. A segment whose exit is a jump entry that leaves on a DIFFERENT heading has to hand off within 8 degrees of that heading (GroundedSegmentController.ShouldComplete's RequireJumpReady gate), and the launch column is the last place the turn can be made. Pinned to the segment heading it never got there: on the course's D1 gap the penalty bottomed out at 40.18 degrees and the bot walked off the ledge with Forward still held, and on H2 it sat at 45.00 against the riser for 22 ticks until the stuck counter ended the segment. The same heading error causes a fall on an open ledge and a stall against a wall.
        //
        // Aim along the exit HEADING rather than at the exit point. An end-point aim inverts when the body crosses the end plane, while the target inside the launch column is a direction with no point to cross.
        //
        // CENTRE-inside, not footprint-inside, and that is load-bearing rather than a nicety: a diagonal approach enters its launch column corner-on and its footprint may never be fully inside it at all (D8's shape). Swept over 41 run-up phases into a 45-degree jump exit, the footprint gate held 0/41 and the centre gate 41/41.
        //
        // RequireJumpReady is what scopes this, and it excludes IsSafeStableTurn by construction (that predicate requires the flag to be false). A stable-footing turn hands the yaw to the NEXT template, which snaps it on its own first tick; steering to the exit heading here would put Plan, ShouldComplete and this template back into the disagreement SegmentTurnHandoffTests pins. ...and except in a doorway, where the cell centre is the WRONG place to aim. An open door's panel leaves a 0.6-wide body 0.0125 blocks of clearance on its side and 0.2 on the far side, and the measured pass band is a lateral offset of 0.5 to 0.7. A two-degree yaw error toward the panel already prevents passage. Aiming at the centre puts the body on the band's very edge: 0.5000 with nothing to spare.
        //
        // Use a pure-pursuit lane hold rather than an end-point aim: look one block ahead ALONG the segment heading, at the lane's biased lateral coordinate. That target travels with the body, so it cannot invert when the body crosses the end plane, and it collapses to exactly the segment heading once the body is on the lane.
        if (_segment.Crossing is { } crossing)
        {
            double laneX = pos.X + (_segment.HeadingX * BarrierCrossing.LaneLookaheadBlocks);
            double laneZ = pos.Z + (_segment.HeadingZ * BarrierCrossing.LaneLookaheadBlocks);
            if (crossing.PanelX != 0)
                laneX = _segment.End.X + (crossing.FreeX * BarrierCrossing.FreeSideBias);

            else
                laneZ = _segment.End.Z + (crossing.FreeZ * BarrierCrossing.FreeSideBias);

            targetYaw = SegmentGeometry.CalculateYaw(laneX - pos.X, laneZ - pos.Z);
        }

        SegmentGeometry.GetExitHeading(_segment, out int exitX, out int exitZ);
        bool turningJumpExit =
            _segment.ExitHints.RequireJumpReady
            && _segment.ExitTransition is PathTransitionType.PrepareJump or PathTransitionType.Turn
            && (exitX != _segment.HeadingX || exitZ != _segment.HeadingZ);
        if (turningJumpExit && SegmentGeometry.IsCenterInsideTargetBlock(pos, _segment.End))
            targetYaw = SegmentGeometry.CalculateYaw(exitX, exitZ);

        float targetPitch = SegmentGeometry.CalculatePitch(dx, dy, dz);

        (MovementInput input, float plannedYaw) = GroundedSegmentController.Plan(_ctx, _segment, pos, physics, physics.Yaw);

        // A submerged walk that has stopped moving and is above its own floor: let go of Sprint so gravity comes back and the body can settle onto the floor it is meant to be walking on. See SubmergedSettleStuckTicks.
        if (input.Sprint
            && physics.InWater
            && !physics.OnGround
            && _stuckTicks >= SubmergedSettleStuckTicks
            && pos.Y - _segment.End.Y > SubmergedSettleThreshold)
            input = input with { Sprint = false };

        // Snap yaw on the first tick so forward input does not push while the bot is still rotating from a stale orientation (narrow lanes cannot tolerate sideways drift).
        float yaw = _tickCount == 1 ? targetYaw : SegmentGeometry.SmoothYaw(plannedYaw, targetYaw, SegmentGeometry.MaxYawStepPerTick);
        float pitch = SegmentGeometry.SmoothPitch(physics.Pitch, targetPitch);

        output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);

        if (GroundedSegmentController.ShouldComplete(_segment, pos, physics))
            return TemplateState.Complete;

        double movedSq = HorizontalDistanceSq(pos, _lastPos);
        _stuckTicks = movedSq < _ctx.Budget.StuckMovedSquared(physics.InWater) ? _stuckTicks + 1 : 0;
        _lastPos = pos;

        // A grounded move that goes airborne for more than a few ticks has stepped off the platform. In water the player floats (never OnGround), so buoyancy is not a failure there: only count airborne ticks on dry land.
        _airborneTicks = physics.OnGround || physics.InWater ? 0 : _airborneTicks + 1;
        if (_airborneTicks > 8)
            return TemplateState.Failed;

        if (_stuckTicks > _stuckLimit || _tickCount > _budget)
            return TemplateState.Failed;

        return TemplateState.InProgress;
    }

    private static double HorizontalDistanceSq(in Vec3d a, in Vec3d b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
