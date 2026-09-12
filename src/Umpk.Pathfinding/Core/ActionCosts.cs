namespace Umpk.Pathfinding.Core;

/// <summary>All pathfinding movement costs in ticks, derived from walking and sprinting speeds. The fall-cost table is precomputed with the same integrated gravity/drag model the physics engine uses per tick, so a planner cost never diverges from the simulated fall duration.</summary>
public static class ActionCosts
{
    /// <summary>Ticks to walk one block (4.317 m/s).</summary>
    public const double WalkOneBlock = 20.0 / 4.317;

    /// <summary>Ticks to sprint one block (5.612 m/s).</summary>
    public const double SprintOneBlock = 20.0 / 5.612;

    /// <summary>Ticks to sneak one block (1.3 m/s).</summary>
    /// <remarks>
    /// <b>This number has no consumers and is deliberately left alone.</b> It flows into <c>CalculationContext.SneakCost</c> and nothing reads that: no move, no template and no budget prices a sneak, and the only two places UMPK actually sneak-walks are the climbable hold and the sub-block approach, neither of which is priced by A* at all.
    /// <para>It is therefore NOT made a function of <c>PhysicsConditions.SneakingSpeedFactor</c>, even though swift sneak genuinely changes what a sneaking block costs (the factor replaces vanilla's 0.3 with <c>clamp(0.3 + 0.15*L, 0, 1)</c>, so level III is two and a half times faster). Wiring a capability into a constant nothing reads would be an unused knob dressed up as a feature. The If a sneaking move is added, this must become a function of the resolved factor.</para>
    /// </remarks>
    public const double SneakOneBlock = 20.0 / 1.3;

    /// <summary>Ticks to swim one block on the surface (~2.2 m/s, calibrated against vanilla swim speed).</summary>
    public const double SwimOneBlock = 20.0 / 2.2;

    /// <summary>Ticks to climb one block up a ladder (2.35 m/s).</summary>
    public const double LadderUpOne = 20.0 / 2.35;

    /// <summary>Ticks to climb one block down a ladder (3.0 m/s).</summary>
    public const double LadderDownOne = 20.0 / 3.0;

    /// <summary>The cost of stepping off a block edge before a fall.</summary>
    public const double WalkOffBlock = WalkOneBlock * 0.8;

    /// <summary>The sprint speed multiplier relative to walking.</summary>
    public const double SprintMultiplier = SprintOneBlock / WalkOneBlock;

    /// <summary>The diagonal distance multiplier (sqrt 2).</summary>
    public const double DiagonalMultiplier = 1.4142135623730951;

    /// <summary>Ticks to sink one block through water under no input at all: 40, the passive-sink rate on <see cref="Umpk.Physics.WaterTravelEra.SprintAware"/>.</summary>
    /// <remarks>
    /// <para>This is NOT a member of the fall table and must never be confused with it. The fall table integrates gravity against air drag and reaches one block in 5 ticks; water replaces that with the fluid-falling adjustment plus <c>WaterYDamping 0.8</c>, whose steady descent measures <c>-0.025</c> blocks a tick on protocol 393+ and <c>-0.1</c> on 47-340 (the legacy arm is <c>motionY = motionY * 0.8 - LegacyWaterSink</c>, so the fixed point is exactly <c>-0.02 / (1 - 0.8)</c>). <c>FallTemplate</c> presses <c>MovementInput.None</c> on every tick, so a planned fall through water really is the passive sink and really does take eight times what the fall table charges.</para>
    /// <para>The modern rate is used on both eras deliberately: 40 is four times the legacy 10, and over-charging a legacy sink refuses a route the player could have survived, while under-charging a modern one drowns it.</para>
    /// </remarks>
    public const double WaterSinkOneBlock = 40.0;

    /// <summary>The cost representing an infeasible move.</summary>
    public const double CostInf = 1_000_000;

    /// <summary>A water current's push divided by a swimmer's own thrust: <c>0.014 / 0.0196</c>.</summary>
    /// <remarks>
    /// <para><c>PhysicsConstants.WaterPushScale</c> is 0.014 whatever the geometry, because vanilla AVERAGES the per-cell unit flows over the cells a PLAYER overlaps rather than normalizing their sum (only other entities normalize). Channel width is therefore irrelevant: a one-block flume and an eight-block river deliver the same impulse. The thrust is <c>WaterBaseSpeed 0.02</c> through <c>MapInput</c>'s 0.98, i.e. 0.0196, and it is the same number with and without <c>Sprint</c> - sprint flips the slow-down from 0.8 to 0.9, which changes the terminal speed and cancels here, because both accelerations pass through the same <c>1 / (1 - d)</c>.</para>
    /// <para>Being under 1.0 is the whole result: no current in the game can stall a forward-swimming bot, so a flow-aware planner must price the current and must NOT gate on it. A threshold gate would refuse routes the bot demonstrably completes.</para>
    /// </remarks>
    public const double CurrentToThrustRatio = 0.014 / 0.0196;

