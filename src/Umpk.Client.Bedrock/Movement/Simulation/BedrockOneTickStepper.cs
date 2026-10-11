namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Deterministic one-tick scaffold stepper over an immutable versioned snapshot.</summary>
/// <remarks>Axis order, Euler integration, full-cube collision, epsilon offsets, and the on-ground rule are scaffold policy, not measured Bedrock facts.</remarks>
internal static class BedrockOneTickStepper
{
    /// <summary>Advances one tick over the given snapshot and reports the next state with applied-input and collision outcomes.</summary>
    /// <remarks>The tick must advance exactly one step past the state tick with explicit wrap from ulong.MaxValue to zero; unavailable modes and unknown terrain return the input state unchanged, and applied motion uses scaffold-policy instant horizontal velocity with explicit Euler vertical integration over X-then-Z-then-vertical full-cube collision with epsilon offsets.</remarks>
    internal static (BedrockPhysicalState Next, BedrockAppliedInputResult Applied, BedrockCollisionResult Collision) Step(
        BedrockPhysicalState state,
        BedrockMovementInput input,
        BedrockMovementTick tick,
        BedrockMovementProfile profile,
        BedrockCollisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);
        state.Validate();
        input.Validate();
        tick.Validate();
        ulong expectedTick = state.TickIndex == ulong.MaxValue ? 0UL : state.TickIndex + 1;
        if (tick.TickIndex != expectedTick)
            throw new ArgumentException("The tick must advance exactly one step past the state tick.");

        // Unavailable paths run no collision query, so the KnownFree result means unevaluated rather than proven free terrain.
        if (input.WantSprint || input.WantCrouch)
            return (state, new BedrockAppliedInputResult(BedrockInputDisposition.UnavailableMode, input.WishX, input.WishZ, false), new BedrockCollisionResult(BedrockCollisionOutcome.KnownFree, BedrockBlockedAxes.None, 0));
        if (input.WantJump && !profile.SupportsJump)
            return (state, new BedrockAppliedInputResult(BedrockInputDisposition.UnavailableMode, input.WishX, input.WishZ, false), new BedrockCollisionResult(BedrockCollisionOutcome.KnownFree, BedrockBlockedAxes.None, 0));
        if ((input.WishX != 0.0 || input.WishZ != 0.0) && !profile.SupportsHorizontal)
            return (state, new BedrockAppliedInputResult(BedrockInputDisposition.UnavailableMode, input.WishX, input.WishZ, false), new BedrockCollisionResult(BedrockCollisionOutcome.KnownFree, BedrockBlockedAxes.None, 0));
        if ((!profile.PlayerWidthBlocks.HasValue || !profile.PlayerHeightBlocks.HasValue) && (profile.GravityBlocksPerSecondSquared.HasValue || profile.TerminalVelocityBlocksPerSecond.HasValue || profile.JumpImpulseBlocksPerSecond.HasValue || state.VelocityY != 0.0 || input.WantJump))
            return (state, new BedrockAppliedInputResult(BedrockInputDisposition.UnavailableMode, input.WishX, input.WishZ, false), new BedrockCollisionResult(BedrockCollisionOutcome.KnownFree, BedrockBlockedAxes.None, 0));

        // Scaffold integrator: horizontal velocity snaps to wish times speed with no acceleration or momentum carry, and vertical motion uses explicit Euler integration.
        double velocityX = profile.SupportsHorizontal ? input.WishX * profile.WalkSpeedBlocksPerSecond!.Value : 0.0;
        double velocityZ = profile.SupportsHorizontal ? input.WishZ * profile.WalkSpeedBlocksPerSecond!.Value : 0.0;
        double velocityY = state.VelocityY;
        bool appliedJump = false;
        if (input.WantJump && state.OnGround)
        {
            velocityY = profile.JumpImpulseBlocksPerSecond!.Value;
            appliedJump = true;
        }
        else if (profile.SupportsGravity)
        {
            velocityY -= profile.GravityBlocksPerSecondSquared!.Value * tick.DeltaSeconds;
            double floor = -profile.TerminalVelocityBlocksPerSecond!.Value;
            if (velocityY < floor)
                velocityY = floor;
            if (state.OnGround && velocityY < 0.0 && HasSupportBelow(state.PositionX, state.PositionY, state.PositionZ, profile, snapshot, out bool known) && known)
                velocityY = 0.0;
        }

        double targetX = state.PositionX + velocityX * tick.DeltaSeconds;
        double targetY = state.PositionY + velocityY * tick.DeltaSeconds;
        double targetZ = state.PositionZ + velocityZ * tick.DeltaSeconds;

        BedrockBlockedAxes blocked = BedrockBlockedAxes.None;
        int unknown = 0;

