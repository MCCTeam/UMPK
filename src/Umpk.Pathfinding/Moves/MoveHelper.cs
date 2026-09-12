using Umpk.Game.Blocks;
using Umpk.Pathfinding.Core;
using Umpk.Physics;

namespace Umpk.Pathfinding.Moves;

/// <summary>Block passability checks for planning against <see cref="BlockState"/> flags. <see cref="CanWalkThrough"/> admits water when swimming is allowed, and <see cref="CanTraverseWater"/> / <see cref="CanStandAt"/> enforce water-column occupancy.</summary>
public static class MoveHelper
{
    /// <summary>Can a player's body/head occupy this block position (air, open gate, grass, or swimmable water)?</summary>
    public static bool CanWalkThrough(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BlockState state = ctx.GetBlock(x, y, z);
        if (state.IsAir)
            return !ReachedIntoFromBelow(ctx, x, y, z);

        if (IsWater(state))
        {
            // Admit water only when the caller allows swimming; lava is always impassable.
            //
            // The `!BlocksMotion` half is not optional and must not be short-circuited past. Since IsWater took in waterlogged states, "there is water in this cell" and "a body fits in this cell" stopped being the same question: a waterlogged fence, stair, slab, wall, chest or trapdoor is water AND a collision box. Returning AllowSwim on the water alone
            // let the planner route a swim through a solid.
            return ctx.AllowSwim && !state.BlocksMotion;
        }

        if (state.IsFluid)
        {
            // Lava (or any non-water fluid).
            return false;
        }

        if (state.IsClimbable)
            return true;

        if (IsHazard(ctx, state))
            return false;

        // Powder snow a booted body may WALK ON is a wall to that same body, and this arm is the half of the feature an implementation that only clears the hazard forgets. Collision handling A body above the cell receives a full cube, while a body inside receives an empty shape, so the plan may stand on the cell and may never occupy it. Without this the state falls through to the "shapeless is passable" arm below - powder snow carries no BlocksMotion flag - and the planner plants a node with the feet inside the snow, standing on whatever is beneath it, in a cell the executor cannot get into.
        if (ctx.PowderSnowWalkable && state.IsPowderSnow)
            return false;

        if (!state.BlocksMotion)
        {
            // Anything that does not block motion (open gates, tall grass, flowers, buttons) is passable.
            return !ReachedIntoFromBelow(ctx, x, y, z);
        }

        // The one family whose passability is a PROPERTY rather than a shape. See BarrierKind. A CLOSED barrier the plan is allowed to open counts too: the plan pays ActionCosts.InteractLatency for it at the edge that crosses it, so admitting the cell here is not a free lunch.
        if (!ctx.MayContainBarrier)
            return false;

        return ClassifyBarrier(ctx, state, x, y, z) switch
        {
            BarrierKind.PassableNow => true,
            BarrierKind.NeedsInteraction or BarrierKind.NeedsActivator => ctx.AllowDoorInteraction,
            _ => false,
        };
    }

    /// <summary><see cref="ClassifyBarrier(BlockState)"/>, plus the one answer a state alone cannot give: whether a closed IRON door or trapdoor has a switch that opens it.</summary>
    /// <remarks>
    /// <para>The pure overload returns <see cref="BarrierKind.Wall"/> for both "closed iron" and "this source cannot read <c>open</c>", and only the first of those can be promoted. The <c>open</c> read is repeated here rather than inferred, so a pre-flattening state - where every property answers false - is never handed to an activator search that would then reason about a door it cannot even tell is shut.</para>
    /// <para>The search itself is memoised per door on the context, because it is a neighbourhood scan and this predicate runs on every node evaluation.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="state">The state at the cell.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="y">The cell's Y.</param>
    /// <param name="z">The cell's Z.</param>
    /// <returns>The barrier classification, including <see cref="BarrierKind.NeedsActivator"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static BarrierKind ClassifyBarrier(CalculationContext ctx, BlockState state, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BarrierKind kind = ClassifyBarrier(state);
        if (kind != BarrierKind.Wall
            || !ctx.AllowDoorInteraction
            || !state.TryGetProperty(OpenProperty, out _))
            return kind;