    /// <summary>The same ratio for the VERTICAL axis: a current's downward push divided by a swimmer's own vertical thrust, 0.113.</summary>
    /// <remarks>
    /// <para><see cref="CurrentToThrustRatio"/> is a HORIZONTAL number - the 0.014 push over the 0.0196 horizontal swim thrust - and applying it to a vertical flow charged a climb up a falling column 3.5x where the executor takes 1.125x. A swimmer's vertical authority is far larger than its horizontal one, which is why the same push barely moves the vertical rate.</para>
    /// <para>Measured, driving eight one-block vertical swim segments through the real <c>PathExecutor</c> over the real <c>PlayerPhysics</c> on protocol 772, in a 1x1 shaft, still water against a falling column whose <c>GetWaterFlow</c> reads exactly <c>(0, -1, 0)</c>:</para>
    /// <code>
    /// still   up  : 24 ticks / 8 blocks = 3.000 t/blk still   down: 29 ticks / 8 blocks = 3.625 t/blk falling up  : 27 ticks / 8 blocks = 3.375 t/blk   -> 1.125x still water falling down: 26 ticks / 8 blocks = 3.250 t/blk   -> 0.8966x still water
    /// </code>
    /// <para>Solving <c>R = 1 -/+ k</c> on each direction gives <c>k = 0.111</c> from the climb and <c>0.115</c> from the ride, so 0.113 is the pair's centre and reproduces both to under 0.3 percent (1.1274 against 1.125, 0.8985 against 0.8966).</para>
    /// <para>This is a MEASURED ratio, in the same sense every slack in <c>SegmentBudgetPolicy</c> is. The first-principles estimate - the same 0.014 push over the 0.0667 thrust a 0.3333 blocks-a-tick terminal ascent implies under <c>WaterYDamping</c> 0.8 - gives 0.21, which over-predicts the engine by 1.86x because a templated ascent is not a steady terminal state. The engine is the oracle, so the engine's number is used while the analytical estimate remains documented.</para>
    /// </remarks>
    public const double VerticalCurrentToThrustRatio = 0.113;

    /// <summary>The factor a current multiplies a swim's cost by, for a real flow vector and a real move direction, scaling each axis of the flow by the thrust the swimmer has along THAT axis.</summary>
    /// <param name="flow">The cell's vanilla flow vector (a unit vector, or zero for still water).</param>
    /// <param name="moveDirection">The move's direction. Need not be normalised; a zero vector yields 1.0.</param>
    /// <remarks>Identical to the horizontal <see cref="CurrentCostMultiplier(double, double)"/> whenever the flow and the move are both horizontal, which is every channel, sheet and crossing case. It differs only where the flow has a vertical component, and there it differs by a factor of three.</remarks>
    public static double CurrentCostMultiplier(Umpk.Geometry.Vec3d flow, Umpk.Geometry.Vec3d moveDirection)
    {
        double length = moveDirection.Length();
        if (length <= 0.0)
            return 1.0;

        Umpk.Geometry.Vec3d unit = moveDirection.Scale(1.0 / length);
        var scaled = new Umpk.Geometry.Vec3d(
            CurrentToThrustRatio * flow.X,
            VerticalCurrentToThrustRatio * flow.Y,
            CurrentToThrustRatio * flow.Z);

        double along = (scaled.X * unit.X) + (scaled.Y * unit.Y) + (scaled.Z * unit.Z);
        double cross = Math.Sqrt(Math.Max(0.0, scaled.LengthSqr() - (along * along)));
        return FromScaledComponents(along, cross);
    }

    /// <summary>The factor a current multiplies a swim's cost by, from the current's components along and across the move.</summary>
    /// <param name="alongFlow">The unit flow vector's component along the move direction: +1 dead downstream, -1 dead upstream, 0 for a pure crossing or still water.</param>
    /// <param name="crossFlow">The magnitude of the unit flow vector's component ACROSS the move direction. Zero for still water, which is what keeps this multiplier exactly 1.0 everywhere there is no current.</param>
    /// <remarks>
    /// <para>A swimmer crossing a current holds a crab angle so thrust plus push runs along the track, which spends <c>k sin(phi)</c> of its thrust on cancelling the cross-track push and leaves <c>sqrt(1 - k^2 sin^2 phi)</c> for the track itself. Adding the current's own along-track component gives the achievable fraction of free-water speed:</para>
    /// <code>R(phi) = sqrt(1 - k^2 sin^2 phi) + k cos phi</code>
    /// <para>With <c>k = 0.714</c> that is 1.714 dead downstream (0.583x the flat cost), 0.700 at a dead 90-degree crossing (1.429x) and 0.286 dead upstream (3.500x). The crossing term is the reason this is not the naive <c>a + flow . move</c> model: that one prices a 90-degree crossing as FREE, and a 90-degree crossing is where the bot actually gets washed off its line.</para>
    /// </remarks>
    public static double CurrentCostMultiplier(double alongFlow, double crossFlow)
        => FromScaledComponents(CurrentToThrustRatio * alongFlow, CurrentToThrustRatio * crossFlow);

