using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>
/// Cardinal swim traversal within a water volume (the swim move family). Covers both surface swimming and submerged swimming: the start feet cell must be water (the swimmer is already in the pool), and the destination cell must be a passable water column. Entry into water from land is a walk/descend/fall into a water block; exit onto land is handled by the (relaxed) walk and ascend moves, so this move models the in-water portion only.
///
/// <para>Swim support consists of water-column passability (<see cref="MoveHelper.CanTraverseWater"/>), a dedicated <see cref="MoveType.Swim"/> classification, and a cost calibrated against observed swim speed.</para>
/// </summary>
public sealed class MoveSwim : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Swim;

    /// <inheritdoc/>
    public int XOffset { get; }

    /// <inheritdoc/>
    public int ZOffset { get; }

    /// <inheritdoc/>
    public bool DynamicY => false;

    /// <summary>Creates a cardinal swim move.</summary>
    public MoveSwim(int xOffset, int zOffset)
    {
        XOffset = xOffset;
        ZOffset = zOffset;
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

        // The swimmer must already be in water (feet cell is water). Otherwise a walk move covers it.
        if (!MoveHelper.IsWater(ctx.GetBlock(x, y, z)))
        {
            result.SetImpossible();
            return;
        }

        int destX = x + XOffset;
        int destZ = z + ZOffset;

        BlockState dest = ctx.GetBlock(destX, y, destZ);

        // Swim into another water cell, or swim to a water-edge cell whose feet are air but which sits over water (allows crossing the surface into a shallow step).
        if (MoveHelper.IsWater(dest))
        {
            // Both halves of the destination question, and the second one is not decoration. The water column has to hold a body (CanTraverseWater), and the FLOOR that body would come to rest on has to be one the plan may touch. In a shallow lane a swim node's feet are flush on the bed, so a lane over magma must not be planned as Swim segments sitting on the hazard. See MoveHelper.CanSwimTo for why the condition is contact rather than proximity, and why deep water is unaffected.
            if (!MoveHelper.CanSwimTo(ctx, destX, y, destZ))
            {
                result.SetImpossible();
                return;
            }

            result.Set(destX, y, destZ, ctx.SwimCostThrough(x, y, z, XOffset, 0, ZOffset));
            return;
        }

        result.SetImpossible();
    }
}
