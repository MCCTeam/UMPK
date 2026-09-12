using Umpk.Game.Blocks;
using Umpk.Pathfinding.Moves;

namespace Umpk.Pathfinding.Core;

/// <summary>
/// The per-search planning context: an immutable <see cref="PlanningWorldView"/>, the caller's <see cref="PathfinderOptions"/>, and the derived per-move costs. Created once per search; every move calculation reads from it (a genuinely immutable point-in-time view, not a live world reference).
///
/// <para><see cref="PreviousMoveType"/> and <see cref="CurrentEntryPreparation"/> are per-search scratch fields the A* driver sets before expanding each node; they are instance state on a short-lived context, never process-global.</para>
/// </summary>
public sealed class CalculationContext
{
    /// <summary>The immutable planning world view.</summary>
    public PlanningWorldView World { get; }

    /// <summary>The caller's options.</summary>
    public PathfinderOptions Options { get; }

    /// <summary>The player's capabilities at capture time (effects, carried items). Frozen exactly like <see cref="World"/>: it describes the instant the plan was captured, and a replan is what refreshes it. <see cref="PathfinderCapabilities.None"/> when nobody captured any, which is what every session-free planner run gets, so nothing prices differently by default.</summary>
    public PathfinderCapabilities Capabilities { get; }

    /// <summary>Whether this plan may treat the fire-damage hazards as harmless, because the player holds fire resistance for longer than the route takes.</summary>
    /// <remarks>
    /// <para><b>The caller owns the evidence.</b> This is a statement, not a question: the context has no route and therefore no way to ask <see cref="EffectCoverage"/> whether the duration covers one, and answering it optimistically here would clear a hazard on an estimate. The caller plans once with it set, prices the finished route with <see cref="BreathValidator.RouteRealTicks"/>, re-checks <see cref="EffectCoverage.Covers(PathfinderCapabilities, Identifier, double)"/> against that, and re-plans with it clear if the potion falls short. Automating that round trip inside the planner is its own commit; what this one owns is the predicate.</para>
    /// <para>Default false, so every existing construction site and every hazard row prices exactly as it did.</para>
    /// </remarks>
    public bool FireHazardsCleared { get; }

    /// <summary>Whether this plan may STAND ON powder snow, because the capture found leather boots on the player's feet. See <see cref="PathfinderCapabilities.PowderSnowWalkable"/>.</summary>
    /// <remarks>
    /// <para><b>Derived, not a constructor parameter, and the difference from <see cref="FireHazardsCleared"/> is the point.</b> Fire resistance clears a hazard only if it outlasts the ROUTE, which the context has no way to know, so the caller has to own that evidence. Boots are a plain fact already sitting in the capture and no route-length argument applies to them: they are on or they are not.</para>
    /// <para><b>Resolved once, in the constructor, not per read.</b> <c>PathfinderCapabilities.PowderSnowWalkable</c> is a linear scan of up to 46 captured slots, and <see cref="MoveHelper.IsHazard"/> and <see cref="MoveHelper.CanWalkThrough"/> ask this on every node evaluation - the same reason <see cref="FallDamageBudget"/> is resolved here rather than recomputed.</para>
    /// <para><b>What it changes, exactly.</b> Powder snow leaves the curated hazard set, starts presenting a full unit cube as a floor, and becomes IMPASSABLE. The third is not decoration: a body inside the cell receives an empty shape, so a booted body can stand on the snow and can never enter it, and a plan that only cleared the hazard would plant nodes in a cell the executor cannot reach.</para>
    /// <para>Default false, so every existing construction site prices exactly as it did.</para>
    /// </remarks>
    public bool PowderSnowWalkable { get; }

    /// <summary>Whether sprinting is allowed.</summary>
    public bool CanSprint => Options.AllowSprint;

    /// <summary>Whether the parkour (sprint-jump) family is allowed.</summary>
    public bool AllowParkour => Options.AllowParkour;

    /// <summary>Whether upward parkour is allowed.</summary>
    public bool AllowParkourAscend => Options.AllowParkourAscend;

    /// <summary>Whether swimming moves are allowed.</summary>
    public bool AllowSwim => Options.AllowSwim;