    /// <summary>The model itself, over components that have ALREADY been scaled by their axis's ratio.</summary>
    private static double FromScaledComponents(double along, double cross)
    {
        double fraction = Math.Sqrt(Math.Max(0.0, 1.0 - (cross * cross))) + along;

        // k < 1 bounds the fraction to [1 - k, 1 + k] for any unit flow, so this clamp never fires on a real GetFlow result; it is here so a hypothetical stacked or non-unit flow cannot produce a free or an infinite swim.
        return Math.Clamp(1.0 / Math.Max(fraction, MinUsefulSpeedFraction), MinCurrentCostMultiplier, MaxCurrentCostMultiplier);
    }

    /// <summary>The smallest fraction of free-water speed a swim is allowed to be priced at.</summary>
    private const double MinUsefulSpeedFraction = 1.0 / MaxCurrentCostMultiplier;

    /// <summary>The cheapest a current can make a swim: dead downstream, <c>1 / (1 + CurrentToThrustRatio)</c>.</summary>
    public const double MinCurrentCostMultiplier = 0.5833333333333334;

    /// <summary>The dearest a current can make a swim: dead upstream, <c>1 / (1 - CurrentToThrustRatio)</c>. This is a BOUND, not a refusal - the ratio is under 1, so a forward swimmer always makes headway and the planner must keep offering the move.</summary>
    public const double MaxCurrentCostMultiplier = 3.5;

    /// <summary>A water current's push divided by a WADER's own thrust, 0.7324. The wade's own number, not <see cref="CurrentToThrustRatio"/>.</summary>
    /// <remarks>
    /// <para>Measured the way <see cref="VerticalCurrentToThrustRatio"/> was measured: an open flooded hall on protocol 774, a sheet running +z, sprint-forward for 40 ticks, the displacement projected onto the intended track, against a still-water control in the same geometry.</para>
    /// <code>
    /// heading  flow b/t   still b/t   R = flow/still   cost mult = 1/R      the SWIM model charges
    ///    0     0.2335      0.1526        1.5307            0.6533                 0.5833
    ///   45     0.2134      0.1526        1.3991            0.7147                    -
    ///   90     0.1526      0.1526        1.0000            1.0000                 1.4289
    ///  135     0.0736      0.1526        0.4821            2.0741                    -
    ///  180     0.0408      0.1526        0.2676            3.7371                 3.5000
    /// </code>
    /// <para>Solving <c>R = 1 - k</c> on the dead-upstream row gives <c>k = 0.7324</c>. Reusing the swimmer's 0.7143 would have been a fabricated number: a body fighting upstream falls out of sprint, so the 0.9/0.8 slow-down split does not cancel the way it does for a swimmer, and the downstream fit gives a different <c>k</c> again (0.5307). That asymmetry is recorded rather than resolved, because <see cref="WadeCurrentCostMultiplier(Umpk.Geometry.Vec3d, Umpk.Geometry.Vec3d, int)"/> clamps the downstream side away.</para>
    /// </remarks>
    public const double WadeCurrentToThrustRatio = 0.7324;

    /// <summary>The dearest a current can make a wade: dead upstream, <c>1 / (1 - WadeCurrentToThrustRatio)</c>. A BOUND, not a refusal - a dead-upstream wade still measures 0.0408 blocks a tick, so the planner must keep offering the move.</summary>
    public const double WadeMaxCurrentCostMultiplier = 3.7371;

