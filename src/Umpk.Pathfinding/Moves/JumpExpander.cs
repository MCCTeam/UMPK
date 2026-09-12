using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves;

/// <summary>The dynamic expander for the jump family (walk, step, sprint-jump, sidewall). Walk, step, diagonal sprint-jump, and sidewall remain descriptor-driven; cardinal sprint jumps and sidewalls come from a Baritone-style near-to-far scan (<see cref="ProbeCardinal"/>) that emits at most one candidate per shape, letting A* re-probe from each landing. Material checks use <c>BlockState</c> flags.</summary>
public sealed class JumpExpander : IMoveExpander
{
    private const int CardinalProbeSlots = 48;

    private static readonly JumpDescriptor[] Descriptors = BuildDescriptors();

    /// <inheritdoc/>
    public int MaxNeighbors => Descriptors.Length + CardinalProbeSlots;

    /// <inheritdoc/>
    public int Expand(CalculationContext ctx, int x, int y, int z, Span<MoveNeighbor> buffer)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        int count = 0;
        MoveResult result = default;

        bool jumpFamilyAllowed = ctx.AllowParkour && ctx.CanSprint;
        bool canSprintTakeoff = false;
        if (jumpFamilyAllowed)
        {
            bool standingClimbable = ctx.GetBlock(x, y - 1, z).IsClimbable;
            bool feetFluid = ctx.GetBlock(x, y, z).IsFluid;
            canSprintTakeoff = !standingClimbable && !feetFluid && ctx.CanWalkThrough(x, y + 2, z);
        }

        Span<bool> directionGapOpen = stackalloc bool[9];
        if (canSprintTakeoff)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    int idx = ((dx + 1) * 3) + (dz + 1);

