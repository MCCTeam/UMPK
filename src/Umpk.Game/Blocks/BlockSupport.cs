using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Game.Blocks;

/// <summary>What a collision shape offers to stand on. Derived from the version's generated shape table, never from <see cref="BlockState.IsSolid"/>. <see cref="BlockFlags.Solid"/> applies only when a state's collision shape is a single full unit cube, so every slab, snow layer, dirt path and stair reads as unsupported even though vanilla walks on all of them (a bottom slab, a snow layer, and a dirt path present a flat, full-footprint top just like a full block; only the height differs).</summary>
public static class BlockSupport
{
    /// <summary>The default player step-up is <c>0.6</c> blocks. A player auto-steps onto anything at or below this height (a bottom slab at 0.5) without jumping, and needs a jump for anything above it (a full block at 1.0).</summary>
    public const double PlayerStepHeight = 0.6;

    /// <summary>The height at which the shape's boxes together cover the whole 1x1 XZ footprint, or null when no such height exists. Only the boxes reaching the shape's highest point (its global max Y) are considered: a full cube answers 1.0, a bottom slab 0.5, a snow layer its own height, a dirt path 0.9375. A bottom-half stair is excluded because its highest boxes cover only the step's partial footprint, leaving the rest of the cell topped out at the lower 0.5 shelf instead; a fence post is excluded because its highest (and only) box never reaches the full footprint at any height.</summary>
    public static double? FullCoverSupportHeight(ReadOnlySpan<Aabb> collisionShapes)
    {
        if (collisionShapes.IsEmpty)
            return null;

        double top = double.NegativeInfinity;
        foreach (Aabb box in collisionShapes)
            if (box.MaxY > top)
                top = box.MaxY;

        return CoversFullFootprintAt(collisionShapes, top) ? top : null;
    }

    /// <summary>How many samples <see cref="StepLadder"/> takes across the entry sweep, and therefore the most distinct levels it can report. A <c>levels</c> span must be at least this long.</summary>
    public const int StepLadderSamples = 33;

    /// <summary>The height a body of half-width <paramref name="half"/> centred at (<paramref name="cx"/>, <paramref name="cz"/>) rests at over this cell: the highest top face among the boxes its footprint overlaps, or <c>0</c> when it overlaps none.</summary>
    /// <remarks>
    /// <para>This is the question the physics answers, and it is a different question from <see cref="FullCoverSupportHeight(ReadOnlySpan{Aabb})"/>'s. Full cover asks "does this shape hold a body ANYWHERE in the cell", which is the right test for a column's edges and the wrong one for where a body actually sits: it returns null for a honey block and a lily pad, both of which are inset 1/16 on each side and neither of which the player falls through, and null for every bottom-half stair, which the player stands on at 1.0.</para>
    /// <para>A 60-tick settling measurement across 29 shapes matches this value in every case: honey at 0.9375, lily pad at 0.09375, and all eight bottom stairs at 1.0 like a full block, because a stair's raised octet always overlaps a centred 0.6-wide footprint.</para>
    /// <para>The coordinates are in the CELL's own frame and may sit outside <c>[0, 1]</c>: that is how <see cref="StepLadder"/> asks a neighbouring cell what it offers a body that is only partly over it. Overlap is strict, so a footprint that merely touches a box's face at a single coordinate does not count as standing on it.</para>
    /// </remarks>
    /// <param name="collisionShapes">The cell's collision boxes, in local (0..1) coordinates.</param>
    /// <param name="cx">The body centre's X in the cell's local frame.</param>
    /// <param name="cz">The body centre's Z in the cell's local frame.</param>
    /// <param name="half">Half the body's collision width (<c>PhysicsConstants.PlayerWidth / 2</c>).</param>
    public static double FootprintSupportHeight(ReadOnlySpan<Aabb> collisionShapes, double cx, double cz, double half)
    {
        double top = 0.0;
        foreach (Aabb box in collisionShapes)
        {
            bool overlaps = cx - half < box.MaxX && cx + half > box.MinX
                && cz - half < box.MaxZ && cz + half > box.MinZ;
            if (overlaps && box.MaxY > top)
                top = box.MaxY;

        }

        return top;
    }