    /// <summary>The factor a current multiplies a WALK's cost by, for a real flow vector and a real move direction. Charged for a current that OPPOSES the move and never credited for one that helps it.</summary>
    /// <param name="flow">The cell's vanilla flow vector (a unit vector, or zero for still water).</param>
    /// <param name="moveDirection">The move's direction. Need not be normalised; a zero vector, or still water, yields exactly 1.0.</param>
    /// <returns>A multiplier in <c>[1.0, <see cref="WadeMaxCurrentCostMultiplier"/>]</c>.</returns>
    /// <remarks>
    /// <para><b>The model, written out, because the obvious wrong one lives in this same file.</b> <c>along</c> is the dot product of the two HORIZONTAL unit vectors: +1 dead downstream, -1 dead upstream, 0 for a crossing or for still water. The cost multiplier is the reciprocal of the achievable fraction of free-water speed:</para>
    /// <code>
    /// along = dot(flow.Horizontal().Normalized(), moveDirection.Horizontal().Normalized()) return Math.Clamp(1.0 / (1.0 + k * along), 1.0, WadeMaxCurrentCostMultiplier)
    /// </code>
    /// <para>which reproduces the measured table above on the penalty side at every point taken: <c>1.0000</c> at 90 degrees, <c>2.0742</c> against a measured 2.0741 at 135, and <c>3.7369</c> against a measured 3.7371 dead upstream.</para>
    /// <para><b>HORIZONTAL COMPONENTS ONLY, on both sides, and that is not a simplification.</b> <see cref="WadeCurrentToThrustRatio"/> was measured on a horizontal sheet over horizontal headings, and it is a WADER's number: a body standing on a floor, whose vertical authority is the step and the jump, not a swim thrust. Feeding a falling column's <c>(0, -1, 0)</c> through the full three-dimensional dot product charges a walked step UP out of a plunge basin <c>1 / (1 - 0.7324 cos 45) = 2.0742</c> for a current with NO horizontal component at all, which is precisely the mistake <see cref="VerticalCurrentToThrustRatio"/> exists to have fixed for the swim family: a horizontal ratio applied to a vertical flow charged a climb 3.5x where the executor takes 1.125x. The plunge-basin exit is the one cell on the course where a waterfall genuinely beats the bot, so over-charging it is not theoretical.</para>
    /// <para>A wader therefore reads a purely vertical current as exactly 1.0. That is not a claim that a falling column costs a wader nothing - it is a claim that this multiplier is the wrong instrument for it, and that the vertical case belongs to the swim family, which already prices it with its own measured ratio. A depth-strider term likewise must never reach <see cref="VerticalCurrentToThrustRatio"/>: a booted bot that under-prices every metre of every waterfall by 11.4 percent drowns on a climb a bare bot refuses.</para>
    /// <para><b>Do NOT reuse <c>FromScaledComponents</c>.</b> The swim model's <c>sqrt(1 - cross^2)</c> term charges a 90-degree crossing <c>1/sqrt(1-k^2)</c>, which is 1.4289 for the swimmer. That term exists because a SWIMMER holds a crab angle; the wade executor was measured holding yaw 0 and paying nothing in time for a crossing, so charging it the crab's cost would invent a cost the engine does not charge. A crossing costs lateral POSITION, not time, and no price can fix that.</para>
    /// <para><b>The clamp's lower bound must be the literal 1.0, and this is load-bearing to the last bit.</b> <c>SprintOneBlock</c> is the heuristic's own per-block rate (<c>GoalBlock.DistanceHeuristic</c>), so a wade edge priced under 1.0x is an INADMISSIBLE edge and A* is free to close nodes it should have expanded. The Diagonal family has EXACTLY 0.000000 of margin today - <c>SprintOneBlock * DiagonalMultiplier = 5.039963</c> against <c>h(1,0,1) = 5.039963</c> - so a multiplier below 1.0 by a single ULP breaks admissibility on the FIRST diagonal it touches. It must therefore be <c>Math.Clamp(value, 1.0, max)</c> with the literal, never a <c>Math.Max</c> against a computed bound, never a subtraction, and never an epsilon-relaxed comparison. A downstream wade is priced at its still-water rate and merely fails to be a bargain, which mis-ranks nothing the search can act on: the penalty side is what changes a route choice. <c>HeuristicAdmissibilityTests</c> verifies this bound.</para>
    /// <para>Exactly 1.0 for still water and for a dry cell, because <c>GetWaterFlow</c> returns zero there. <b>Dry terrain is byte-identical by construction</b>, which is what makes the whole non-water half of the course arithmetically untouchable by this pricing.</para>
    /// </remarks>
    public static double WadeCurrentCostMultiplier(Umpk.Geometry.Vec3d flow, Umpk.Geometry.Vec3d moveDirection)
        => WadeCurrentCostMultiplier(flow, moveDirection, depthStriderLevel: 0);

