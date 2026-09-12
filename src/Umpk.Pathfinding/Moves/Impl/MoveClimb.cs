using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>Climb up or down a ladder/vine at the current X,Z position.</summary>
public sealed class MoveClimb : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Climb;

    /// <inheritdoc/>
    public int XOffset => 0;

    /// <inheritdoc/>
    public int ZOffset => 0;

    /// <inheritdoc/>
    public bool DynamicY => false;

    private readonly bool _up;

    /// <summary>Creates an up or down climb move.</summary>
    public MoveClimb(bool up)
    {
        _up = up;
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.AllowClimb)
        {
            result.SetImpossible();
            return;
        }

        BlockState current = ctx.GetBlock(x, y, z);
        if (!current.IsClimbable)
        {
            result.SetImpossible();
            return;
        }

        int destY = _up ? y + 1 : y - 1;
        BlockState destination = ctx.GetBlock(x, destY, z);

        // The cell the body's FEET move into has to be one the body may be in, and neither arm ever asked. Both decided on `IsClimbable || !IsSolid`, and BlockAttributeResolver.ClassifyShape reserves Solid for a single full unit cube, so lava, fire, soul fire, a cactus, a campfire and powder snow are every one of them "not solid" and every one of them was a legal rung. The down arm is the reachable half in practice, but the up arm has the same requirement for scaffolding. State the guard once, before both arms, against their shared destination.
        //
        // The fluid clause is not redundant with the hazard clause. IsHazard clears lava under fire resistance, and CanWalkThrough refuses a non-water fluid outright whatever the potion says ('s `if (state.IsFluid) return false;`): the effect stops fire DAMAGE, and this library has no lava-traversal cost model to plan a swim through it with. Refusing here keeps the climb family saying what every other family already says.
        if (MoveHelper.IsHazard(ctx, destination)
            || (destination.IsFluid && !MoveHelper.IsWater(destination)))
        {
            result.SetImpossible();
            return;
        }

        if (_up)
        {
            if (!ctx.CanWalkThrough(x, destY + 1, z))
            {
                result.SetImpossible();
                return;
            }

            if (destination.IsClimbable)
            {
                result.Set(x, destY, z, ActionCosts.LadderUpOne);
                return;
            }

            if (!destination.IsSolid && ctx.CanWalkOn(x, destY - 1, z))
            {
                result.Set(x, destY, z, ActionCosts.LadderUpOne);
                return;
            }

            result.SetImpossible();
        }
        else
        {
            if (destination.IsClimbable || !destination.IsSolid)
            {
                result.Set(x, destY, z, ActionCosts.LadderDownOne);
                return;
            }

            result.SetImpossible();
        }
    }
}
