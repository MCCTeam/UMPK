using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Physics;

namespace Umpk.Pathfinding.Moves;

/// <summary>What kind of switch opens a door, which is really a question about how long it stays open.</summary>
public enum ActivatorKind
{
    /// <summary>A button sets <c>powered</c> and schedules its own release, so the door it powers is open for a fixed window and then shuts.</summary>
    Button = 0,

    /// <summary>A lever latches, and the door stays open until another action flips it back.</summary>
    Lever = 1,

    /// <summary>A pressure plate. A body activates it with its feet, so there is no interaction packet and the stand cell is the plate's own cell.</summary>
    /// <remarks>
    /// <para>The release tick is scheduled at the press and re-armed only when a later check finds the plate occupied. A plate therefore releases at the first interval after the initial press at which its <c>TOUCH_AABB</c> is empty, so the power remaining when the body steps off is somewhere in <c>[0, PressedTime)</c> and not a guaranteed <c>PressedTime</c>. See <see cref="DoorActivation.PlatePressedTicks"/> for why the model charges the whole period anyway and what makes that acceptable here and not for a button.</para>
    /// <para>Ranked BELOW a button, which is ranked below a lever. A switch is worked from a cell the plan was going to visit anyway and constrains nothing about the approach; a plate makes the crossing legal from one cell and no other. Ranking it last is what keeps every route this planner already produced for a door with a switch byte-identical.</para>
    /// </remarks>
    PressurePlate = 2,
}

