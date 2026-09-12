using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves;

/// <summary>The single source of truth for jump-family feasibility and cost. Each <see cref="JumpFlavor"/> selects one Evaluate* method; the methods share low-level primitives so a physics rule is implemented exactly once.</summary>
/// <remarks>The one physics-constant dependency is the slow-floor walk penalty, and it is read from the dataset's <see cref="BlockState.SpeedFactor"/> through <see cref="MoveHelper.FloorSpeedPenalty"/> rather than duplicated as a constant. Every arm that puts the body's feet on a floor carries it - the level walk, both diagonals, both ascend arms and the diagonal descend - because the charge exists to price arms AGAINST each other and an uncharged arm wins for a reason that is not about the world. The airborne family (<see cref="EvaluateSprintJump"/>, <see cref="EvaluateSidewall"/>) deliberately does not: an arc is not slowed by the floor it left, and the takeoff rule a slow floor really imposes is the jump-factor one.</remarks>
internal static class JumpFeasibility
{
    public static void Evaluate(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        switch (desc.Flavor)
        {
            case JumpFlavor.Walk:
                EvaluateWalk(ctx, x, y, z, desc, ref result);
                return;
            case JumpFlavor.Step:
                EvaluateStep(ctx, x, y, z, desc, ref result);
                return;
            case JumpFlavor.SprintJump:
                EvaluateSprintJump(ctx, x, y, z, desc, ref result);
                return;
            case JumpFlavor.Sidewall:
                EvaluateSidewall(ctx, x, y, z, desc, ref result);
                return;
            default:
                result.SetImpossible();
                return;
        }
    }