        // Scaffold axis order is X then Z then vertical with full-cube cells and epsilon offsets; this order is arbitrary policy, not a measured Bedrock sequence.
        if (profile.SupportsHorizontal)
        {
            if (!TryMoveAxis(state.PositionX, targetX, state.PositionY, state.PositionZ, 0, profile, snapshot, ref unknown, out double resolvedX, out bool hitX))
                return Refused(state, input, unknown);
            targetX = resolvedX;
            if (hitX)
            {
                blocked |= BedrockBlockedAxes.X;
                velocityX = 0.0;
            }

            if (!TryMoveAxis(state.PositionZ, targetZ, targetX, state.PositionY, 2, profile, snapshot, ref unknown, out double resolvedZ, out bool hitZ))
                return Refused(state, input, unknown);
            targetZ = resolvedZ;
            if (hitZ)
            {
                blocked |= BedrockBlockedAxes.Z;
                velocityZ = 0.0;
            }
        }
        else
        {
            if (!ProbePath(state.PositionX, state.PositionY, state.PositionZ, targetX, targetY, targetZ, snapshot, ref unknown))
                return Refused(state, input, unknown);
        }

        if (!TryMoveVertical(state.PositionY, targetY, targetX, state.PositionZ, profile, snapshot, ref unknown, out double resolvedY, out bool hitY, out bool landed))
            return Refused(state, input, unknown);
        targetY = resolvedY;
        if (hitY)
        {
            blocked |= BedrockBlockedAxes.Y;
            velocityY = 0.0;
        }

        bool onGround = landed;
        if (!onGround)
        {
            // Scaffold on-ground rule treats zero vertical velocity over known support as grounded; the exact Bedrock derivation remains unresolved.
            bool solidBelow = HasSupportBelow(targetX, targetY, targetZ, profile, snapshot, out bool supportKnown);
            if (!supportKnown)
                return Refused(state, input, unknown + 1);
            if (solidBelow && velocityY == 0.0)
                onGround = true;
        }