    /// <summary>Whether ladder/vine climbing is allowed.</summary>
    public bool AllowClimb => Options.AllowClimb;

    /// <summary>Whether diagonal descend steps are allowed.</summary>
    public bool AllowDiagonalDescend => Options.AllowDiagonalDescend;

    /// <summary>Whether this plan may open a hand-openable door, trapdoor or fence gate on its way through. See <see cref="PathfinderOptions.AllowDoorInteraction"/>.</summary>
    public bool AllowDoorInteraction => Options.AllowDoorInteraction;

    /// <summary>The maximum safe fall height onto solid ground.</summary>
    public int MaxFallHeight => Options.MaxFallHeight;

    /// <summary>The maximum fall height into water.</summary>
    public int MaxFallHeightWater => Options.MaxFallHeightIntoWater;

    /// <summary>Whether mid-fall ladder grabs are allowed.</summary>
    public bool AllowLadderGrabDuringFall => Options.AllowLadderGrabDuringFall;

    /// <summary>The jump-takeoff penalty in ticks.</summary>
    public double JumpPenalty => Options.JumpPenalty;

    /// <summary>The walk cost per block.</summary>
    public double WalkCost { get; }

    /// <summary>The sprint cost per block (falls back to walk cost when sprinting is disabled).</summary>
    public double SprintCost { get; }

    /// <summary>The sneak cost per block.</summary>
    public double SneakCost { get; }

    /// <summary>The swim cost per block in still water.</summary>
    /// <remarks>Use <see cref="SwimCostThrough"/> for a move that is actually being emitted: this is the flat number the current scales.</remarks>
    public double SwimCost { get; }

    /// <summary>The hearts this plan may spend on landings, from the capability snapshot's vitals. Zero when the producer could not see the player's health, which is what keeps a partly-absorbing landing (hay) out of a plan that cannot price it. See <see cref="FallDamageModel.Budget"/>.</summary>
    public double FallDamageBudget { get; }

    /// <summary>Whether a body that drops <paramref name="dropBlocks"/> blocks onto <paramref name="onto"/> would FALL THROUGH it rather than land on it, which happens on exactly one block and only for a body that could otherwise stand there.</summary>
    /// <remarks>
    /// <para>After a fall distance above 2.5 blocks, powder snow presents a 0.9-tall box even to a booted body. The body rests at 0.9, its fall distance resets, and on the next tick it sinks the rest of the way. This long-drop behavior is why powder snow breaks falls.</para>
    /// <para><b>Why the plan must refuse rather than model the sink.</b> Landing a booted body on the snow's top face from three blocks up describes something that will not happen, and the body ends a whole cell low, INSIDE a snow column that <see cref="MoveHelper.CanWalkThrough"/> will not route out of. Refusing loses a route vanilla allows and can never produce a plan the body cannot run, which is the direction this planner takes every time the two disagree.</para>
    /// <para><b>Boundary.</b> The threshold is in whole blocks because a drop of N blocks accumulates a fall distance of very nearly N - the collide clamps the body at the surface, it does not overshoot - so two is safe by half a block and three trips the arm with the whole of vanilla's margin to spare.</para>
    /// <para>Answers false for a body that is not booted, because such a body cannot be planned onto powder snow at any height: it is still a hazard.</para>
    /// </remarks>
    /// <param name="onto">The block the body would come to rest on.</param>
    /// <param name="dropBlocks">The vertical distance the body falls, in whole blocks.</param>
    /// <returns>True when the landing is not a landing.</returns>
    public bool LandingSinksThroughPowderSnow(BlockState onto, int dropBlocks)
        => PowderSnowWalkable && dropBlocks > PowderSnowFallThroughBlocks && onto.IsPowderSnow;

    /// <summary>Powder snow's fall-through threshold is 2.5 blocks, so two whole blocks is the last drop that lands.</summary>
    private const int PowderSnowFallThroughBlocks = 2;