                    // A hazard floor is a floor, not a gap. MoveHelper.IsOpenGap draws the distinction !CanWalkOn cannot: magma is a full unit cube a body comes to rest on and is hurt by, so refusing to stand on it must not read as "there is nothing here to stand on" and licence a jump over it.
                    directionGapOpen[idx] = MoveHelper.IsOpenGap(ctx, x + dx, y - 1, z + dz);
                }
            }
        }

        for (int i = 0; i < Descriptors.Length; i++)
        {
            JumpDescriptor desc = Descriptors[i];

            switch (desc.Flavor)
            {
                case JumpFlavor.SprintJump:
                    if (!canSprintTakeoff)
                        continue;

                    {
                        int sx = Math.Sign(desc.XOffset);
                        int sz = Math.Sign(desc.ZOffset);
                        int idx = ((sx + 1) * 3) + (sz + 1);
                        if (!directionGapOpen[idx])
                            continue;

                    }

                    break;
                case JumpFlavor.Sidewall:
                    continue;
                default:
                    break;
            }

            result.Cost = 0;
            JumpFeasibility.Evaluate(ctx, x, y, z, desc, ref result);
            if (result.IsImpossible)
                continue;

            MoveType type = DeriveMoveType(desc);
            if (count < buffer.Length)
                buffer[count++] = new MoveNeighbor(result, type);

        }

        if (canSprintTakeoff)
        {
            ProbeCardinal(ctx, x, y, z, +1, 0, directionGapOpen, buffer, ref count, ref result);
            ProbeCardinal(ctx, x, y, z, -1, 0, directionGapOpen, buffer, ref count, ref result);
            ProbeCardinal(ctx, x, y, z, 0, +1, directionGapOpen, buffer, ref count, ref result);
            ProbeCardinal(ctx, x, y, z, 0, -1, directionGapOpen, buffer, ref count, ref result);
        }

        return count;
    }

    private static void ProbeCardinal(
        CalculationContext ctx,
        int x, int y, int z,
        int fx, int fz,
        ReadOnlySpan<bool> directionGapOpen,
        Span<MoveNeighbor> buffer,
        ref int count,
        ref MoveResult result)
    {
        int firstStepIdx = ((fx + 1) * 3) + (fz + 1);
        if (!directionGapOpen[firstStepIdx])
            return;

        int sx1 = x + fx;
        int sz1 = z + fz;
        if (!ctx.CanWalkThrough(sx1, y, sz1) || !ctx.CanWalkThrough(sx1, y + 1, sz1))
            return;

        int lxP, lzP, lxN, lzN;
        if (fx != 0)
        {
            lxP = 0; lzP = +1;
            lxN = 0; lzN = -1;
        }
        else
        {
            lxP = +1; lzP = 0;
            lxN = -1; lzN = 0;
        }

        bool wallP = !ctx.CanWalkThrough(x + lxP, y, z + lzP) || !ctx.CanWalkThrough(x + lxP, y + 1, z + lzP);
        bool wallN = !ctx.CanWalkThrough(x + lxN, y, z + lzN) || !ctx.CanWalkThrough(x + lxN, y + 1, z + lzN);

        int bestAscend = 0;
        int bestFlat = 0;
        int bestDescend1 = 0;
        int bestDescend2 = 0;

        int bestSwP0 = 0, bestSwP1 = 0, bestSwP2 = 0, bestSwP3 = 0;
        int bestSwN0 = 0, bestSwN1 = 0, bestSwN2 = 0, bestSwN3 = 0;

        const int MaxJumpDistance = 5;
        for (int i = 2; i <= MaxJumpDistance; i++)
        {
            int dx = x + (fx * i);
            int dz = z + (fz * i);

            if (!ctx.CanWalkThrough(dx, y + 1, dz) || !ctx.CanWalkThrough(dx, y + 2, dz))
                break;

            // The scan only ever looked at the body columns, so a hazard in the support plane was invisible to it and every landing beyond the hazard stayed a candidate. Stop here: the arc from the takeoff cell to anything further along this ray passes over the hazard.
            if (MoveHelper.IsHazardAt(ctx, dx, y - 1, dz))
                break;

            if (!ctx.CanWalkThrough(dx, y, dz))
            {
                if (i <= 3 && ctx.CanWalkOn(dx, y, dz))
                    bestAscend = i;

                break;
            }

            if (ctx.CanWalkOn(dx, y - 1, dz))
                bestFlat = i;

            else if (ctx.CanWalkOn(dx, y - 2, dz))
                bestDescend1 = i;

            else if (ctx.CanWalkOn(dx, y - 3, dz))
                bestDescend2 = i;

            if (wallP)
                TrackSidewallCandidates(ctx, dx, y, dz, lxP, lzP, i, ref bestSwP0, ref bestSwP1, ref bestSwP2, ref bestSwP3);

            if (wallN)
                TrackSidewallCandidates(ctx, dx, y, dz, lxN, lzN, i, ref bestSwN0, ref bestSwN1, ref bestSwN2, ref bestSwN3);

        }

        if (bestAscend > 0)
            TryEmitSprintJump(ctx, x, y, z, fx * bestAscend, fz * bestAscend, +1, buffer, ref count, ref result);

        if (bestFlat > 0)
            TryEmitSprintJump(ctx, x, y, z, fx * bestFlat, fz * bestFlat, 0, buffer, ref count, ref result);

        if (bestDescend1 > 0)
            TryEmitSprintJump(ctx, x, y, z, fx * bestDescend1, fz * bestDescend1, -1, buffer, ref count, ref result);

        if (bestDescend2 > 0)
            TryEmitSprintJump(ctx, x, y, z, fx * bestDescend2, fz * bestDescend2, -2, buffer, ref count, ref result);

        EmitSidewall(ctx, x, y, z, fx, fz, lxP, lzP, +1, bestSwP0, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxP, lzP, 0, bestSwP1, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxP, lzP, -1, bestSwP2, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxP, lzP, -2, bestSwP3, buffer, ref count, ref result);

        EmitSidewall(ctx, x, y, z, fx, fz, lxN, lzN, +1, bestSwN0, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxN, lzN, 0, bestSwN1, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxN, lzN, -1, bestSwN2, buffer, ref count, ref result);
        EmitSidewall(ctx, x, y, z, fx, fz, lxN, lzN, -2, bestSwN3, buffer, ref count, ref result);
    }

    private static void TrackSidewallCandidates(
        CalculationContext ctx,
        int dx, int y, int dz,
        int lateralX, int lateralZ,
        int i,
        ref int bestPlus1,
        ref int bestFlat,
        ref int bestMinus1,
        ref int bestMinus2)
    {
        int lx = dx + lateralX;
        int lz = dz + lateralZ;

        if (i <= 3
            && ctx.CanWalkOn(lx, y, lz)
            && ctx.CanWalkThrough(lx, y + 1, lz)
            && ctx.CanWalkThrough(lx, y + 2, lz))
            bestPlus1 = i;

        if (!ctx.CanWalkThrough(lx, y, lz) || !ctx.CanWalkThrough(lx, y + 1, lz))
            return;

        if (ctx.CanWalkOn(lx, y - 1, lz))
            bestFlat = i;

        else if (ctx.CanWalkOn(lx, y - 2, lz))
            bestMinus1 = i;

        else if (ctx.CanWalkOn(lx, y - 3, lz))
            bestMinus2 = i;

    }

    private static void EmitSidewall(
        CalculationContext ctx,
        int x, int y, int z,
        int fx, int fz,
        int lateralX, int lateralZ,
        int yDelta,
        int bestI,
        Span<MoveNeighbor> buffer,
        ref int count,
        ref MoveResult result)
    {
        if (bestI <= 0)
            return;

        int xOffset = (fx * bestI) + lateralX;
        int zOffset = (fz * bestI) + lateralZ;
        var desc = new JumpDescriptor(xOffset, zOffset, yDelta, JumpFlavor.Sidewall);
        result.Cost = 0;
        JumpFeasibility.Evaluate(ctx, x, y, z, desc, ref result);
        if (result.IsImpossible)
            return;

        if (count < buffer.Length)
            buffer[count++] = new MoveNeighbor(result, MoveType.Parkour);

    }

    private static void TryEmitSprintJump(
        CalculationContext ctx,
        int x, int y, int z,
        int xOffset, int zOffset, int yDelta,
        Span<MoveNeighbor> buffer,
        ref int count,
        ref MoveResult result)
    {
        var desc = new JumpDescriptor(xOffset, zOffset, yDelta, JumpFlavor.SprintJump);
        result.Cost = 0;
        JumpFeasibility.Evaluate(ctx, x, y, z, desc, ref result);
        if (result.IsImpossible)
            return;

        if (count < buffer.Length)
            buffer[count++] = new MoveNeighbor(result, MoveType.Parkour);

    }

    private static MoveType DeriveMoveType(JumpDescriptor d) => d.Flavor switch
    {
        JumpFlavor.Walk => d.IsCardinal ? MoveType.Traverse : MoveType.Diagonal,
        JumpFlavor.Step => d.YDelta > 0 ? MoveType.Ascend : MoveType.Descend,
        JumpFlavor.SprintJump => MoveType.Parkour,
        JumpFlavor.Sidewall => MoveType.Parkour,
        _ => MoveType.Traverse,
    };

    private static JumpDescriptor[] BuildDescriptors()
    {
        var list = new List<JumpDescriptor>(256);
        int[] offsets = [1, -1];

        foreach (int dx in offsets)
        {
            list.Add(new JumpDescriptor(dx, 0, 0, JumpFlavor.Walk));
            list.Add(new JumpDescriptor(dx, 0, 1, JumpFlavor.Step));
        }

        foreach (int dz in offsets)
        {
            list.Add(new JumpDescriptor(0, dz, 0, JumpFlavor.Walk));
            list.Add(new JumpDescriptor(0, dz, 1, JumpFlavor.Step));
        }

        foreach (int dx in offsets)
            foreach (int dz in offsets)
            {
                list.Add(new JumpDescriptor(dx, dz, 0, JumpFlavor.Walk));
                list.Add(new JumpDescriptor(dx, dz, 1, JumpFlavor.Step));
                list.Add(new JumpDescriptor(dx, dz, -1, JumpFlavor.Step));
            }

        foreach (int dx in offsets)
            foreach (int dz in offsets)
            {
                list.Add(new JumpDescriptor(dx * 2, dz * 1, 0, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 1, dz * 2, 0, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 2, dz * 2, 0, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 3, dz * 1, 0, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 1, dz * 3, 0, JumpFlavor.SprintJump));

                list.Add(new JumpDescriptor(dx * 2, dz * 1, -1, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 1, dz * 2, -1, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 2, dz * 2, -1, JumpFlavor.SprintJump));

                list.Add(new JumpDescriptor(dx * 2, dz * 1, 1, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 1, dz * 2, 1, JumpFlavor.SprintJump));
                list.Add(new JumpDescriptor(dx * 2, dz * 2, 1, JumpFlavor.SprintJump));
            }

        return [.. list];
    }
}