    /// <summary>The height this cell holds a body of half-width <paramref name="half"/> at for EVERY stance whose centre is inside the cell: the highest top face among the boxes that overlap the footprint no matter where in <c>[0, 1]^2</c> that centre sits, or <c>0</c> when no box does.</summary>
    /// <remarks>
    /// <para>The pessimistic twin of <see cref="FootprintSupportHeight"/>, and the two differ exactly where a plan can be surprised. Footprint support answers "where does a body standing HERE rest", which is the right question for a body whose position is known. This one answers "what floor is under this cell whatever stance the body ends up in", which is the right question for a plan that is about to aim a JUMP at the cell and cannot control where the landing puts it.</para>
    /// <para>A box qualifies when it reaches past the extreme footprints on both axes: <c>MinX &lt; half</c> and <c>MaxX &gt; 1 - half</c>, likewise Z. That is a reach test and not a width test, and the distinction is load-bearing - a pointed dripstone <c>frustum</c> is only 8/16 wide but is inset 4/16, so it still reaches under a body pressed into either corner, while a <c>tip</c> is 6/16 wide inset 5/16 and does not.</para>
    /// <para>Against the registered shapes: a full cube, a slab, a snow layer, a carpet, a honey block, a lily pad and a bottom stair's BASE all qualify (the stair's raised octet does not, which is why a stair yields 0.5 here against 1.0 from the centred probe); a pointed dripstone <c>tip</c> yields 0.</para>
    /// </remarks>
    /// <param name="collisionShapes">The cell's collision boxes, in local (0..1) coordinates.</param>
    /// <param name="half">Half the body's collision width (<c>PhysicsConstants.PlayerWidth / 2</c>).</param>
    /// <returns>The guaranteed support height, or <c>0</c> when the cell guarantees none.</returns>
    public static double GuaranteedSupportHeight(ReadOnlySpan<Aabb> collisionShapes, double half)
    {
        double top = 0.0;
        foreach (Aabb box in collisionShapes)
        {
            bool reachesEveryStance = box.MinX < half && box.MaxX > 1.0 - half
                && box.MinZ < half && box.MaxZ > 1.0 - half;
            if (reachesEveryStance && box.MaxY > top)
                top = box.MaxY;

        }

        return top;
    }

    /// <summary>The distinct support levels a body meets entering a cell along (<paramref name="dx"/>, <paramref name="dz"/>): the footprint support swept from the cell's near face to its centre, consecutive duplicates collapsed, taken as the MAX over the destination cell and the source cell the footprint is still partly over.</summary>
    /// <remarks>
    /// <para><b>Why a sweep and not one number.</b> A stair is climbed in two 0.5 shelves, and which of its four cardinal entries is walkable depends on where the raised octet sits relative to the approach. Sampling the footprint support ALONG the entry reproduces that with no reference to <c>facing</c>, <c>half</c> or <c>shape</c>. The swept ladder agrees with all 116 measured shape-and-direction cases, including all four straight bottom stairs walking from exactly one direction and the two outer corners walking from exactly two.</para>
    /// <para><b>Why TWO cells and not one.</b> The design that produced the 116 sampled the destination cell alone, and every one of those 116 cases starts from a source elevation of zero (a support laid on a bare floor plane), so the source cell is air and contributes nothing. Off that validation set the one-cell form is simply wrong, because a 0.6-wide body straddles the boundary for the whole first half of the sweep. Across six source/destination pairs with a non-zero source elevation, the one-cell ladder predicts BLOCKED on five of the six pairs the engine walks - <c>snow[layers=8] -&gt; stone</c>, <c>dirt_path -&gt; stone</c>, <c>oak_slab -&gt; stone</c>, <c>soul_sand -&gt; stone</c> and <c>snow[5] -&gt; snow[8]</c> - because its ladder opens at 0 where the body is standing at 0.875, making the next shelf read as a full-block rise. The max over the two cells reports <c>[0.875, 1]</c> instead of <c>[0, 1]</c> and agrees 6 of 6.</para>
    /// <para>The ladder OPENS at the source's elevation for that reason, so a caller starts its walk from where the body already is rather than from the destination's floor.</para>
    /// </remarks>
    /// <param name="destination">The entered cell's collision boxes.</param>
    /// <param name="source">The collision boxes of the cell being left, i.e. the one at <c>destination - (dx, dz)</c>. Pass an empty span for "nothing there", which is what a support laid on a bare floor plane presents and what makes this reduce to the single-cell form.</param>
    /// <param name="dx">The travel step's X, -1, 0 or 1; exactly one of the two must be non-zero.</param>
    /// <param name="dz">The travel step's Z, -1, 0 or 1.</param>
    /// <param name="half">Half the body's collision width.</param>
    /// <param name="levels">Receives the ladder; must hold <see cref="StepLadderSamples"/> doubles.</param>
    /// <param name="count">How many levels were written.</param>
    /// <exception cref="ArgumentOutOfRangeException">The step is not exactly one cardinal.</exception>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is too short.</exception>
    public static void StepLadder(
        ReadOnlySpan<Aabb> destination,
        ReadOnlySpan<Aabb> source,
        int dx,
        int dz,
        double half,
        Span<double> levels,
        out int count)
    {
        if (dx is < -1 or > 1 || dz is < -1 or > 1 || (dx == 0) == (dz == 0))
            throw new ArgumentOutOfRangeException(nameof(dx), "the entry needs exactly one cardinal step");

        if (levels.Length < StepLadderSamples)
            throw new ArgumentException(
                $"the ladder needs room for {StepLadderSamples} levels", nameof(levels));

        // From the near face (the footprint just touching the cell) to the cell centre. Half a cell plus the body's own half-width is exactly the travel over which the footprint goes from wholly outside to wholly inside.
        int sign = dx + dz;
        double from = sign > 0 ? -half : 1.0 + half;
        double span = 0.5 + half;

        count = 0;
        for (int i = 0; i < StepLadderSamples; i++)
        {
            double t = i / (double)(StepLadderSamples - 1);
            double along = from + (sign * t * span);
            double cx = dx != 0 ? along : 0.5;
            double cz = dz != 0 ? along : 0.5;

            // The source cell's local frame has its origin at (dx, dz) relative to the destination's, so the same body centre reads (cx + dx, cz + dz) there.
            double height = Math.Max(
                FootprintSupportHeight(destination, cx, cz, half),
                FootprintSupportHeight(source, cx + dx, cz + dz, half));

            if (count == 0 || Math.Abs(levels[count - 1] - height) > Epsilon)
                levels[count++] = height;

        }
    }

