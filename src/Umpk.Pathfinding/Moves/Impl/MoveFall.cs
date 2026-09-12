using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>A straight-down fall at the current X,Z position. Supports water landings and mid-fall ladder/vine grabbing.</summary>
public sealed class MoveFall : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Fall;

    /// <inheritdoc/>
    public int XOffset => 0;

    /// <inheritdoc/>
    public int ZOffset => 0;

    /// <inheritdoc/>
    public bool DynamicY => true;

    private readonly int _maxScanDepth;

    /// <summary>Creates a fall move with an optional scan-depth cap.</summary>
    public MoveFall(int maxScanDepth = 256)
    {
        _maxScanDepth = maxScanDepth;
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.CanWalkThrough(x, y - 1, z))
        {
            result.SetImpossible();
            return;
        }

        double costSoFar = 0;
        int effectiveStartHeight = y;

        // A drop that BEGINS in water is not a fall, it is a sink: the body never leaves the fluid, so the air fall table does not describe it. See the water arm below.
        bool startsInWater = MoveHelper.IsWater(ctx.GetBlock(x, y, z));

        for (int fallDist = 1; fallDist <= _maxScanDepth; fallDist++)
        {
            int landY = y - fallDist;
            if (landY < -64)
                break;

            BlockState onto = ctx.GetBlock(x, landY, z);
            int unprotectedFallHeight = fallDist - (y - effectiveStartHeight);

            // The water arm's own S1b guard. `IsWater` is the engine's predicate, `(IsFluid && !IsLava) || IsWaterlogged`, so "there is water here" stopped being the same question as "a body fits here": a waterlogged stair, fence, slab, wall, chest or trapdoor is water AND a collision box, and this arm lands the body IN the cell it stops at. It also handed that landing the splash price, which MoveHelper.AbsorbsFallDamage already refuses to grant for exactly the same cell. A motion-blocking cell falls through to the arms below and is treated as what it is, a floor: the body lands ON it and pays the fall.
            if (MoveHelper.IsWater(onto) && ctx.AllowSwim && !onto.BlocksMotion)
            {
                // The scan stops at the FIRST water cell, so `fallDist == 1` with a wet start is the only shape in which the whole drop happened inside water. That one is the passive sink, 40 ticks a block, not the 5 the fall table charges: the repaired-E18 plan (Descend x1 + Fall x38) is priced at 202.7 ticks by the table and takes about 1520, which is how a bot sinks 40 blocks quietly and drowns at tick 320 without any segment ever exceeding its budget.
                if (startsInWater && fallDist == 1)
                {
                    result.Set(x, landY, z, ActionCosts.WaterSinkOneBlock + costSoFar);
                    return;
                }

                // MaxFallHeightIntoWater is the feasibility bound for the downward scan. Course row C7, a 25-block drop into a basin, is refused unless the caller enables UnsafeFalls.
                //
                // The height compared is the UNPROTECTED one, the distance since the last ladder grab, because that is the distance the body is actually exposed to. And it is compared without the solid arm's `+ 1`: this arm lands the body IN the water cell it found, so it descends exactly `unprotectedFallHeight` blocks, where the solid arm lands on TOP of the block it found and descends one fewer.
                if (unprotectedFallHeight > ctx.MaxFallHeightWater)
                {
                    result.SetImpossible();
                    return;
                }

                result.Set(x, landY, z, ActionCosts.FallCost(unprotectedFallHeight) + costSoFar);
                return;
            }

            if (ctx.AllowLadderGrabDuringFall && unprotectedFallHeight <= 11 && onto.IsClimbable)
            {
                costSoFar += ActionCosts.FallCost(unprotectedFallHeight - 1);
                costSoFar += ActionCosts.LadderDownOne;
                effectiveStartHeight = landY;
                continue;
            }

            if (ctx.CanWalkThrough(x, landY, z))
                continue;

            if (!ctx.CanWalkOn(x, landY, z))
            {
                result.SetImpossible();
                return;
            }

            if (MoveHelper.IsHazard(ctx, onto))
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

            // Two gates, and they ask different questions. The HEIGHT gate is the caller's policy - UnsafeFalls raises it to the world span on purpose - and the AFFORDABILITY gate is the player's own health, which no policy flag can raise. See CalculationContext.LandingIsAffordable: with no vitals observed it answers true and nothing here changes.
            if ((unprotectedFallHeight <= ctx.MaxFallHeight + 1
                    || SurvivesTheLanding(ctx, onto, unprotectedFallHeight))
                && ctx.LandingIsAffordable(onto, unprotectedFallHeight))
            {
                double cost = ActionCosts.FallCost(unprotectedFallHeight) + costSoFar;
                result.Set(x, landY + 1, z, cost);
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
