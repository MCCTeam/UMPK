using Umpk.Pathfinding.Execution.Templates;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>The tuning of a <see cref="StationController"/>. A record rather than a set of constants so every part of the controller can be ABLATED individually in a test and shown to be load-bearing; the executor always builds <see cref="Default"/>.</summary>
/// <param name="DeadbandBlocks">The cross-track error below which nothing is pressed. See <see cref="StationController.CrossTrackDeadbandBlocks"/>.</param>
/// <param name="AxisDeadband">The magnitude the world-space correction must project onto the body's STRAFE axis before that axis is pressed. See <see cref="StationController.StrafeAxisDeadband"/>.</param>
/// <param name="ReplanBlocks">The cross-track error at which the executor starts asking for a replan. See <see cref="StationController.CrossTrackReplanBlocks"/>.</param>
/// <param name="ReplanTicks">How many consecutive ticks the error must stay past <paramref name="ReplanBlocks"/> before the executor asks. See <see cref="StationController.CrossTrackReplanTicks"/>.</param>
/// <param name="RequireSurfaceWater">Whether the controller is scoped to a body WADING - in water with its head in air. See <see cref="StationController"/>'s remarks for the live A/B that put the head test there.</param>
internal readonly record struct StationControllerOptions(
    double DeadbandBlocks,
    double AxisDeadband,
    double ReplanBlocks,
    int ReplanTicks,
    bool RequireSurfaceWater)
{
    /// <summary>The default tuning used by <see cref="PathExecutor"/>.</summary>
    internal static StationControllerOptions Default => new(
        StationController.CrossTrackDeadbandBlocks,
        StationController.StrafeAxisDeadband,
        StationController.CrossTrackReplanBlocks,
        StationController.CrossTrackReplanTicks,
        RequireSurfaceWater: true);

    /// <summary>A disabled controller. Infinite thresholds suppress input and replanning.</summary>
    /// <remarks>Tests use this option to compare behavior with and without station correction.</remarks>
    internal static StationControllerOptions Disabled => new(
        double.PositiveInfinity,
        StationController.StrafeAxisDeadband,
        double.PositiveInfinity,
        StationController.CrossTrackReplanTicks,
        RequireSurfaceWater: true);
}

/// <summary>The lateral half of an executing segment: the authority to hold the body on the LINE its segment was planned along, while the template drives it forward along that same line.</summary>
/// <remarks>
/// <para>While a segment executes, a current can push the body sideways. This controller corrects that drift without changing the template's along-track input.</para>
/// <para><b>What it corrects, and what it must never touch.</b> The body's horizontal error is decomposed in the SEGMENT's frame. The along-track component is the segment's own business and is never touched: this controller adds, removes and flips nothing but <see cref="MovementInput.Left"/> and <see cref="MovementInput.Right"/>. Only the cross-track component - the perpendicular distance from the start-to-end line, which <see cref="SegmentGeometry.LateralOffsetFromSegmentLine"/> already computes - is corrected.</para>
/// <para><b>Why the correction is a strafe and not a heading.</b> Steering the yaw would be the other way to close a lateral error, and it is the wrong one here: the yaw is what the completion gates read (<c>GroundedSegmentController.ShouldComplete</c>'s heading penalty, and <c>SprintJumpTemplate</c>'s jump gate). A strafe does not change those headings.</para>
/// <para><b>Why the forward axis of the correction is DISCARDED rather than pressed.</b> The world-space correction is rotated into the body frame by the yaw the template is emitting THIS tick, and only the strafe axis is kept. When the yaw happens to point along the correction the projection onto the strafe axis is small and the controller presses nothing - which is right, because pressing there would be an along-track press, and along-track is the segment's. That makes "it cannot fight the segment's own forward motion" a property of the construction rather than a hope.</para>
/// <para><b>Why it is scoped to a WADE - in water, head in air.</b> On dry land the drift a segment shows is produced by the template's own steering and momentum, and a strafe there would fight the controller that produced it. A swimmer is also out of scope because lateral correction conflicts with three-dimensional steering. <see cref="StationControllerOptions.RequireSurfaceWater"/> exists so a test can ablate the gate and show it is load-bearing.</para>
/// <para>The controller anchors on the segment line rather than a point. A point anchor is unsuitable while the body is expected to keep moving along the segment.</para>
/// </remarks>
internal sealed class StationController
{
    /// <summary>The cross-track error below which nothing is pressed, in blocks.</summary>
    /// <remarks>A quarter of the 0.6-wide body's clearance in a 1-wide lane: the body has 0.2 blocks either side of the lane centre before a face touches the column boundary, and 0.15 leaves the correction armed while the body is still comfortably inside its own column. Below this the drift is sub-footprint and pressing would only chatter the strafe bit.</remarks>
    internal const double CrossTrackDeadbandBlocks = 0.15;

