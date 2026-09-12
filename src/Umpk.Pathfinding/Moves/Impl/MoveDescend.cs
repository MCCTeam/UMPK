using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>Walk off a ledge and drop 1-N blocks in a cardinal direction. Short drops use a simple scan; longer drops delegate to a dynamic scan supporting water landings and mid-fall ladder grabs.</summary>
public sealed class MoveDescend : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Descend;

    /// <inheritdoc/>
    public int XOffset { get; }

    /// <inheritdoc/>
    public int ZOffset { get; }

    /// <inheritdoc/>
    public bool DynamicY => true;

    /// <summary>Creates a descend move with a fixed horizontal offset.</summary>
    public MoveDescend(int xOffset, int zOffset)
    {
        XOffset = xOffset;
        ZOffset = zOffset;
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        int destX = x + XOffset;
        int destZ = z + ZOffset;

        if (!ctx.CanWalkThrough(destX, y, destZ) || !ctx.CanWalkThrough(destX, y + 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (!ctx.CanWalkThrough(destX, y - 1, destZ))
        {
            result.SetImpossible();
            return;
        }

        if (ctx.GetBlock(x, y - 1, z).IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        if (ctx.CanWalkOn(destX, y - 2, destZ))
        {
            BlockState landOn = ctx.GetBlock(destX, y - 2, destZ);
            if (MoveHelper.IsHazard(ctx, landOn))
            {
                result.SetImpossible();
                return;
            }

            if (ctx.GetBlock(destX, y - 1, destZ).IsClimbable)
            {
                result.SetImpossible();
                return;
            }

            // A node-Y drop of one is not a drop of one BLOCK. Stepping off a carpet onto the bare floor beside it loses 0.0625; off a slab, 0.5; off a snow layer, whatever that layer is. All of those the body walks, and charging them a step off a ledge plus a full one-block fall (8.71 ticks against 3.56) both over-prices the move and hands it to DescendTemplate, which brakes for a landing that never happens. Only a genuine drop past the auto-step is a Descend. See MoveHelper.IsWalkedStep. The destination's FEET cell is y - 1, not the support at y - 2: the charge is resolved from the feet down, exactly as the engine resolves it. See MoveHelper.FloorSpeedPenalty.
            double floorPenalty = MoveHelper.FloorSpeedPenalty(ctx, destX, y - 1, destZ);

            if (MoveHelper.IsWalkedStep(ctx, x, y, z, XOffset, ZOffset, y - 1))
            {
                result.Set(
                    destX, y - 1, destZ,
                    ctx.WadeCostThrough(x, y, z, XOffset, -1, ZOffset) * floorPenalty,
                    MoveType.Traverse);
                return;
            }

            // The walk-off term carries the destination floor's charge for the same reason the walked arm above does, and for the same reason JumpFeasibility's diagonal descend does: two arms that land in the same cell have to be priced against the same floor or the cheaper one wins for a reason that is not about the world. The FALL term is gravity and is not scaled.
            double cost = (ActionCosts.WalkOffBlock * floorPenalty) + ActionCosts.FallCost(1);
            result.Set(destX, y - 1, destZ, cost);
            return;
        }

        DynamicFallCost(ctx, x, y, z, destX, destZ, ref result);
    }

    private static void DynamicFallCost(CalculationContext ctx, int x, int y, int z, int destX, int destZ, ref MoveResult result)
    {
        _ = x;
        _ = z;
        if (!ctx.CanWalkThrough(destX, y - 2, destZ))
        {
            result.SetImpossible();
            return;
        }

        double costSoFar = 0;
        int effectiveStartHeight = y;

        // The same distinction MoveFall draws: a drop whose whole column is water is the passive sink, not a fall. The scan below starts at fallHeight 3, so the two cells it steps over have to be tested here.
        bool wholeDropInWater = MoveHelper.IsWater(ctx.GetBlock(x, y, z))
            && MoveHelper.IsWater(ctx.GetBlock(destX, y - 1, destZ))
            && MoveHelper.IsWater(ctx.GetBlock(destX, y - 2, destZ));

        int maxScan = ctx.MaxFallHeightWater > ctx.MaxFallHeight
            ? ctx.MaxFallHeightWater
            : ctx.MaxFallHeight;

        for (int fallHeight = 3; fallHeight <= maxScan; fallHeight++)
        {
            int newY = y - fallHeight;
            if (newY < -64)
                break;

            BlockState onto = ctx.GetBlock(destX, newY, destZ);

            int unprotectedFallHeight = fallHeight - (y - effectiveStartHeight);
            double tentativeCost = ActionCosts.WalkOffBlock + ActionCosts.FallCost(unprotectedFallHeight) + costSoFar;

            // The same S1b guard MoveFall's water arm carries, for the same reason: this arm lands the body IN the cell it stops at, and a waterlogged solid is water and a collision box at once. See .
            if (MoveHelper.IsWater(onto) && ctx.AllowSwim && !onto.BlocksMotion)
            {
                double waterCost = wholeDropInWater
                    ? ActionCosts.WalkOffBlock + (ActionCosts.WaterSinkOneBlock * fallHeight) + costSoFar
                    : tentativeCost;
                result.Set(destX, newY, destZ, waterCost);
                return;
            }

            if (ctx.AllowLadderGrabDuringFall && unprotectedFallHeight <= 11 && onto.IsClimbable)
            {
                costSoFar += ActionCosts.FallCost(unprotectedFallHeight - 1);
                costSoFar += ActionCosts.LadderDownOne;
                effectiveStartHeight = newY;
                wholeDropInWater = false;
                continue;
            }

            if (ctx.CanWalkThrough(destX, newY, destZ))
            {
                wholeDropInWater = wholeDropInWater && MoveHelper.IsWater(onto);
                continue;
            }

            if (MoveHelper.IsHazard(ctx, onto))
            {
                result.SetImpossible();
                return;
            }

            if (!ctx.CanWalkOn(destX, newY, destZ))
            {
                result.SetImpossible();
                return;
            }

            // A drop that SINKS is not a landing, and it is asked before the height and affordability gates because those two price a landing that will not happen. unprotectedFallHeight counts the SUPPORT cell too, so the body's own drop is one less.
            if (ctx.LandingSinksThroughPowderSnow(onto, unprotectedFallHeight - 1))
            {
                result.SetImpossible();
                return;
            }

            // The height gate is the caller's policy and the affordability gate is the player's own health; see CalculationContext.LandingIsAffordable, and MoveFall for the same pair.
            if ((unprotectedFallHeight <= ctx.MaxFallHeight + 1
                    || SurvivesTheLanding(ctx, onto, unprotectedFallHeight))
                && ctx.LandingIsAffordable(onto, unprotectedFallHeight))
            {
                result.Set(destX, newY + 1, destZ, tentativeCost);
                return;
            }

            result.SetImpossible();
            return;
        }

        result.SetImpossible();
    }

    /// <summary>Whether a landing the ordinary height gate refuses is nonetheless one the plan may take, because the floor absorbs the fall.</summary>
    /// <remarks>
    /// <para><b>Additive, never subtractive.</b> The height gate above is untouched, so every fall this planner accepted yesterday it accepts today at the same price. This arm only ADMITS drops the height gate refused, and only onto a floor whose landing multiplier makes them survivable: slime at 0.0 and hay at 0.2. Everything else keeps the 1.0 multiplier and is refused by the budget at any height the gate already refused.</para>
    /// <para>Slime needs no budget at all - a zero multiplier is zero at any height - which is why course rows N1 and C8-slime flip on an unobserved session too, and why N2 and C8-hay need a session that can see the player's health. That asymmetry is vanilla's, not a policy.</para>
    /// </remarks>
    private static bool SurvivesTheLanding(CalculationContext ctx, BlockState onto, int fallHeight)
    {
        double multiplier = FallDamageModel.MultiplierFor(onto);
        if (multiplier >= 1.0)
            return false;

        return FallDamageModel.Damage(fallHeight, multiplier) <= ctx.FallDamageBudget;
    }
}
