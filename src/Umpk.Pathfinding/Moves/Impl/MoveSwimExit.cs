using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>
/// Water-exit move (swim move family: water exit onto land, the "swim-to-ledge climb-out"). From a water cell, climb out onto an adjacent solid ledge that is level with or one block above the water surface. This is the counterpart to the parkour takeoff gate, which excludes wet-footed jumps so the swim family can price them correctly.
///
/// <para>One block above is the ceiling, and it is measured rather than conventional. The lift is 0.3, reapplied every tick while the body is still touching the fluid; the moment it is not, the rise is ballistic against gravity. Driven at Forward+Jump against a two-tall bank over a surface plane at y=65.0, this engine tops out at y=65.9203 on protocols 47, 340, 477 and 776 - it clears a deck at +1 and never reaches the y=66.0 a deck at +2 would need (<c>Umpk.Physics.Tests.FluidLedgeHopTests.SurfaceHop_ReachesOneBlockOverThe WaterlineButNeverTwo</c>). A two-up arm would therefore plan a move no executor can run.</para>
/// </summary>
public sealed class MoveSwimExit : IMove
{
    /// <inheritdoc/>
    public MoveType Type => MoveType.Swim;

    /// <inheritdoc/>
    public int XOffset { get; }

    /// <inheritdoc/>
    public int ZOffset { get; }

    /// <inheritdoc/>
    public bool DynamicY => true;

    /// <summary>Creates a cardinal water-exit move.</summary>
    public MoveSwimExit(int xOffset, int zOffset)
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

        if (!MoveHelper.IsWater(ctx.GetBlock(x, y, z)))
        {
            result.SetImpossible();
            return;
        }

        int destX = x + XOffset;
        int destZ = z + ZOffset;

        // Level exit: step onto a solid ledge at the same Y whose surface supports the player. The ledge cells must be DRY, not merely walk-through: CanWalkThrough admits water whenever AllowSwim is set, so a flooded ledge read as an exit the engine cannot perform.
        if (ctx.CanWalkOn(destX, y - 1, destZ)
            && IsDryPassage(ctx, destX, y, destZ)
            && IsDryPassage(ctx, destX, y + 1, destZ))
        {
            result.Set(destX, y, destZ, ctx.SwimCostThrough(x, y, z, XOffset, 0, ZOffset) + ctx.JumpPenalty);
            return;
        }

        // One-up exit: hop out of the water onto a ledge one block above (climb-out). This is the one arm that depends on vanilla's ledge hop, so it carries that gate's own preconditions.
        int upY = y + 1;
        if (ctx.CanWalkOn(destX, y, destZ)
            && IsDryPassage(ctx, destX, upY, destZ)
            && IsDryPassage(ctx, destX, upY + 1, destZ)
            && IsDryPassage(ctx, x, upY, z)
            && IsDryPassage(ctx, x, upY + 1, z))
        {
            result.Set(destX, upY, destZ, ctx.SwimCostThrough(x, y, z, XOffset, 1, ZOffset) + (ctx.JumpPenalty * 2));
            return;
        }

        result.SetImpossible();
    }

    /// <summary>A cell the climb-out probe can pass through: passable AND free of liquid.</summary>
    /// <remarks>
    /// <para>The raised probe box must be free of both collisions and liquid, and A fluid-exit hop is the only action that lifts a swimmer onto a bank. The planner's <see cref="Umpk.Pathfinding.Moves.MoveHelper.CanWalkThrough"/> answers a different question and admits water outright when <c>AllowSwim</c> is set, so an exit under an overhang or in a corner between pools was approved BECAUSE the cells were water and then never executed: the 0.3 lift simply never fires and the template grinds into the bank until its stuck counter trips.</para>
    /// <para>The swimmer's own column is in that probe box too. The ledge hop zeroes the collided horizontal component before probing, so the box sits over the swimmer's own X/Z raised by 0.6 of the tick's net travel: cells <c>(x, y+1, z)</c> and <c>(x, y+2, z)</c>. Only <c>y+2</c> was ever checked, so a swimmer with a stone slab directly over its head was told to climb out through it - which is precisely the shape of the pathfinding course's E11 elevator.</para>
    /// </remarks>
    private static bool IsDryPassage(CalculationContext ctx, int x, int y, int z)
    {
        BlockState state = ctx.GetBlock(x, y, z);
        return !state.IsFluid && !state.IsWaterlogged && ctx.CanWalkThrough(x, y, z);
    }
}