    private static void EvaluateWalk(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        int dx = desc.XOffset;
        int dz = desc.ZOffset;
        int destX = x + dx;
        int destZ = z + dz;

        bool centred = ctx.CurrentLateralX == LateralQuantum.Centre && ctx.CurrentLateralZ == LateralQuantum.Centre;

        if (!ctx.CanWalkThrough(destX, y, destZ) || !ctx.CanWalkThrough(destX, y + 1, destZ) || !centred)
        {
            // The cell gate is left exactly as it was; a refusal is asked ONE more question, per edge, and only where a lane could exist at all. A body already standing off centre comes through here too even when the destination column is open, because it has to be told WHICH lateral it may leave at - a re-centring shift is not free while the post it just passed is still beside it.
            EvaluateSqueeze(ctx, x, y, z, desc, ref result);
            return;
        }

        if (!ctx.CanWalkOn(destX, y - 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        // The destination's FEET cell, which is the node's own Y: the floor charge is resolved from there down, exactly as the engine resolves it. See MoveHelper.FloorSpeedPenalty.
        double floorPenalty = FloorSpeedPenalty(ctx, destX, y, destZ);

        // Same feet cell is not the same elevation. A carpet and a full block both put a body's feet in the cell above them, so a crossing between them is offered here as a LEVEL walk while hiding a 0.9375 rise the auto-step cannot take. See MoveHelper.IsWalkedLevelCrossing.
        bool walked = MoveHelper.IsWalkedLevelCrossing(ctx, x, y, z, dx, dz);

        if (desc.IsCardinal)
        {
            if (!walked)
            {
                EvaluateLevelJump(ctx, x, y, z, destX, destZ, floorPenalty, ref result);
                return;
            }

            result.Set(destX, y, destZ, ctx.WadeCostThrough(x, y, z, dx, 0, dz) * floorPenalty);
            return;
        }

        if (!walked)
        {
            // A diagonal is deliberately not given a jump arm of its own. The cardinal pair covers the same geometry through the arm above, A* already prefers it (the diagonal is the dearer of the two), and a diagonal takeoff over a rise this size is an arc nothing here has measured. Refusing is the conservative half of the pair.
            result.SetImpossible();
            return;
        }

        bool sideX = ctx.CanWalkThrough(x + dx, y, z) && ctx.CanWalkThrough(x + dx, y + 1, z);
        bool sideZ = ctx.CanWalkThrough(x, y, z + dz) && ctx.CanWalkThrough(x, y + 1, z + dz);

        if (!sideX && !sideZ)
        {
            result.SetImpossible();
            return;
        }

        // The diagonal carries the same floor charge as the cardinal it is compared against. Charging one arm of a pair and not the other does not under-price the diagonal, it re-prices the CHOICE between them: over a soul-sand field the unpenalised diagonal costs 5.04 for 1.414 blocks against the cardinal's 8.91 for one, so the planner would zig-zag instead of crossing directly.
        double diagCost = (sideX && sideZ ? ctx.WadeCostThrough(x, y, z, dx, 0, dz) : ctx.WalkCost)
            * ActionCosts.DiagonalMultiplier
            * floorPenalty;

        result.Set(destX, y, destZ, diagCost);
    }

    /// <summary>The lateral squeeze: a cardinal level walk aimed at an OFF-CENTRE point in the destination cell, which is the only way a 0.6 body gets past a bamboo post.</summary>
    /// <remarks>
    /// <para><b>It is reached only from a refusal, or from a body that is already off centre.</b> On open ground with a centred body the walk arm above answers first and this method never runs, so a search over terrain with no bamboo in it emits exactly the edges it always did, with exactly the costs it always did, and explores exactly the nodes it always did.</para>
    /// <para><b>Diagonals are refused outright.</b> A diagonal has no single perpendicular axis, so a lane has no meaning along one; and the cardinal pair covers the same geometry, which is the same argument <see cref="EvaluateWalk"/> already makes for not giving the diagonal a jump arm. A body standing off centre is likewise refused every non-cardinal-traverse move by the driver, because every other family's feasibility was computed for a CENTRED body and none of it has been re-derived for an offset one.</para>
    /// <para><b>The floor test is tightened, deliberately.</b> <c>CanWalkOn</c> asks whether a CENTRED footprint rests on the cell below, and an off-centre footprint is a different question - a body flush with a cell face is standing on the outer 0.6 of the floor, which a lily pad or a honey block (both inset 1/16) still cover but which nothing narrower need. Rather than build an offset footprint query for one move family, a non-centred lateral requires a FULL unit cube underfoot. That is conservative - it refuses a squeeze over a slab path that would in fact work - and it is the half of the pair that cannot put the body somewhere it falls.</para>
    /// </remarks>
    private static void EvaluateSqueeze(
        CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        if (!desc.IsCardinal || !ctx.MayContainShapeOffset)
        {
            result.SetImpossible();
            return;
        }

        int dx = desc.XOffset;
        int dz = desc.ZOffset;
        int destX = x + dx;
        int destZ = z + dz;

        // Nothing about the DESTINATION FLOOR is sub-cell, so it is asked first and unchanged, and the extra full-cube requirement rides on top of it.
        if (!ctx.CanWalkOn(destX, y - 1, destZ) || !ctx.GetBlock(destX, y - 1, destZ).IsSolid)
        {
            result.SetImpossible();
            return;
        }

        // A rise the auto-step would have to take is a different move with different physics; a squeeze is a flat walk or it is nothing.
        if (!MoveHelper.IsWalkedLevelCrossing(ctx, x, y, z, dx, dz))
        {
            result.SetImpossible();
            return;
        }

        // The body's position ALONG the heading is resolved by the traverse itself: it ends at the destination cell's centre on that axis whatever it started at, so the source's along-axis component is neither carried nor searched. Discarding it is sound because the lane test reads the WHOLE source and destination columns on the heading axis, so whatever the body's along-axis position was inside the source cell, the band it sweeps was checked.
        bool alongX = dx != 0;
        LateralQuantum source = alongX ? ctx.CurrentLateralZ : ctx.CurrentLateralX;

        if (!TrySqueezeLane(ctx, x, y, z, dx, dz, source, out LateralQuantum destination))
        {
            result.SetImpossible();
            return;
        }

        double floorPenalty = FloorSpeedPenalty(ctx, destX, y, destZ);
        result.Set(destX, y, destZ, ctx.WalkCost * ActionCosts.SqueezeMultiplier * floorPenalty);
        result.Squeezed = true;

        if (alongX)
        {
            result.LateralX = LateralQuantum.Centre;
            result.LateralZ = destination;
        }
        else
        {
            result.LateralX = destination;
            result.LateralZ = LateralQuantum.Centre;
        }
    }

    /// <summary>The cheapest lane out of <c>(x, y, z)</c> along <c>(dx, dz)</c>, given where the body starts on the perpendicular axis.</summary>
    /// <remarks>Ordered <see cref="LateralQuantum.Centre"/> first, then the source's own lateral, then the remaining one, and it returns the FIRST that clears. That makes the choice deterministic and biases it toward the centre, so the body re-centres as soon as the geometry lets it instead of hugging a wall for the rest of the route. Only one lane is offered per edge: offering all feasible ones would multiply the branching factor of every cell in a grove for a choice A* cannot use, since all three cost the same and the cheapest-to-centre one is never worse.</remarks>
    private static bool TrySqueezeLane(
        CalculationContext ctx, int x, int y, int z, int dx, int dz,
        LateralQuantum source, out LateralQuantum destination)
    {
        if (SqueezeLane.IsClear(ctx, x, y, z, dx, dz, source, LateralQuantum.Centre))
        {
            destination = LateralQuantum.Centre;
            return true;
        }

        if (source != LateralQuantum.Centre
            && SqueezeLane.IsClear(ctx, x, y, z, dx, dz, source, source))
        {
            destination = source;
            return true;
        }

        foreach (LateralQuantum candidate in Faces)
            if (candidate != source && SqueezeLane.IsClear(ctx, x, y, z, dx, dz, source, candidate))
            {
                destination = candidate;
                return true;
            }

        destination = LateralQuantum.Centre;
        return false;
    }

    /// <summary>The two off-centre laterals, in a fixed order so the search is deterministic.</summary>
    private static readonly LateralQuantum[] Faces = [LateralQuantum.NearFace, LateralQuantum.FarFace];

    /// <summary>A cardinal crossing that stays in the same FEET cell but rises further than the auto-step: the body has to jump it, so it is priced and typed as a jump instead of being walked for free.</summary>
    /// <remarks>
    /// <para>The gates are <see cref="EvaluateStepAscend"/>'s jumped half, for the same reasons and in the same order: a floor can take the jump away (honey's 0.5 factor gives an apex of 0.383852, under even the 0.6 auto-step), and the body needs the cell above its own head clear to take off through. The destination column's two cells were cleared by the caller.</para>
    /// <para>It is emitted as <see cref="MoveType.Ascend"/> because that is what the body does and what the executor already knows how to drive: <c>AscendTemplate</c> steers on the segment's RESOLVED elevations, not on node Y, and <c>PathSegmentBuilder</c> treats an Ascend as the next-segment-jumps case its braking and approach planning handle. The move remains an ascend in every way except the node arithmetic.</para>
    /// </remarks>
    private static void EvaluateLevelJump(
        CalculationContext ctx, int x, int y, int z, int destX, int destZ, double floorPenalty, ref MoveResult result)
    {
        if (!MoveHelper.CanTakeOffForAJump(ctx, x, y, z))
        {
            ctx.NoteSlowJumpFloorTakeoff();
            result.SetImpossible();
            return;
        }

        // A move that begins AND ends afloat is a swim, and the jump family may not price it. A body Afloat bodies have no 0.42 ground jump. Above the 0.4 fluid threshold, held jump contributes only the 0.04 liquid impulse, grounded or not - so this family's distances, impulses AND tick budgets are all calibrated on a jump that is not there. Measured, the 0.04 buys 0.232-0.279 over the waterline against a grounded jump's 1.2522 apex (Umpk.Physics.Tests.FloatingTakeoffReachTests). What covers it instead is the swim family, which is priced and budgeted for water: MoveSwimVertical to rise inside the column, MoveSwim to cross it.
        //
        // A move ending on dry land can use the bank's 0.9203-block fluid-exit lift. If both ends leave the body afloat, the move never obtains the ground contact required for completion.
        if (MoveHelper.IsAfloatInWater(ctx, x, y, z)
            && MoveHelper.IsAfloatInWater(ctx, destX, y, destZ))
        {
            ctx.NoteFloatingTakeoff();
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(x, y + 2, z))
        {
            result.SetImpossible();
            return;
        }

        // The wade price rides on this arm for the same reason it rides on EvaluateStepAscend's jumped half: this is the JUMPED half of the level walk above, and leaving it at the still-water rate would let a body buy a free upstream block by routing over a kerb. WadeCostThrough returns SprintCost byte-identically on a dry or still cell, so every dry level-jump is unchanged.
        result.Set(
            destX,
            y,
            destZ,
            (ctx.WadeCostThrough(x, y, z, destX - x, 0, destZ - z) * floorPenalty) + ctx.JumpPenalty,
            MoveType.Ascend);
    }

    private static void EvaluateStep(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        if (desc.YDelta == 1)
            EvaluateStepAscend(ctx, x, y, z, desc, ref result);

        else if (desc.YDelta == -1)
            EvaluateStepDescend(ctx, x, y, z, desc, ref result);

        else
            result.SetImpossible();

    }

    private static void EvaluateStepAscend(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        int dx = desc.XOffset;
        int dz = desc.ZOffset;
        int destX = x + dx;
        int destZ = z + dz;
        int destY = y + 1;

        if (!ctx.CanWalkOn(destX, y, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(destX, destY, destZ) || !ctx.CanWalkThrough(destX, destY + 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        // The rise the BODY sees, not the node-Y delta. A slab kerb, a snow ramp and a stair tread are all +1 here and are all walked; a full block is +1 too and is not. See MoveHelper.IsWalkedStep. A walked step is charged the flat walk rate with the destination floor's own slowdown, exactly as the level walk arm above is, and needs only the destination column's own two clearance cells - the y+2 headroom below is a JUMP's requirement, and over-constrains a carpet, a lily pad or snow[layers=2] whose head tops out at 1.8625, 1.89375 and 1.925.
        double floorPenalty = MoveHelper.FloorSpeedPenalty(ctx, destX, destY, destZ);

        if (desc.IsCardinal && MoveHelper.IsWalkedStep(ctx, x, y, z, dx, dz, destY))
        {
            result.Set(
                destX, destY, destZ,
                ctx.WadeCostThrough(x, y, z, dx, destY - y, dz) * floorPenalty,
                MoveType.Traverse);
            return;
        }

        // Past here the body has to JUMP, and a floor can take the jump away: honey's 0.5 jump factor gives an apex of 0.383852, under even the 0.6 auto-step. See MoveHelper.CanTakeOffForAJump. The walked arm above is deliberately on the other side of this gate - an auto-step needs no jump power at all, so a body on honey still walks up a slab kerb.
        if (!MoveHelper.CanTakeOffForAJump(ctx, x, y, z))
        {
            ctx.NoteSlowJumpFloorTakeoff();
            result.SetImpossible();
            return;
        }

        // A move that begins AND ends afloat is a swim, and the jump family may not price it. A body Afloat bodies have no 0.42 ground jump. Above the 0.4 fluid threshold, held jump contributes only the 0.04 liquid impulse, grounded or not - so this family's distances, impulses AND tick budgets are all calibrated on a jump that is not there. Measured, the 0.04 buys 0.232-0.279 over the waterline against a grounded jump's 1.2522 apex (Umpk.Physics.Tests.FloatingTakeoffReachTests). What covers it instead is the swim family, which is priced and budgeted for water: MoveSwimVertical to rise inside the column, MoveSwim to cross it.
        //
        // A move ending on dry land can use the bank's 0.9203-block fluid-exit lift. If both ends leave the body afloat, the move never obtains the ground contact required for completion.
        if (MoveHelper.IsAfloatInWater(ctx, x, y, z)
            && MoveHelper.IsAfloatInWater(ctx, destX, destY, destZ))
        {
            ctx.NoteFloatingTakeoff();
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(x, y + 2, z))
        {
            result.SetImpossible();
            return;
        }

        // The JUMPED arms carry the charge too. The body still walks INTO the destination cell and is still slowed by its floor once it lands there, and leaving these two uncharged re-creates the walk arms' asymmetry one level up: a cardinal ascend onto soul sand would cost less than a level walk onto the same block. The jump penalty is additive and is not scaled - it is a takeoff cost, paid on the floor the body leaves.
        if (desc.IsCardinal)
        {
            result.Set(
                destX, destY, destZ,
                (ctx.WadeCostThrough(x, y, z, dx, destY - y, dz) * floorPenalty) + ctx.JumpPenalty);
            return;
        }

        // Past here the move is a DIAGONAL, and a diagonal is where a hanging body and a standing one come apart. The cardinal arm above is deliberately still offered to a hanging body: the climb lift really does reach a block up - 1.2520 measured on a real vine against a grounded jump's own 1.2522 apex, see Umpk.Client.Tests.ClimbLiftReachTests - and a straight step-off lands inside that. A diagonal is not a longer version of the same move. It is an ARC over a corner, and a hanging body has no arc: the corner cell holds no climbable, so the lift ends the tick the feet leave the column and what is left is a fall with 0.15 of clamped lateral to spend. Measured: with the vine's own column carrying one more block above the top rung, the body is pinned against that column, slides along its face and drops - the owner's report, and VineClimbEndToEndTests' fourth row.
        if (MoveHelper.IsHangingOnAClimbable(ctx, x, y, z))
        {
            ctx.NoteHangingTakeoff();
            result.SetImpossible();
            return;
        }

        bool pathViaX = ctx.CanWalkThrough(x + dx, y, z) && ctx.CanWalkThrough(x + dx, y + 1, z) && ctx.CanWalkThrough(x + dx, y + 2, z);
        bool pathViaZ = ctx.CanWalkThrough(x, y, z + dz) && ctx.CanWalkThrough(x, y + 1, z + dz) && ctx.CanWalkThrough(x, y + 2, z + dz);

        if (!pathViaX && !pathViaZ)
        {
            result.SetImpossible();
            return;
        }

        bool cardinalWalkableViaX = pathViaX && ctx.CanWalkOn(x + dx, y - 1, z);
        bool cardinalWalkableViaZ = pathViaZ && ctx.CanWalkOn(x, y - 1, z + dz);
        if (cardinalWalkableViaX || cardinalWalkableViaZ)
        {
            result.SetImpossible();
            return;
        }

        double diagCost =
            (ctx.WadeCostThrough(x, y, z, dx, destY - y, dz) * ActionCosts.DiagonalMultiplier * floorPenalty)
            + ctx.JumpPenalty;
        result.Set(destX, destY, destZ, diagCost);
    }

    private static void EvaluateStepDescend(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        int dx = desc.XOffset;
        int dz = desc.ZOffset;
        int destX = x + dx;
        int destZ = z + dz;
        int destY = y - 1;

        if (!ctx.CanWalkOn(destX, destY - 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(destX, destY, destZ) || !ctx.CanWalkThrough(destX, destY + 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (MoveHelper.IsHazard(ctx, ctx.GetBlock(destX, destY - 1, destZ)))
        {
            result.SetImpossible();
            return;
        }

        if (ctx.GetBlock(x, y - 1, z).IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        if (!desc.IsDiagonal)
        {
            result.SetImpossible();
            return;
        }

        bool pathViaX = ctx.CanWalkThrough(x + dx, y, z) && ctx.CanWalkThrough(x + dx, y + 1, z);
        bool pathViaZ = ctx.CanWalkThrough(x, y, z + dz) && ctx.CanWalkThrough(x, y + 1, z + dz);

        if (!pathViaX || !pathViaZ)
        {
            result.SetImpossible();
            return;
        }

        // The walk-off term is charged at the destination floor, like every other ground arm, so that a diagonal drop into soul sand cannot undercut the cardinal drop into the same cell (MoveDescend's own arm charges the same way). The FALL term is not scaled: a fall is gravity, and the floor the body has not reached yet does not slow it.
        double cost = (ActionCosts.WalkOffBlock
                * ActionCosts.DiagonalMultiplier
                * MoveHelper.FloorSpeedPenalty(ctx, destX, destY, destZ))
            + ActionCosts.FallCost(1);
        result.Set(destX, destY, destZ, cost);
    }

    private static void EvaluateSprintJump(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        int xOffset = desc.XOffset;
        int zOffset = desc.ZOffset;
        int yDelta = desc.YDelta;

        if (!ctx.AllowParkour)
        {
            result.SetImpossible();
            return;
        }

        if (yDelta > 0 && !ctx.AllowParkourAscend)
        {
            result.SetImpossible();
            return;
        }

        if (yDelta < 0 && -yDelta > ctx.MaxFallHeight)
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanSprint)
        {
            result.SetImpossible();
            return;
        }

        bool cardinal = (xOffset == 0) != (zOffset == 0);
        if (cardinal)
        {
            int distance = Math.Max(Math.Abs(xOffset), Math.Abs(zOffset));
            int maxDistance = yDelta switch
            {
                > 0 => 3,
                < 0 => 5,
                _ => 5,
            };

            if (distance > maxDistance)
            {
                result.SetImpossible();
                return;
            }
        }

        if (ctx.GetBlock(x, y - 1, z).IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        // The pair to the gate above, and the other half of the same question. That one is a body standing ON a climbable; this one is a body HANGING IN one, which no arm asked about until the owner's vine. A hanging body has neither of the two things a parkour distance is made of: no jump (jumpFromGround is behind onGround()) and no run-up (handleOnClimbable clamps lateral travel to 0.15 while the body is in the column).
        if (MoveHelper.IsHangingOnAClimbable(ctx, x, y, z))
        {
            ctx.NoteHangingTakeoff();
            result.SetImpossible();
            return;
        }

        // A move that begins AND ends afloat is a swim, and the jump family may not price it. A body Afloat bodies have no 0.42 ground jump. Above the 0.4 fluid threshold, held jump contributes only the 0.04 liquid impulse, grounded or not - so this family's distances, impulses AND tick budgets are all calibrated on a jump that is not there. Measured, the 0.04 buys 0.232-0.279 over the waterline against a grounded jump's 1.2522 apex (Umpk.Physics.Tests.FloatingTakeoffReachTests). What covers it instead is the swim family, which is priced and budgeted for water: MoveSwimVertical to rise inside the column, MoveSwim to cross it.
        //
        // A move ending on dry land can use the bank's 0.9203-block fluid-exit lift. If both ends leave the body afloat, the move never obtains the ground contact required for completion.
        if (MoveHelper.IsAfloatInWater(ctx, x, y, z)
            && MoveHelper.IsAfloatInWater(ctx, x + xOffset, y + yDelta, z + zOffset))
        {
            ctx.NoteFloatingTakeoff();
            result.SetImpossible();
            return;
        }

        // A parkour arc is a jump before it is anything else, and every distance in the tables above is calibrated on a full-power one. See MoveHelper.CanTakeOffForAJump.
        if (!MoveHelper.CanTakeOffForAJump(ctx, x, y, z))
        {
            ctx.NoteSlowJumpFloorTakeoff();
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasRunUp(ctx, x, y, z, xOffset, zOffset, yDelta))
        {
            result.SetImpossible();
            return;
        }

        int destX = x + xOffset;
        int destZ = z + zOffset;
        int destY = y + yDelta;

        // A parkour arc that DROPS more than two blocks onto powder snow is not a landing: vanilla hands any body past fallDistance 2.5 a 0.9-tall box instead of the cube, and it sinks through on the next tick. See CalculationContext.LandingSinksThroughPowderSnow.
        if (ctx.LandingSinksThroughPowderSnow(ctx.GetBlock(destX, destY - 1, destZ), -yDelta))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(x, y + 2, z))
        {
            result.SetImpossible();
            return;
        }

        // Takeoff from a water block is not a valid sprint jump.
        if (ctx.GetBlock(x, y, z).IsFluid)
        {
            result.SetImpossible();
            return;
        }

        // CanLandOn, not CanWalkOn: a jump's landing floor has to be under the body wherever the arc puts it, not only under a centred stance. CanLandOn subsumes CanWalkOn.
        if (!ctx.CanLandOn(destX, destY - 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(destX, destY, destZ) || !ctx.CanWalkThrough(destX, destY + 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (ParkourFeasibility.HasIntermediateLandingConflict(ctx, x, y, z, xOffset, zOffset, yDelta))
        {
            result.SetImpossible();
            return;
        }

        int xSign = Math.Sign(xOffset);
        int zSign = Math.Sign(zOffset);
        int xAbs = Math.Abs(xOffset);
        int zAbs = Math.Abs(zOffset);

        if (!CheckSprintJumpFlightPath(ctx, x, y, z, xSign, zSign, xAbs, zAbs, yDelta))
        {
            result.SetImpossible();
            return;
        }

        // A sprint jump is only worth planning over a gap, and MoveHelper.IsOpenGap is what "a gap" means: no floor at all. The old !CanWalkOn form let a hazard floor pass this gate, because "must not stand here" and "nothing here to stand on" gave the same answer.
        if (xAbs > 0 && zAbs == 0)
        {
            if (!MoveHelper.IsOpenGap(ctx, x + xSign, y - 1, z))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (xAbs == 0 && zAbs > 0)
        {
            if (!MoveHelper.IsOpenGap(ctx, x, y - 1, z + zSign))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (!MoveHelper.IsOpenGap(ctx, x + xSign, y - 1, z + zSign))
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasDiagonalShoulderClearance(ctx, x, y, z, xOffset, zOffset))
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasCardinalSideClearance(ctx, x, y, z, xOffset, zOffset))
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasLandingOvershootClearance(ctx, destX, destY, destZ, xSign, zSign))
        {
            result.SetImpossible();
            return;
        }

        double horizDist = Math.Sqrt((double)((xOffset * xOffset) + (zOffset * zOffset)));
        double cost;
        if (yDelta > 0)
            cost = horizDist * ctx.SprintCost + ctx.JumpPenalty * 2;

        else if (yDelta < 0)
            cost = horizDist * ctx.SprintCost + ctx.JumpPenalty + ActionCosts.FallCost(-yDelta);

        else if (horizDist >= 3.5)
            cost = horizDist * ctx.SprintCost + ctx.JumpPenalty;

        else
            cost = horizDist * ctx.WalkCost + ctx.JumpPenalty;

        result.Set(destX, destY, destZ, cost, ParkourProfile.Default);
    }

    private static bool CheckSprintJumpFlightPath(CalculationContext ctx, int x, int y, int z, int xSign, int zSign, int xAbs, int zAbs, int yDelta)
    {
        if (xAbs == 0 || zAbs == 0)
        {
            for (int step = 1; step < Math.Max(xAbs, zAbs); step++)
            {
                int gx = x + (xSign * (xAbs > 0 ? step : 0));
                int gz = z + (zSign * (zAbs > 0 ? step : 0));
                if (!ClearSprintJumpColumn(ctx, gx, y, gz, yDelta))
                    return false;

            }

            return true;
        }

        int maxSteps = Math.Max(xAbs, zAbs);
        for (int step = 1; step < maxSteps; step++)
        {
            double fx = (double)step * xAbs / maxSteps;
            double fz = (double)step * zAbs / maxSteps;

            int ix = (int)Math.Round(fx);
            int iz = (int)Math.Round(fz);

            int gx = x + (xSign * ix);
            int gz = z + (zSign * iz);

            if (!ClearSprintJumpColumn(ctx, gx, y, gz, yDelta))
                return false;

            if (xAbs != zAbs)
            {
                double fracX = fx - Math.Floor(fx);
                double fracZ = fz - Math.Floor(fz);
                if (fracX > 0.2 && fracX < 0.8 && ix > 0 && ix < xAbs)
                    if (!ClearSprintJumpColumn(ctx, x + (xSign * (ix - 1)), y, gz, yDelta))
                        return false;

                if (fracZ > 0.2 && fracZ < 0.8 && iz > 0 && iz < zAbs)
                    if (!ClearSprintJumpColumn(ctx, gx, y, z + (zSign * (iz - 1)), yDelta))
                        return false;

            }
        }

        return true;
    }

    private static bool ClearSprintJumpColumn(CalculationContext ctx, int gx, int y, int gz, int yDelta)
    {
        if (!ctx.CanWalkThrough(gx, y, gz) || !ctx.CanWalkThrough(gx, y + 1, gz) || !ctx.CanWalkThrough(gx, y + 2, gz))
            return false;

        // The flight path is cleared by body columns alone, which said nothing about what the arc passes over. A hazard in the takeoff's support plane is exactly what a sprint jump must not be planned across: the body is carried over it, and a jump that falls short lands in it.
        if (MoveHelper.IsHazardAt(ctx, gx, y - 1, gz))
            return false;

        // Nor may the arc pass THROUGH a doorway. An open door's panel leaves 0.0125 blocks of clearance on its side, and an airborne body has no lateral authority at all: there is nothing to aim with once the jump is taken. The A* entry restriction covers the landing cell; this covers the cells the body is carried across on the way there.
        if (MoveHelper.HasBarrierPanel(ctx, gx, y, gz))
            return false;

        if (yDelta > 0 && !ctx.CanWalkThrough(gx, y + 3, gz))
            return false;

        return true;
    }

    private static void EvaluateSidewall(CalculationContext ctx, int x, int y, int z, JumpDescriptor desc, ref MoveResult result)
    {
        int xOffset = desc.XOffset;
        int zOffset = desc.ZOffset;
        int yDelta = desc.YDelta;

        if (!ctx.AllowParkour || !ctx.CanSprint)
        {
            result.SetImpossible();
            return;
        }

        if (yDelta > 0 && !ctx.AllowParkourAscend)
        {
            result.SetImpossible();
            return;
        }

        if (yDelta < 0 && -yDelta > ctx.MaxFallHeight)
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.IsSidewallProfile(xOffset, zOffset, yDelta))
        {
            result.SetImpossible();
            return;
        }

        if (ctx.GetBlock(x, y - 1, z).IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        // Sidewall takeoff from a water block is not valid.
        if (ctx.GetBlock(x, y, z).IsFluid)
        {
            result.SetImpossible();
            return;
        }

        // Nor from a rung. A sidewall is a sprint jump with a wall to bounce along, and a hanging body has neither the jump nor the sprint; see the same gate in EvaluateSprintJump.
        if (MoveHelper.IsHangingOnAClimbable(ctx, x, y, z))
        {
            ctx.NoteHangingTakeoff();
            result.SetImpossible();
            return;
        }

        // Nor from a floor that halves the jump power. See MoveHelper.CanTakeOffForAJump.
        if (!MoveHelper.CanTakeOffForAJump(ctx, x, y, z))
        {
            ctx.NoteSlowJumpFloorTakeoff();
            result.SetImpossible();
            return;
        }

        ParkourFeasibility.GetSidewallAxes(xOffset, zOffset, out int forwardX, out int forwardZ, out int lateralX, out int lateralZ);

        int destX = x + xOffset;
        int destY = y + yDelta;
        int destZ = z + zOffset;

        // A parkour arc that DROPS more than two blocks onto powder snow is not a landing: vanilla hands any body past fallDistance 2.5 a 0.9-tall box instead of the cube, and it sinks through on the next tick. See CalculationContext.LandingSinksThroughPowderSnow.
        if (ctx.LandingSinksThroughPowderSnow(ctx.GetBlock(destX, destY - 1, destZ), -yDelta))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(x, y + 2, z))
        {
            result.SetImpossible();
            return;
        }

        if (ParkourFeasibility.TryGetRequiredStaticEntryRunupSteps(ctx.PreviousMoveType, xOffset, zOffset, yDelta, out int requiredSteps))
        {
            if (!ParkourFeasibility.HasPreparedRunup(ctx.CurrentEntryPreparation, x, y, z, forwardX, forwardZ, requiredSteps))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (!ParkourFeasibility.HasDominantAxisRunUp(ctx, x, y, z, forwardX, forwardZ, xOffset, zOffset, yDelta))
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasSidewallArcClearance(ctx, x, y, z, forwardX, forwardZ, lateralX, lateralZ, xOffset, zOffset, yDelta))
        {
            result.SetImpossible();
            return;
        }

        if (!ParkourFeasibility.HasSidewallLandingClearance(ctx, destX, destY, destZ, forwardX, forwardZ, lateralX, lateralZ))
        {
            result.SetImpossible();
            return;
        }

        double horizDist = Math.Sqrt((double)((xOffset * xOffset) + (zOffset * zOffset)));
        double cost = yDelta switch
        {
            > 0 => horizDist * ctx.SprintCost + ctx.JumpPenalty * 2,
            < 0 => horizDist * ctx.SprintCost + ctx.JumpPenalty + ActionCosts.FallCost(-yDelta),
            _ => horizDist * ctx.SprintCost + ctx.JumpPenalty,
        };

        result.Set(destX, destY, destZ, cost, ParkourProfile.Sidewall);
    }

    /// <summary>The walk-cost multiplier for a destination floor's speed factor, keyed on the destination's feet cell. Every ground arm uses the same calculation.</summary>
    private static double FloorSpeedPenalty(CalculationContext ctx, int x, int feetY, int z)
        => MoveHelper.FloorSpeedPenalty(ctx, x, feetY, z);
}