        var next = new BedrockPhysicalState(targetX, targetY, targetZ, velocityX, velocityY, velocityZ, input.YawDegrees, input.PitchDegrees, onGround, tick.TickIndex);
        next.Validate();
        BedrockCollisionOutcome outcome = blocked == BedrockBlockedAxes.None ? BedrockCollisionOutcome.KnownFree : BedrockCollisionOutcome.CollidedSolid;
        return (next, new BedrockAppliedInputResult(BedrockInputDisposition.Applied, input.WishX, input.WishZ, appliedJump), new BedrockCollisionResult(outcome, blocked, 0));
    }

    /// <summary>Returns the unchanged state with refused-unknown-terrain disposition and the lower-bound unknown count.</summary>
    private static (BedrockPhysicalState Next, BedrockAppliedInputResult Applied, BedrockCollisionResult Collision) Refused(BedrockPhysicalState state, BedrockMovementInput input, int unknown) =>
        (state, new BedrockAppliedInputResult(BedrockInputDisposition.RefusedUnknownTerrain, input.WishX, input.WishZ, false), new BedrockCollisionResult(BedrockCollisionOutcome.RefusedUnknown, BedrockBlockedAxes.None, unknown));

    /// <summary>Reports whether known solid supports the scaffold feet position, with unknown support reported through the known flag rather than read as air.</summary>
    private static bool HasSupportBelow(double x, double y, double z, BedrockMovementProfile profile, BedrockCollisionSnapshot snapshot, out bool known)
    {
        known = true;
        double half = profile.PlayerWidthBlocks.HasValue ? profile.PlayerWidthBlocks.Value / 2.0 : 0.0;
        double epsilon = profile.CollisionEpsilonBlocks;
        double sampleY = y - epsilon;
        int minX = (int)Math.Floor(x - half);
        int maxX = (int)Math.Floor(x + half);
        int minZ = (int)Math.Floor(z - half);
        int maxZ = (int)Math.Floor(z + half);
        int cellY = (int)Math.Floor(sampleY);
        bool solid = false;
        for (int cx = minX; cx <= maxX; cx++)
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                BedrockCollisionCell cell = snapshot.GetCell(cx, cellY, cz);
                if (cell == BedrockCollisionCell.Unknown)
                {
                    known = false;
                    return false;
                }

                if (cell == BedrockCollisionCell.KnownSolid)
                    solid = true;
            }

        return solid;
    }

    /// <summary>Probes the integer cells along the extents-free path and refuses on the first unknown cell.</summary>
    private static bool ProbePath(double fromX, double fromY, double fromZ, double toX, double toY, double toZ, BedrockCollisionSnapshot snapshot, ref int unknown)
    {
        int minX = (int)Math.Floor(Math.Min(fromX, toX));
        int maxX = (int)Math.Floor(Math.Max(fromX, toX));
        int minY = (int)Math.Floor(Math.Min(fromY, toY));
        int maxY = (int)Math.Floor(Math.Max(fromY, toY));
        int minZ = (int)Math.Floor(Math.Min(fromZ, toZ));
        int maxZ = (int)Math.Floor(Math.Max(fromZ, toZ));
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    BedrockCollisionCell cell = snapshot.GetCell(x, y, z);
                    if (cell == BedrockCollisionCell.Unknown)
                    {
                        unknown++;
                        return false;
                    }
                }

        return true;
    }

    /// <summary>Moves one horizontal scaffold axis against full-cube cells with epsilon offsets, resolving contact at the nearest solid or refusing on unknown terrain.</summary>
    private static bool TryMoveAxis(double from, double to, double fixedY, double fixedOther, int axis, BedrockMovementProfile profile, BedrockCollisionSnapshot snapshot, ref int unknown, out double resolved, out bool hit)
    {
        resolved = to;
        hit = false;
        if (to == from)
            return true;
        double half = profile.PlayerWidthBlocks!.Value / 2.0;
        double height = profile.PlayerHeightBlocks!.Value;
        double epsilon = profile.CollisionEpsilonBlocks;
        double lo = Math.Min(from, to) - half - epsilon;
        double hi = Math.Max(from, to) + half + epsilon;
        int minCell = (int)Math.Floor(lo);
        int maxCell = (int)Math.Floor(hi);
        int minY = (int)Math.Floor(fixedY + epsilon);
        int maxY = (int)Math.Floor(fixedY + height - epsilon);
        int minOther = (int)Math.Floor(fixedOther - half - epsilon);
        int maxOther = (int)Math.Floor(fixedOther + half + epsilon);
        int? stopCell = null;
        for (int cell = minCell; cell <= maxCell; cell++)
            for (int y = minY; y <= maxY; y++)
                for (int other = minOther; other <= maxOther; other++)
                {
                    int x = axis == 0 ? cell : other;
                    int z = axis == 2 ? cell : other;
                    BedrockCollisionCell found = snapshot.GetCell(x, y, z);
                    if (found == BedrockCollisionCell.Unknown)
                    {
                        unknown++;
                        return false;
                    }

                    if (found != BedrockCollisionCell.KnownSolid)
                        continue;
                    if (!OverlapsTarget(to, half, fixedY, height, fixedOther, axis, cell, y, z))
                        continue;
                    if (to > from)
                        stopCell = stopCell.HasValue ? Math.Min(stopCell.Value, cell) : cell;
                    else
                        stopCell = stopCell.HasValue ? Math.Max(stopCell.Value, cell) : cell;
                }

        if (!stopCell.HasValue)
            return true;
        hit = true;
        resolved = to > from ? stopCell.Value - half - epsilon : stopCell.Value + 1 + half + epsilon;
        return true;
    }

    /// <summary>Reports whether the resolved scaffold body at the target position overlaps the given full-cube cell.</summary>
    private static bool OverlapsTarget(double to, double half, double fixedY, double height, double fixedOther, int axis, int cell, int y, int z)
    {
        double minX = axis == 0 ? to - half : fixedOther - half;
        double maxX = axis == 0 ? to + half : fixedOther + half;
        double minZ = axis == 2 ? to - half : fixedOther - half;
        double maxZ = axis == 2 ? to + half : fixedOther + half;
        return maxX > cell && minX < cell + 1 && fixedY + height > y && fixedY < y + 1 && maxZ > z && minZ < z + 1;
    }

    /// <summary>Moves the scaffold body vertically against full-cube cells with epsilon offsets, reporting landing when descending onto known solid and refusing on unknown terrain.</summary>
    private static bool TryMoveVertical(double fromY, double toY, double x, double z, BedrockMovementProfile profile, BedrockCollisionSnapshot snapshot, ref int unknown, out double resolved, out bool hit, out bool landed)
    {
        resolved = toY;
        hit = false;
        landed = false;
        if (!profile.PlayerWidthBlocks.HasValue || !profile.PlayerHeightBlocks.HasValue)
        {
            if (!ProbePath(x, fromY, z, x, toY, z, snapshot, ref unknown))
                return false;
            return true;
        }

        double half = profile.PlayerWidthBlocks!.Value / 2.0;
        double height = profile.PlayerHeightBlocks!.Value;
        double epsilon = profile.CollisionEpsilonBlocks;
        double lo = Math.Min(fromY, toY);
        double hi = Math.Max(fromY, toY) + height;
        int minY = (int)Math.Floor(lo + epsilon);
        int maxY = (int)Math.Floor(hi - epsilon);
        int minX = (int)Math.Floor(x - half - epsilon);
        int maxX = (int)Math.Floor(x + half + epsilon);
        int minZ = (int)Math.Floor(z - half - epsilon);
        int maxZ = (int)Math.Floor(z + half + epsilon);
        for (int y = minY; y <= maxY; y++)
            for (int cx = minX; cx <= maxX; cx++)
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    BedrockCollisionCell cell = snapshot.GetCell(cx, y, cz);
                    if (cell == BedrockCollisionCell.Unknown)
                    {
                        unknown++;
                        return false;
                    }

                    if (cell != BedrockCollisionCell.KnownSolid)
                        continue;
                    if (toY + height - epsilon <= y || toY + epsilon >= y + 1)
                        continue;
                    if (x + half <= cx || x - half >= cx + 1 || z + half <= cz || z - half >= cz + 1)
                        continue;
                    hit = true;
                    if (toY <= fromY)
                    {
                        resolved = y + 1;
                        landed = true;
                    }
                    else
                    {
                        resolved = y - height - epsilon;
                    }

                    return true;
                }

        return true;
    }
}
