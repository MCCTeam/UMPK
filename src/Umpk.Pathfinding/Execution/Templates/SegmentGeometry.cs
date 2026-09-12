using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>Pure geometry helpers shared by the action templates over an immutable <see cref="PhysicsState"/> snapshot. Yaw and pitch are computed and returned rather than written into a live engine, and speed projections read <see cref="PhysicsState.Velocity"/>. Internal, with test visibility.</summary>
internal static class SegmentGeometry
{
    private const double HalfWidth = PhysicsConstants.PlayerWidth / 2.0;

    /// <summary>The maximum yaw change per tick when smoothing toward a target.</summary>
    internal const float MaxYawStepPerTick = 35f;

    /// <summary>The maximum pitch change per tick when smoothing toward a target.</summary>
    internal const float MaxPitchStepPerTick = 25f;

    /// <summary>The yaw (degrees) that faces a horizontal direction, in vanilla convention.</summary>
    internal static float CalculateYaw(double dx, double dz)
    {
        float yaw = (float)(-Math.Atan2(dx, dz) / Math.PI * 180.0);
        if (yaw < 0)
            yaw += 360;

        return yaw;
    }

    /// <summary>The pitch (degrees) that looks toward a target offset.</summary>
    internal static float CalculatePitch(double dx, double dy, double dz)
    {
        double horizDist = Math.Sqrt((dx * dx) + (dz * dz));
        float pitch = (float)(-Math.Atan2(dy, horizDist) / Math.PI * 180.0);
        return Math.Clamp(pitch, -90f, 90f);
    }

    /// <summary>Interpolates yaw toward a target, respecting 0/360 wrap-around.</summary>
    internal static float SmoothYaw(float current, float target, float maxStep = MaxYawStepPerTick)
    {
        float delta = WrapDegrees(target - current);
        if (Math.Abs(delta) <= maxStep)
            return Normalize360(target);

        return Normalize360(current + (Math.Sign(delta) * maxStep));
    }

    /// <summary>Interpolates pitch toward a target.</summary>
    internal static float SmoothPitch(float current, float target, float maxStep = MaxPitchStepPerTick)
    {
        float delta = target - current;
        if (Math.Abs(delta) <= maxStep)
            return target;

        return current + (Math.Sign(delta) * maxStep);
    }

    /// <summary>The absolute yaw difference in degrees, normalized to [0, 180].</summary>
    internal static double HeadingPenaltyDegrees(float yaw, int headingX, int headingZ)
    {
        if (headingX == 0 && headingZ == 0)
            return 0.0;

        float targetYaw = CalculateYaw(headingX, headingZ);
        return Math.Abs(WrapDegrees(targetYaw - yaw));
    }

    /// <summary>The magnitude of the horizontal velocity.</summary>
    internal static double HorizontalSpeed(in PhysicsState physics)
        => Math.Sqrt((physics.Velocity.X * physics.Velocity.X) + (physics.Velocity.Z * physics.Velocity.Z));

    /// <summary>The horizontal speed projected onto a cardinal/diagonal heading.</summary>
    /// <remarks>The heading arrives as a pair of <c>Math.Sign</c> values, so a diagonal one is (±1, ±1) with magnitude sqrt, and a raw dot product against it reports a diagonal speed 1.4142 times what it is. Dividing by the heading's own magnitude is what makes this a projection rather than a scaled one; the cardinal case divides by exactly 1.0 and is bit-for-bit unchanged. <see cref="LateralOffsetFromSegmentLine"/> and <see cref="HasReachedSegmentEndPlane"/> in this same file always normalised, which is why the inflation was invisible: the two halves of a diagonal handoff decision disagreed with each other rather than both being wrong.</remarks>
    internal static double ProjectSpeedAlongHeading(in PhysicsState physics, int headingX, int headingZ)
    {
        if (headingX == 0 && headingZ == 0)
            return HorizontalSpeed(physics);

        double length = Math.Sqrt((double)((headingX * headingX) + (headingZ * headingZ)));
        return ((physics.Velocity.X * headingX) + (physics.Velocity.Z * headingZ)) / length;
    }

    /// <summary>The exit heading a segment hands off along (falls back to segment heading).</summary>
    internal static void GetExitHeading(PathSegment segment, out int headingX, out int headingZ)
    {
        headingX = segment.ExitHints.DesiredHeadingX;
        headingZ = segment.ExitHints.DesiredHeadingZ;
        if (headingX == 0 && headingZ == 0)
        {
            headingX = segment.HeadingX;
            headingZ = segment.HeadingZ;
        }
    }

    /// <summary>The distance still to travel along the segment heading toward the end.</summary>
    /// <remarks>Normalised for the same reason as <see cref="ProjectSpeedAlongHeading"/>: the heading is a pair of signs, so a one-block diagonal reports 1.4142 blocks rather than 2. A purely vertical segment has no horizontal heading and answers 0.</remarks>
    internal static double RemainingDistanceAlongSegment(in Vec3d pos, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        int headingX = segment.HeadingX;
        int headingZ = segment.HeadingZ;
        if (headingX == 0 && headingZ == 0)
            return 0.0;

        double dx = segment.End.X - pos.X;
        double dz = segment.End.Z - pos.Z;
        double length = Math.Sqrt((double)((headingX * headingX) + (headingZ * headingZ)));
        return ((dx * headingX) + (dz * headingZ)) / length;
    }