        return ctx.TryResolveActivation(new Geometry.BlockPos(x, y, z), out _)
            ? BarrierKind.NeedsActivator
            : BarrierKind.Wall;
    }

    /// <summary>Whether a barrier in the cell below reaches up into this one. A closed fence gate's collision column tops out at <b>1.5</b> and half a block of it stands inside the cell above, which the shape table records as <c>[[0,0,0.375,1,1.5,0.625]]</c>. Reading that cell as air is incorrect, and it is not a shape question a caller can ask of the cell itself: the cell is genuinely empty, and what is in it belongs to its neighbour.</summary>
    /// <remarks>Scoped to the barrier family on purpose, not generalised to "anything whose shape exceeds 1.0". A fence post and a wall reach 1.5 the same way and the same reasoning applies to them, but that is intentionally remains outside this barrier-specific rule. The check is bounded by <see cref="PlanningWorldView.MayContainBarrier"/> to regions that hold a door, trapdoor or gate at all.</remarks>
    private static bool ReachedIntoFromBelow(CalculationContext ctx, int x, int y, int z)
        => ctx.MayContainBarrier
            && ProtrudesIntoCellAbove(ctx.GetBlock(x, y - 1, z), ctx.AllowDoorInteraction);

    /// <summary>What this state is to a plan as a door, trapdoor or fence gate. The rule uses one property read.</summary>
    /// <remarks>The family is detected by registry-id suffix (<c>_door</c>, <c>_trapdoor</c>, <c>_fence_gate</c>), which covers all 12 wood doors, the 8 copper doors, <c>iron_door</c>, all 21 trapdoors and all 12 fence gates without an enumeration to keep in sync. A modded or future block that ends the same way but carries no <c>open</c> property classifies as <see cref="BarrierKind.Wall"/>, which is also the default predicate's answer, so the suffix rule cannot make anything MORE passable than it was.</remarks>
    /// <param name="state">The state to classify.</param>
    /// <returns>The barrier classification.</returns>
    public static BarrierKind ClassifyBarrier(BlockState state)
    {
        if (state.IsDefault || !IsBarrierFamily(state))
            return BarrierKind.None;

        // Pre-flattening sources answer false for every property name, so protocols 47-340 land here. Refuse rather than decode a metadata bit on a guess. This leaves those eras exactly where the default predicate already includes them.
        if (!state.TryGetProperty(OpenProperty, out string open))
            return BarrierKind.Wall;

        if (open == TrueValue)
            return BarrierKind.PassableNow;

        return CanOpenByHand(state) ? BarrierKind.NeedsInteraction : BarrierKind.Wall;
    }

    /// <summary>Whether a bare hand opens this barrier. False for exactly two blocks in the game, <c>iron_door</c> and <c>iron_trapdoor</c>.</summary>
    /// <remarks>
    /// <para>Only <c>iron_door</c> and <c>iron_trapdoor</c> are not hand-openable, so the enumeration is two ids rather than a set-type model this library has no dataset axis for. There is no metal fence gate, and every fence gate opens by hand.</para>
    /// <para>Matched on the registry PATH, like <see cref="IsBarrierFamily"/> itself, so a namespaced copy of an iron door in a mod's own namespace reads as hand-openable. That is the permissive direction on a block this library has no other information about, and it is bounded by the executor, which reads the door's own state back and replans when it did not move.</para>
    /// </remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True when a <c>use_item_on</c> with an empty hand would toggle it.</returns>
    public static bool CanOpenByHand(BlockState state)
        => state.Block.Id.Path is not (IronDoorPath or IronTrapdoorPath);

    /// <summary>Whether this state is an open door or trapdoor, i.e. a cell that is passable and still holds a 3/16 vertical PANEL against one of its faces.</summary>
    /// <remarks>An open fence gate is deliberately excluded because its collision shape is empty, so its cell is as empty as air. The distinction matters because everything the panel costs - the entry restriction, the lateral bias - is paid for by the 0.0125 blocks of clearance the panel leaves, and an open gate leaves 0.2 on both sides like any other empty cell.</remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True for an open door or an open trapdoor.</returns>
    public static bool IsPanelBarrier(BlockState state)
        => ClassifyBarrier(state) == BarrierKind.PassableNow
            && !state.Block.Id.Path.EndsWith(FenceGateSuffix, StringComparison.Ordinal);

    /// <summary>Whether CLOSING this barrier turns its cell from a wall into a floor: an OPEN trapdoor whose <c>half</c> is <c>bottom</c>, and nothing else in the game.</summary>
    /// <remarks>
    /// <para><b>Why the toggle needs a direction at all.</b> Every other member of this family is crossed by opening it. This one is the mirror: an open trapdoor is a vertical 3/16 panel on the face opposite <c>facing</c>, while its closed shape is keyed on <c>half</c>. Every open state maps to one of four vertical panels, every <c>half=bottom</c> closed state to <c>[[0,0,0,1,0.1875,1]]</c>, and every <c>half=top</c> closed state to ref 52 <c>[[0,0.8125,0,1,1,1]]</c>. So the closed shape is PREDICTABLE from a property this state already carries, exactly as <c>BarrierCrossing.TryPredictAfterOpening</c> predicts the open panel's side from <c>facing</c>.</para>
    /// <para><b>Why only this one.</b> Closing has to leave something a body can walk. A <c>half=bottom</c> trapdoor closes into a 3/16 slab: the cell becomes passable on every heading and standable at 0.1875, which is a quarter of vanilla's 0.6 step. A <c>half=top</c> trapdoor closes into a plate at 0.8125-1.0 of its own cell, which is inside the body that would have to stand there. A door closes into the panel across the crossing axis, and a fence gate closes into a 1.5-block-high column. Closing any of those three trades one wall for another, so the move class stops here rather than generalising.</para>
    /// <para>The <c>half</c> read must SUCCEED: on a pre-flattening source every property answers false and there is no way to tell which way a trapdoor would fold, so those eras keep the answer they already had.</para>
    /// </remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True for an open <c>half=bottom</c> trapdoor.</returns>
    public static bool ClosesIntoAFloor(BlockState state)
        => IsPanelBarrier(state)
            && state.Block.Id.Path.EndsWith(TrapdoorSuffix, StringComparison.Ordinal)
            && CanOpenByHand(state)
            && state.TryGetProperty(HalfProperty, out string half)
            && half == BottomValue;

    /// <summary>Whether a body standing at this cell would share its column with an open door or trapdoor panel, at feet height or at head height.</summary>
    /// <remarks>The head cell counts because an open trapdoor overhead is the same vertical panel at shoulder height, and a lane that clears the hips can still stop the shoulders.</remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="y">The FEET cell's Y.</param>
    /// <param name="z">The cell's Z.</param>
    /// <returns>True when either body cell holds a panel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static bool HasBarrierPanel(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.MayContainBarrier)
            return false;

        return WillHoldPanel(ctx, ctx.GetBlock(x, y, z)) || WillHoldPanel(ctx, ctx.GetBlock(x, y + 1, z));
    }

    /// <summary>Whether this cell holds a panel by the time the body walks into it: an open door or trapdoor already, or a CLOSED one this plan is going to open.</summary>
    /// <remarks>
    /// <para>A closed door is the same 3/16 panel as an open one, rotated ninety degrees onto the crossing axis. Opening it does not remove the obstacle, it turns it side-on - and the 0.0125 blocks of clearance that leaves is exactly what E1b's entry restriction exists for. So a doorway the plan will open is a doorway, and the same moves are refused into it.</para>
    /// <para><b>The heading half falls out for free, and that is worth stating rather than leaving as an omission.</b> <see cref="TryGetPanelSide"/> reads the OPEN panel's side off the shape, and a closed state's shape is the wrong one, so a closed barrier contributes no side and <see cref="IsBarrierCrossingLegal"/>'s heading test passes it. That is sound rather than lax: the rotation is ninety degrees, so the crossing a CLOSED door blocks is, by construction, the one that runs ALONG the panel once it is open. A heading the closed door did not block never needed to cross it in the first place.</para>
    /// <para>A fence gate is excluded for the same reason <see cref="IsPanelBarrier"/> excludes an open one: its open shape is empty, so there is never a panel to scrape.</para>
    /// </remarks>
    private static bool WillHoldPanel(CalculationContext ctx, BlockState state)
    {
        if (IsPanelBarrier(state))
        {
            // ...unless the plan is going to fold it flat. See ClosesIntoAFloor: an open trapdoor whose closed shape is the 3/16 floor slab is not a panel by the time the body arrives, it is a step, and the move that enters its cell pays ActionCosts.InteractLatency for saying so.
            return !(ctx.AllowDoorInteraction && ClosesIntoAFloor(state));
        }

        if (!ctx.AllowDoorInteraction || state.Block.Id.Path.EndsWith(FenceGateSuffix, StringComparison.Ordinal))
            return false;

        // An iron door opened by a switch leaves the same rotated panel a hand-opened one does, so the entry restriction has to cover it too. The cell coordinates are not needed to answer that: the pure classification already separates "closed and openable somehow" from "wall", and the only states it can call Wall-but-openable are the two iron ones, which the caller has already established are in this column.
        BarrierKind kind = ClassifyBarrier(state);
        return kind == BarrierKind.NeedsInteraction
            || (kind == BarrierKind.Wall && !CanOpenByHand(state) && state.TryGetProperty(OpenProperty, out _));
    }

    /// <summary>The barrier a move has to open before its body may make it, if any: the door in the destination column, or the lid the body is standing on when it drops out of its own cell.</summary>
    /// <remarks>
    /// <para><b>The DESTINATION column, from the destination's feet up to whichever is higher, its own head cell or the source's feet.</b> On a level crossing that is two cells, the feet and the head, which is a whole two-block door. One interaction opens both halves because the <c>open</c> property propagates from the clicked half to the other. On an ascend it is the same two cells. On a DESCEND or a FALL it is the whole swept column, because that is what the body passes through on its way down and a barrier anywhere in it is what stops the drop.</para>
    /// <para>The swept column is not a generalisation for its own sake, it is the only way to see a LID. A closed TOP trapdoor sits flush with the floor of the cell above it, so a body standing on one and dropping through meets the barrier neither at its destination (clear air, three cells down) nor at its own feet, but between them. Course rows G5a and L5 are exactly that shape, and they are reached by a lateral <c>Descend</c> as readily as by a straight <c>Fall</c>: the plan this rule was first written against steps sideways into the shaft column at <c>(4,100,1) -&gt; (5,97,1)</c> and passes through the lid at <c>(5,99,1)</c>, which a resolver looking only under the source's feet reads as a free fall through solid ground.</para>
    /// <para>Leaving a doorway is deliberately free: the source column is never consulted, so the segment that walks out of the door does not pay for the door a second time.</para>
    /// <para><b>One barrier per move, and that bound is real.</b> The lowest one in the column wins and the scan stops. A column sealed by two lids at once is priced as one interaction and carries one requirement, which under-prices it; the shape does not occur in the course and closing it means a segment that can carry a LIST, which is a bigger change than the price is worth.</para>
    /// <para><b>A PLATE's press cell is the source cell by construction.</b> <see cref="IsBarrierCrossingLegal"/> refuses any edge into a plate-driven barrier whose source is not the plate's own cell, so by the time this runs the two are the same position and the reach-from-source arm below picks it for free (a body's eye trivially reaches the cell it is standing in). Nothing here special-cases it, and that is the point: the narrow design's whole claim is that a plate needs no separate press-cell machinery because the plan was already routing the body through the only cell that works.</para>
    /// <para><b>The press cell.</b> For a hand-opened barrier it is the move's own source cell, which is where the plan already puts the body. For an activator it is the source cell TOO whenever the switch is in reach from there, and the resolver's own stand cell otherwise - and the planner does not detour to that second one. Every course row in the L family places the switch in the approach cell, so the first arm covers them all; routing to an off-route press cell is a waypoint feature with its own machinery, and until it exists this is the honest bound.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="fromX">The source cell's X.</param>
    /// <param name="fromY">The source FEET cell's Y.</param>
    /// <param name="fromZ">The source cell's Z.</param>
    /// <param name="toX">The destination cell's X.</param>
    /// <param name="toY">The destination FEET cell's Y.</param>
    /// <param name="toZ">The destination cell's Z.</param>
    /// <param name="requirement">What has to happen, and from where.</param>
    /// <returns>True when the move needs an interaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static bool TryGetInteraction(
        CalculationContext ctx,
        int fromX, int fromY, int fromZ,
        int toX, int toY, int toZ,
        out Execution.InteractionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        requirement = default;
        if (!ctx.MayContainBarrier || !ctx.AllowDoorInteraction)
            return false;

        var from = new Geometry.BlockPos(fromX, fromY, fromZ);
        int top = Math.Max(toY + 1, fromY);
        for (int y = toY; y <= top; y++)
        {
            var at = new Geometry.BlockPos(toX, y, toZ);
            BlockState here = ctx.GetBlock(at.X, at.Y, at.Z);

            // The one arm that runs the toggle the other way. It is tested BEFORE the classification because an open barrier classifies as PassableNow and would otherwise fall straight through the default: the cell IS passable, and the price is for the SHAPE the plan needs it to have.
            if (ClosesIntoAFloor(here))
            {
                requirement = Execution.InteractionRequirement.CloseByHandAt(at, from);
                return true;
            }

            switch (ClassifyBarrier(ctx, here, at.X, at.Y, at.Z))
            {
                case BarrierKind.NeedsInteraction:
                    requirement = Execution.InteractionRequirement.OpenByHandAt(at, from);
                    return true;

                case BarrierKind.NeedsActivator:
                    if (!ctx.TryResolveActivation(at, out DoorActivation activation))
                    {
                        // Unreachable in practice - the classification above IS the resolve, memoised - but a barrier the plan cannot describe must not be reported as free.
                        continue;
                    }

                    // Press from the cell the plan already puts the body in when it can, and fall back to the resolver's own stand cell when it cannot. The planner does NOT detour to a press cell that is off the route; every course row places the switch in the approach cell, and a route that needs a detour is a feature with its own waypoint machinery.
                    if (activation.Kind == ActivatorKind.PressurePlate)
                    {
                        // Nothing is sent for a plate: the body IS the press. See InteractionKind.StandOnPlate for why sending a use_item_on at one is worse than useless rather than merely pointless.
                        requirement = Execution.InteractionRequirement.StandOnAt(
                            activation.StandCell, at, activation.WindowTicks);
                        return true;
                    }

                    Geometry.BlockPos press =
                        DoorActivation.CanReachFrom(from, activation.Activator) ? from : activation.StandCell;
                    requirement = Execution.InteractionRequirement.PressAt(
                        activation.Activator, at, press, activation.WindowTicks);
                    return true;

                default:
                    continue;
            }
        }

        return false;
    }

    /// <summary>The side of the cell an open door's or trapdoor's panel stands against, as a unit direction from the cell centre toward the panel.</summary>
    /// <remarks>Read off the SHAPE rather than off <c>facing</c> and <c>hinge</c>, because the shape is what the body collides with and it is one rule for doors, trapdoors, every facing and both hinges. A panel that hugs no single face is not recognised and the caller gets false rather than a guessed side.</remarks>
    /// <param name="world">The world to read.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="y">The cell's Y.</param>
    /// <param name="z">The cell's Z.</param>
    /// <param name="panelX">The X component of the direction toward the panel.</param>
    /// <param name="panelZ">The Z component of the direction toward the panel.</param>
    /// <returns>True when the cell holds a recognisable open panel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static bool TryGetPanelSide(
        IPhysicsWorldView world, int x, int y, int z, out int panelX, out int panelZ)
    {
        ArgumentNullException.ThrowIfNull(world);
        panelX = 0;
        panelZ = 0;

        BlockState state = world.GetBlock(new Geometry.BlockPos(x, y, z));
        if (!IsPanelBarrier(state))
            return false;

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minZ = double.PositiveInfinity;
        double maxZ = double.NegativeInfinity;
        foreach (Geometry.Aabb box in world.GetCollisionShapes(state))
        {
            minX = Math.Min(minX, box.MinX);
            maxX = Math.Max(maxX, box.MaxX);
            minZ = Math.Min(minZ, box.MinZ);
            maxZ = Math.Max(maxZ, box.MaxZ);
        }

        if (maxZ <= 0.5)
        {
            panelZ = -1;
            return true;
        }

        if (minZ >= 0.5)
        {
            panelZ = 1;
            return true;
        }

        if (maxX <= 0.5)
        {
            panelX = -1;
            return true;
        }

        if (minX >= 0.5)
        {
            panelX = 1;
            return true;
        }

        return false;
    }

    /// <summary>Whether a move from one cell to another may touch a barrier panel at all: a panel column may be entered, and left, ONLY by a cardinal <see cref="MoveType.Traverse"/> between adjacent cells whose heading runs ALONG the panel rather than into it.</summary>
    /// <remarks>
    /// <para><b>Why the move set and not the executor.</b> An open door leaves a 0.6-wide body 0.0125 blocks of clearance on the panel side, against 0.2 on the far side, and the measured pass band is a lateral offset of 0.5 to 0.7. A two-degree yaw error toward the panel already prevents passage. Nothing but a squared-up cardinal walk has the lateral authority to land inside that band: a diagonal arrives corner-on, a step arrives mid-rise, and a jump arrives with no steering authority at all. A plan that emits one of those is a plan the executor cannot walk, and the honest place to refuse it is where the move is emitted.</para>
    /// <para><b>Why the heading matters.</b> The panel is 3/16 thick along its own normal and spans the whole cell across it, so along the normal it is simply a wall: a body crossing that way meets its face. Only the two headings that run along the panel pass. That is the same distinction the closed state makes - a closed door is the same panel rotated onto the crossing axis - and it falls out of the geometry rather than out of the property.</para>
    /// <para>An open fence gate is not a panel (<see cref="IsPanelBarrier"/>) and is not restricted.</para>
    /// <para><b>And the one rule that is about WHICH cell rather than which move: a barrier whose only opener is a pressure plate may be ENTERED from that plate's cell and from nowhere else.</b> Every other activator is worked from a distance - the body presses a button and the door opens whichever way it then walks - so "there is a switch for this door" is a fact about the door alone and <see cref="CanWalkThrough"/> can carry it per cell. A plate is not: it opens the door only while a body occupies it, so the same door is passable from the plate's side and a wall from the other, and no per-cell predicate can say that. Putting it here costs nothing anywhere else, because the gate is already consulted on exactly these columns (<see cref="WillHoldPanel"/> answers true for a closed iron door) and returns early on terrain that holds no barrier at all.</para>
    /// <para>Note what this deliberately does NOT restrict: LEAVING a plate-driven doorway. The door is open by then, and a body stepping out of it - including back onto the plate - is doing nothing the plan has to prove.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="fromX">The source cell's X.</param>
    /// <param name="fromY">The source FEET cell's Y.</param>
    /// <param name="fromZ">The source cell's Z.</param>
    /// <param name="toX">The destination cell's X.</param>
    /// <param name="toY">The destination FEET cell's Y.</param>
    /// <param name="toZ">The destination cell's Z.</param>
    /// <param name="moveType">The move that would be emitted.</param>
    /// <returns>True when the move is allowed to touch whatever panels those columns hold.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static bool IsBarrierCrossingLegal(
        CalculationContext ctx, int fromX, int fromY, int fromZ, int toX, int toY, int toZ, MoveType moveType)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.MayContainBarrier)
            return true;

        bool fromPanel = HasBarrierPanel(ctx, fromX, fromY, fromZ);
        bool toPanel = HasBarrierPanel(ctx, toX, toY, toZ);
        if (!fromPanel && !toPanel)
            return true;

        int dx = toX - fromX;
        int dz = toZ - fromZ;
        if (moveType != MoveType.Traverse || toY != fromY || Math.Abs(dx) + Math.Abs(dz) != 1)
            return false;

        return (!fromPanel || HeadingRunsAlongThePanel(ctx, fromX, fromY, fromZ, dx, dz))
            && (!toPanel || HeadingRunsAlongThePanel(ctx, toX, toY, toZ, dx, dz))
            && (!toPanel || PlateEntryIsLegal(ctx, fromX, fromY, fromZ, toX, toY, toZ));
    }

    /// <summary>Whether the destination column's barrier, if a PLATE is the only thing that opens it, is being entered from that plate's own cell.</summary>
    /// <remarks>
    /// <para>The column is scanned feet-first and then head, the same two cells <see cref="TryGetInteraction"/> prices, because either half of a door carries the crossing and either half opens the pair when it receives a neighboring signal.</para>
    /// <para>Everything here is answered out of the per-door memo the classifier already fills, so this adds a dictionary lookup to an edge that was already doing one and nothing at all to an edge that touches no panel.</para>
    /// </remarks>
    private static bool PlateEntryIsLegal(
        CalculationContext ctx, int fromX, int fromY, int fromZ, int toX, int toY, int toZ)
    {
        for (int y = toY; y <= toY + 1; y++)
        {
            BlockState here = ctx.GetBlock(toX, y, toZ);
            if (ClassifyBarrier(ctx, here, toX, y, toZ) != BarrierKind.NeedsActivator
                || !ctx.TryResolveActivation(new Geometry.BlockPos(toX, y, toZ), out DoorActivation activation)
                || activation.Kind != ActivatorKind.PressurePlate)
                continue;

            if (activation.StandCell != new Geometry.BlockPos(fromX, fromY, fromZ))
                return false;

        }

        return true;
    }

    private static bool HeadingRunsAlongThePanel(CalculationContext ctx, int x, int y, int z, int dx, int dz)
        => RunsAlong(ctx.World, x, y, z, dx, dz) && RunsAlong(ctx.World, x, y + 1, z, dx, dz);

    private static bool RunsAlong(PlanningWorldView world, int x, int y, int z, int dx, int dz)
    {
        if (!TryGetPanelSide(world, x, y, z, out int panelX, out int panelZ))
            return true;

        // The dot product of the heading with the panel's normal: zero means the body slides along the panel, non-zero means it walks into its face.
        return (dx * panelX) + (dz * panelZ) == 0;
    }

    /// <summary>Whether this state is a door, trapdoor or fence gate at all, whatever its <c>open</c> value.</summary>
    /// <remarks>This is what <see cref="PlanningWorldView.MayContainBarrier"/> tests palettes with, so a region with no such block in it pays nothing for the family at all.</remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True when the state belongs to the barrier family.</returns>
    public static bool IsBarrierFamily(BlockState state)
    {
        if (state.IsDefault || state.IsAir)
            return false;

        string path = state.Block.Id.Path;
        return path.EndsWith(DoorSuffix, StringComparison.Ordinal)
            || path.EndsWith(TrapdoorSuffix, StringComparison.Ordinal)
            || path.EndsWith(FenceGateSuffix, StringComparison.Ordinal);
    }

    /// <summary>Whether this state's collision reaches into the cell ABOVE its own. True for a closed fence gate and nothing else in the family: doors and trapdoors top out at 1.0.</summary>
    /// <remarks>The <c>open</c> read must SUCCEED for this to answer true. On a source that cannot resolve the property there is no way to tell a closed gate from an open one, and answering true there would wall the cell above every open legacy gate, creating a false refusal. Same policy as <see cref="ClassifyBarrier(BlockState)"/>: where the era cannot say, leave the era alone.</remarks>
    private static bool ProtrudesIntoCellAbove(BlockState state, bool allowInteraction)
    {
        if (state.IsDefault
            || state.IsAir
            || !state.Block.Id.Path.EndsWith(FenceGateSuffix, StringComparison.Ordinal)
            || !state.TryGetProperty(OpenProperty, out string open)
            || open == TrueValue)
            return false;

        // A gate the plan is going to open has an empty collision shape, so both its own cell and the cell above it are as clear as air by the time the body arrives. Every gate is hand-openable - a gate takes a WoodType and there is no metal one - so this is the whole of the exception.
        return !allowInteraction;
    }

    private const string OpenProperty = "open";

    private const string TrueValue = "true";

    /// <summary>A trapdoor's <c>half</c>, which is what its CLOSED shape is keyed on.</summary>
    private const string HalfProperty = "half";

    private const string BottomValue = "bottom";

    private const string DoorSuffix = "_door";

    private const string TrapdoorSuffix = "_trapdoor";

    private const string FenceGateSuffix = "_fence_gate";

    /// <summary>The only door that cannot be opened by hand.</summary>
    private const string IronDoorPath = "iron_door";

    /// <summary>The only trapdoor that cannot be opened by hand.</summary>
    private const string IronTrapdoorPath = "iron_trapdoor";

    /// <summary>Half the player's collision box, <c>PhysicsConstants.PlayerWidth</c> 0.6 over two.</summary>
    internal const double PlayerHalfWidth = PhysicsConstants.PlayerWidth / 2.0;

    /// <summary>Can a player stand on top of this block? A full solid block always qualifies. When <see cref="PathfinderOptions.AllowPartialHeightSupport"/> is set, so does any state whose collision shape holds a CENTRED body somewhere in <c>(0, 1]</c>, which is <see cref="BlockSupport.FootprintSupportHeight"/> rather than the <see cref="BlockState.IsSolid"/> flag, which identifies only a single full unit cube.</summary>
    /// <remarks>
    /// <para>The footprint query replaced <c>BlockSupport.FullCoverSupportHeight</c>, which asks whether the shape covers the WHOLE cell at one height and therefore refuses three families the physics stands on perfectly well: a honey block and a lily pad (inset 1/16 on each side, so a 0.6-wide centred body's whole footprint is inside them - measured rest 0.9375 and 0.09375), and every bottom-half stair (whose raised octet always overlaps a centred footprint, producing a measured rest height of 1.0). These results cover 29 measured shapes.</para>
    /// <para><b>The bounds are load-bearing, both of them.</b> The <c>&gt; 0.0</c> half keeps a pass-through out: <c>snow[layers=1]</c> has the EMPTY collision shape, so the body walks over it at the floor's level and the cell is not a floor. The <c>&lt;= 1.0</c> half keeps a body off anything that protrudes past its own cell: a fence post and a closed fence gate both top out at 1.5, and a plan that stood on one would put the body's feet half a block inside the cell above.</para>
    /// <para>Widening this admits furniture nobody enumerated - cauldrons, composters, chests, hoppers, brewing stands, enchanting tables, end portal frames, anvils - and that is correct rather than accidental, because vanilla's physics rests a body on every one of them. What each of them offers is pinned shape by shape in <c>Umpk.Game.Tests.Blocks.FootprintSupportTests</c>; the hazard set above still decides which of them a plan is allowed to use.</para>
    /// <para>A climbable is not refused solely because it is climbable. For one block in the game, refusing before reading the shape gives simply the wrong answer: scaffolding carries a top plate from 14/16 to 16/16 across the whole cross-section except the four corners, so a centred 0.6-wide footprint rests on it at 1.0. Deleting the flag test rather than special-casing scaffolding keeps the answer geometric: a ladder's 3/16 wall plate spans x 0.8125-1.0 and misses a footprint spanning 0.2-0.8, and every vine - <c>vine</c>, both nether pairs and both cave ones - has the EMPTY collision shape, so all of them are refused at the footprint arm for the reason they were always meant to be refused. Course rows B11 and K4 exercise this refusal before search. B11 therefore reports <c>near_miss</c> rather than producing an invalid route.</para>
    /// </remarks>
    public static bool CanWalkOn(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BlockState state = ctx.GetBlock(x, y, z);

        // The hazard gate stays FIRST, exactly where it was: a campfire is a full-footprint 0.4375 slab and is refused for what it is rather than for its shape, and FootprintSupportPlanningTests pins that precedence.
        return !IsHazard(ctx, state) && PresentsAFloor(ctx, state);
    }

    /// <summary>Whether a cell is a floor a JUMP may be aimed at: walkable, and holding a body wherever in the destination column the landing happens to put it.</summary>
    /// <remarks>
    /// <para><see cref="CanWalkOn"/> asks where a CENTRED body rests, which is the honest question for a body that walks in - a walk arrives slowly, along a heading, with the executor still steering. A jump does not: the arc is committed at take-off, the landing is resolved by axis-ordered collision, and the stance it produces is whatever the destination's own boxes push the body into. So a support that is only under a centred footprint is a floor for a walk and a coin flip for a jump, and the plan is not allowed to spend the coin.</para>
    /// <para>A <c>Parkour</c> landing can target a cell whose only support is a <c>pointed_dripstone[thickness=tip]</c>, 6/16 wide and inset 5/16. The centred probe reads 0.6875 and the segment declared its end there; the arriving body was stopped by the tip's own side face at the exact tangency where its footprint no longer overlaps the box, so it rested on the floor 0.6875 lower, wedged between the tip and a full block, and 40 held-forward ticks did not move it by one digit.</para>
    /// <para><b>Walking is deliberately untouched.</b> Course row O7 walks eleven cells of stalagmite tips and its notes say in as many words that a "fix" which makes <c>CanWalkOn</c> false for them leaves no route at all. This gate narrows only what a jump may LAND on, and <c>StalagmiteLandingTests.ADeckOfThese_IsStillWalkable</c> pins that separation.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The support cell's X.</param>
    /// <param name="y">The support cell's Y (one below the landing body's feet).</param>
    /// <param name="z">The support cell's Z.</param>
    /// <returns>True when a jump may be aimed at the cell above this one.</returns>
    public static bool CanLandOn(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!CanWalkOn(ctx, x, y, z))
            return false;

        BlockState state = ctx.GetBlock(x, y, z);

        // The two arms CanWalkOn answers before it ever looks at a shape, answered the same way here: a full cube holds a body at any stance by definition, and powder snow under a booted body is that same cube with no entry in the shape table at all.
        if (state.IsSolid || (ctx.PowderSnowWalkable && state.IsPowderSnow))
            return true;

        double guaranteed = BlockSupport.GuaranteedSupportHeight(
            ctx.World.Shapes.GetCollisionShapes(state), PlayerHalfWidth);
        return guaranteed is > 0.0 and <= 1.0;
    }

    /// <summary>Whether this state holds a body UP, with no opinion at all about whether the plan is allowed to use it. <see cref="CanWalkOn"/> is exactly this predicate narrowed by <see cref="IsHazard"/>.</summary>
    /// <remarks>
    /// <para>The two questions have to be separable, because for a hazard the answers point opposite ways and one of the two callers needs each. "May the plan stand here" is <c>CanWalkOn</c> and a magma block answers no. "Will the body come to rest on this" is this predicate and the same magma block answers yes because it is a full unit cube and a body over it rests on its top face. Asking only the narrow question is how <see cref="IsOpenGap"/> once read a hazard as empty air, and it is how the swim family once read a hot floor as no floor at all.</para>
    /// <para>The bounds and their reasons are <see cref="CanWalkOn"/>'s; this is the same arithmetic moved, not new arithmetic.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="state">The state at the cell.</param>
    /// <returns>True when a centred body comes to rest on this state.</returns>
    internal static bool PresentsAFloor(CalculationContext ctx, BlockState state)
    {
        if (state.IsAir || state.IsFluid)
            return false;

        if (state.IsSolid)
            return true;

        // Powder snow under a booted body is a full unit cube, and it is not in the shape table: a table has no entity to ask, so it records an empty shape. This arm sits ABOVE the partial-height gate deliberately - the cube is not partial-height support and must not be gated on AllowPartialHeightSupport, which a caller may switch off.
        if (ctx.PowderSnowWalkable && state.IsPowderSnow)
            return true;

        if (!ctx.Options.AllowPartialHeightSupport)
            return false;

        double supportHeight = BlockSupport.FootprintSupportHeight(
            ctx.World.Shapes.GetCollisionShapes(state), 0.5, 0.5, PlayerHalfWidth);
        return supportHeight is > 0.0 and <= 1.0;
    }

    /// <summary>Whether a body whose FEET cell is <c>(x, feetY, z)</c> would be resting on a hazard: there is a floor under it, and the plan is not allowed to use that floor.</summary>
    /// <remarks>The exact complement of <see cref="CanWalkOn"/> inside <see cref="PresentsAFloor"/>. It exists because "no floor here" and "a floor the plan must not touch" are the two answers <c>CanWalkOn</c> collapses into one <c>false</c>, and the swim family needs them apart.</remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="feetY">The body's FEET cell Y.</param>
    /// <param name="z">The cell's Z.</param>
    /// <returns>True when the body's own weight puts it on a hazard.</returns>
    internal static bool RestsOnAHazardousFloor(CalculationContext ctx, int x, int feetY, int z)
    {
        BlockState support = ctx.GetBlock(x, feetY - 1, z);
        return PresentsAFloor(ctx, support) && IsHazard(ctx, support);
    }

    /// <summary>Whether the swim family may plant a node at <c>(x, feetY, z)</c>: the water column is one a 1.8-tall body can occupy (<see cref="CanTraverseWater"/>), and the floor the body would come to rest on is not a hazard.</summary>
    /// <remarks>
    /// <para><b>Why the floor is the swim family's business at all.</b> The planner emits two different moves through the same flooded cell. A submerged bottom-walk is an ordinary <c>Traverse</c>, gated on <see cref="CanWalkOn"/>, which consults the hazard set; a swim is <c>MoveSwim</c>, gated on water passability, which did not. Over a hot floor that asymmetry does not merely leave a hole, it inverts the gate: the hazard DELETES the safe arm and leaves the blind one as the only survivor, so the plan that crossed the magma was the plan the hazard set produced. Course row M8 measured it as 81 <c>Swim</c> segments planned back across a strip that had just taken the bot from 20 health to 0.</para>
    /// <para><b>Why the condition is CONTACT and not proximity.</b> A swimmer is hurt by the block under it while resting on the block under its feet. In a shallow lane the swim node's feet are flush on the bed - <see cref="CalculationContext.SupportElevation"/> resolves the node to the top face of the cell below whenever that cell presents footprint support, which is the same arithmetic the segment endpoints use - so the contact is what the plan itself says it is. In deep water the cell below is more water, nothing holds the body up, and a sprinting swimmer does not sink toward a floor because fluid movement leaves velocity unchanged while <c>SwimTemplate</c> holds <c>Sprint</c> on every level tick. So depth is the whole of the difference, and it is read from the floor predicate rather than off a depth count.</para>
    /// <para>Fire resistance reaches this gate through <see cref="IsHazard"/> and nowhere else, so a magma bed under a swimmer costs a plan exactly what a magma floor under a walker costs it.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The destination cell's X.</param>
    /// <param name="feetY">The destination's FEET cell Y.</param>
    /// <param name="z">The destination cell's Z.</param>
    /// <returns>True when a swim move may end here.</returns>
    internal static bool CanSwimTo(CalculationContext ctx, int x, int feetY, int z)
        => CanTraverseWater(ctx, x, feetY, z) && !RestsOnAHazardousFloor(ctx, x, feetY, z);

    /// <summary>Whether a body WALKS a one-cell crossing that stays at the same FEET cell <paramref name="y"/>, or has to jump the rise that crossing hides.</summary>
    /// <remarks>
    /// <para><b>Same node Y does not mean same elevation.</b> A feet cell's node Y is the cell above its support, so a carpet at 100 and a full block at 100 both put a body's feet in cell 101 while the two surfaces are 0.9375 apart. The level-walk arm must compare those elevations rather than stopping after <c>CanWalkOn</c> and <c>CanWalkThrough</c>, or it treats that 0.9375 rise as if it were flat, and because the crossing was never classified a jump the takeoff rules a slow floor imposes were never consulted on it either (course row J9).</para>
    /// <para><b>Only rises are gated.</b> A DROP inside a cell is free and the body takes it: walking off a bare floor onto a carpet in the same feet cell is a 0.9375 fall that costs nothing and needs no arm of its own, which is why this compares one direction only. That asymmetry is the whole difference between this predicate and <see cref="IsWalkedStep"/>, and it is not cosmetic: the three-cell bridges of carpet, lily pad and cauldron in <c>FootprintSupportPlanningTests.Planner_CrossesABridgeOfEverySupportFamily</c> are entered by exactly such a drop, and gating drops here refuses all three.</para>
    /// <para><b>Why the resolved elevations and not <see cref="IsWalkedStep"/>'s swept entry.</b> The swept entry exists because a STEP move enters from a different plane and has to be taken shelf by shelf - that is what stops a bottom-half stair from being refused for presenting a 1.0 rise it really offers in two halves. A level crossing has no shelves to walk: both ends are supported out of the same cell layer, <c>y - 1</c>, so the elevation each end presents IS the answer, and <see cref="CalculationContext.SupportElevation"/> is the same arithmetic <c>PathSegmentBuilder.ResolveElevation</c> will use for the segment endpoints. Comparing them also keeps a support the shape table cannot describe out of trouble: powder snow under a booted body is a full cube with the EMPTY collision shape, so both ends fall back to the feet cell, the rise is zero, and the crossing walks - where a swept entry would read the empty shape as a hole and refuse it.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The source cell's X.</param>
    /// <param name="y">The FEET cell's Y, shared by both ends.</param>
    /// <param name="z">The source cell's Z.</param>
    /// <param name="dx">The travel step's X.</param>
    /// <param name="dz">The travel step's Z.</param>
    /// <returns>True when the body walks the crossing.</returns>
    internal static bool IsWalkedLevelCrossing(CalculationContext ctx, int x, int y, int z, int dx, int dz)
    {
        double rise = ctx.SupportElevation(x + dx, y, z + dz) - ctx.SupportElevation(x, y, z);
        return rise <= BlockSupport.PlayerStepHeight + ElevationEpsilon;
    }

    /// <summary>Whether a one-cell step from <c>(x, y, z)</c> into the cardinal neighbour at <c>(x + dx, destFeetY, z + dz)</c> is one the body WALKS - auto-stepping every shelf it meets - rather than one it has to jump or fall through.</summary>
    /// <remarks>
    /// <para><b>The node-Y delta cannot answer this.</b> A slab kerb and a full block are both +1 there, and a carpet beside a bare floor is -1 there while being a step of one sixteenth of a block. What decides it is the elevation the two supports actually present, which is <see cref="CalculationContext.SupportElevation"/>, taken through the destination's entry ladder, against <see cref="BlockSupport.PlayerStepHeight"/> of 0.6.</para>
    /// <para><b>Two gates, and each earns its place.</b> The DROP is compared directly, because a fall of any size is free inside a cell and a ladder alone would call a one-block step down a walk. The RISE is not compared directly, because a rise is taken shelf by shelf: a bottom-half stair lifts the body a whole 1.0 and is walked, in two 0.5 shelves, from the one or two directions its raised octet leaves open. All 116 measured shape-and-direction cases agree. Comparing resolved elevations alone would refuse every stair; sampling the cell's centre instead of the swept entry would accept all four of a stair's sides, three of which the engine refuses.</para>
    /// <para><b>Cardinal only.</b> The ladder is a swept entry along one axis, and that is the whole of its evidence base. A diagonal step keeps the classification it always had, which costs nothing: the diagonal arm is already the dearer of the two and A* prefers the cardinal pair.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The source cell's X.</param>
    /// <param name="y">The source FEET cell's Y.</param>
    /// <param name="z">The source cell's Z.</param>
    /// <param name="dx">The travel step's X, -1, 0 or 1.</param>
    /// <param name="dz">The travel step's Z, -1, 0 or 1.</param>
    /// <param name="destFeetY">The destination FEET cell's Y.</param>
    /// <returns>True when the body walks the step.</returns>
    internal static bool IsWalkedStep(CalculationContext ctx, int x, int y, int z, int dx, int dz, int destFeetY)
    {
        if ((dx == 0) == (dz == 0))
            return false;

        double sourceElevation = ctx.SupportElevation(x, y, z);
        double destinationElevation = ctx.SupportElevation(x + dx, destFeetY, z + dz);

        if (sourceElevation - destinationElevation > BlockSupport.PlayerStepHeight + ElevationEpsilon)
            return false;

        int supportY = destFeetY - 1;
        if (!BlockSupport.TryEnterFrom(
                ctx.World.Shapes.GetCollisionShapes(ctx.GetBlock(x + dx, supportY, z + dz)),
                sourceElevation - supportY,
                dx,
                dz,
                PlayerHalfWidth,
                BlockSupport.PlayerStepHeight,
                out double restElevation))
            return false;

        // The walk has to end where the segment endpoint will say it does. Anything else means the body came to rest somewhere the plan does not describe, and the completion gates read the plan.
        return Math.Abs(supportY + restElevation - destinationElevation) <= ElevationEpsilon;
    }

    /// <summary>The walk-cost multiplier a floor's speed factor imposes on a body whose FEET cell is <c>(x, feetY, z)</c> (soul sand and honey slow the player).</summary>
    /// <remarks>
    /// <para><b>Two cells, feet first.</b> Read the cell containing the body's feet and consult the cell below only when its factor is exactly 1.0. A body on soul sand rests at 0.875 and has its feet inside the soul-sand cell. A body on carpet over soul sand reads the carpet's 1.0, then the soul sand below, and measures 6.135 ticks per block.</para>
    /// <para><b>The feet cell is derived, not assumed.</b> A node's Y is a logical feet cell; where the support is partial the body physically rests below it, and the resting elevation determines which cell contains the feet. <see cref="CalculationContext.SupportElevation"/> is the same arithmetic the segment endpoints and the step classification use, so all three agree about where the body is.</para>
    /// <para><b>The charge is measured</b>, see <see cref="ActionCosts.SpeedFactorCostMultiplier(double)"/>: 0.4 costs 1.691x, not the 2.5x that <c>1 / 0.4</c> implies.</para>
    /// <para>A cobweb in the destination's body column is charged here too, and it belongs here rather than in a predicate of its own: it is the same question - what does one block of travel into this cell cost - answered by a different mechanism. Cobweb contact applies a stuck multiplier rather than a floor speed factor, so it cannot be read from <see cref="BlockState.SpeedFactor"/>. The two compose multiplicatively, which is what the engine does: a web over soul sand slows the body twice, through two different code paths. See <see cref="ActionCosts.MeasuredWebCostMultiplier"/> for the measurement.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The destination cell's X.</param>
    /// <param name="feetY">The destination node's logical FEET cell Y, not its support cell.</param>
    /// <param name="z">The destination cell's Z.</param>
    /// <returns>The multiplier to apply to the move's walk term.</returns>
    internal static double FloorSpeedPenalty(CalculationContext ctx, int x, int feetY, int z)
    {
        int here = PhysicalFeetCellY(ctx, x, feetY, z);

        BlockState feet = ctx.GetBlock(x, here, z);
        BlockState below = ctx.GetBlock(x, here - 1, z);
        double factor = feet.IsDefault ? 1.0 : feet.SpeedFactor;
        if (factor == 1.0)
            factor = below.IsDefault ? 1.0 : below.SpeedFactor;

        // A soul-speed wearer does not pay for a soul block at all: the engine lerps the floor factor up to 1.0. On protocol 774, the same 20-block lane took 6.014 s bare and 3.009 s booted - FASTER than the 4.013 s stone control, because the server's movement-speed boost rides on top. The charge drops to 1.0, which prices away the slowdown and deliberately does not price in the boost.
        //
        // The predicate is the TAG, matched on the cell the CHARGE came from, not on the speed factor. 0.4 is the only sub-unit factor in the game, so `factor == 0.4` cannot tell soul sand from honey - and honey is not in BlockTags.SOUL_SPEED_BLOCKS, so a booted bot really does still pay for it.
        bool bypassed = ctx.Capabilities.SoulSpeedLevel > 0
            && (IsSoulSpeedBlock(feet) || (feet.IsDefault || feet.SpeedFactor == 1.0) && IsSoulSpeedBlock(below));

        return ActionCosts.SpeedFactorCostMultiplier(factor, bypassed) * WebPenalty(ctx, x, here, z);
    }

    /// <summary>Whether a block is in vanilla's <c>BlockTags.SOUL_SPEED_BLOCKS</c>.</summary>
    /// <remarks>
    /// The tag holds exactly soul sand and soul soil on every version that has it. UMPK has no block-tag model, so this matches by identifier, the same way <c>PlayerPhysics.IsSoulSpeedBlock</c> does - and the two must agree, because one prices the lane and the other walks it.
    /// <para>Soul soil is in the tag at speed factor 1.0, so it never cost anything and still does not. It is here because the predicate has to be the tag: an implementation keyed on <c>factor == 0.4</c> would omit soul soil silently, and course row G12f is what catches that.</para>
    /// </remarks>
    private static bool IsSoulSpeedBlock(BlockState state)
    {
        if (state.IsDefault)
            return false;

        string path = state.Block.Id.Path;
        return path.Equals("soul_sand", StringComparison.Ordinal)
            || path.Equals("soul_soil", StringComparison.Ordinal);
    }

    /// <summary>What a cobweb in the body's column costs one block of travel: 1.0 with no web, and <see cref="ActionCosts.MeasuredWebCostMultiplier"/> with one.</summary>
    /// <remarks><b>Both body cells.</b> <c>inside-block processing</c> runs the hook for every cell the box overlaps, and a 1.8-tall body overlaps two, so a web hung at head height holds the body exactly as one at foot height does. Charging once rather than per cell is deliberate: the multiplier is assigned rather than accumulated, so two web cells slow the body no more than one.</remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="feetCellY">The cell the body's feet are PHYSICALLY in.</param>
    /// <param name="z">The cell's Z.</param>
    /// <returns>The multiplier to apply to the move's walk term.</returns>
    internal static double WebPenalty(CalculationContext ctx, int x, int feetCellY, int z)
        => IsCobweb(ctx.GetBlock(x, feetCellY, z)) || IsCobweb(ctx.GetBlock(x, feetCellY + 1, z))
            ? ActionCosts.MeasuredWebCostMultiplier
            : 1.0;

    /// <summary><c>minecraft:cobweb</c>, and <c>minecraft:web</c> - the same block under the name the pre-flattening registries carry (block id 30, <c>legacy block registration</c>).</summary>
    /// <param name="state">The state to test.</param>
    /// <returns>True for a cobweb on any of the 49 protocols.</returns>
    public static bool IsCobweb(BlockState state)
        => !state.IsDefault && (state.Block.Id == CobwebId || state.Block.Id == LegacyCobwebId);

    /// <summary><c>minecraft:bubble_column</c>, which is always a water source but is exempt from air drain.</summary>
    /// <remarks>The two facts are independent, which is why this predicate exists separately from <see cref="IsWater"/> rather than being derived from it. See <c>BreathModel.IsSubmerged</c> for the vanilla clause and what it costs to get wrong. The block arrives with the flattening (1.13, protocol 393) and has no pre-flattening spelling.</remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True for a bubble column, upward or downward.</returns>
    public static bool IsBubbleColumn(BlockState state)
        => !state.IsDefault && state.Block.Id == BubbleColumnId;

    /// <summary>A landing that BOUNCES a body rather than stopping it: <c>minecraft:slime_block</c>, and the pre-flattening registries' <c>minecraft:slime</c>.</summary>
    /// <remarks><c>slime bounce response</c> inverts a downward velocity outright for a living entity - <c>vy = -vy * 1.0</c>, no decay factor at all - unless the player is sneaking, in which case landing zeroes vertical velocity instead. The executor reads this to decide whether a landing needs the sneak that cancels the bounce, and whether its completion has to wait for a body that is still bouncing. Hay is deliberately NOT here: it softens a fall and does not bounce.</remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True for a slime block on any of the 49 protocols.</returns>
    public static bool IsBouncyLanding(BlockState state)
        => !state.IsDefault && (state.Block.Id == SlimeBlockId || state.Block.Id == LegacySlimeBlockId);

    /// <summary>An UPWARD bubble column, i.e. one whose <c>drag</c> property is false - the state a SOUL SAND base produces.</summary>
    /// <remarks>The property read must succeed. A source that cannot resolve <c>drag</c> answers false here and the cell is priced as ordinary water, which is the conservative direction and is what every pre-flattening era gets for free - bubble columns arrive with the flattening, so no such era has one at all. A downward column deliberately never answers true because its downdraft is unmodelled. Pricing a descent through one at a lift rate the engine does not produce would be a plan the executor cannot walk.</remarks>
    /// <param name="state">The state to test.</param>
    /// <returns>True for a column that lifts.</returns>
    public static bool IsUpwardBubbleColumn(BlockState state)
        => IsBubbleColumn(state)
            && state.TryGetProperty(DragProperty, out string drag)
            && drag == FalseValue;

    private const string DragProperty = "drag";

    private const string FalseValue = "false";

    /// <summary>Whether the body at <c>(x, feetY, z)</c> is HANGING on a climbable rather than standing: its feet cell is a ladder or a vine and there is nothing under it to rest on.</summary>
    /// <remarks>
    /// <para><b>Why the pair, and not either half.</b> "The feet cell is climbable" is not enough - a body walking along a floor with a vine growing at ankle height is standing on the floor and has every move a bare floor gives it, and so is a body inside a scaffold tower, whose own top plate is a floor that <see cref="CanWalkOn"/> accepts. "Nothing under it" is not enough either - that is every airborne cell in the world, and those are not nodes a plan takes off from. It is the conjunction that names a body held up by a rung.</para>
    /// <para><b>Water is excluded on purpose.</b> A waterlogged ladder is water as far as the engine is concerned (<c>PlayerPhysics.IsWater</c> reads <c>IsWaterlogged</c> too), and a body in water is buoyant and uses the fluid-jump arm that fires instead of the ground gate. It is not hanging, and the swim family already prices it.</para>
    /// <para><b>The node's own Y is the right cell to read</b>, without the <see cref="CalculationContext.SupportElevation"/> round trip <see cref="CanTakeOffForAJump"/> needs. That round trip exists to find the cell a body on a PARTIAL support really stands in - on soul sand at 0.875 the feet are one cell down - and a partial support under the rung is exactly the case where <see cref="CanWalkOn"/> answers true and this returns false anyway.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The cell's X.</param>
    /// <param name="feetY">The node's feet cell Y.</param>
    /// <param name="z">The cell's Z.</param>
    /// <returns>True when the body is held up by a climbable and by nothing else.</returns>
    internal static bool IsHangingOnAClimbable(CalculationContext ctx, int x, int feetY, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BlockState feet = ctx.GetBlock(x, feetY, z);
        return feet.IsClimbable && !IsWater(feet) && !ctx.CanWalkOn(x, feetY - 1, z);
    }

    /// <summary>Whether a body standing at <c>(x, feetY, z)</c> is AFLOAT there, i.e. deep enough in water that vanilla gives it no ground jump at all.</summary>
    /// <remarks>
    /// <para><b>This is a routing rule, not a heuristic.</b> Movement reads the sensed fluid height once and sends a held jump down one of two paths:</para>
    /// <code>
    /// if (!inWaterAndHasFluidHeight || onGround() &amp;&amp; !(fluidHeight &gt; threshold)) { jumpFromGround, 0.42 } else                                                                            { jumpInLiquid,  0.04 }
    /// </code>
    /// <para>The threshold is 0.4 for anything whose eye is at least 0.4 high. Over that line the 0.42 impulse does not exist, grounded or not, so it is a property of the TAKEOFF CELL rather than of the body's contact state - which is why this can be answered from a node at all.</para>
    /// <para><b>What it costs the body, measured on the real engine</b> (<c>Umpk.Physics.Tests.FloatingTakeoffReachTests</c>): holding Jump afloat reaches a steady climb and stops the instant the box leaves the fluid, peaking <b>0.232 to 0.279 over the waterline</b> at one, two and three layers alike - against a grounded jump's 1.2522 apex, which is what every distance in <see cref="JumpFeasibility"/> and every tick budget in <c>SegmentBudgetPolicy</c> is calibrated on. A source column and the FALLING state give bit-identical peaks, so this is buoyancy and not current.</para>
    /// <para><b>The replacement already exists and is priced for water.</b> A swimmer rises with <c>MoveSwimVertical</c> and leaves the water with <c>MoveSwimExit</c>, whose one-up arm reaches a measured 0.9203 over the waterline (<c>Umpk.Physics.Tests.FluidLedgeHopTests</c>). Refusing the land arms is what lets A* reach them: the land jump was simply cheaper, so it always won.</para>
    /// <para><b>Water only, deliberately.</b> Vanilla applies the same threshold to lava, but a lava cell is refused by the hazard gate long before any takeoff question is asked (<see cref="IsHazard"/>), so a lava arm here would be unreachable and could never be shown to bite.</para>
    /// <para><b>The stance is the node's real one</b>, through <see cref="CalculationContext.SupportElevation"/> rather than the node's integer Y: on soul sand at 0.875, or a slab at 0.5, the feet plane is not the cell floor and the sensed depth is smaller by exactly that much - which is the difference between refusing and allowing a jump out of a flooded slab step.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The takeoff cell's X.</param>
    /// <param name="feetY">The takeoff node's feet cell Y.</param>
    /// <param name="z">The takeoff cell's Z.</param>
    /// <returns>True when the body has no ground jump at that stance.</returns>
    internal static bool IsAfloatInWater(CalculationContext ctx, int x, int feetY, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Cheap reject first: with no water in either of the two cells a standing body occupies there is nothing to sense, and a dry search must not pay for the scan at all.
        if (!IsWater(ctx.GetBlock(x, feetY, z)) && !IsWater(ctx.GetBlock(x, feetY + 1, z)))
            return false;

        double height = Umpk.Physics.PlayerPhysics.WaterHeightForStance(
            ctx.World, x, ctx.SupportElevation(x, feetY, z), z);
        return height > Umpk.Physics.PhysicsConstants.FluidJumpThreshold;
    }

    /// <summary>Whether a body standing at <c>(x, feetY, z)</c> can take off for a JUMP at all, i.e. whether its floor leaves it the jump power every jump-requiring move is calibrated on.</summary>
    /// <remarks>
    /// <para><b>The floor can take the jump away.</b> Effective jump strength is multiplied by the supporting block's jump factor, and the only block in the game with a jump factor under 1.0 is <c>minecraft:honey_block</c> at 0.5. A stone takeoff apexes at 1.2522 blocks and clears an ordinary kerb, and a honey takeoff apexes at <b>0.383852</b> - below even the 0.6 auto-step, so a jump out of honey buys the body nothing a walk did not already give it. No jump-requiring move may be planned from one.</para>
    /// <para>The feet cell is read first, and the cell below is consulted only when its factor is 1.0. This preserves the behavior of a carpet laid over honey, where the feet never touch the honey's own box: the carpet reads 1.0, the fallback finds the honey, and the apex is the same 0.383852. A one-cell classifier sees carpet and clears the jump.</para>
    /// <para><b>Exact today, conservative if the dataset grows.</b> Across all 49 supported protocols, 0.5 is the only sub-unit factor. A future factor near 1.0 might still clear a kerb and would be refused here; refusing costs a route, granting strands the bot in front of a wall it cannot climb.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="x">The takeoff cell's X.</param>
    /// <param name="feetY">The takeoff node's logical FEET cell Y.</param>
    /// <param name="z">The takeoff cell's Z.</param>
    /// <returns>False when the floor's effective jump factor is under 1.0.</returns>
    internal static bool CanTakeOffForAJump(CalculationContext ctx, int x, int feetY, int z)
    {
        int here = PhysicalFeetCellY(ctx, x, feetY, z);

        // A body in a cobweb has no jump at all, and this is a stronger statement than the floor factors below. Cobweb contact zeroes velocity every tick the body is inside a web, so the jump impulse is discarded before the next tick's move ever sees it and no ballistic arc exists. Charging such a jump 8.8107x, as WebPenalty does for a walk, would price a move the executor cannot make at all.
        if (WebPenalty(ctx, x, here, z) != 1.0)
            return false;

        BlockState feet = ctx.GetBlock(x, here, z);
        double factor = feet.IsDefault ? 1.0 : feet.JumpFactor;
        if (factor == 1.0)
        {
            BlockState below = ctx.GetBlock(x, here - 1, z);
            factor = below.IsDefault ? 1.0 : below.JumpFactor;
        }

        return factor >= 1.0;
    }

    /// <summary>The cell physically containing the body's feet when its node says <c>(x, feetY, z)</c>.</summary>
    /// <remarks>Not the node's Y. A node's Y is a LOGICAL feet cell; where the support is partial the body rests below it - on soul sand at 0.875, on a carpet at 0.0625 - and both of those put the feet one cell down. <see cref="CalculationContext.SupportElevation"/> is the same arithmetic the segment endpoints and the step classification use, so all three agree about where the body is, and it is memoised for the life of the search.</remarks>
    private static int PhysicalFeetCellY(CalculationContext ctx, int x, int feetY, int z)
        => (int)Math.Floor(ctx.SupportElevation(x, feetY, z));

    /// <summary>The elevation tolerance the step classification compares at: a sixteenth of a sixteenth.</summary>
    private const double ElevationEpsilon = 1.0E-9;

    /// <summary>Is this block completely passable with no slowdown or interaction (air only)?</summary>
    public static bool IsFullyPassable(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.GetBlock(x, y, z).IsAir;
    }

    /// <summary>True when the state is a climbable block (ladder/vine/scaffolding).</summary>
    public static bool IsClimbable(BlockState state) => state.IsClimbable;

    /// <summary>True when the block is water (source or flowing).</summary>
    public static bool IsWater(BlockState state)
    {
        if (state.IsDefault)
            return false;

        return (state.IsFluid && !IsLavaState(state)) || state.IsWaterlogged;
    }

    private static bool IsLavaState(BlockState state)
        => state.IsFluid && state.Block.Id.Path.Contains("lava", StringComparison.Ordinal);

    /// <summary>A water column is passable for swimming when both the feet and head cells are water (or air above the surface) and neither is a wall.</summary>
    public static bool CanTraverseWater(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.AllowSwim)
            return false;

        // Feet cell must be water the body can occupy. A waterlogged fence is water and a collision box at once, and a swimmer occupies neither half of it.
        BlockState feet = ctx.GetBlock(x, y, z);
        if (!IsWater(feet) || feet.BlocksMotion)
            return false;

        // Head cell must be water or air (the surface). A solid ceiling blocks the swim column, and that includes a waterlogged one, whose collision still blocks the body.
        BlockState head = ctx.GetBlock(x, y + 1, z);
        return !head.BlocksMotion && (IsWater(head) || head.IsAir || !head.IsFluid);
    }

    /// <summary>A swimmer can complete a segment at a water block even though the liquid is not walkable support.</summary>
    public static bool CanStandAt(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (CanWalkOn(ctx, x, y - 1, z) && CanWalkThrough(ctx, x, y, z) && CanWalkThrough(ctx, x, y + 1, z))
            return true;

        // Water-surface / submerged completion: feet in water with a passable head column.
        return CanTraverseWater(ctx, x, y, z);
    }

    /// <summary>True when the block would harm the player (curated hazard set or the caller's avoid set).</summary>
    public static bool IsHazard(CalculationContext ctx, BlockState state)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (state.IsDefault || state.IsAir)
            return false;

        // Lava is a fluid hazard, and the only hazard that can arrive as a fluid state rather than as a block id the curated set names. It is also in FireHazards, so the clearance reaches it here.
        if (state.IsFluid && state.Block.Id == LavaId)
            return !ctx.FireHazardsCleared;

        Identifier id = state.Block.Id;
        if (ctx.Options.BlocksToAvoid.Contains(id))
            return true;

        if (!HazardBlocks.Contains(id))
            return false;

        // Powder snow with leather boots on is not a hazard at all, on either count the curated set put it there for. It is not a hole: vanilla's collision hands a booted body above the cell a full cube. And it does not freeze: A body cannot freeze while any armor slot holds one of the five leather wearables. Frozen ticks therefore decay by 2 per tick, and the fully-frozen is never true and the tickCount % 40 freeze damage never fires. A body ON TOP is not even isInPowderSnow. HazardBlocks itself is untouched: this is a CLEARANCE, the same shape FireHazardsCleared has, so a bootless plan sees the set it always saw.
        if (ctx.PowderSnowWalkable && id == PowderSnowId)
            return false;

        // The caller's own avoid set is never relaxed: it is an instruction, not a damage model.
        return !ctx.FireHazardsCleared || !FireHazards.Contains(id);
    }

    /// <summary>True when a hazard block occupies this position (the positional form of <see cref="IsHazard"/>).</summary>
    public static bool IsHazardAt(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return IsHazard(ctx, ctx.GetBlock(x, y, z));
    }

    /// <summary>
    /// True when this cell is a gap the jump family may plan a sprint jump across: nothing standable, and no hazard either. It is <c>!CanWalkOn</c> narrowed by exactly the hazard case, and by nothing else, so a ladder or (with <see cref="PathfinderOptions.AllowPartialHeightSupport"/> off) a slab still reads as a gap here, as it always did.
    ///
    /// <para><see cref="CanWalkOn"/> answers a different question, "may the plan stand here", and for a hazard the two answers point opposite ways. A magma block is a full unit cube, and a body walking onto it comes to rest on top of it and is hurt for it: hurts a living entity unless it steps carefully. So the floor is there; the plan simply must not use it. Reading <c>!CanWalkOn</c> as "there is nothing here" turned that refusal into its opposite, a licence to sprint-jump over the hazard, which is how the pathfinding course's F1, F4, F7 and F10 rows all crossed the hazard they exist to route around.</para>
    /// </summary>
    public static bool IsOpenGap(CalculationContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return !CanWalkOn(ctx, x, y, z) && !IsHazardAt(ctx, x, y, z);
    }

    /// <summary>Does this block absorb or negate fall damage (water, slime, hay, powder snow)?</summary>
    /// <remarks>The water arm is gated on <c>!BlocksMotion</c> for the same reason the passability predicates are, and the reason is not merely conservatism. Touching water clears accumulated fall distance before fall damage is processed. A body that comes to rest on top of a waterlogged stair's upper step, or on a waterlogged wall, is standing ABOVE the cell that holds the water, its box does not reach into it, and it takes the whole fall. Whether it does depends on the block's collision height, which a flag cannot say, so this takes the half that cannot kill anyone: a cell that blocks motion is a landing, not a splash.</remarks>
    public static bool AbsorbsFallDamage(BlockState state)
    {
        if (state.IsDefault)
            return false;

        if (IsWater(state) && !state.BlocksMotion)
            return true;

        Identifier id = state.Block.Id;
        return id == SlimeBlockId || id == LegacySlimeBlockId || id == HayBlockId || id == PowderSnowId;
    }

    private static readonly Identifier LavaId = Identifier.Minecraft("lava");
    private static readonly Identifier SlimeBlockId = Identifier.Minecraft("slime_block");

    // The flattening renamed minecraft:slime to minecraft:slime_block at 1.13; 1.8-1.12.2 registries (legacy block registration, id 165) spell it the short way, so the modern name alone missed it on every pre-flattening protocol.
    private static readonly Identifier LegacySlimeBlockId = Identifier.Minecraft("slime");
    private static readonly Identifier HayBlockId = Identifier.Minecraft("hay_block");
    private static readonly Identifier CobwebId = Identifier.Minecraft("cobweb");

    // The flattening renamed minecraft:web to minecraft:cobweb at 1.13; 1.8-1.12.2 registries spell it the short way (block id 30), which is the name those datasets carry.
    private static readonly Identifier LegacyCobwebId = Identifier.Minecraft("web");

    private static readonly Identifier BubbleColumnId = Identifier.Minecraft("bubble_column");
    private static readonly Identifier PowderSnowId = Identifier.Minecraft("powder_snow");

    private static readonly HashSet<Identifier> HazardBlocks =
    [
        Identifier.Minecraft("lava"),
        Identifier.Minecraft("fire"),
        Identifier.Minecraft("soul_fire"),
        Identifier.Minecraft("cactus"),
        Identifier.Minecraft("magma_block"),
        Identifier.Minecraft("sweet_berry_bush"),
        Identifier.Minecraft("wither_rose"),
        Identifier.Minecraft("powder_snow"),
        Identifier.Minecraft("campfire"),
        Identifier.Minecraft("soul_campfire"),
    ];

    /// <summary>The hazards fire resistance neutralises.</summary>
    /// <remarks>
    /// <para>Six of the ten curated hazards deal fire damage and four do not.</para>
    /// <para><b>The four exclusions are evidence-based.</b> Damage from a cactus, sweet berry bush, wither rose, or powder snow is not fire damage, and a bot that walked into one holding a fire-resistance potion would take every point of it. Getting this list wrong in the permissive direction is the failure mode that kills, so it is stated here as a literal set and again, independently, as a literal table in <c>FireResistancePlanningTests.FireResistanceClearsExactlyTheFireDamageHazards</c>.</para>
    /// </remarks>
    private static readonly HashSet<Identifier> FireHazards =
    [
        Identifier.Minecraft("lava"),
        Identifier.Minecraft("fire"),
        Identifier.Minecraft("soul_fire"),
        Identifier.Minecraft("magma_block"),
        Identifier.Minecraft("campfire"),
        Identifier.Minecraft("soul_campfire"),
    ];

    /// <summary>Whether this state is one of the six <see cref="FireHazards"/>, asked of a state rather than of a context because the caller has no clearance decision to make yet.</summary>
    /// <remarks>This is what lets a finished plan say whether it LEANS on the potion: a route that never enters a cell in this set is the same route the strict world would have produced, so revoking the effect mid-route has nothing to invalidate. It reads the same set <see cref="IsHazard"/> reads, so the two cannot drift apart; the lava arm is spelled out because lava arrives as a fluid state and <see cref="IsHazard"/> handles it before the block-id lookup for the same reason.</remarks>
    /// <param name="state">The block state.</param>
    /// <returns>True when fire resistance is what makes the cell survivable.</returns>
    internal static bool IsClearedByFireResistance(BlockState state)
    {
        if (state.IsDefault)
            return false;

        return state.IsFluid ? state.Block.Id == LavaId : FireHazards.Contains(state.Block.Id);
    }
}
