using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Physics;

namespace Umpk.Pathfinding.Moves;

/// <summary>The lateral offset a body carries INSIDE its own cell, quantised to three positions.</summary>
/// <remarks>
/// <para>A body centred in a bamboo cell overlaps the post for every possible position offset. Passing the post requires an off-centre stance.</para>
/// <para><b>Three values, and the magnitude is derived rather than chosen.</b> <see cref="SqueezeLane.LateralOffset"/> is <c>(1 - PlayerWidth) / 2 = 0.2</c>: the offset at which the body's own face is flush with the cell's face, so it spans exactly <c>[0, 0.6]</c> or <c>[0.4, 1.0]</c> of the cell. It is the largest lateral that keeps the whole body inside one cell, which is what makes every downstream consumer of a node's position (goal test, floor support, elevation) keep meaning what it meant.</para>
/// <para><b>What the magnitude buys, exactly.</b> A bamboo post spans <c>[0.40625 + o, 0.59375 + o]</c> with <c>o = (k - 7.5) / 30</c> for a nibble <c>k</c> in <c>0..15</c>. A flush body clears it iff <c>|o| &gt;= 0.19375</c>, i.e. <c>k in {0, 1, 14, 15}</c>: <b>4 of 16 columns per axis</b>. A gap between posts in adjacent cells requires a body to straddle their shared face and is outside this model.</para>
/// </remarks>
public enum LateralQuantum : sbyte
{
    /// <summary>The body's face flush with the cell's LOW face on the perpendicular axis.</summary>
    NearFace = -1,

    /// <summary>The cell centre, which remains the default.</summary>
    Centre = 0,

    /// <summary>The body's face flush with the cell's HIGH face on the perpendicular axis.</summary>
    FarFace = 1,
}

/// <summary>The per-edge squeeze-lane predicate: whether a cardinal traverse can be aimed at an off-centre point that clears the collision boxes a centred body could not.</summary>
/// <remarks>
/// <para>This remains a separate predicate because most consumers of <see cref="BlockState.BlocksMotion"/> are about water and all nine break in the permissive direction if the flag or <see cref="MoveHelper.CanWalkThrough"/> is widened - a waterlogged fence would readmit swims through solids, a waterlogged wall would be read as a splash and price a real fall at zero. So the cell gate is left exactly as it was and the squeeze is asked as a separate question, per EDGE, only where the cell gate has already said no.</para>
/// <para>The offset model is required because the shape table is keyed on state rather than position. <see cref="BlockShapeOffset"/> supplies the position-dependent bamboo box.</para>
/// </remarks>
public static class SqueezeLane
{
    /// <summary>The lateral a <see cref="LateralQuantum.NearFace"/> or <see cref="LateralQuantum.FarFace"/> body stands at, as a signed offset from the cell centre: <c>(1 - PlayerWidth) / 2</c>.</summary>
    /// <remarks>Derived, not tuned. At exactly this offset the body's face coincides with the cell's face, so the body still occupies exactly one cell and no node's meaning changes; one step further and a node would describe a body in two cells at once, which is a much larger change than a number.</remarks>
    public const double LateralOffset = (1.0 - PhysicsConstants.PlayerWidth) / 2.0;

    /// <summary>The clearance a lane must have beyond the body's own width before it is called free.</summary>
    /// <remarks>Not a safety margin: it is the tolerance that keeps a box whose face EXACTLY touches the body's face from being read as an overlap. A flush body's face sits on the cell boundary and a neighbouring wall's face sits on the same boundary, and those two touching is what walking along a wall IS. The value is small enough that it can never admit a real overlap - the tightest real bamboo clearance is 0.0229, three orders of magnitude larger.</remarks>
    private const double Touch = 1.0E-9;

    /// <summary>The body's height above its feet, for the band a lane has to be clear over.</summary>
    /// <remarks><c>PhysicsConstants.PlayerHeight</c> is 1.8, so the band is the feet cell and 0.8 of the cell above it. Both cells are read; a box in the head cell that starts above 0.8 cannot touch the body and is skipped by the band test rather than by a cell-index rule.</remarks>
    private const double BodyHeight = 1.8;