    /// <summary>Whether a body that drops <paramref name="fallHeight"/> blocks onto <paramref name="onto"/> can pay for the landing out of <see cref="FallDamageBudget"/>.</summary>
    /// <remarks>
    /// <para><b>Never assumed, only ever asserted.</b> With no vitals in the capability snapshot this answers TRUE, so a session that cannot see the player's health plans exactly the falls it planned before. The refusal needs an observation to stand on; a guess in this direction is a bot that will not walk a route it could have walked, and a guess in the other is a bot that dies of a plan.</para>
    /// <para><b>What it is for, and why the height gate is not enough.</b> <see cref="PathfinderOptions.UnsafeFalls"/> raises <see cref="MaxFallHeight"/> to the world span, which is its whole purpose - course row C7's drop is 25 blocks and the default scan stops at 3 - but raising the gate also made a LETHAL landing cost the search exactly what a survivable one costs, so A* can buy the cheaper route with it. C7's numbers: the drop onto the basin's stone lip at (391,75,128) is 25 blocks for <c>ceil((25 - 3) * 1.0)</c> = 22 hearts against a 20-heart player, and it beat the drop into the water two cells over because the water route pays for two swims and an ascend afterwards.</para>
    /// <para><b>It does not narrow <c>UnsafeFalls</c>' contract.</b> That contract is that the planner stops REFUSING drops the safe limits refuse, and it still does: a six-block drop costs three hearts and a healthy body affords it, which is what course rows C4 and C5 need. What the contract never said is that the planner should be unable to tell a fall it survives from one it does not when the session is right there telling it.</para>
    /// <para>The reserve inside <see cref="FallDamageBudget"/> is <see cref="FallDamageModel.HealthReserve"/>, so this is the same policy the soft-landing arm already applies to hay, applied to every floor rather than to the two that soften.</para>
    /// </remarks>
    /// <param name="onto">The block the body comes to rest on.</param>
    /// <param name="fallHeight">The unprotected fall height, in blocks.</param>
    /// <returns>True when the landing is affordable, or when the vitals are unknown.</returns>
    public bool LandingIsAffordable(BlockState onto, int fallHeight)
    {
        if (!Capabilities.VitalsKnown)
            return true;

        FallDamageModel.LandingImpact impact = FallDamageModel.ImpactFor(onto);
        return FallDamageModel.Damage(fallHeight, impact.DistanceBonus, impact.Multiplier) <= FallDamageBudget;
    }

    /// <summary>The move type of the node currently being expanded (A* run-up modeling).</summary>
    public MoveType PreviousMoveType { get; internal set; }

    /// <summary>The run-up preparation state of the node currently being expanded.</summary>
    public EntryPreparationState CurrentEntryPreparation { get; internal set; }

    /// <summary>Where inside its cell the node currently being expanded stands, on X and on Z, quantised to <see cref="LateralQuantum"/>.</summary>
    /// <remarks><see cref="LateralQuantum.Centre"/> on every node of every search over terrain with no shape-offset block in it, which is why a plan over such terrain explores exactly the nodes it always did. Set by the A* driver beside <see cref="CurrentEntryPreparation"/>, and per-search scratch on a short-lived context in exactly the same way.</remarks>
    public LateralQuantum CurrentLateralX { get; internal set; }

    /// <inheritdoc cref="CurrentLateralX"/>
    public LateralQuantum CurrentLateralZ { get; internal set; }

    private readonly Dictionary<long, Geometry.Vec3d> _flowMemo = [];

    private readonly Dictionary<long, double> _elevationMemo = [];

    private bool? _mayContainBarrier;

    private bool? _mayContainShapeOffset;

    private Dictionary<long, DoorActivation?>? _activationMemo;

    /// <summary>Whether this search's region may hold a door, trapdoor or fence gate at all. Cached here as well as on the view because <see cref="MoveHelper.CanWalkThrough"/> reads it on both of its exits and that is the hottest predicate in the search. Answered lazily, so a context nobody plans with never pays for the palette scan.</summary>
    internal bool MayContainBarrier => _mayContainBarrier ??= World.MayContainBarrier;