    /// <summary>The perpendicular offset of a position from the segment line.</summary>
    internal static double LateralOffsetFromSegmentLine(in Vec3d pos, PathSegment segment)
        => Math.Abs(SignedLateralOffsetFromSegmentLine(pos, segment));

    /// <summary>The perpendicular offset of a position from the segment line, SIGNED: positive on the side the line's left-hand normal <c>(-dirZ, dirX)</c> points to.</summary>
    /// <remarks><see cref="LateralOffsetFromSegmentLine"/> is this with the sign thrown away, and every existing caller wants it thrown away - they are all asking "how far off the lane", never "which side". <see cref="StationController"/> is the first caller that has to press an input, and an input has a direction. A purely vertical segment has no line to be off, and answers zero.</remarks>
    internal static double SignedLateralOffsetFromSegmentLine(in Vec3d pos, PathSegment segment)
    {
        GetNormalizedSegmentDirection(segment, out double dirX, out double dirZ);
        if (dirX == 0.0 && dirZ == 0.0)
            return 0.0;

        double relX = pos.X - segment.Start.X;
        double relZ = pos.Z - segment.Start.Z;
        return (-dirZ * relX) + (dirX * relZ);
    }

    /// <summary>Whether the position is within a small horizontal/vertical box of the target.</summary>
    internal static bool IsNear(in Vec3d pos, in Vec3d target, double horizThresholdSq = 0.25, double vertThreshold = 0.8)
    {
        double dx = target.X - pos.X;
        double dz = target.Z - pos.Z;
        double dy = target.Y - pos.Y;
        return (dx * dx) + (dz * dz) < horizThresholdSq && Math.Abs(dy) < vertThreshold;
    }

    /// <summary>Whether <paramref name="y"/> is an elevation the destination legitimately rests a body at: <c>[end.Y - tolerance, end.Y + slack + tolerance]</c>.</summary>
    /// <remarks>
    /// <para>The band is one-sided on purpose. Below the destination's own elevation there is nothing to allow - a body cannot be lower than the floor it is standing on - so the lower arm uses the default tolerance. Above it, a body 0.6 wide standing anywhere in a 1-wide column has its footprint over the neighbours too, and rests on whichever is highest. <c>PathSegment.EndElevationSlack</c> is how much higher that can be, and it is 0 over uniform terrain, so this collapses to <c>|dy| &lt; tolerance</c> everywhere the straddle does not exist.</para>
    /// <para>Over a bottom slab abutting a full block one higher, 11 of 24 measured approach phases land with the centre inside the column, 5 at the slab's 1.5 and 6 at the stone's 2.0. <c>|dy| &lt; 0.2</c> against 1.5 sees 5 of 11 and against 2.0 sees 6 of 11; this band, with a slack of 0.5, sees all 11.</para>
    /// </remarks>
    internal static bool IsAtEndElevation(double y, in Vec3d end, double slack, double tolerance)
        => y > end.Y - tolerance && y < end.Y + slack + tolerance;

    /// <summary>Whether the player's footprint is fully inside the destination block's XZ column.</summary>
    internal static bool IsFootprintInsideTargetBlock(in Vec3d pos, in Vec3d target, double epsilon = 1.0E-4)
    {
        double minX = pos.X - HalfWidth;
        double maxX = pos.X + HalfWidth;
        double minZ = pos.Z - HalfWidth;
        double maxZ = pos.Z + HalfWidth;

        double blockMinX = Math.Floor(target.X);
        double blockMinZ = Math.Floor(target.Z);
        return minX >= blockMinX - epsilon
            && maxX <= blockMinX + 1.0 + epsilon
            && minZ >= blockMinZ - epsilon
            && maxZ <= blockMinZ + 1.0 + epsilon;
    }

    /// <summary>Whether all four corners of the player's footprint still have something solid directly under them. False on the tick the bot is hanging off a ledge but has not started to fall.</summary>
    /// <remarks>
    /// <para>This is the "coyote tick" test. Grounded state stays true while any part of the collider rests on a block, so a bot walking off a ledge gets a run of grounded ticks in which it is already partly over the void. That run is where a long jump has to be taken from, and it is invisible to a gate written in terms of distance travelled along the segment.</para>
    /// <para>Pure geometry, deliberately: four block lookups and their collision boxes, no <see cref="PhysicsSimulator"/> call. The support block is the one containing the point just under the corner rather than <c>floor(pos.Y) - 1</c>, so a player standing on a slab or any other partial-height support is read correctly instead of being told the block a whole metre below it is what holds it up.</para>
    /// </remarks>
    internal static bool HasSupportUnderFootprint(IPhysicsWorldView world, in Vec3d pos, double epsilon = 1.0E-3)
        => IsSupported(world, pos.X - HalfWidth, pos.Y, pos.Z - HalfWidth, epsilon)
            || IsSupported(world, pos.X - HalfWidth, pos.Y, pos.Z + HalfWidth, epsilon)
            || IsSupported(world, pos.X + HalfWidth, pos.Y, pos.Z - HalfWidth, epsilon)
            || IsSupported(world, pos.X + HalfWidth, pos.Y, pos.Z + HalfWidth, epsilon);