    /// <summary>Walks a <see cref="StepLadder"/> from <paramref name="fromElevation"/>, auto-stepping every rise up to <paramref name="stepHeight"/>. Returns whether the whole ladder is walkable, and where the body ends up.</summary>
    /// <remarks>Only RISES are gated; a drop of any size inside a cell is a fall the body takes for free, which is what the engine does. <paramref name="restElevation"/> is the last level reached either way, so a refusal reports where the body stops rather than nothing at all.</remarks>
    /// <param name="levels">The ladder, as written by <see cref="StepLadder"/>.</param>
    /// <param name="fromElevation">The elevation the body enters at, in the destination cell's frame.</param>
    /// <param name="stepHeight">The auto-step, normally <see cref="PlayerStepHeight"/>.</param>
    /// <param name="restElevation">The elevation the walk reached.</param>
    public static bool TryStepUp(
        ReadOnlySpan<double> levels, double fromElevation, double stepHeight, out double restElevation)
    {
        double current = fromElevation;
        foreach (double level in levels)
        {
            if (level - current > stepHeight + Epsilon)
            {
                restElevation = current;
                return false;
            }

            current = level;
        }

        restElevation = current;
        return true;
    }

    /// <summary>Whether a body of half-width <paramref name="half"/> standing at <paramref name="sourceElevation"/> can WALK into this cell along (<paramref name="dx"/>, <paramref name="dz"/>), auto-stepping every rise up to <paramref name="stepHeight"/>, and where it comes to rest.</summary>
    /// <remarks>
    /// <para><b>Why this and not <see cref="StepLadder"/> plus <see cref="TryStepUp"/>.</b> Those two answer the question for a body entering from the SAME support plane, where the cell being left is a coplanar neighbour whose own footprint support opens the ladder. A step move enters from a different plane - one cell down for an ascend, one up for a descend - and there the coplanar neighbour is the body's own feet cell, which is air. Fed to the ladder it contributes 0, and <see cref="TryStepUp"/> then reads that 0 as a FLOOR at the destination cell's base rather than as the absence of one. The error is not academic: it makes a slab kerb climbed FROM a slab - a genuine 1.0 rise, and a jump - read as two 0.5 shelves and walk.</para>
    /// <para>So "no box under the footprint here" is treated as no floor at all, and the body carries its own <paramref name="sourceElevation"/> for exactly as long as its footprint is still over the cell it is leaving. That is the physical picture and it is what makes both directions come out right: a bare floor beside a carpet is a 0.0625 step down that the body walks, and a bare floor beside a full block is a 1.0 step down that it does not.</para>
    /// <para>Coordinates are this cell's own frame, so <paramref name="sourceElevation"/> is negative when the body comes from the plane below and above 1 when it comes from the plane above. Only RISES are gated, exactly as in <see cref="TryStepUp"/>; a drop inside the cell is free, and a caller that cares how far the body drops has to say so itself.</para>
    /// </remarks>
    /// <param name="destination">The entered cell's collision boxes.</param>
    /// <param name="sourceElevation">The top of what the body is standing on, in this cell's frame.</param>
    /// <param name="dx">The travel step's X, -1, 0 or 1; exactly one of the two must be non-zero.</param>
    /// <param name="dz">The travel step's Z, -1, 0 or 1.</param>
    /// <param name="half">Half the body's collision width.</param>
    /// <param name="stepHeight">The auto-step, normally <see cref="PlayerStepHeight"/>.</param>
    /// <param name="restElevation">The elevation the walk reached, in this cell's frame.</param>
    /// <exception cref="ArgumentOutOfRangeException">The step is not exactly one cardinal.</exception>
    public static bool TryEnterFrom(
        ReadOnlySpan<Aabb> destination,
        double sourceElevation,
        int dx,
        int dz,
        double half,
        double stepHeight,
        out double restElevation)
    {
        if (dx is < -1 or > 1 || dz is < -1 or > 1 || (dx == 0) == (dz == 0))
            throw new ArgumentOutOfRangeException(nameof(dx), "the entry needs exactly one cardinal step");

        int sign = dx + dz;
        double from = sign > 0 ? -half : 1.0 + half;
        double span = 0.5 + half;

        double current = sourceElevation;
        restElevation = sourceElevation;

        for (int i = 0; i < StepLadderSamples; i++)
        {
            double t = i / (double)(StepLadderSamples - 1);
            double along = from + (sign * t * span);
            double cx = dx != 0 ? along : 0.5;
            double cz = dz != 0 ? along : 0.5;

            double height = FootprintSupportHeight(destination, cx, cz, half);
            bool overSource = sign > 0 ? along - half < 0.0 : along + half > 1.0;

            double level;
            if (height > 0.0)
                level = overSource ? Math.Max(height, sourceElevation) : height;

            else if (overSource)
                level = sourceElevation;

            else
            {
                // Nothing under the footprint and nothing left of the cell being vacated: the body is over a hole. A walk cannot cross that, whatever the destination offers further in.
                restElevation = current;
                return false;
            }

            if (level - current > stepHeight + Epsilon)
            {
                restElevation = current;
                return false;
            }

            current = level;
        }

        restElevation = current;
        return true;
    }