    /// <summary>The same multiplier for a body wearing depth-strider boots, which collapse the whole curve toward 1.0.</summary>
    /// <param name="flow">The cell's vanilla flow vector (a unit vector, or zero for still water).</param>
    /// <param name="moveDirection">The move's direction; need not be normalised.</param>
    /// <param name="depthStriderLevel">The enchantment level from <see cref="PathfinderCapabilities.DepthStriderLevel"/>. Values outside 0..3 are clamped, matching vanilla's own re-cap at 3.</param>
    /// <remarks>
    /// <para>The ratio is a MEASURED TABLE indexed by level, not a formula, for the same stated reason <see cref="VerticalCurrentToThrustRatio"/> is a measured number: the engine is the oracle. Fitting <c>1/(1-k) = R_up</c> to the measured ratios at each level:</para>
    /// <code>
    /// level   WaterMovementEfficiency   measured upstream multiplier   implied k
    ///   0            0.0000                       3.7371                0.7324
    ///   I            0.3333                       1.3571                0.2631
    ///  II            0.6667                       1.1324                0.1169
    /// III            1.0000                       1.0870                0.0800
    /// </code>
    /// <para>The first-principles interpolation <c>k(e) = k0 (1 - e)</c> predicts 0.488 at level I against the measured 0.263 and would over-charge a booted bot by 1.85x. The analytical estimate remains documented, as does the 1.86x miss for <see cref="VerticalCurrentToThrustRatio"/>.</para>
    /// <para><b>Recorded, not fixed:</b> with depth strider the same model UNDER-prices an OBLIQUE upstream move by up to 10.8 percent - at level I the model gives 1.2286 at 135 degrees against a measured 1.3616. It is not an admissibility issue, because the result is still at or above 1.0, and it is absorbed many times over by <c>SegmentBudgetPolicy.SubmergedWalkSlack</c>. Fitting a second per-level shape parameter to close it would be curve-fitting past what the measurement supports.</para>
    /// <para><b>The term is HORIZONTAL-ONLY and must never reach <see cref="VerticalCurrentToThrustRatio"/>.</b> Measured on the real executor, a waterfall climb takes 44 ticks with depth strider and 44 ticks without: identical. Water movement efficiency affects horizontal speed and damping only, while vertical authority is <c>WaterYDamping</c> and the ascent impulse. A "tidy" implementation that scaled the vertical ratio too would take a level III capture's falling-column climb from 1.1274 to 1.0125, an 11.4 percent under-price on every metre of every waterfall, with two consequences. The first is a wrong route choice. <b>The second is that a BOOTED bot drowns on a climb a BARE bot refuses</b>, because <c>BreathValidator</c> prices a submerged vertical leg in real ticks and refuses on the deficit: a cheaper vertical price shortens a long submerged climb below the lung bound the validator would otherwise have refused, and the body executing it is 11.4 percent slower than the plan believed, in a shaft with no air pocket. Gear making the bot LESS safe is the inversion this paragraph exists to prevent, and <c>FlowChoicePricingTests</c> is the tripwire.</para>
    /// </remarks>
    /// <returns>A multiplier in <c>[1.0, <see cref="WadeMaxCurrentCostMultiplier"/>]</c>.</returns>
    public static double WadeCurrentCostMultiplier(
        Umpk.Geometry.Vec3d flow, Umpk.Geometry.Vec3d moveDirection, int depthStriderLevel)
    {
        // HORIZONTAL components only, on both sides. See the remarks: the ratio is a horizontal measurement and a wader has no swim thrust to spend against a vertical current.
        double flowLength = Math.Sqrt((flow.X * flow.X) + (flow.Z * flow.Z));
        double moveLength =
            Math.Sqrt((moveDirection.X * moveDirection.X) + (moveDirection.Z * moveDirection.Z));
        if (flowLength <= 0.0 || moveLength <= 0.0)
            return 1.0;

        double along =
            ((flow.X * moveDirection.X) + (flow.Z * moveDirection.Z)) / (flowLength * moveLength);

        double ratio = WadeCurrentToThrustRatioFor(depthStriderLevel);
        return Math.Clamp(1.0 / (1.0 + (ratio * along)), 1.0, WadeMaxCurrentCostMultiplier);
    }

    /// <summary>The measured current-to-thrust ratio for a depth-strider level, clamped to 0..3 the way vanilla re-caps its own enchantment contribution. See <see cref="WadeCurrentCostMultiplier(Umpk.Geometry.Vec3d, Umpk.Geometry.Vec3d, int)"/> for the table and for why it is a table.</summary>
    private static double WadeCurrentToThrustRatioFor(int depthStriderLevel) => depthStriderLevel switch
    {
        <= 0 => WadeCurrentToThrustRatio,
        1 => 0.2631,
        2 => 0.1169,
        _ => 0.0800,
    };

    /// <summary>The default additive jump-takeoff penalty in ticks.</summary>
    public const double JumpPenalty = 2.0;