    /// <summary>The real lateral for a quantum.</summary>
    public static double Offset(LateralQuantum quantum) => (sbyte)quantum * LateralOffset;

    /// <summary>Whether a cardinal traverse from <c>(x, y, z)</c> along <c>(dx, dz)</c> can be walked with the body at <paramref name="destination"/> on the perpendicular axis, given that it starts the edge at <paramref name="source"/>.</summary>
    /// <remarks>
    /// <para><b>The test.</b> Project every collision box in the source column and the destination column - both the feet cell and the head cell - onto the axis perpendicular to the heading, at the box's TRUE offset position, keeping only boxes that overlap the body's own height band. The body must miss all of them, at its source lateral, at its destination lateral, and everywhere between: the swept band is a single interval, so one interval test covers the forward travel and the lateral shift at once.</para>
    /// <para><b>Why the two flanking columns the design lists are NOT read.</b> §B.3 asks for the destination's perpendicular neighbours as well. At a flush lateral they cannot matter and the arithmetic says so exactly: the body's swept band is contained in <c>[0, 1]</c> of its own cell by construction, and a collision box is contained in <c>[0, 1]</c> of ITS own cell - vanilla's horizontal shapes never leave their cell, and the two offset families are clamped to their own widest inset precisely so they cannot. Two intervals in adjacent unit cells can touch and can never overlap. Reading the flanks would therefore be code that cannot change an answer, which is worse than not reading them; <c>SqueezeLaneGeometryTests</c> pins the claim with a flank packed solid instead. The flanks become load-bearing the moment a straddling lateral is added, and that is recorded rather than pre-built.</para>
    /// <para><b>What may be squeezed past.</b> Only a state that blocks motion for its SHAPE. Water, lava, any hazard, a climbable, powder snow and the whole barrier family are refused outright even though some of them have no box to project, because their cell verdict is a property rather than a geometry and a purely geometric lane would walk a body into a fire or read a waterlogged wall as open air.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The source cell's X.</param>
    /// <param name="y">The source cell's feet Y.</param>
    /// <param name="z">The source cell's Z.</param>
    /// <param name="dx">The heading's X component, -1, 0 or 1.</param>
    /// <param name="dz">The heading's Z component, -1, 0 or 1.</param>
    /// <param name="source">Where the body stands on the perpendicular axis when the edge starts.</param>
    /// <param name="destination">Where it is to stand when the edge ends.</param>
    /// <returns>True when the whole swept band is free.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static bool IsClear(
        CalculationContext ctx,
        int x, int y, int z,
        int dx, int dz,
        LateralQuantum source,
        LateralQuantum destination)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Perpendicular to the heading: a move along X is constrained on Z and vice versa. A non-cardinal heading has no single perpendicular axis and never reaches here.
        bool alongX = dx != 0;
        if (alongX == (dz != 0))
            return false;

        const double Half = PhysicsConstants.PlayerWidth / 2.0;
        double from = Offset(source);
        double to = Offset(destination);

        // The SOURCE column carries the lateral shift: the body is modelled as reaching its destination lateral before it leaves the cell it started in, so the source has to be clear over the whole band the shift sweeps.
        //
        // The DESTINATION column is asked only about the lateral the body ARRIVES at, and that asymmetry is the whole reason the feature works: a body that had to be clear at its old lateral in the new cell as well could never enter the post's cell at all, since the lane that clears the post is precisely the one the centre does not.
        //
        // What the executor really does is aim straight at the endpoint, so its line cuts the corner and the body grazes the post it is passing - measured at up to 0.099 of penetration for the worst pairing, which vanilla's collision resolver slides out of by blocking the heading axis
        // for two or three ticks while the perpendicular one keeps moving. That is what a player does
        // walking past a stalk, it is progress rather than a stall, and it is what ActionCosts.SqueezeMultiplier is calibrated against.
        double sourceLo = Math.Min(from, to) + 0.5 - Half;
        double sourceHi = Math.Max(from, to) + 0.5 + Half;

        int destX = x + dx;
        int destZ = z + dz;

        return ColumnAdmits(ctx, x, y, z, alongX, sourceLo, sourceHi)
            && ColumnAdmits(ctx, destX, y, destZ, alongX, to + 0.5 - Half, to + 0.5 + Half);
    }

    /// <summary>Whether the two body cells of one column leave the band <c>[lo, hi]</c> free on the perpendicular axis.</summary>
    private static bool ColumnAdmits(
        CalculationContext ctx, int x, int y, int z, bool alongX, double lo, double hi)
    {
        for (int level = 0; level <= 1; level++)
        {
            int cellY = y + level;
            BlockState state = ctx.GetBlock(x, cellY, z);

            if (MoveHelper.CanWalkThrough(ctx, x, cellY, z))
            {
                // Passable for a centred body, but not necessarily empty: an open fence gate, a scaffolding, a snow layer all have boxes, and an off-centre body meets them where a centred one did not. Project them.
                if (!BoxesMissTheBand(ctx, state, x, cellY, z, alongX, lo, hi, level))
                    return false;

                continue;
            }

            if (!IsSqueezableObstruction(ctx, state))
                return false;

            if (!BoxesMissTheBand(ctx, state, x, cellY, z, alongX, lo, hi, level))
                return false;

        }

        return true;
    }

    /// <summary>Whether a cell's verdict is a matter of SHAPE alone, so that measuring the shape is a complete answer.</summary>
    /// <remarks>Everything excluded here is excluded because its cell verdict comes from a property rather than a box, and for several of them there is no box at all: lava and water are refused by <see cref="MoveHelper.CanWalkThrough"/> with an EMPTY collision shape, so a purely geometric lane would walk a body straight into them. A climbable is refused because a body inside one is climbing rather than walking, powder snow because a body inside it gets an empty shape while a body above it gets a full cube, and the barrier family because a door's passability is its <c>open</c> property and its interaction cost is charged elsewhere.</remarks>
    private static bool IsSqueezableObstruction(CalculationContext ctx, BlockState state)
    {
        // The one arm that does the bulk of the work, and it is a flag read. Everything whose cell verdict is a PROPERTY rather than a box is on the wrong side of it already: air, water, lava and powder snow all carry an EMPTY collision shape in the dataset, so none of them blocks motion, and a purely geometric lane would have read every one of them as open air.
        if (!state.BlocksMotion)
            return false;

        // A waterlogged solid is both water and a collision box. A predicate that stops after reading only the water would walk a body through a fence.
        if (state.IsWaterlogged)
            return false;

        // No currently supported candidate reaches these guards, but the candidate set is version data. Keep them so future narrow offset blocks cannot bypass climbable, hazard, or barrier rules.
        if (state.IsClimbable)
            return false;

        return !MoveHelper.IsHazard(ctx, state) && !MoveHelper.IsBarrierFamily(state);
    }

    /// <summary>Whether every collision box of <paramref name="state"/> that overlaps the body's height band misses the perpendicular band <c>[lo, hi]</c>.</summary>
    private static bool BoxesMissTheBand(
        CalculationContext ctx, BlockState state, int x, int cellY, int z, bool alongX,
        double lo, double hi, int level)
    {
        ReadOnlySpan<Aabb> boxes = ctx.World.GetCollisionShapes(state);
        if (boxes.Length == 0)
            return true;

        // The one line the whole feature rests on: the box is where VANILLA puts it, not where a position-blind shape table put it. See BlockShapeOffset.
        Vec3d shift = BlockShapeOffset.For(state, new BlockPos(x, cellY, z));
        double lateralShift = alongX ? shift.Z : shift.X;

        // The band the body's own height occupies inside THIS cell. Feet cell: all of it from 0 up. Head cell: only up to 0.8, because the body is 1.8 tall.
        double bandTop = level == 0 ? 1.0 : BodyHeight - 1.0;

        for (int i = 0; i < boxes.Length; i++)
        {
            Aabb box = boxes[i];
            if (box.MinY >= bandTop - Touch)
                continue;

            double boxLo = (alongX ? box.MinZ : box.MinX) + lateralShift;
            double boxHi = (alongX ? box.MaxZ : box.MaxX) + lateralShift;

            if (boxLo < hi - Touch && boxHi > lo + Touch)
                return false;

        }

        return true;
    }
}