    /// <summary>The highest surface anywhere in the cell, whether or not it covers the footprint.</summary>
    public static double MaxSupportHeight(ReadOnlySpan<Aabb> collisionShapes)
    {
        double top = 0.0;
        foreach (Aabb box in collisionShapes)
            if (box.MaxY > top)
                top = box.MaxY;

        return top;
    }

    /// <summary>Convenience overload resolving a state's collision shapes before computing support.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="shapes"/> is null.</exception>
    public static double? FullCoverSupportHeight(BlockState state, IBlockShapeSource shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        return FullCoverSupportHeight(shapes.GetCollisionShapes(state));
    }

    /// <summary>Whether the boxes whose top face sits at <paramref name="height"/> (within <see cref="Epsilon"/>) together cover the entire 1x1 XZ cell. Coordinate-compresses the X/Z extents of those boxes (clamped into the cell) into a grid and checks that every resulting sub-rectangle's midpoint is covered by at least one of them; a gap anywhere fails the check.</summary>
    private static bool CoversFullFootprintAt(ReadOnlySpan<Aabb> shapes, double height)
    {
        List<double> xs = [0.0, 1.0];
        List<double> zs = [0.0, 1.0];
        foreach (Aabb box in shapes)
        {
            if (Math.Abs(box.MaxY - height) > Epsilon)
                continue;

            xs.Add(Clamp01(box.MinX));
            xs.Add(Clamp01(box.MaxX));
            zs.Add(Clamp01(box.MinZ));
            zs.Add(Clamp01(box.MaxZ));
        }

        xs.Sort();
        zs.Sort();

        for (int i = 0; i < xs.Count - 1; i++)
        {
            double x0 = xs[i];
            double x1 = xs[i + 1];
            if (x1 - x0 <= Epsilon)
                continue;

            double xm = (x0 + x1) / 2.0;
            for (int j = 0; j < zs.Count - 1; j++)
            {
                double z0 = zs[j];
                double z1 = zs[j + 1];
                if (z1 - z0 <= Epsilon)
                    continue;

                double zm = (z0 + z1) / 2.0;
                if (!IsCoveredAtHeight(shapes, height, xm, zm))
                    return false;

            }
        }

        return true;
    }

    private static bool IsCoveredAtHeight(ReadOnlySpan<Aabb> shapes, double height, double x, double z)
    {
        foreach (Aabb box in shapes)
        {
            if (Math.Abs(box.MaxY - height) > Epsilon)
                continue;

            if (x >= box.MinX - Epsilon && x <= box.MaxX + Epsilon && z >= box.MinZ - Epsilon && z <= box.MaxZ + Epsilon)
                return true;

        }

        return false;
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);

    private const double Epsilon = 1.0E-9;
}