/// <summary>The switch that opens one iron door, where a body has to stand to work it, and how long the door stays open afterwards.</summary>
/// <remarks>
/// <para>One resolver, shared by the planner's feasibility test and by the executor's press step, so the two can never pick different switches for the same door. That is not tidiness: a planner that proved the crossing against a lever while the executor pressed a button would have proved nothing about the crossing that actually happens.</para>
/// <para>Pressure plates activate when a body stands on them. They do not consume a <c>use_item_on</c> action, so their activation belongs to the route rather than an interaction.</para>
/// </remarks>
/// <param name="Activator">The button or lever to send the <c>use_item_on</c> against.</param>
/// <param name="StandCell">The feet cell a body works it from, chosen for reach and nearness to the door.</param>
/// <param name="Kind">Button or lever.</param>
/// <param name="WindowTicks">How long the door stays open after the press, or <see cref="NoWindow"/> for a lever.</param>
public readonly record struct DoorActivation(
    BlockPos Activator,
    BlockPos StandCell,
    ActivatorKind Kind,
    int WindowTicks)
{
    /// <summary><c>stone_button</c> and <c>polished_blackstone_button</c>: <b>20</b> ticks.</summary>
    public const int StoneButtonWindowTicks = 20;

    /// <summary>Every wood and nether button: <b>30</b> ticks.</summary>
    public const int WoodenButtonWindowTicks = 30;

    /// <summary>A lever's "window": there is not one. Used as the sentinel, and it is feasible by definition.</summary>
    public const int NoWindow = 0;

    /// <summary>Every plain pressure plate - all eleven woods, both nether woods, bamboo, cherry, stone and polished blackstone: <b>20</b> ticks.</summary>
    /// <remarks>
    /// <para>The duration is 20 ticks in every supported era.</para>
    /// <para><b>The honest weakness, stated rather than buried.</b> This is charged as though a plate held its signal for the whole period after the body left it, and vanilla makes no such promise: the release tick is armed at the press and re-armed only while occupied, so the residual after departure is anywhere in <c>[0, 20)</c>. Three things make that acceptable for a plate and would not make it acceptable for a button. First, the body leaves the plate's contact box only 0.2375 blocks into the doorway (the box is <c>[0.0625, 0.9375]</c> horizontally against a 0.6-wide body) and is past <see cref="Execution.BarrierCrossing.CommitOffset"/> 0.4875 blocks in, so the interval in which a re-close can overlap the body is a quarter of a block. Second, a re-close before that commit point is already handled - the executor abandons and replans. Third, unlike a single-shot press, the recovery is one cell backwards ONTO the plate, which presses it again.</para>
    /// </remarks>
    public const int PlatePressedTicks = 20;

    /// <summary>The two weighted plates, gold and iron: <b>10</b> ticks in every supported era.</summary>
    /// <remarks>A single player powers both with signal strength 1. What refuses them is that the shortest crossing this resolver can construct is two blocks, which <see cref="RealTicksToClear(int)"/> charges at 17, and 17 does not fit 10. That falls out of the arithmetic in exactly the way "a stone button is never an activator" does.</remarks>
    public const int WeightedPlatePressedTicks = 10;

    /// <summary>Interaction reach, in blocks: eye (feet + 1.62) to the nearest point of the target block's unit cube. One number covers every protocol this library speaks; see <c>Umpk.Client.Actions.InteractionActions.BlockInteractionRange</c>, which is this constant, for the three per-era server checks it satisfies.</summary>
    public const double InteractReach = 4.5;

    /// <summary>The fixed part of the real-tick model, in ticks. See <see cref="RealTicksToClear(int)"/>.</summary>
    public const double ClearInterceptTicks = 7.0;

    /// <summary>The per-block part of the real-tick model, in ticks. See <see cref="RealTicksToClear(int)"/>.</summary>
    public const double ClearTicksPerBlock = 5.0;

    /// <summary>How many REAL ticks a standing-start walk of <paramref name="blocks"/> blocks takes, which is what a press-to-clear costs and is nothing like what the planner charges for the same walk.</summary>
    /// <remarks>
    /// <para><b>Measured, then fitted upward.</b> Driving the real <c>PathExecutor</c> over the real <c>PlayerPhysics</c> down a walled lane from rest, this harness measures 15/21/22/26/29/39 ticks for 2/3/4/5/6/8 blocks against planner charges of 7.13/10.69/14.26/17.82/21.38/28.51 - a ratio of 1.4 to 2.1. A second measurement produced 15/22/27/29/36/45 for the same lengths. The model covers the higher value at every point: <c>7 + 5n</c> gives 17/22/27/32/37/47.</para>
    /// <para><b>Which way it is allowed to be wrong.</b> Over-predicting refuses an activation, which leaves an iron door a wall and sends the planner round. Under-predicting walks a body into a doorway whose panel is about to close on it, which is the worst failure this whole feature can produce. So the model dominates every measurement rather than fitting through them, and the linear form keeps doing so past the measured range: the marginal cost of a block at terminal sprint is about 3.6 ticks, well under this slope.</para>
    /// <para>The model covers both measurement sets and favors the safer, longer duration.</para>
    /// </remarks>
    /// <param name="blocks">How many blocks the body walks from the press cell to clear the doorway.</param>
    /// <returns>The modelled real-tick cost.</returns>
    public static double RealTicksToClear(int blocks)
        => ClearInterceptTicks + (ClearTicksPerBlock * blocks);

    /// <summary>The fixed part of the LID model, in ticks. See <see cref="LidTicksToClear(int)"/>.</summary>
    public const double LidInterceptTicks = 5.0;

    /// <summary>The per-block part of the LID model, in ticks. See <see cref="LidTicksToClear(int)"/>.</summary>
    public const double LidTicksPerBlock = 4.0;

    /// <summary>How many REAL ticks it takes to get THROUGH a lid - a horizontal trapdoor the body drops past - from a standing start <paramref name="blocks"/> blocks away from it.</summary>
    /// <remarks>
    /// <para><b>A lid is not a doorway, and <see cref="RealTicksToClear(int)"/> is the wrong question for one.</b> A doorway is crossed by walking IN and then OUT, so its charge is the walk plus the one cell that carries the body clear - and it has to be generous, because a panel that shuts on a body standing in the doorway stops it. A lid is crossed by stepping OFF: the moment the body's feet are below the plate's plane it is falling, the plate can reappear above it and nothing happens, and there is no second cell to walk. Charging a lid the doorway's <c>blocks + 1</c> is what refuses course row L5, whose whole geometry is one lateral step off a lid three blocks above its landing: <c>RealTicksToClear + 10 = 27</c> against a stone button's 20, so no stone button could ever open any lid.</para>
    /// <para><b>Measured, then fitted upward.</b> Driving the real <c>PathExecutor</c> over the real <c>PlayerPhysics</c> off L5's own lid, from a standing start at 1, 2 and 3 blocks, and counting ticks until the body's feet pass below the closed plate's plane (99.8125): <b>8, 12, 15</b>. <c>5 + 4n</c> gives 9, 13, 17 and dominates all three.</para>
    /// <para><b>Which way it is allowed to be wrong, and why it is the opposite of a doorway's.</b> Under-predicting a DOORWAY walks a body into a panel that is about to close on it. Under-predicting a LID leaves the body standing on a plate that has shut again - exactly where it was standing before the press, on solid ground, with the segment failing and a replan available. The consequence is a wasted interaction, not a trapped body, so the lid model is allowed to sit close to its measurements where the doorway model is not.</para>
    /// <para><b>The margin is thin and that is stated rather than hidden.</b> One block plus <see cref="Core.ActionCosts.InteractLatency"/> is 19 against a stone button's 20. If that latency calibrates UP in W5, L5's stone button stops fitting and the row needs a wooden one; if it calibrates down, the margin grows. Nothing else in the family is near the boundary.</para>
    /// </remarks>
    /// <param name="blocks">How many blocks the body walks from the press cell to reach the lid.</param>
    /// <returns>The modelled real-tick cost.</returns>
    public static double LidTicksToClear(int blocks)
        => LidInterceptTicks + (LidTicksPerBlock * blocks);

    /// <summary>Whether a crossing of <paramref name="blocks"/> blocks fits inside a <paramref name="windowTicks"/>-tick window.</summary>
    /// <remarks>
    /// <para><b>The observe latency is INSIDE the window, not before it.</b> The window clock starts when the button is pressed, and the round trip that tells this client the door actually opened is spent standing still at the press cell - so <see cref="Core.ActionCosts.InteractLatency"/> comes out of the same budget the walk does. A check that charged only the walk would grant a crossing that has already burned half its window before the body moves.</para>
    /// <para>Worked, on the course's own rows. L1 is two blocks: 17 model ticks plus 10 of latency is 27, which clears a wooden button's 30 with 3 to spare and misses a stone button's 20 by seven. That is why L1's build is oak and not stone. L4 is nine: 52 plus 10 is 62, and no button in the game holds a signal that long.</para>
    /// </remarks>
    /// <param name="blocks">Blocks from the press cell to clear of the doorway.</param>
    /// <param name="windowTicks">The window, or <see cref="NoWindow"/> for a latch.</param>
    /// <returns>True when the crossing fits.</returns>
    public static bool FitsWindow(int blocks, int windowTicks)
        => FitsWindow(blocks, windowTicks, lid: false);

    /// <summary>Whether a crossing of <paramref name="blocks"/> blocks fits inside a <paramref name="windowTicks"/>-tick window, charged as a doorway or as a lid.</summary>
    /// <remarks>The two models differ in what "crossing" means; see <see cref="LidTicksToClear(int)"/>. The observe latency is inside the window either way, because the round trip that tells this client the barrier moved is spent standing still at the press cell whichever way the barrier moves.</remarks>
    /// <param name="blocks">Blocks from the press cell to the barrier, plus one for a doorway's exit.</param>
    /// <param name="windowTicks">The window, or <see cref="NoWindow"/> for a latch.</param>
    /// <param name="lid">True when the barrier is a trapdoor the body drops past rather than walks through.</param>
    /// <returns>True when the crossing fits.</returns>
    public static bool FitsWindow(int blocks, int windowTicks, bool lid)
        => FitsWindow(blocks, windowTicks, lid, chargeLatency: true);

    /// <summary><see cref="FitsWindow(int, int, bool)"/>, with the choice of whether the observe latency comes out of the window.</summary>
    /// <remarks>
    /// <para><b>This is the one line of arithmetic that separates a plate from a button.</b> A button's window clock starts at the press, and the round trip that tells this client the door actually moved is spent standing still at the press cell - inside the same budget the walk comes out of. A PLATE's round trip is spent standing ON THE PLATE, and standing on the plate is what holds the door open, so the door cannot close during it. Charging that time would refuse every plate in the game: two blocks is 17 model ticks, and 17 + 10 = 27 against the 20 a plain plate holds.</para>
    /// <para><paramref name="chargeLatency"/> defaults to true in every other overload, so the existing button and lid arithmetic is byte-identical.</para>
    /// </remarks>
    /// <param name="blocks">Blocks from the stand cell to the barrier, plus one for a doorway's exit.</param>
    /// <param name="windowTicks">The window, or <see cref="NoWindow"/> for a latch.</param>
    /// <param name="lid">True when the barrier is a trapdoor the body drops past rather than walks through.</param>
    /// <param name="chargeLatency">False only for an activator the body is already standing on, i.e. a pressure plate.</param>
    /// <returns>True when the crossing fits.</returns>
    public static bool FitsWindow(int blocks, int windowTicks, bool lid, bool chargeLatency)
        => windowTicks == NoWindow
            || (lid ? LidTicksToClear(blocks) : RealTicksToClear(blocks))
                + (chargeLatency ? ActionCosts.InteractLatency : 0.0)
                <= windowTicks;

    /// <summary>Finds the switch that opens the door at <paramref name="door"/>, or reports that none does.</summary>
    /// <remarks>
    /// <para><b>What counts as powering the door.</b> The game checks the six face-adjacent cells for a redstone signal. Two of those answers can come from a button or a lever:</para>
    /// <list type="number">
    /// <item><b>Direct.</b> The switch's own cell is face-adjacent to either half. A powered button or
    /// lever supplies strength 15 in every direction, so this needs nothing of its attachment.</item>
    /// <item><b>Through a conductor.</b> A redstone conductor takes the maximum of its own signal and
    /// the direct signal of its six neighbours, and a switch's direct signal reaches exactly one cell: the block it hangs on. For <c>face=WALL</c>, the attachment lies opposite <c>FACING</c>; for attachment is the neighbour in the OPPOSITE direction; for <c>FLOOR</c> it is the cell below and for <c>CEILING</c> the cell above. So a switch also powers the door when its attachment is a full solid cube face-adjacent to a half.</item>
    /// </list>
    /// <para>Both halves of the door count because either block can receive the signal, and the door keeps <c>open</c> synchronized between them.</para>
    /// <para><b>What is NOT modelled, and why the omission is the safe direction.</b> Redstone WIRE is not traced. A far switch wired to a door through dust resolves to no switch at all, and the door stays a wall. That refuses a route a player could walk, which costs a detour; the alternative - guessing at wire propagation, signal strength and the directional rules of redstone wire risks the opposite error, which is a bot that presses something, stands in front of a door that never moves, and burns its whole interaction budget there. Course row L4 is exactly that shape and refuses; its window arithmetic is pinned separately.</para>
    /// <para><b>A stone button is never an activator under the configured constants, and that falls out of the arithmetic rather than being a rule.</b> The shortest crossing this resolver can construct is two blocks - a press cell adjacent to the door, one cell into the doorway and one out of it - which is 17 model ticks plus 10 of observe latency against a 20-tick window. Course row L1 uses an oak button for this reason. If <see cref="Core.ActionCosts.InteractLatency"/> calibrates down in W5, this is the boundary that moves.</para>
    /// <para><b>Preference.</b> A lever beats a button beats a plate. A latch imposes no window at all and a press imposes one the crossing then has to fit; and a switch of either kind is worked from a cell the plan was going to visit anyway, where a PLATE additionally constrains which cell the body may enter the doorway from (see <see cref="MoveHelper.IsBarrierCrossingLegal"/>). Ranking the plate last is what keeps every route this planner already produced for a door with a switch byte-identical. Within a kind, the nearest stand cell wins, because the window charge is a function of that distance. An activator whose crossing does not fit its own window is not returned: the caller's only correct response would have been to discard it.</para>
    /// <para><b>A plate is resolved only for a DOORWAY, never for a lid.</b> A trapdoor is crossed by dropping out of the cell the body is standing in, so a body opening one with its feet would release the plate at the same instant it needs the lid gone, and unlike a doorway there is no commit point past which a re-close is harmless - the body is in free fall through the plate's own plane. That is a claim no fixture and no course row here supports, so it is refused rather than guessed.</para>
    /// <para><b>Cost.</b> This scans a small box for candidates and then a larger one for a stand cell, so it is far too expensive for a per-node predicate and must be memoised per door by whatever calls it from inside a search.</para>
    /// </remarks>
    /// <param name="ctx">The planning context.</param>
    /// <param name="door">Either half of the door, or the trapdoor's own cell.</param>
    /// <param name="activation">The resolved activation.</param>
    /// <returns>True when a usable switch exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    public static bool TryResolve(CalculationContext ctx, BlockPos door, out DoorActivation activation)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        activation = default;

        BlockState state = ctx.GetBlock(door.X, door.Y, door.Z);
        if (!state.TryGetProperty(OpenProperty, out _)
            || MoveHelper.ClassifyBarrier(state) != BarrierKind.Wall
            || MoveHelper.CanOpenByHand(state))
        {
            // Either a source that cannot read `open` at all (every pre-flattening protocol - reasoning about a switch for a door this library cannot tell is shut would be a guess), not a closed barrier at all, or one a bare hand opens, in which case BarrierKind.NeedsInteraction already covers it and pressing a switch would pay for one crossing twice.
            return false;
        }

        // A trapdoor is a lid: its closed shape is horizontal, never a vertical panel, so the body crosses it by stepping off rather than by walking through, and the window it has to fit is a different arithmetic. See LidTicksToClear.
        bool lid = state.Block.Id.Path.EndsWith(TrapdoorSuffix, StringComparison.Ordinal);

        Span<BlockPos> halves = stackalloc BlockPos[2];
        int halfCount = CollectHalves(ctx, door, state, halves);

        bool found = false;
        DoorActivation best = default;
        int bestDistance = int.MaxValue;

        for (int dx = -RelayReach; dx <= RelayReach; dx++)
        {
            for (int dy = -RelayReach; dy <= RelayReach; dy++)
            {
                for (int dz = -RelayReach; dz <= RelayReach; dz++)
                {
                    var at = new BlockPos(door.X + dx, door.Y + dy, door.Z + dz);
                    if (!TryReadActivator(ctx, at, out ActivatorKind kind, out int window))
                        continue;

                    if (!PowersAnyHalf(ctx, at, halves, halfCount))
                        continue;

                    bool plate = kind == ActivatorKind.PressurePlate;
                    if (plate && lid)
                    {
                        // See the class remarks: a plate cannot honestly open a lid, because the body releases it in the same instant it starts falling through it.
                        continue;
                    }

                    BlockPos stand;
                    int distance;
                    if (plate)
                    {
                        // A plate has exactly one stand cell and it is the plate's own: the body works it by occupying it. There is no reach question and therefore no search - which is also the whole reason this needs no causal planning, because the cell the plan must put the body in IS the cell it was already routing through.
                        if (OverlapsAHalf(at.X, at.Y, at.Z, halves, halfCount)
                            || !MoveHelper.CanStandAt(ctx, at.X, at.Y, at.Z))
                            continue;

                        stand = at;
                        distance = Math.Abs(at.X - door.X) + Math.Abs(at.Z - door.Z);
                    }
                    else if (!TryFindStandCell(ctx, at, door, halves, halfCount, lid, out stand, out distance))
                        continue;

                    // Blocks from the press cell to clear of the doorway: the walk to the door plus the one cell that carries the body out of it. A LID has no second cell - the body steps off it and is falling - so it is charged the walk alone. A PLATE is charged the same doorway walk as a button and NOT the observe latency, because that round trip is spent standing on the plate, which is what holds the door open. See FitsWindow.
                    if (!FitsWindow(lid ? distance : distance + 1, window, lid, chargeLatency: !plate))
                        continue;

                    // A lever wins outright, then a button, then a plate; within a kind the nearest stand cell does.
                    bool better = !found
                        || Rank(kind) < Rank(best.Kind)
                        || (kind == best.Kind && distance < bestDistance);
                    if (!better)
                        continue;

                    found = true;
                    bestDistance = distance;
                    best = new DoorActivation(at, stand, kind, window);
                }
            }
        }

        activation = best;
        return found;
    }

    /// <summary>The preference order, lowest first: a lever imposes no window, a button imposes one but no routing constraint, and a plate imposes both.</summary>
    private static int Rank(ActivatorKind kind) => kind switch
    {
        ActivatorKind.Lever => 0,
        ActivatorKind.Button => 1,
        _ => 2,
    };

    /// <summary>How far a candidate switch may sit from the door: two cells, which is the face-adjacent cell plus one block of relay through a conductor, and is the whole of what <c>SignalGetter</c> can carry without a wire.</summary>
    private const int RelayReach = 2;

    /// <summary>How far from the switch a stand cell is looked for, bounded by <see cref="InteractReach"/>.</summary>
    private const int StandSearchRadius = 4;

    /// <summary>The two blocks of a door, or the one block of a trapdoor.</summary>
    private static int CollectHalves(CalculationContext ctx, BlockPos door, BlockState state, Span<BlockPos> halves)
    {
        halves[0] = door;
        int count = 1;
        var id = state.Block.Id;
        for (int dy = -1; dy <= 1; dy += 2)
        {
            BlockState other = ctx.GetBlock(door.X, door.Y + dy, door.Z);
            if (!other.IsDefault && other.Block.Id == id)
            {
                halves[count++] = new BlockPos(door.X, door.Y + dy, door.Z);
                break;
            }
        }

        return count;
    }

    private static bool TryReadActivator(
        CalculationContext ctx, BlockPos at, out ActivatorKind kind, out int windowTicks)
    {
        kind = default;
        windowTicks = NoWindow;

        BlockState state = ctx.GetBlock(at.X, at.Y, at.Z);
        if (state.IsDefault || state.IsAir)
            return false;

        string path = state.Block.Id.Path;
        if (path == LeverPath)
        {
            kind = ActivatorKind.Lever;
            windowTicks = NoWindow;
            return true;
        }

        if (path.EndsWith(PressurePlateSuffix, StringComparison.Ordinal))
        {
            // Weighted plates carry `power` (an int 0-15) and every plain plate carries `powered` (a boolean. Requiring the correct property to resolve refuses a pre-flattening source, where every property read answers false and reasoning about a plate this library cannot tell pressed from unpressed would be guessing metadata. Same policy the door's own `open` read states.
            bool weighted = path is LightWeightedPressurePlatePath or HeavyWeightedPressurePlatePath;
            if (!state.TryGetProperty(weighted ? PowerProperty : PoweredProperty, out _))
                return false;

            kind = ActivatorKind.PressurePlate;
            windowTicks = weighted ? WeightedPlatePressedTicks : PlatePressedTicks;
            return true;
        }

        if (!path.EndsWith(ButtonSuffix, StringComparison.Ordinal))
            return false;

        kind = ActivatorKind.Button;
        windowTicks = path is StoneButtonPath or PolishedBlackstoneButtonPath
            ? StoneButtonWindowTicks
            : WoodenButtonWindowTicks;
        return true;
    }

    private static bool PowersAnyHalf(
        CalculationContext ctx, BlockPos activator, ReadOnlySpan<BlockPos> halves, int halfCount)
    {
        for (int i = 0; i < halfCount; i++)
            if (IsFaceAdjacent(activator, halves[i]))
                return true;

        // A PLATE never reaches this arm, and that is deliberate rather than accidental: TryGetAttachment reads `face`, which no plate carries, so it answers false. A plate can relay through the full cube below it, but every door that reaches sits at or below that support's level, so no legal level crossing starts from the plate cell and modelling it would only produce activations the edge rule then refuses.
        if (!TryGetAttachment(ctx, activator, out BlockPos attachment)
            || !ctx.GetBlock(attachment.X, attachment.Y, attachment.Z).IsSolid)
            return false;

        for (int i = 0; i < halfCount; i++)
            if (IsFaceAdjacent(attachment, halves[i]))
                return true;

        return false;
    }

    /// <summary>The block a switch hangs on, which is the one cell its direct signal reaches. The attachment direction is <c>DOWN</c> for <c>CEILING</c>, <c>UP</c> for <c>FLOOR</c> and <c>FACING</c> for <c>WALL</c>, and the attachment is the neighbour in the opposite direction.</summary>
    private static bool TryGetAttachment(CalculationContext ctx, BlockPos activator, out BlockPos attachment)
    {
        attachment = default;
        BlockState state = ctx.GetBlock(activator.X, activator.Y, activator.Z);
        if (!state.TryGetProperty(FaceProperty, out string face))
            return false;

        switch (face)
        {
            case "floor":
                attachment = new BlockPos(activator.X, activator.Y - 1, activator.Z);
                return true;
            case "ceiling":
                attachment = new BlockPos(activator.X, activator.Y + 1, activator.Z);
                return true;
            default:
                break;
        }

        if (!state.TryGetProperty(FacingProperty, out string facing))
            return false;

        (int dx, int dz) = facing switch
        {
            "north" => (0, 1),
            "south" => (0, -1),
            "west" => (1, 0),
            "east" => (-1, 0),
            _ => (0, 0),
        };

        if (dx == 0 && dz == 0)
            return false;

        attachment = new BlockPos(activator.X + dx, activator.Y, activator.Z + dz);
        return true;
    }

    /// <summary>Whether a body standing at this cell would have its feet or its head inside the door.</summary>
    private static bool OverlapsAHalf(int x, int y, int z, ReadOnlySpan<BlockPos> halves, int halfCount)
    {
        for (int i = 0; i < halfCount; i++)
        {
            BlockPos half = halves[i];
            if (half.X == x && half.Z == z && (half.Y == y || half.Y == y + 1))
                return true;

        }

        return false;
    }

    private static bool IsFaceAdjacent(BlockPos a, BlockPos b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;

    /// <summary>The standable cell nearest the door from which the switch is in reach, and its block distance to the door. Ties break on the scan order, which is deterministic.</summary>
    private static bool TryFindStandCell(
        CalculationContext ctx,
        BlockPos activator,
        BlockPos door,
        ReadOnlySpan<BlockPos> halves,
        int halfCount,
        bool lid,
        out BlockPos stand,
        out int distance)
    {
        stand = default;
        distance = int.MaxValue;
        int bestRise = int.MaxValue;
        bool found = false;

        for (int dx = -StandSearchRadius; dx <= StandSearchRadius; dx++)
        {
            for (int dy = -StandSearchRadius; dy <= StandSearchRadius; dy++)
            {
                for (int dz = -StandSearchRadius; dz <= StandSearchRadius; dz++)
                {
                    int x = activator.X + dx;
                    int y = activator.Y + dy;
                    int z = activator.Z + dz;
                    // A body cannot stand in the doorway to press the switch that opens the doorway. The classifier's own memo guards the HALF being classified against that recursion, but the other half is a different memo key and would otherwise answer "passable, and therefore standable" - which is true only AFTER the press this stand cell is for.
                    if (OverlapsAHalf(x, y, z, halves, halfCount))
                        continue;

                    // ...and for a LID, nowhere in the lid's own COLUMN, which is the same rule one axis over. OverlapsAHalf covers a doorway because a door's two halves span the body's own two cells; a lid is one block with an open shaft under it and a floor over it, so the cells that rule would miss are exactly the ones a body could stand in - on the plate, or already at the bottom of the shaft. Neither is a cell the crossing starts from: MoveHelper.TryGetInteraction presses from the move's own source cell whenever the
                    // switch is in reach, and the move that uses a lid ENTERS its column rather than
                    // starting in it. Left in, the shaft floor scores a zero-block window for a one-block walk, which is the under-prediction the doorway model exists to refuse.
                    if (lid && x == door.X && z == door.Z)
                        continue;

                    if (!EyeReaches(x, y, z, activator) || !MoveHelper.CanStandAt(ctx, x, y, z))
                        continue;

                    // HORIZONTAL blocks only, and that is not a simplification. The number this feeds is how far the body WALKS from the press cell to clear the doorway, and a door's two halves are the same doorway: counting the one-block rise to an upper half would price the identical crossing differently depending on which half the classifier happened to be asking about, which for a wooden button is the difference between 27 ticks and 32 against a window of 30.
                    int candidate = Math.Abs(x - door.X) + Math.Abs(z - door.Z);
                    int rise = Math.Abs(y - door.Y);
                    if (found && (candidate > distance || (candidate == distance && rise >= bestRise)))
                        continue;

                    found = true;
                    distance = candidate;
                    bestRise = rise;
                    stand = new BlockPos(x, y, z);
                }
            }
        }

        return found;
    }

    /// <summary>Whether a body standing with its feet in <paramref name="feet"/> has its eye within <see cref="InteractReach"/> of the nearest point of the target's unit cube. The same arithmetic <c>Umpk.Client.Actions.InteractionActions.UseBlockVerifiedAsync</c> applies before it sends, so the planner cannot promise a press the client will then refuse.</summary>
    /// <param name="feet">The body's feet cell.</param>
    /// <param name="target">The block being reached for.</param>
    /// <returns>True when the eye is in range.</returns>
    public static bool CanReachFrom(BlockPos feet, BlockPos target)
        => EyeReaches(feet.X, feet.Y, feet.Z, target);

    private static bool EyeReaches(int x, int y, int z, BlockPos target)
    {
        double eyeX = x + 0.5;
        double eyeY = y + PhysicsConstants.PlayerStandingEyeHeight;
        double eyeZ = z + 0.5;

        double dx = Gap(eyeX, target.X);
        double dy = Gap(eyeY, target.Y);
        double dz = Gap(eyeZ, target.Z);
        return (dx * dx) + (dy * dy) + (dz * dz) <= InteractReach * InteractReach;

        static double Gap(double value, int min)
            => value < min ? min - value : value > min + 1 ? value - (min + 1) : 0.0;
    }

    private const string LeverPath = "lever";

    /// <summary>The suffix that separates a lid from a doorway; see <see cref="LidTicksToClear(int)"/>.</summary>
    private const string TrapdoorSuffix = "_trapdoor";

    private const string ButtonSuffix = "_button";

    private const string StoneButtonPath = "stone_button";

    /// <summary>The suffix every one of vanilla's sixteen pressure plates ends in.</summary>
    private const string PressurePlateSuffix = "_pressure_plate";

    private const string LightWeightedPressurePlatePath = "light_weighted_pressure_plate";

    private const string HeavyWeightedPressurePlatePath = "heavy_weighted_pressure_plate";

    /// <summary>A plain plate's boolean property.</summary>
    private const string PoweredProperty = "powered";

    /// <summary>A weighted plate's integer property in the range 0-15.</summary>
    private const string PowerProperty = "power";

    private const string PolishedBlackstoneButtonPath = "polished_blackstone_button";

    private const string OpenProperty = "open";

    private const string FaceProperty = "face";

    private const string FacingProperty = "facing";
}