    /// <summary>Whether this search's region may hold a block whose collision box moves with its position, and therefore whether a lateral squeeze lane can exist anywhere in it at all. Cached here for the same reason <see cref="MayContainBarrier"/> is: the walk arm asks it on every refused destination column.</summary>
    internal bool MayContainShapeOffset => _mayContainShapeOffset ??= World.MayContainShapeOffset;

    /// <summary>The switch that opens the door at <paramref name="door"/>, memoised for the life of the search.</summary>
    /// <remarks>
    /// <para><see cref="DoorActivation.TryResolve"/> scans the door's redstone neighbourhood and then a larger box for a cell to stand in, which is orders of magnitude too expensive to run per node evaluation - and <c>CanWalkThrough</c> asks about the same door thousands of times in one search. The snapshot is immutable, so the memo can never go stale, exactly as <see cref="SupportElevation"/>'s and the flow memo's cannot.</para>
    /// <para>The dictionary is created lazily and stays null on every search that never meets an iron door, which is nearly all of them.</para>
    /// </remarks>
    /// <param name="door">The door's block position.</param>
    /// <param name="activation">The resolved activation.</param>
    /// <returns>True when a usable switch exists.</returns>
    internal bool TryResolveActivation(Geometry.BlockPos door, out DoorActivation activation)
    {
        _activationMemo ??= [];
        long key = PathNode.Pack(door.X, door.Y, door.Z);
        if (!_activationMemo.TryGetValue(key, out DoorActivation? cached))
        {
            // The placeholder goes in BEFORE the resolve, and it is not an optimisation. The resolve looks for a cell to stand in, which asks CanStandAt, which asks CanWalkThrough, which asks
            // for this door's classification, which asks this method again - and without an entry to
            // find, that recursion has no bottom. Answering "no activator" to a question asked from inside this door's own resolution is also the right answer: a body cannot stand in the doorway to press the switch that opens it.
            _activationMemo[key] = null;
            cached = DoorActivation.TryResolve(this, door, out DoorActivation resolved) ? resolved : null;
            _activationMemo[key] = cached;
        }

        activation = cached ?? default;
        return cached is not null;
    }