    /// <summary>The only floor speed factor below 1.0 in supported versions: <b>0.4</b>, used by <c>minecraft:soul_sand</c> and <c>minecraft:honey_block</c>.</summary>
    /// <remarks>Across all 49 supported protocol registries, these are the only blocks whose speed factor is not 1.0. This exhaustive result allows <see cref="SpeedFactorCostMultiplier(double)"/> to be a MEASURED number at 0.4 and a conservative estimate everywhere else.</remarks>
    public const double MeasuredSlowFloorSpeedFactor = 0.4;

    /// <summary>What walking over a <see cref="MeasuredSlowFloorSpeedFactor"/> floor really costs, relative to open ground: <b>1.691</b>, not the 2.5 that <c>1 / 0.4</c> implies.</summary>
    /// <remarks>
    /// <para>Measured over 200 ticks of a <c>PlayerPhysics</c> body sprinting a straight lane on protocol 774:</para>
    /// <code>
    /// stone / slab / carpet / dirt path / snow / lily pad / stairs : 3.62765 ticks a block soul sand, honey block                                       : 6.13501 ticks a block  -> 1.691x ice                                                          : 3.70034 ticks a block  -> 1.020x
    /// </code>
    /// <para>The first-principles <c>1 / speedFactor</c> over-charges that by 48 percent, because the factor multiplies velocity inside the friction loop rather than scaling a terminal speed: the next tick's acceleration is applied to the already-scaled velocity, so the steady state settles well above <c>factor x</c> free speed. Same relationship, and same resolution, as <see cref="VerticalCurrentToThrustRatio"/>: the engine is the oracle, while the analytical estimate remains documented.</para>
    /// </remarks>
    public const double MeasuredSlowFloorCostMultiplier = 1.691;

    /// <summary>The walk-cost multiplier a floor's <see cref="Umpk.Game.Blocks.BlockState.SpeedFactor"/> imposes.</summary>
    /// <param name="speedFactor">The EFFECTIVE floor speed factor, already resolved over both cells.</param>
    /// <returns>1.0 for open ground, <see cref="MeasuredSlowFloorCostMultiplier"/> at 0.4.</returns>
    /// <remarks>Measured where the game has a measurement and conservative everywhere else. The dataset carries exactly one sub-unit factor (see <see cref="MeasuredSlowFloorSpeedFactor"/>), so the fallback arm is unreachable on real data today; it is <c>1 / factor</c> because that IS the upper bound the naive model gives, and over-charging a hypothetical future floor refuses a route rather than stranding a bot on one.</remarks>
    public static double SpeedFactorCostMultiplier(double speedFactor)
    {
        if (speedFactor is <= 0.0 or >= 1.0)
            return 1.0;

        // The dataset value arrives as a float widened to double, so 0.4 is 0.4000000059604645 here.
        return Math.Abs(speedFactor - MeasuredSlowFloorSpeedFactor) < SpeedFactorEpsilon
            ? MeasuredSlowFloorCostMultiplier
            : 1.0 / speedFactor;
    }

    /// <summary>The walk-cost multiplier a floor imposes when the body may be BYPASSING that floor's slowdown.</summary>
    /// <param name="speedFactor">The EFFECTIVE floor speed factor, already resolved over both cells.</param>
    /// <param name="bypassed">Whether the body ignores this floor's speed factor entirely. True only for a soul-speed wearer standing on a block in <c>BlockTags.SOUL_SPEED_BLOCKS</c>; see <c>MoveHelper.FloorSpeedPenalty</c> for the predicate and <see cref="Umpk.Pathfinding.Core.PathfinderCapabilities.SoulSpeedLevel"/> for the capability.</param>
    /// <returns>1.0 when bypassed, otherwise <see cref="SpeedFactorCostMultiplier(double)"/>.</returns>
    /// <remarks>
    /// <para>The one-argument overload is the context-free calculation: what a floor costs a body that does not bypass it. The overload with a current vector adds flow-aware pricing without weakening that simpler contract.</para>
    /// <para>An efficiency value of 1.0 resolves the floor factor to exactly 1.0, so the floor costs what open ground costs. A 20-block soul-sand lane measured 6.014 s bare and 3.009 s with soul-speed-III boots, against a stone lane's 4.013 s. The booted lane is FASTER than stone, because the server's movement-speed boost rides on top of the bypass - so 1.0 is if anything still conservative, and deliberately so: this prices away the slowdown and does not price in the boost.</para>
    /// </remarks>
    public static double SpeedFactorCostMultiplier(double speedFactor, bool bypassed) =>
        bypassed ? 1.0 : SpeedFactorCostMultiplier(speedFactor);

    /// <summary>The tolerance a float-widened dataset scalar is matched at.</summary>
    private const double SpeedFactorEpsilon = 1.0E-6;