    /// <summary>The magnitude the world-space correction must project onto the body's strafe axis before that axis is pressed.</summary>
    /// <remarks>The same 0.05 <c>PhysicsEngineHolder.HoldStationAxisDeadband</c> uses, and for the same reason: it is what actually prevents the hold oscillating, and it is what makes a nearly-along-track correction press nothing rather than press a full strafe for a hundredth of a block of lateral benefit.</remarks>
    internal const double StrafeAxisDeadband = 0.05;

    /// <summary>The cross-track error at which the executor starts asking for a replan, in blocks.</summary>
    /// <remarks>Half a block, which is where the body's CENTRE leaves the lane of cells its segment was planned through: past it every completion predicate in <c>GroundedSegmentController</c> is being asked about a cell the body is not in. It is the same 0.5 <c>ShouldComplete</c>'s landing arm already uses against <see cref="SegmentGeometry.LateralOffsetFromSegmentLine"/> when it decides whether an overshoot counts as an arrival, so the two halves of the executor agree on where a lane ends.</remarks>
    internal const double CrossTrackReplanBlocks = 0.5;

    /// <summary>Consecutive ticks the error must stay past <see cref="CrossTrackReplanBlocks"/> before the executor asks for a replan.</summary>
    /// <remarks>Ten, half a second. The controller is already pressing at full authority by then (0.5 is well past <see cref="CrossTrackDeadbandBlocks"/>), so ten ticks of no recovery is the honest reading of "asking is not working". Firing on the first tick instead would turn a transient - a body swung wide by a corner it is about to come back from - into a replan, and a replan storm is a worse failure than the drift.</remarks>
    internal const int CrossTrackReplanTicks = 10;

    private readonly StationControllerOptions _options;
    private int _pastLaneTicks;

    /// <summary>Creates a controller with the default tuning.</summary>
    internal StationController()
        : this(StationControllerOptions.Default)
    {
    }

    /// <summary>Creates a controller with an explicit tuning, for ablation.</summary>
    internal StationController(StationControllerOptions options) => _options = options;

    /// <summary>The signed cross-track error the last <see cref="Correct"/> acted on, for tests.</summary>
    internal double LastCrossTrack { get; private set; }

    /// <summary>Whether the last <see cref="Correct"/> actually pressed a strafe, for tests.</summary>
    internal bool LastPressed { get; private set; }

    /// <summary>Forgets the per-segment replan accumulator. Called when the executor advances.</summary>
    internal void Reset() => _pastLaneTicks = 0;

    /// <summary>Whether the body has been outside its segment's lane for long enough that the executor should ask for a replan, and by how much.</summary>
    /// <remarks>This is a SEPARATE replan trigger from <c>PathExecutor.CheckDeviation</c>, and the two measure different quantities. That one compares the fed state against <c>TemplateOutput.ExpectedState</c>, a ONE-TICK forward simulation made by the same <c>PlayerPhysics</c> that applies <c>ApplyFluidPushing</c> - so the prediction already contains the current's push and the body is washed off its lane exactly as predicted, one tick at a time. Measured on the offline crossing in <c>CrossTrackStationTests</c>: worst per-tick prediction error <c>0.0000</c> against <c>1.0037</c> blocks of cross-track drift. No value of <c>deviationThreshold</c> can see that, which is why this arm exists rather than a smaller threshold.</remarks>
    internal bool CheckCrossTrack(PathSegment? segment, in PhysicsState physics, out double offset)
    {
        offset = 0;
        if (segment is null || !IsInScope(physics))
        {
            _pastLaneTicks = 0;
            return false;
        }

        offset = SegmentGeometry.SignedLateralOffsetFromSegmentLine(physics.Position, segment);
        if (Math.Abs(offset) <= _options.ReplanBlocks)
        {
            _pastLaneTicks = 0;
            return false;
        }

        _pastLaneTicks++;
        return _pastLaneTicks >= _options.ReplanTicks;
    }

