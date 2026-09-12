using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>Sprint off a ledge and land 2 blocks away horizontally while dropping 1-3 blocks. Supports cardinal (2,0)/(0,2) and diagonal (1,1) offsets.</summary>
public sealed class MoveSprintDescend : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Descend;

    /// <inheritdoc/>
    public int XOffset { get; }

    /// <inheritdoc/>
    public int ZOffset { get; }

    /// <inheritdoc/>
    public bool DynamicY => true;

    /// <summary>Creates a sprint-descend move with a fixed horizontal offset.</summary>
    public MoveSprintDescend(int xOffset, int zOffset)
    {
        XOffset = xOffset;
        ZOffset = zOffset;
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.CanSprint)
        {
            result.SetImpossible();
            return;
        }

        int destX = x + XOffset;
        int destZ = z + ZOffset;

        if (ctx.GetBlock(x, y - 1, z).IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        int xSign = Math.Sign(XOffset);
        int zSign = Math.Sign(ZOffset);
        int xAbs = Math.Abs(XOffset);
        int zAbs = Math.Abs(ZOffset);

        if (!ctx.CanWalkThrough(destX, y, destZ) || !ctx.CanWalkThrough(destX, y + 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (xAbs == 2 && zAbs == 0)
        {
            if (!ctx.CanWalkThrough(x + xSign, y, z) || !ctx.CanWalkThrough(x + xSign, y + 1, z))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (xAbs == 0 && zAbs == 2)
        {
            if (!ctx.CanWalkThrough(x, y, z + zSign) || !ctx.CanWalkThrough(x, y + 1, z + zSign))
            {
                result.SetImpossible();
                return;
            }
        }
        else
        {
            // A diagonal needs a shoulder. The body is 0.6 wide and leaves the source cell through the corner between (x + xSign, z) and (x, z + zSign); if BOTH of those columns are solid over the body's height there is no gap at the corner and the body wedges against the two faces, at x +/- halfWidth and z -/+ halfWidth. This is the same predicate the level diagonal walk has always had (JumpFeasibility.EvaluateWalk). Without it, the planner can accept a diagonal descent whose corner is blocked, leaving the body grounded against the obstacle. One clear shoulder is sufficient.
            bool sideX = ctx.CanWalkThrough(x + xSign, y, z) && ctx.CanWalkThrough(x + xSign, y + 1, z);
            bool sideZ = ctx.CanWalkThrough(x, y, z + zSign) && ctx.CanWalkThrough(x, y + 1, z + zSign);

            if (!sideX && !sideZ)
            {
                result.SetImpossible();
                return;
            }
        }

        if (xAbs > 0 && zAbs == 0)
        {
            if (ctx.CanWalkOn(x + xSign, y - 1, z))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (xAbs == 0 && zAbs > 0)
        {
            if (ctx.CanWalkOn(x, y - 1, z + zSign))
            {
                result.SetImpossible();
                return;
            }
        }
        else if (ctx.CanWalkOn(x + xSign, y - 1, z + zSign))
        {
            result.SetImpossible();
            return;
        }

        double horizDist = Math.Sqrt((double)((XOffset * XOffset) + (ZOffset * ZOffset)));
        for (int drop = 1; drop <= ctx.MaxFallHeight; drop++)
        {
            int landY = y - drop - 1;
            if (landY < -64)
                break;

            if (!ctx.CanWalkOn(destX, landY, destZ))
                continue;

            BlockState landMat = ctx.GetBlock(destX, landY, destZ);
            if (MoveHelper.IsHazard(ctx, landMat))
            {
                result.SetImpossible();
                return;
            }

            if (!ctx.CanWalkThrough(destX, landY + 1, destZ) || !ctx.CanWalkThrough(destX, landY + 2, destZ))
                continue;

            // A drop that SINKS is not a landing, and it is asked before the affordability gate because that gate prices a landing that will not happen. `drop` here IS the body's own fall in whole blocks (landY = y - drop - 1).
            if (ctx.LandingSinksThroughPowderSnow(landMat, drop))
            {
                result.SetImpossible();
                return;
            }

            // This arm's scan is bounded by MaxFallHeight alone, so UnsafeFalls lets it run the whole world span - which is how course row C7's plan reached a 25-block drop onto the stone lip beside its basin. The landing still has to be one the body can pay for; see CalculationContext.LandingIsAffordable.
            if (!ctx.LandingIsAffordable(landMat, drop))
            {
                result.SetImpossible();
                return;
            }

            double cost = horizDist * ctx.SprintCost + ActionCosts.FallCost(drop);
            result.Set(destX, landY + 1, destZ, cost);
            return;
        }

        result.SetImpossible();
    }
}