    private static bool IsSupported(IPhysicsWorldView world, double x, double feetY, double z, double epsilon)
    {
        var below = BlockPos.Containing(new Vec3d(x, feetY - epsilon, z));
        BlockState state = world.GetBlock(below);
        double localX = x - below.X;
        double localZ = z - below.Z;
        foreach (Aabb box in world.GetCollisionShapes(state))
        {
            // The box has to be under this corner in XZ and reach up to the feet. Collision shapes are block-local, so the top is below.Y + box.MaxY.
            if (localX >= box.MinX && localX <= box.MaxX
                && localZ >= box.MinZ && localZ <= box.MaxZ
                && below.Y + box.MaxY >= feetY - epsilon)
                return true;

        }

        return false;
    }

    /// <summary>Whether the player's center is inside the destination block's XZ column.</summary>
    internal static bool IsCenterInsideTargetBlock(in Vec3d pos, in Vec3d target, double epsilon = 1.0E-4)
    {
        double blockMinX = Math.Floor(target.X);
        double blockMinZ = Math.Floor(target.Z);
        return pos.X >= blockMinX - epsilon
            && pos.X <= blockMinX + 1.0 + epsilon
            && pos.Z >= blockMinZ - epsilon
            && pos.Z <= blockMinZ + 1.0 + epsilon;
    }

    /// <summary>Whether next tick's projected footprint leaves the destination block.</summary>
    internal static bool WillLeaveTargetBlockNextTick(in Vec3d pos, in PhysicsState physics, in Vec3d target, double epsilon = 1.0E-4)
    {
        var nextPos = new Vec3d(pos.X + physics.Velocity.X, pos.Y, pos.Z + physics.Velocity.Z);
        return !IsFootprintInsideTargetBlock(nextPos, target, epsilon);
    }

    /// <summary>Whether the bot has reached the plane through the segment end perpendicular to the heading.</summary>
    internal static bool HasReachedSegmentEndPlane(in Vec3d pos, PathSegment segment, double tolerance = 0.05)
    {
        GetNormalizedSegmentDirection(segment, out double dirX, out double dirZ);
        double relX = pos.X - segment.End.X;
        double relZ = pos.Z - segment.End.Z;
        return (relX * dirX) + (relZ * dirZ) >= -tolerance;
    }

    /// <summary>Whether the bot is settled inside the target block with negligible horizontal speed.</summary>
    internal static bool IsSettledOnTargetBlock(in Vec3d pos, in Vec3d target, in PhysicsState physics, double speedThresholdSq = 0.0016)
    {
        double horizontalSpeedSq = (physics.Velocity.X * physics.Velocity.X) + (physics.Velocity.Z * physics.Velocity.Z);
        return IsFootprintInsideTargetBlock(pos, target)
            && !WillLeaveTargetBlockNextTick(pos, physics, target)
            && horizontalSpeedSq <= speedThresholdSq;
    }

    /// <summary>A relaxed settle check used at a final stop (center-in-block or a small radius).</summary>
    internal static bool IsSettledAtEnd(in Vec3d pos, in Vec3d target, in PhysicsState physics,
        double horizThresholdSq = 0.0025, double speedThresholdSq = 0.0016)
    {
        if (IsSettledOnTargetBlock(pos, target, physics, speedThresholdSq))
            return true;

        double horizontalSpeedSq = (physics.Velocity.X * physics.Velocity.X) + (physics.Velocity.Z * physics.Velocity.Z);
        if (horizontalSpeedSq > speedThresholdSq)
            return false;

        if (IsCenterInsideTargetBlock(pos, target))
        {
            var nextPos = new Vec3d(pos.X + physics.Velocity.X, pos.Y, pos.Z + physics.Velocity.Z);
            if (IsCenterInsideTargetBlock(nextPos, target))
                return true;

        }

        double dx = target.X - pos.X;
        double dz = target.Z - pos.Z;
        return (dx * dx) + (dz * dz) <= horizThresholdSq;
    }

    /// <summary>Wraps a degree delta to (-180, 180].</summary>
    internal static float WrapDegrees(float delta)
    {
        while (delta > 180f)
            delta -= 360f;

        while (delta < -180f)
            delta += 360f;

        return delta;
    }

    private static float Normalize360(float value)
    {
        while (value < 0f)
            value += 360f;

        while (value >= 360f)
            value -= 360f;

        return value;
    }

    internal static void GetNormalizedSegmentDirection(PathSegment segment, out double dirX, out double dirZ)
    {
        dirX = segment.End.X - segment.Start.X;
        dirZ = segment.End.Z - segment.Start.Z;
        double len = Math.Sqrt((dirX * dirX) + (dirZ * dirZ));
        if (len < 1.0E-6)
        {
            dirX = 0.0;
            dirZ = 0.0;
            return;
        }

        dirX /= len;
        dirZ /= len;
    }
}
