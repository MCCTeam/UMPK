using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>Vertical swim within a water column (swim move family: vertical ascend/descend). Ascend rises through water, descend dives deeper. Both require the current cell to be water and the destination to be a water cell the body can occupy; neither ever leaves the fluid, because nothing in the engine can hold a body whose feet are in the air over a pool.</summary>
public sealed class MoveSwimVertical : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Swim;

    /// <inheritdoc/>
    public int XOffset => 0;

    /// <inheritdoc/>
    public int ZOffset => 0;

    /// <inheritdoc/>
    public bool DynamicY => true;

    private readonly bool _up;

    /// <summary>Creates an ascend (up) or descend (down) swim move.</summary>
    public MoveSwimVertical(bool up)
    {
        _up = up;
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.AllowSwim)
        {
            result.SetImpossible();
            return;
        }

        if (!MoveHelper.IsWater(ctx.GetBlock(x, y, z)))
        {
            result.SetImpossible();
            return;
        }

        if (_up)
        {
            int destY = y + 1;

            // Ascend into a water cell that can actually hold a 1.8-tall body: water at the feet AND a head cell that is not a ceiling. Accepting on the destination alone planned a node in a one-block-tall pocket. Both arms of the swim graph use the same occupancy rule.
            //
            // There is deliberately no surface-break arm here. An air destination over the top water cell would plant the feet at the waterline, which is a state the engine cannot hold: the body stops touching the fluid, TravelInAir applies full gravity, and it drops straight back in. Anything planned out of such a node is planned out of a position that never exists - an Ascend onto a bank in particular, whose `OnGround || inFluid` jump gate never opens there. The top water cell is already the surfacing node (a body floating in it has its eye above the waterline and is breathing), and the climb-out onto a bank belongs to MoveSwimExit, which gates it on the physics that actually performs it.
            if (MoveHelper.CanSwimTo(ctx, x, destY, z))
            {
                // An UPWARD bubble column lifts the body far faster than it can swim, and the rate is measured rather than derived: see ActionCosts.BubbleColumnUpOneBlock. Both cells have to be column, so the last rise out of a column into plain water is priced as the swim it is. A DOWNWARD column is not special-cased anywhere: its downdraft is unmodelled, so it is priced, and behaves, as the plain water it also is.
                bool lifted = MoveHelper.IsUpwardBubbleColumn(ctx.GetBlock(x, y, z))
                    && MoveHelper.IsUpwardBubbleColumn(ctx.GetBlock(x, destY, z));

                result.Set(
                    x,
                    destY,
                    z,
                    lifted ? ActionCosts.BubbleColumnUpOneBlock : ctx.SwimCostThrough(x, y, z, 0, 1, 0));
                return;
            }

            result.SetImpossible();
            return;
        }

        // The descend arm asked IsWater(below) and nothing else, which stopped being the same question as "can a body be there" when S1b widened IsWater to the engine's own predicate: a waterlogged fence, stair, slab, wall, chest or trapdoor is water AND a collision box, and a swimmer occupies neither half of one. The ascend arm above has always asked through CanTraverseWater, so the two arms of the same graph disagreed about the same cell. They ask the same question now, floor included: a dive onto a magma bed plants the body on the hazard exactly as a level swim does.
        int downY = y - 1;
        if (!MoveHelper.CanSwimTo(ctx, x, downY, z))
        {
            result.SetImpossible();
            return;
        }

        result.Set(x, downY, z, ctx.SwimCostThrough(x, y, z, 0, -1, 0));
    }
}