    /// <summary>What crossing a cobweb really costs, relative to the same lane without one: <b>8.81057</b>.</summary>
    /// <remarks>
    /// <para>Measured on a <c>PlayerPhysics</c> body over a 400-tick straight lane on protocol 774:</para>
    /// <code>
    /// open lane, walk   : 0.21585907 blocks/tick ->  4.63265225 ticks a block open lane, sprint : 0.28061681 blocks/tick ->  3.56357843 ticks a block cobweb, walk      : 0.02450000 blocks/tick -> 40.81632513 ticks a block cobweb, sprint    : 0.03185000 blocks/tick -> 31.39717120 ticks a block
    ///
    /// walk   ratio: 0.21585907 / 0.02450000 = 8.8105739179753861 sprint ratio: 0.28061681 / 0.03185000 = 8.8105739179754305
    /// </code>
    /// <para>The two ratios agree to twelve decimal places, and that is not a coincidence worth hiding: Cobweb contact zeroes velocity and multiplies the tick's movement by 0.25, so the body restarts from rest every tick and its speed no longer depends on what it would have reached unimpeded. A web costs the same MULTIPLE of whatever the lane was worth, which is exactly the shape a cost multiplier wants.</para>
    /// <para>The first-principles <c>1 / 0.25</c> would charge 4x, under HALF the truth, because it models the multiplier as scaling a terminal speed rather than as resetting the acceleration ramp every tick. Same relationship as <see cref="MeasuredSlowFloorCostMultiplier"/>, in the opposite direction: there the naive model over-charges, here it under-charges, and under-charging is the dangerous one - it is how the pathfinding course's F8 row planned a sprint through a wall of web the client then crawled through, desynchronising from the server.</para>
    /// </remarks>
    public const double MeasuredWebCostMultiplier = 8.81057;

    /// <summary>Ticks to rise one block inside an UPWARD bubble column: <b>2.28571</b>, four times cheaper than <see cref="SwimOneBlock"/>'s 9.0909 and still dearer than a sprint on land.</summary>
    /// <remarks>
    /// <para>Measured on a real <c>PlayerPhysics</c> body in a 1x1 shaft on protocol 774, from rest at the column's base to fourteen blocks up:</para>
    /// <code>
    /// column, no input at all        : 25 ticks / 14 blocks = 1.78571 t/blk column, Jump only              : 22 ticks / 14 blocks = 1.57143 t/blk column, Forward+Sprint+Jump    : 32 ticks / 14 blocks = 2.28571 t/blk   &lt;- the executor's input column, Forward+Sprint         : 45 ticks / 14 blocks = 3.21429 t/blk plain water, Forward+Sprint+Jump: 96 ticks / 14 blocks = 6.85714 t/blk
    /// </code>
    /// <para><b>2.28571 rather than the faster 1.57143</b>, because the charge has to describe what the EXECUTOR does, not what a passenger could do. <c>SwimTemplate</c> holds <c>Forward + Sprint</c> and adds <c>Jump</c> on a rising segment, and that is the slowest of the three lifting inputs: the swim thrust fights the column rather than helping it, because the column's impulse is a per-tick <c>min(0.7, vy + 0.06)</c> CLAMP and a body already at the clamp gains nothing from pressing up. Charging the 1.57143 a hands-off rider gets would under-price the segment and shrink its tick budget below what the template actually spends.</para>
    /// <para>The heuristic is purely horizontal (<c>GoalBlock.DistanceHeuristic</c> takes dx and dz only), so a vertical edge cheaper than <see cref="SprintOneBlock"/> cannot make it inadmissible.</para>
    /// <para>A DOWNWARD column is not priced here at all: its downdraft is unmodelled, and the engine measurement says so out loud - a magma column rides at 6.85714 t/blk, which is plain water's rate to four decimal places.</para>
    /// </remarks>
    public const double BubbleColumnUpOneBlock = 2.28571;

    /// <summary>What one block interaction costs a plan, in ticks: the <c>use_item_on</c> round trip plus the wait for the world to report that the block moved. This is a derived estimate, not a direct measurement, and is calibrated against per-row tick counts in scenario W5.</summary>
    /// <remarks>
    /// <para><b>Where 10 comes from.</b> The only comparable numbers this repository has measured are the dig verifier's: <c>InteractionActions.DigConfirmationPoll</c> is 50 ms, i.e. one tick, and <c>DefaultDigConfirmationWindow</c> is one second, i.e. twenty. An interaction is a send, a server round trip, and then one or two poll slices before the block update lands, so half a second - ten ticks, about 2.2 blocks of walking - is the midpoint of that range.</para>
    /// <para><b>What it has to be right about.</b> Its only job in the search is to decide whether a door is cheaper than the detour around it, so what matters is its size relative to <see cref="WalkOneBlock"/> (4.63) rather than its absolute value: at 10 ticks a door is worth about two blocks of detour. The same number is what the activator window check charges as the interaction's own observe latency, deliberately, because it is the same round trip - see <c>DoorActivation</c>.</para>
    /// </remarks>
    public const double InteractLatency = 10.0;

