using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>Turns a planned <see cref="PathNode"/> sequence into executable <see cref="PathSegment"/>s with a transition taxonomy and per-segment exit hints, with block-center points as <see cref="Vec3d"/> and a swim transition path that sets <see cref="PathTransitionHints.AllowUngrounded"/> so water segments complete off the ground.</summary>
public static class PathSegmentBuilder
{
    /// <summary>Builds the executable segment list for a planned path, with every endpoint's Y left as the planner's INTEGER node Y.</summary>
    /// <remarks>This overload is exact only where every support is a full unit cube. Where one is not, the body can never occupy the elevation the endpoint names, and the completion gates that read it refuse a landing that is perfectly correct: see <see cref="FromPath(IReadOnlyList{PathNode}, IPhysicsWorldView)"/>, which is what production builds through. Kept for callers that assemble nodes without a world - the tests do - alongside the <c>PlannedTickCost == 0</c> "unknown" convention.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is null.</exception>
    public static IReadOnlyList<PathSegment> FromPath(IReadOnlyList<PathNode> nodes) => Build(nodes, world: null, ctx: null);

    /// <summary>Builds the executable segment list for a planned path, resolving each endpoint's Y to the elevation a body standing in that column actually rests at.</summary>
    /// <remarks>
    /// <para>The planner's node Y is a logical feet cell, not a position. Copying it directly into the segment's <c>Start.Y</c>/<c>End.Y</c> would treat it as a position. Over a full unit cube those coincide. Over anything else they do not, and the segment then names an elevation the body can never occupy: standing on a bottom slab in cell 61 puts the feet at 60.5, on a carpet at 60.0625, on a lily pad at 60.09375.</para>
    /// <para>Across thirteen supports, an integer endpoint makes Ascend fail on oak_slab, snow[5], snow[4], snow[2], white_carpet and lily_pad - every support under 0.8 - and completes on the rest; with the resolved endpoint all thirteen complete, in 3 to 12 ticks, and <c>AscendTemplate</c>'s <c>|dy| &lt; 0.2</c> gate is not touched. A three-segment slab street completes in 20 ticks with resolved endpoints.</para>
    /// <para><b>What "the elevation" means.</b> It is <c>BlockSupport.FootprintSupportHeight</c> of the support cell under the node, i.e. the max over the boxes a centred 0.6-wide body's footprint overlaps - NOT <c>FullCoverSupportHeight</c>, which answers null for honey, lily pads and bottom stairs, and not <c>MaxSupportHeight</c>, which would perch the body on a cauldron's rim. A support outside <c>(0, 1]</c> leaves the endpoint alone: that covers a swim node, a ladder rung and an unloaded cell, none of which stand on anything.</para>
    /// <para><b>The straddle is NOT modelled here, deliberately.</b> A moving body holds the MAXIMUM of the supports its footprint overlaps, so on a mixed street the mid-segment Y can sit up to a block above the destination cell's own value, and at rest the body can settle at a neighbour's elevation with its centre still inside the destination column. The endpoint stays the destination column's own elevation, because it is also what the templates STEER at - AscendTemplate presses Jump while <c>dy &gt; 0.1</c>, and an endpoint aimed at a neighbour's higher support would have the bot hopping in place after a perfectly good landing. What the neighbours reach has to be carried separately, for the completion gates alone.</para>
    /// </remarks>
    /// <param name="nodes">The planned nodes.</param>
    /// <param name="world">The world the path was planned against.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IReadOnlyList<PathSegment> FromPath(IReadOnlyList<PathNode> nodes, IPhysicsWorldView world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Build(nodes, world, ctx: null);
    }

    /// <summary><see cref="FromPath(IReadOnlyList{PathNode}, IPhysicsWorldView)"/> against the planning view and the options the search itself ran under, which is what production builds through.</summary>
    /// <remarks>The extra thing this overload can do is resolve an ACTIVATOR. A hand-opened door is a property read and needs nothing but the world, but an iron door's switch is a neighbourhood search plus a stand-cell search - <c>DoorActivation.TryResolve</c> - which needs a <see cref="CalculationContext"/> for <c>CanStandAt</c> and for the per-door memo. Building one here from the same view and options the search used is what keeps the segment's payload and the search's price describing the same door.</remarks>
    /// <param name="nodes">The planned nodes.</param>
    /// <param name="world">The planning view the path was planned against.</param>
    /// <param name="options">The options the search ran under.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IReadOnlyList<PathSegment> FromPath(
        IReadOnlyList<PathNode> nodes, PlanningWorldView world, PathfinderOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        return Build(nodes, world, new CalculationContext(world, options));
    }

    private static IReadOnlyList<PathSegment> Build(
        IReadOnlyList<PathNode> nodes, IPhysicsWorldView? world, CalculationContext? ctx)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var segments = new List<PathSegment>(Math.Max(0, nodes.Count - 1));
        for (int i = 1; i < nodes.Count; i++)
        {
            PathSegment current = CreatePreview(nodes[i - 1], nodes[i], world, ctx);
            PathSegment? next = i + 1 < nodes.Count ? CreatePreview(nodes[i], nodes[i + 1], world, ctx) : null;
            PathSegment? nextNext = i + 2 < nodes.Count ? CreatePreview(nodes[i + 1], nodes[i + 2], world, ctx) : null;

            PathTransitionType exitTransition = Classify(current, next);
            segments.Add(current with
            {
                ExitTransition = exitTransition,
                ExitHints = BuildHints(current, next, nextNext, exitTransition, world),
                PreserveSprint = exitTransition is PathTransitionType.ContinueStraight or PathTransitionType.PrepareJump,
            });
        }

        return segments;
    }

    private static PathSegment CreatePreview(
        PathNode start, PathNode end, IPhysicsWorldView? world, CalculationContext? ctx) => new()
        {
            // + 0.5 is the cell centre and was the ONLY endpoint the planner could produce until
            // lateral squeeze lanes landed. The lateral is 0 on every node of every plan that meets no bamboo, so every existing route's endpoints are bit-identical; where it is not 0 it is +/- 0.2, the offset at which the body's face is flush with the cell's, which is the whole executor-side change. See SqueezeLane and PathNode.LateralX.
            Start = new Vec3d(
                start.X + 0.5 + SqueezeLane.Offset(start.LateralX),
                ResolveElevation(world, start.X, start.Y, start.Z),
                start.Z + 0.5 + SqueezeLane.Offset(start.LateralZ)),
            End = new Vec3d(
                end.X + 0.5 + SqueezeLane.Offset(end.LateralX),
                ResolveElevation(world, end.X, end.Y, end.Z),
                end.Z + 0.5 + SqueezeLane.Offset(end.LateralZ)),

            // The planner's own cells travel with the segment, so no consumer has to recover them by flooring an endpoint that no longer carries them. See PathSegment.StartFeetY.
            StartFeetY = start.Y,
            EndFeetY = end.Y,

            // How far above its own elevation the destination column can rest a body whose centre is still inside it. See PathSegment.EndElevationSlack; only the completion gates read it.
            EndElevationSlack = ResolveNeighbourSlack(world, end.X, end.Y, end.Z),
            MoveType = end.MoveUsed,
            ParkourProfile = end.ParkourProfile,

            // The exact edge cost, already sitting in the two nodes this preview is built from, MINUS the breathing pause the search charged to the same g-cost. The wait is real time and it belongs in the plan, but it is not time spent CROSSING this segment, and SegmentBudgetPolicy reads this number as though it were: before the subtraction a one-block move at a bell was handed a 70,3-tick travel budget against a wet block's real 10,2. See PathSegment.BreathHoldTicks. Clamped at zero so a caller that hands in nodes from somewhere other than a completed search gets "unknown" rather than a negative budget.
            PlannedTickCost = Math.Max(0.0, end.GCost - start.GCost - end.BreathHoldTicks),

            // The pause the search priced at this segment's destination, carried so the executor can actually perform it. Zero for every segment that needs no pause.
            BreathHoldTicks = end.BreathHoldTicks,

            // The doorway this segment passes through, if either end of it shares a column with an open door or trapdoor panel. BOTH the segment that enters the doorway and the one that leaves it get it, because the body's footprint is beside the panel for the whole width of the cell and neither half of that traversal is over when the centre reaches the middle.
            Crossing = ResolveCrossing(world, ctx, start, end),

            // What has to be OPENED before this segment's body may cross, and the cell it is opened from. The search has already paid ActionCosts.InteractLatency for it at this very edge; this is the payload that says which block the price was for.
            Interaction = ResolveInteraction(ctx, start, end),
        };

    /// <summary>The interaction this segment needs, resolved against the same frozen world the search ran on and by the same predicate the search charged with, so the segment can never name a door the plan did not price (or miss one it did).</summary>
    private static InteractionRequirement? ResolveInteraction(
        CalculationContext? ctx, PathNode start, PathNode end)
    {
        if (ctx is null)
            return null;

        return MoveHelper.TryGetInteraction(
            ctx, start.X, start.Y, start.Z, end.X, end.Y, end.Z, out InteractionRequirement requirement)
            ? requirement
            : null;
    }

    private static BarrierCrossing? ResolveCrossing(
        IPhysicsWorldView? world, CalculationContext? ctx, PathNode start, PathNode end)
    {
        if (world is null)
            return null;

        // A panel the plan is going to FOLD FLAT is not a doorway to squeeze past, and aligning to its free-side band would be aiming at a band that will not exist. The interaction is resolved first here for that one case only; every other kind still gets the observed or predicted panel below.
        if (ctx is not null
            && MoveHelper.TryGetInteraction(
                ctx, start.X, start.Y, start.Z, end.X, end.Y, end.Z, out InteractionRequirement closing)
            && closing.Kind == InteractionKind.CloseByHand)
            return null;

        if (BarrierCrossing.TryResolve(world, end.X, end.Y, end.Z, out BarrierCrossing atEnd))
            return atEnd;

        if (BarrierCrossing.TryResolve(world, start.X, start.Y, start.Z, out BarrierCrossing atStart))
            return atStart;

        // Nothing is a panel YET. If this segment is going to open a trapdoor on its way through, the panel it will then have to squeeze past is predictable from the closed state's `facing`, and it has to be predicted rather than observed: the move that uses it is a FALL, which has no lateral authority at all, so the band has to be entered before the lid goes rather than after. See BarrierCrossing.TryPredictAfterOpening.
        return ctx is not null
            && MoveHelper.TryGetInteraction(
                ctx, start.X, start.Y, start.Z, end.X, end.Y, end.Z, out InteractionRequirement requirement)
            && BarrierCrossing.TryPredictAfterOpening(world, requirement.Witness, out BarrierCrossing predicted)
            ? predicted
            : null;
    }

    /// <summary>The elevation a body standing in this column comes to rest at: the support cell's own footprint support. Falls back to the integer feet cell with no world, or when the cell under the node holds nothing a body rests on inside <c>(0, 1]</c>.</summary>
    internal static double ResolveElevation(IPhysicsWorldView? world, int x, int feetY, int z)
    {
        if (world is null)
            return feetY;

        double height = SupportHeight(world, x, feetY - 1, z);
        return height is > 0.0 and <= 1.0 ? feetY - 1 + height : feetY;
    }

    /// <summary>How much higher than this column's own support the eight COPLANAR neighbours reach, which is how far above <c>End.Y</c> a body whose centre is inside the column can legitimately settle. Zero over uniform terrain and zero with no world.</summary>
    /// <remarks>Only supports inside <c>(0, 1]</c> count, the same bound <c>MoveHelper.CanWalkOn</c> uses, so a fence post or a closed gate reaching 1.5 next door does not widen anything. The scan stays in the support plane on purpose: see <see cref="PathSegment.EndElevationSlack"/>.</remarks>
    internal static double ResolveNeighbourSlack(IPhysicsWorldView? world, int x, int feetY, int z)
    {
        if (world is null)
            return 0.0;

        int supportY = feetY - 1;
        double own = SupportHeight(world, x, supportY, z);
        if (own is <= 0.0 or > 1.0)
        {
            // Nothing is standing on this column's own cell (a swim node, a rung), so there is no "own" elevation for a neighbour to be measured against.
            return 0.0;
        }

        double highest = own;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                double neighbour = SupportHeight(world, x + dx, supportY, z + dz);
                if (neighbour is > 0.0 and <= 1.0 && neighbour > highest)
                    highest = neighbour;

            }

        return highest - own;
    }

    private static double SupportHeight(IPhysicsWorldView world, int x, int y, int z)
        => BlockSupport.FootprintSupportHeight(
            world.GetCollisionShapes(world.GetBlock(new BlockPos(x, y, z))),
            0.5,
            0.5,
            PhysicsConstants.PlayerWidth / 2.0);

    /// <summary>Whether this grounded segment walks into a cell that will pick the body up: a vertical <see cref="MoveType.Swim"/> out of an UPWARD bubble column.</summary>
    /// <remarks>
    /// <para>Narrow on purpose, and the narrowness is measured rather than cautious. The two conditions are independent: a vertical swim says the next segment has no horizontal heading to receive momentum on, and the column says the body's feet leave the floor BEFORE its centre is inside the cell. Bubble-column lift is applied for every cell the body touches, so the <c>min(0.7, vy + 0.06)</c> impulse fires on a corner clip. Ordinary water does neither: a body wading into a pool keeps its feet down and its braking calibrated, and widening this to every walk-into-a-swim measurably re-times those shapes (<c>PushedSegmentRegressionTests</c>' swim shape 54 ticks to 40, and <c>RoofedLaneBreathPricingTests</c>' 30-block wade from 5.87 to 5.83 ticks a block against a model charging 7.30, which crosses that row's own 1.25 pessimism bound). Re-timing water requires a separate calibration.</para>
    /// <para>The cell read is the WALK's own destination column at its logical feet cell, which is the cell the vertical swim starts in.</para>
    /// </remarks>
    private static bool EntersALiftingColumn(PathSegment current, PathSegment next, IPhysicsWorldView? world)
    {
        if (world is null || next.MoveType != MoveType.Swim || next.HeadingX != 0 || next.HeadingZ != 0)
            return false;

        var at = new BlockPos((int)Math.Floor(current.End.X), current.EndFeetY, (int)Math.Floor(current.End.Z));
        return MoveHelper.IsUpwardBubbleColumn(world.GetBlock(at));
    }

    private static PathTransitionType Classify(PathSegment current, PathSegment? next)
    {
        if (next is null)
            return PathTransitionType.FinalStop;

        // A swim segment (or one entering water) never requires a grounded, momentum-preserving handoff;
        // it completes on a position envelope. Treat any swim transition as a plain continuation.
        if (current.MoveType == MoveType.Swim || next.MoveType == MoveType.Swim)
        {
            // A swim INTO a column declares no horizontal heading at all, so "the headings differ" is vacuous here rather than a turn - Math.Sign against a walk's own heading differs by construction, on every column in the game. The LABEL is left alone anyway: it is read by PreserveSprint and by the braking planner's slow-entry test, and re-labelling it moves tick counts on water shapes that were calibrated against it (PushedSegmentRegressionTests' swim shape 54 -> 39, RoofedLaneBreathPricingTests' wade to 5.80 ticks a block against a model charging 7.30). What the handoff needs is stated in the HINTS instead, where it belongs - see BuildHints - and the braking planner reads those.
            return current.HeadingX == next.HeadingX && current.HeadingZ == next.HeadingZ
                ? PathTransitionType.ContinueStraight
                : PathTransitionType.Turn;
        }

        if (next.MoveType is MoveType.Parkour or MoveType.Ascend)
            return PathTransitionType.PrepareJump;

        if (current.MoveType is MoveType.Parkour or MoveType.Descend or MoveType.Fall)
            return PathTransitionType.LandingRecovery;

        if (current.HeadingX == next.HeadingX && current.HeadingZ == next.HeadingZ)
            return PathTransitionType.ContinueStraight;

        return PathTransitionType.Turn;
    }

    private static PathTransitionHints BuildHints(
        PathSegment current,
        PathSegment? next,
        PathSegment? nextNext,
        PathTransitionType exitTransition,
        IPhysicsWorldView? world)
    {
        // Water segments complete off the ground: the swimmer is never OnGround, so gate on a position envelope instead.
        if (current.MoveType == MoveType.Swim)
            return new PathTransitionHints(
                DesiredHeadingX: current.HeadingX,
                DesiredHeadingZ: current.HeadingZ,
                MinExitSpeed: 0.0,
                MaxExitSpeed: double.PositiveInfinity,
                RequireStableFooting: false,
                RequireGrounded: false,
                RequireJumpReady: false,
                AllowAirBrake: false,
                HorizonTicks: 8,
                AllowUngrounded: true);

        if (next is null)
            return new PathTransitionHints(
                DesiredHeadingX: current.HeadingX,
                DesiredHeadingZ: current.HeadingZ,
                MinExitSpeed: 0.0,
                MaxExitSpeed: 0.02,
                RequireStableFooting: true,
                RequireGrounded: true,
                RequireJumpReady: false,
                AllowAirBrake: false,
                HorizonTicks: 12);

        if (next.MoveType is MoveType.Parkour or MoveType.Ascend)
            return new PathTransitionHints(
                DesiredHeadingX: next.HeadingX,
                DesiredHeadingZ: next.HeadingZ,
                MinExitSpeed: next.MoveType == MoveType.Parkour ? 0.10 : 0.0,
                MaxExitSpeed: double.PositiveInfinity,
                RequireStableFooting: false,
                RequireGrounded: true,
                RequireJumpReady: true,
                AllowAirBrake: false,
                HorizonTicks: 10);

        // A grounded move handing off into a swim that declares NO horizontal heading: a body walking into a column that will pick it up and carry it vertically. It gets the WATER hints - the ones the swim itself carries - and not the ground-turn hints the vacuous heading comparison gave it.
        //
        // Scoped to the no-heading case, not to every walk-into-water handoff. A swim that travels horizontally receives momentum from the walk in the ordinary way and its braking is calibrated: widening this to every `next.MoveType == Swim` measurably speeds those entries up (PushedSegmentRegressionTests' swim shape goes 54 ticks to 39, and RoofedLaneBreathPricingTests' 30-block wade goes to 5.80 ticks a block against a model that charges 7.30), which requires a separate calibration.
        //
        // Every clause matters, and course row E12 measured why. RequireGrounded and RequireStableFooting are unsatisfiable in a lifting cell (the body is never on the ground there again), so they add a flat +2000 to every braking candidate and leave the choice to a distance term computed on a forward simulation that does not know it. AllowAirBrake then lets the planner choose Brake off the ground, and GroundedSegmentController.Plan's `if (decision.HoldBack) return` short-circuits the anti-stall creep that exists to break exactly this stall - so the body sits on the column's edge holding Back until the segment's budget expires, and every replan reproduces it exactly. MaxExitSpeed 0.05 is the third: there is no exit speed to hit, because the next segment's authority is vertical and comes from the block rather than from momentum.
        if (EntersALiftingColumn(current, next, world))
            return new PathTransitionHints(
                DesiredHeadingX: current.HeadingX,
                DesiredHeadingZ: current.HeadingZ,
                MinExitSpeed: 0.0,
                MaxExitSpeed: double.PositiveInfinity,
                RequireStableFooting: false,
                RequireGrounded: false,
                RequireJumpReady: false,
                AllowAirBrake: false,
                HorizonTicks: 8,
                AllowUngrounded: true);

        bool turning = current.HeadingX != next.HeadingX || current.HeadingZ != next.HeadingZ;

        // A run-up is momentum handed THROUGH the next segment into the launch, so the next segment has to be one that carries momentum: a grounded walk. A Descend or a Fall in between is not a handoff, it is a landing - the body's velocity is set by gravity and by the floor it hits, and nothing of the run-up survives it.
        //
        // Course rows C6c and C7 are what the missing clause cost. Their approach turns onto a ledge and then drops nineteen and twenty-five blocks, and the segment after the drop is the Ascend that climbs out of the basin - so `nextNext is Ascend` was true and the APPROACH was given the jump-ready envelope: MinExitSpeed 0.05, RequireStableFooting false, RequireJumpReady true. That envelope forbids the approach from settling, snaps its yaw to the exit heading the moment the body's centre is inside the target cell, and leaves the anti-stall creep pressing forward because the footprint never gets inside. Measured on C6c's own geometry: the Diagonal walks to (325.55, 100, 153.85), holds z there while the creep drives x east through the platform edge at 326.0, and goes airborne at tick 52 - `Segment 3/11 failed (Diagonal) after 39 ticks at (326.6459, 96.6537, 153.4766)` in the live run, reproduced here to four decimal places.
        bool nextImmediatelyJumps = nextNext is not null
            && nextNext.MoveType is MoveType.Parkour or MoveType.Ascend
            && next.MoveType is MoveType.Traverse or MoveType.Diagonal;

        // LandingRecovery takes precedence over the turning branch: after a diagonal descend the bot lands carrying residual momentum that RequireStableFooting cannot shed cleanly, so use the footprint-inside completion shortcut.
        if (exitTransition == PathTransitionType.LandingRecovery)
            return new PathTransitionHints(
                DesiredHeadingX: next.HeadingX,
                DesiredHeadingZ: next.HeadingZ,
                MinExitSpeed: 0.03,
                MaxExitSpeed: double.PositiveInfinity,
                RequireStableFooting: false,
                RequireGrounded: true,
                RequireJumpReady: false,
                AllowAirBrake: true,
                HorizonTicks: 12);

        // A Climb is driven by jump on a rung (vanilla's `(horizontalCollision || jumping) && onClimbable` lift), not by a run-up: it can neither receive momentum nor pass any on. So the segment handing off INTO one brakes onto the rung, and must NOT inherit the jump-ready run-up envelope that a two-moves-ahead Ascend or Parkour would otherwise imply through `nextImmediatelyJumps`. That envelope demands MinExitSpeed 0.05 along the approach heading, which is UNSATISFIABLE whenever the approach ends against a wall, and a ladder shaft with a landing at the top is exactly that: the landing block juts into the head cell, the bot's X velocity is pinned at 0 by the collision, and the walk burned its whole tick budget standing on the bottom rung.
        if (next.MoveType == MoveType.Climb)
            return new PathTransitionHints(
                DesiredHeadingX: current.HeadingX,
                DesiredHeadingZ: current.HeadingZ,
                MinExitSpeed: 0.0,
                MaxExitSpeed: double.PositiveInfinity,
                RequireStableFooting: true,
                RequireGrounded: true,
                RequireJumpReady: false,
                AllowAirBrake: true,
                HorizonTicks: 12);

        if (turning)
            return new PathTransitionHints(
                DesiredHeadingX: next.HeadingX,
                DesiredHeadingZ: next.HeadingZ,
                MinExitSpeed: nextImmediatelyJumps ? 0.05 : 0.0,
                MaxExitSpeed: nextImmediatelyJumps ? 0.16 : 0.05,
                RequireStableFooting: !nextImmediatelyJumps,
                RequireGrounded: true,
                RequireJumpReady: nextImmediatelyJumps,
                AllowAirBrake: true,
                HorizonTicks: 12);

        return new PathTransitionHints(
            DesiredHeadingX: next.HeadingX,
            DesiredHeadingZ: next.HeadingZ,
            MinExitSpeed: 0.06,
            MaxExitSpeed: double.PositiveInfinity,
            RequireStableFooting: false,
            RequireGrounded: false,
            RequireJumpReady: false,
            AllowAirBrake: false,
            HorizonTicks: 8);
    }
}