    /// <summary>Creates a planning context.</summary>
    /// <param name="world">The immutable planning world view.</param>
    /// <param name="options">The caller's options.</param>
    /// <param name="capabilities">The player's capabilities at capture time, or null for <see cref="PathfinderCapabilities.None"/>.</param>
    /// <param name="fireHazardsCleared">Whether the caller has established that fire resistance covers this route. See <see cref="FireHazardsCleared"/>; the default leaves every hazard exactly where it was.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public CalculationContext(
        PlanningWorldView world,
        PathfinderOptions options,
        PathfinderCapabilities? capabilities = null,
        bool fireHazardsCleared = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        World = world;
        Options = options;
        Capabilities = capabilities ?? PathfinderCapabilities.None;
        FallDamageBudget = FallDamageModel.Budget(Capabilities);
        PowderSnowWalkable = Capabilities.PowderSnowWalkable;
        FireHazardsCleared = fireHazardsCleared;
        WalkCost = ActionCosts.WalkOneBlock;
        SprintCost = CanSprint ? ActionCosts.SprintOneBlock : ActionCosts.WalkOneBlock;
        SneakCost = ActionCosts.SneakOneBlock;
        SwimCost = ActionCosts.SwimOneBlock;
    }

    /// <summary>The block state at a block position.</summary>
    public BlockState GetBlock(int x, int y, int z) => World.GetBlock(new Geometry.BlockPos(x, y, z));

    /// <summary>The elevation a body standing with its feet in cell <c>(x, y, z)</c> actually rests at: the footprint support of the cell BELOW it, added to that cell's floor. Falls back to the logical feet cell where nothing in <c>(0, 1]</c> holds a body there (a swim node, a ladder rung, an unloaded cell).</summary>
    /// <remarks>
    /// <para>This is a derived function of the frozen world and the node's own coordinates, NOT extra node state, so nothing about the closed set moves: no node is split and no dimension is added. It is memoised for the life of the search because the step classification asks for it on both ends of every candidate move, and the snapshot is immutable so the memo can never go stale.</para>
    /// <para>Deliberately the same arithmetic <c>PathSegmentBuilder.ResolveElevation</c> uses for a segment endpoint, down to the <c>(0, 1]</c> bound, because the two have to agree: the planner classifies a move by the elevations it will then be executed against.</para>
    /// </remarks>
    internal double SupportElevation(int x, int y, int z)
    {
        long key = PathNode.Pack(x, y, z);
        if (_elevationMemo.TryGetValue(key, out double cached))
            return cached;

        double height = BlockSupport.FootprintSupportHeight(
            World.Shapes.GetCollisionShapes(GetBlock(x, y - 1, z)),
            0.5,
            0.5,
            MoveHelper.PlayerHalfWidth);
        double elevation = height is > 0.0 and <= 1.0 ? y - 1 + height : y;
        _elevationMemo[key] = elevation;
        return elevation;
    }

    /// <summary>The cost of swimming one block out of <c>(x, y, z)</c> along <c>(stepX, stepY, stepZ)</c>, priced against the current in that cell.</summary>
    /// <remarks>
    /// <para>The flow is sampled at the cell the swimmer STARTS in, which is the current it is inside while it makes the move, and it is memoised per cell for the life of the search: the snapshot is immutable, so the memo can never go stale. Without it a swim-heavy search would pay <c>GetFlow</c>'s five block reads on every one of hundreds of thousands of move evaluations instead of once per water cell it visits.</para>
    /// <para>Swim and submerged bottom-walk costs are priced separately. The validator's wade rate is a still-water measurement, so current-aware pricing is required to avoid understating duration.</para>
    /// <para>The two multipliers stay separate on purpose. A swimmer and a wader are different bodies: the swimmer's ratio is 0.7143 and its crossing costs 1.4289 because it crabs, the wader's is 0.7324 and its crossing costs exactly 1.0 because it does not. See <see cref="ActionCosts.WadeCurrentCostMultiplier(Umpk.Geometry.Vec3d, Umpk.Geometry.Vec3d, int)"/>.</para>
    /// </remarks>
    public double SwimCostThrough(int x, int y, int z, int stepX, int stepY, int stepZ)
    {
        double length = Math.Sqrt((double)((stepX * stepX) + (stepY * stepY) + (stepZ * stepZ)));
        if (length <= 0.0)
            return SwimCost;

        Geometry.Vec3d flow = WaterFlowAt(x, y, z);
        return SwimCost * ActionCosts.CurrentCostMultiplier(flow, new Geometry.Vec3d(stepX, stepY, stepZ));
    }

    /// <summary>The cost of WALKING one block out of <c>(x, y, z)</c> along <c>(stepX, stepY, stepZ)</c>, priced against the current in that cell. <see cref="SprintCost"/> exactly whenever the cell is dry or the water is still.</summary>
    /// <remarks>
    /// <para>The twin of <see cref="SwimCostThrough"/> for the family that does most of the bot's water travel: a body whose FEET are in water and whose head is not is emitted as an ordinary <c>Traverse</c>, and until this existed the planner priced an upstream wade, a downstream wade and a dry walk over the same lane at the same 3.5638 while the executor paid 10.50, 5.08 and 4.25 ticks a block for them.</para>
    /// <para><b>The dry short-circuit is not an optimisation, it is the byte-identity argument.</b> The water test happens BEFORE <see cref="WaterFlowAt"/> is called, so a dry search takes exactly zero extra flow samples and returns exactly <see cref="SprintCost"/> - the same <c>double</c>, not a number that rounds to it. That is what makes every dry row in the course untouchable by this change, and it is the same argument the vertical current price used.</para>
    /// <para>The flow is sampled at the cell the body STARTS in, matching <see cref="SwimCostThrough"/>, and shares its per-cell memo.</para>
    /// <para>Priced against <see cref="PathfinderCapabilities.DepthStriderLevel"/> off the capture, which collapses the whole curve toward 1.0: level I alone takes a dead-upstream wade from 3.7371x to 1.3571x. The level is frozen at capture and the budget is what absorbs a plan outliving its boots - see <c>SegmentBudgetPolicy.SubmergedWalkSlack</c>.</para>
    /// </remarks>
    public double WadeCostThrough(int x, int y, int z, int stepX, int stepY, int stepZ)
    {
        if ((stepX == 0 && stepY == 0 && stepZ == 0) || !MoveHelper.IsWater(GetBlock(x, y, z)))
            return SprintCost;

        Geometry.Vec3d flow = WaterFlowAt(x, y, z);
        return SprintCost * ActionCosts.WadeCurrentCostMultiplier(
            flow, new Geometry.Vec3d(stepX, stepY, stepZ), Capabilities.DepthStriderLevel);
    }

    /// <summary>The memoised vanilla flow vector for one water cell (zero for still water and dry cells).</summary>
    internal Geometry.Vec3d WaterFlowAt(int x, int y, int z)
    {
        long key = PathNode.Pack(x, y, z);
        if (_flowMemo.TryGetValue(key, out Geometry.Vec3d cached))
            return cached;

        Geometry.Vec3d flow = Umpk.Physics.PlayerPhysics.GetWaterFlow(World, new Geometry.BlockPos(x, y, z));
        _flowMemo[key] = flow;
        FlowSamplesTaken++;
        return flow;
    }

    /// <summary>How many cells the flow memo has actually sampled, for the memo's own test.</summary>
    internal int FlowSamplesTaken { get; private set; }

    /// <summary>How many jump-family arms this search has refused for a slow-jump takeoff floor. Surfaced on <see cref="PathDiagnostics.SlowJumpFloorTakeoffsRefused"/>, which documents what it means.</summary>
    internal int SlowJumpFloorTakeoffsRefused { get; private set; }

    /// <summary>Records one such refusal. Called only from the takeoff gate in <c>JumpFeasibility</c>.</summary>
    internal void NoteSlowJumpFloorTakeoff() => SlowJumpFloorTakeoffsRefused++;

    /// <summary>How many jump-family arms this search has refused because the takeoff cell is one the body hangs in. Surfaced on <see cref="PathDiagnostics.HangingTakeoffsRefused"/>, which documents it.</summary>
    internal int HangingTakeoffsRefused { get; private set; }

    /// <summary>Records one such refusal. Called only from the hanging gates in <c>JumpFeasibility</c>.</summary>
    internal void NoteHangingTakeoff() => HangingTakeoffsRefused++;

    /// <summary>How many jump-family arms this search has refused because the body is afloat at the takeoff. Surfaced on <see cref="PathDiagnostics.FloatingTakeoffsRefused"/>, which documents it.</summary>
    internal int FloatingTakeoffsRefused { get; private set; }

    /// <summary>Records one such refusal. Called only from the afloat gates in <c>JumpFeasibility</c>.</summary>
    internal void NoteFloatingTakeoff() => FloatingTakeoffsRefused++;

    /// <summary>See <see cref="MoveHelper.CanWalkThrough"/>.</summary>
    public bool CanWalkThrough(int x, int y, int z) => MoveHelper.CanWalkThrough(this, x, y, z);

    /// <summary>See <see cref="MoveHelper.CanWalkOn"/>.</summary>
    public bool CanWalkOn(int x, int y, int z) => MoveHelper.CanWalkOn(this, x, y, z);

    /// <summary>See <see cref="MoveHelper.CanLandOn"/>.</summary>
    public bool CanLandOn(int x, int y, int z) => MoveHelper.CanLandOn(this, x, y, z);

    /// <summary>See <see cref="MoveHelper.IsFullyPassable"/>.</summary>
    public bool IsFullyPassable(int x, int y, int z) => MoveHelper.IsFullyPassable(this, x, y, z);

    /// <summary>See <see cref="MoveHelper.CanTraverseWater"/>.</summary>
    public bool CanTraverseWater(int x, int y, int z) => MoveHelper.CanTraverseWater(this, x, y, z);

    /// <summary>True when the captured region covers this X/Z column at any Y.</summary>
    public bool IsChunkLoaded(int x, int z)
    {
        var region = World.Region;
        return x >= region.Min.X && x < region.Min.X + region.SizeX
            && z >= region.Min.Z && z < region.Min.Z + region.SizeZ;
    }
}