    /// <summary>What a lateral squeeze costs, as a multiplier on <see cref="WalkOneBlock"/>.</summary>
    /// <remarks>
    /// <para><b>Why it is priced on the WALK rate and not the sprint rate.</b> A squeeze aims the body at a point 0.2 off the cell centre and then, on the next edge, usually back again, so the heading changes twice inside one block. A sprint cannot survive that: vanilla drops sprint the moment the body collides horizontally, and the executor's own approach planner brakes for a turn. Charging the sprint rate for a manoeuvre the body cannot sprint is the kind of under-pricing that makes A* prefer a route it then cannot walk.</para>
    /// <para><b>And why that is safe for the heuristic.</b> <c>GoalBlock.DistanceHeuristic</c> promises <see cref="SprintOneBlock"/> a block, 3.5638 ticks. <see cref="WalkOneBlock"/> is 4.6329, already 1.30x that, and this multiplier is at least 1, so a squeeze edge can never cost less than the heuristic removes across it. A squeeze is a PENALTY, which is the safe direction; the Diagonal family's exactly-zero admissibility margin is what a discount here would have eaten. <c>HeuristicAdmissibilityTests</c> sweeps a bamboo shape for exactly this reason.</para>
    /// <para><b>Calibration, measured rather than guessed.</b> 26.1, seed <c>offsetcave</c>, a one-wide walled lane at <c>z = 900</c> with a real bamboo stalk in it, driven by the real client. Timed on the engine's own tick counter, never wall clock.</para>
    /// <code>
    ///   empty lane, 8 interior segments   5 5 4 5 4 5 5 4   = 37 ticks / 8 blocks = 4.625
    ///   one post,  the three squeeze segs      17 15 16     = 48 ticks / 3 blocks = 16.00
    ///   two posts, the five squeeze segs  17 15 16 . 16 10  = 64 ticks / 4 blocks = 16.00
    /// </code>
    /// <para>The executor runs an ordinary sprint traverse at 4.625 ticks where the model charges <see cref="SprintOneBlock"/> 3.5638, so the model-to-executor ratio is 1.298 and it is divided back out rather than baked in. A squeeze at 16.0 executed ticks is therefore <c>16.0 / 1.298 = 12.33</c> model ticks. On top of that a squeeze forces the segment BEFORE it to brake - measured, the approach segment goes from 4.3 ticks to 10 - and the planner has nowhere else to charge that, since the approach edge is an ordinary walk: amortised over the squeezes that caused it that is 1.9 ticks each in the one-post row and 2.6 in the two-post row, so about 2.2, giving <c>(16.0 + 2.2) / 1.298 = 14.02</c>. Against <see cref="WalkOneBlock"/> 4.6329 that is <b>3.03</b>, and the constant is the round 3.0.</para>
    /// <para>The number is large, and it is meant to be: a squeeze really does cost three and a half ordinary blocks, so a four-block detour around a stalk is genuinely cheaper than threading it and A* should say so. Under-pricing it would buy the bot a route it then crawls.</para>
    /// </remarks>
    public const double SqueezeMultiplier = 3.0;

    /// <summary>A squeezed traverse of one block, in ticks.</summary>
    public const double SqueezeOneBlock = WalkOneBlock * SqueezeMultiplier;

    private static readonly double[] FallNBlocksCost = BuildFallTable(257);

    private static double[] BuildFallTable(int maxBlocks)
    {
        var table = new double[maxBlocks];
        table[0] = 0;

        double velocity = 0;
        double distance = 0;
        int ticks = 0;
        int blockIndex = 1;

        while (blockIndex < maxBlocks)
        {
            velocity += 0.08;
            velocity *= 0.98;
            distance += velocity;
            ticks++;

            while (blockIndex < maxBlocks && distance >= blockIndex)
            {
                table[blockIndex] = ticks;
                blockIndex++;
            }

            if (ticks > 10000)
                break;

        }

        for (int i = blockIndex; i < maxBlocks; i++)
            table[i] = CostInf;

        return table;
    }

    /// <summary>The tick cost of falling <paramref name="blocks"/> blocks (infeasible beyond the table range).</summary>
    public static double FallCost(int blocks)
    {
        if (blocks < 0 || blocks >= FallNBlocksCost.Length)
            return CostInf;

        return FallNBlocksCost[blocks];
    }
}