    /// <summary>Adds the lateral correction to a template's emitted output, keeping every other bit of the input exactly as the template chose it.</summary>
    /// <remarks>The returned output is rebuilt through <see cref="TemplateOutput.From"/> so that <see cref="TemplateOutput.ExpectedState"/> is a prediction of the input ACTUALLY pressed. That is the whole reason the authority lives in the executor rather than above it: a correction applied after the executor returned would step the engine with an input the executor never predicted, and <c>PathExecutor.CheckDeviation</c> would then be comparing reality against a prediction of an input nobody pressed.</remarks>
    internal TemplateOutput Correct(
        PathSegment? segment, in PhysicsState physics, in TemplateOutput output, PathExecutionContext ctx)
    {
        LastPressed = false;
        LastCrossTrack = 0;
        if (segment is null || !IsInScope(physics))
            return output;

        double offset = SegmentGeometry.SignedLateralOffsetFromSegmentLine(physics.Position, segment);
        LastCrossTrack = offset;
        if (Math.Abs(offset) <= _options.DeadbandBlocks)
            return output;

        // The unit perpendicular of the segment line is (-dirZ, dirX) and the signed offset is its dot product with (pos - start), so the world direction that REDUCES the offset is that perpendicular scaled by -sign(offset).
        SegmentGeometry.GetNormalizedSegmentDirection(segment, out double dirX, out double dirZ);
        double sign = offset > 0 ? -1.0 : 1.0;
        double correctionX = sign * -dirZ;
        double correctionZ = sign * dirX;

        // Rotated by the yaw the template is emitting THIS tick, not by the body's current yaw: that is the rotation the engine will be set to before the input is applied, so it is the frame the input is actually resolved in.
        double yawRadians = output.TargetYaw * (Math.PI / 180.0);
        double sin = Math.Sin(yawRadians);
        double cos = Math.Cos(yawRadians);
        double xxa = (correctionX * cos) + (correctionZ * sin);

        bool left = xxa > _options.AxisDeadband;
        bool right = xxa < -_options.AxisDeadband;
        if (!left && !right)
        {
            // The correction is nearly along-track in the body's frame. Along-track is the segment's, so nothing is pressed.
            return output;
        }

        LastPressed = true;
        MovementInput corrected = output.Input with { Left = left, Right = right };
        return TemplateOutput.From(corrected, output.TargetYaw, output.TargetPitch, ctx, physics);
    }

    /// <summary>Whether the body is in the situation this controller exists for: <b>WADING</b> - in water, with its head in air, being pushed off a line the template cannot see it leaving.</summary>
    /// <remarks>
    /// <para><c>OnGround</c> cannot distinguish wading from swimming because a body in a one-deep sheet is buoyant for most of the crossing. Head submersion does distinguish them: a one-layer wade keeps the head dry, while a swimmer in a shaft or bore is fully submerged.</para>
    /// <para>A swimmer has no purchase to strafe with and its template is steering in three dimensions; pressing a lateral correction into that fights the template, and in a shaft it presses the body into a wall. The head test keeps the controller armed where a wading body must hold position and pricing needs it and disarms it for the descent that follows.</para>
    /// <para>Excluding the submerged also subsumes what the vacuous clause was reaching for: a ballistic arc through air is never <c>InWater</c> at all, so it remains out of scope.</para>
    /// </remarks>
    private bool IsInScope(in PhysicsState physics)
        => !_options.RequireSurfaceWater || (physics.InWater && !physics.IsUnderWater);
}
