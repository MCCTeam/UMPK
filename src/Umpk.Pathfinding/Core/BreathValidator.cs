using Umpk.Geometry;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding.Core;

/// <summary>The outcome of a breath validation pass over a planned route.</summary>
public readonly record struct BreathValidation
{
    /// <summary>Whether the route can be executed on a single full lung.</summary>
    public bool IsSurvivable { get; init; }

    /// <summary>The largest air deficit the route reaches, in REAL ticks (not planner ticks). Comparable directly against <see cref="BreathModel.MaxDeficit"/>.</summary>
    public double PeakDeficitTicks { get; init; }

    /// <summary>The index of the segment on whose arrival the deficit first exceeded the budget, or <c>-1</c> when the route is survivable.</summary>
    public int FirstViolationSegment { get; init; }

    /// <summary>The budget the route was judged against, in real ticks: the lung the player actually held less the reaction reserve (<see cref="BreathModel.RouteBudget"/>). Recorded so a refusal says what it refused against, rather than being read as a full lung it never was.</summary>
    public double BudgetTicks { get; init; }

    /// <summary>How many times the route surfaces to breathe: segments that end with the head out of the water after submerged travel that has not yet been cleared.</summary>
    /// <remarks>These are the route's own AIR STOPS, and they are what a surfacing budget has to be sized against. The life-safety supervisor spends one recovery per surfacing, and a flat allowance cannot tell a route with five legitimate breaths from a bot oscillating in place: the fifth honest breath and the fifth failed one look identical to a counter that only counts. Consecutive dry segments count once, because the first of them is the breath and the rest are a walk.</remarks>
    public int BreathingStops { get; init; }
}

/// <summary>
/// A post-planning feasibility pass: walks a planned segment list accumulating the air deficit and refuses a route the player cannot survive on one lung.
///
/// <para>The deficit is accumulated in REAL ticks, not in the planner's g-cost. That distinction is the whole point of this class. The planner charges a submerged bottom-walk <see cref="ActionCosts.SprintOneBlock"/> (3.5638 ticks a block) because the move it emitted is an ordinary <c>Traverse</c> executed by <c>WalkTemplate</c>; the water the player is standing in makes that walk run at <see cref="SprintWadeBlocksPerTick"/>, which is 2.0 times slower, or at <see cref="WadeBlocksPerTick"/> with no sprint, which is 3.1 times slower. Validating on the charged cost therefore approves an eighty-block sealed bore at 285 ticks that drowns the player at 584.</para>
/// </summary>
/// <remarks>
/// <para><b>Submerged is a head-cell test.</b> A cell counts as submerged when the cell ABOVE the node's feet cell is water: <c>IsWater(view, x, y + 1, z)</c>. The eye sits 1.62 above the feet in the standing pose and 0.4 in the swimming pose (<c>PhysicsConstants.PlayerEyeHeight</c>/<c>SwimmingEyeHeight</c>), so a water head cell puts the eye inside water in either pose, and an air head cell caps the water surface at <c>y + 1.0</c>, which a standing eye at <c>y + 1.62</c> clears. This is the same eye-submersion predicate <c>AirSupplyRule</c> is driven by, and it is expressed with the planner's OWN water predicate (<see cref="MoveHelper.IsWater"/>) so the validator and the move set can never disagree about what water is. The known edge is a fractional-height film (a level-7 cell is 1/9 deep), which <c>CanTraverseWater</c> already treats as a full column.</para>
/// <para>A segment counts as submerged when EITHER endpoint is: entering or leaving water is charged as a wet move, which is the conservative direction.</para>
/// </remarks>
public static class BreathValidator
{
    /// <summary>Ticks of air recovered per tick with the eyes out of water: <c>min(air + 4, max)</c>.</summary>
    public const double RefillPerTick = 4.0;

    /// <summary>Horizontal blocks per tick for a player moving through water WITHOUT the sprint damping arm: the flat water acceleration <c>WaterBaseSpeed 0.02</c> times <c>InputFriction 0.98</c> over <c>1 - 0.8</c> of slow-down, i.e. <c>0.0196 / 0.2</c> exactly. Measured identical (<c>0.09800</c>) on protocols 47, 340 and 393+ without <c>Sprint</c>.</summary>
    public const double SubmergedBlocksPerTick = 0.098;

    /// <summary>Horizontal blocks per tick for a sprint-swim on <see cref="WaterTravelEra.SprintAware"/> (protocol 393+), where <c>Sprint</c> flips the slow-down from <c>0.8</c> to <c>0.9</c>. Measured 0.19586 over a settled 20-tick window; the closed form <c>0.0196 / (1 - 0.9)</c> is 0.196, so the measured value is the slower and therefore the safer of the two.</summary>
    public const double SprintSwimBlocksPerTick = 0.19586;

    /// <summary>Horizontal blocks per tick for a submerged BOTTOM-WALK the executor is allowed to sprint, on <see cref="WaterTravelEra.SprintAware"/>: 0.137, which is 7.299 ticks a block.</summary>
    /// <remarks>
    /// <para><b>Why this is not <see cref="SubmergedBlocksPerTick"/>.</b> A submerged bottom-walk is <c>MoveType.Traverse</c> or <c>MoveType.Diagonal</c> executed by <c>WalkTemplate</c>. Its observed rate ranges from 5.1 to 10.2 ticks per block and depends on <c>AllowSprint</c>, which travels with the plan and selects water damping of 0.9 instead of the ordinary slowdown, which is the whole factor of two. The swim arm of this class already keys on exactly that flag; the walk arm did not, and priced every wade as if the executor never sprinted. Course rows E15 and E16 were refused needing 285.5 and 275.3 ticks of lung against the ~270 a submerged start can carry, on walks of 27 and 26 blocks the executor makes in about 170.</para>
    /// <para><b>Measured</b>, driving the real <c>PathExecutor</c> over the real <c>PlayerPhysics</c> engine on protocol 772, counting only the ticks spent on submerged <c>Traverse</c> segments, over six shapes of course row E15's own lane that the executor completes (lengths 10 to 34, roofed spans 6 to 23, water one and three deep): <b>6.300, 6.333, 6.500, 6.538, 6.885, 7.250</b> ticks a block, with <c>Sprint</c> held on 82% of the driven ticks. The steady-state one-block Traverse is a flat five ticks; the rest is the approach-and-settle the walk template spends per segment and the swim template does not (the same asymmetry <c>SegmentBudgetPolicy</c> states as <c>FixedOverheadTicks</c> 20 against <c>SwimFixedOverheadTicks</c> 8), which is why the 8-block route is the slowest of the six and the 30-block ones the fastest.</para>
    /// <para>7.299 stands above the slowest of those six, so the price is never optimistic on any of them, and it is 1.15 times the long-run rate rather than the 1.61 the old number charged.</para>
    /// <para><b>Boundary.</b> Neither this nor the old number covers a wade against a CURRENT: E6 measured 22.2 ticks a block dead upstream live, which 10.204 did not bound either. That is the missing flow field, a known open issue, and it is priced nowhere in this model.</para>
    /// </remarks>
    public const double SprintWadeBlocksPerTick = 0.137;

    /// <summary>Horizontal blocks per tick for a submerged bottom-walk the executor may NOT sprint, or on an era with no sprint arm in water: 0.0895, which is 11.173 ticks a block.</summary>
    /// <remarks>Measured the same way with <c>AllowSprint</c> false, over the same lane at two lengths: <b>10.778</b> and <b>11.129</b> ticks a block, with <c>Sprint</c> emitted on 0 of 297 ticks and the steady-state one-block Traverse at ten to eleven. Both are SLOWER than the <see cref="SubmergedBlocksPerTick"/> terminal velocity. The 11.173 bound stands above both measured rates and includes the per-segment settle cost.</remarks>
    public const double WadeBlocksPerTick = 0.0895;

    /// <summary>Validates a planned route against a single lung.</summary>
    /// <param name="segments">The planned segments, carrying their own <c>PlannedTickCost</c>.</param>
    /// <param name="world">The planning view the segments were planned against.</param>
    /// <param name="profile">The physics profile, which decides the water-travel era.</param>
    /// <param name="allowSprint">Whether the executor may hold <c>Sprint</c>. <c>TemplateOutput.From</c> clears the bit when the context forbids it, so a swim planned with sprinting disabled really does run at <see cref="SubmergedBlocksPerTick"/>.</param>
    /// <param name="airTicks">The breath the player holds RIGHT NOW, in ticks (<c>SelfState.AirSupply</c>), captured on the session loop with the rest of the plan. Not a full lung: the player planning this route is very often already submerged, and the life-safety supervisor judges it on this number, so the validator has to as well or the two disagree by construction. See <see cref="BreathModel.RouteBudget"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static BreathValidation Validate(
        IReadOnlyList<PathSegment> segments,
        PlanningWorldView world,
        PhysicsProfile profile,
        bool allowSprint,
        int airTicks)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        double maxDeficit = BreathModel.RouteBudget(profile, airTicks);
        Accumulation walk = Accumulate(segments, 0, world, profile, allowSprint, maxDeficit);
        double deficit = walk.Deficit;
        double peak = walk.Peak;
        int violation = walk.Violation;
        int breathingStops = walk.BreathingStops;

        // The arrival reserve. Everywhere along the route the route itself IS the escape, so no per-node reserve is charged; at the destination there is nothing left to walk, and a goal the player reaches with no breath to climb back out is a goal it drowns on. This is section 3.2's `D + EscapeTicks(n) + Margin <= lung`, evaluated where it changes an answer, with the lung being the one the player is holding rather than a full one.
        //
        // An UNKNOWN reserve is not a violation, and that polarity is the same one the search's own per-node test uses (AStarPathFinder.IsBreathFeasible): an unknown escape means the vertical scan met a ceiling, and under a ceiling the route is the escape - which the deficit accounting above has already priced in full. Reading it the other way made the validator strictly stricter than the rule every node on the route was admitted under, and it refused any goal inside a sealed water volume at all. Course row E5's goal is the far trench bed under an unbroken lid by design, so that reading refused the row on its destination after the route itself had been proved survivable: `peak 201,4 ticks, budget 290, first violation at segment 76 of 77`.
        if (violation < 0 && segments.Count > 0)
        {
            PathSegment last = segments[^1];

            // The FEET CELL, not floor(End.Y). Once PathSegmentBuilder resolves a partial support's elevation into the endpoint, flooring it lands one cell low and the vertical escape scan starts a block deeper. A bottom slab at the bottom of a six-deep pool measures 21.72 ticks against the node cell's own 19.1, which refuses goals the player surfaces from. See PathSegment.EndFeetY.
            BreathEscape reserve = BreathModel.EscapeTicks(
                world, (int)Math.Floor(last.End.X), last.EndFeetY, (int)Math.Floor(last.End.Z), profile);
            if (reserve.IsKnown && deficit + reserve.Ticks > maxDeficit)
            {
                violation = segments.Count - 1;
                peak = Math.Max(peak, deficit + reserve.Ticks);
            }
        }

        return new BreathValidation
        {
            IsSurvivable = violation < 0,
            PeakDeficitTicks = peak,
            FirstViolationSegment = violation,
            BudgetTicks = maxDeficit,
            BreathingStops = breathingStops,
        };
    }

    /// <summary>The largest air deficit the TAIL of a route reaches, in REAL ticks: the same walk <see cref="Validate"/> runs, started at <paramref name="firstSegment"/> and with no budget to violate.</summary>
    /// <remarks>
    /// <para>This is what a surfacing has to refill AGAINST. The life-safety supervisor's own trip threshold is the cost of reaching the NEXT breathing node, which near an air pocket is a handful of ticks, and the vertical escape threshold is the climb out of the deepest cell on the route, which in a shallow trench is under ten. Neither is a statement about the leg the route takes AFTER that breath, and that leg is what <see cref="Validate"/> refuses the continuation on. So a hold sized by the bar that fired it releases straight back into a route the player cannot afford: course row E5 surfaceholes requires a continuation whose <c>peakDeficit</c> is 191.26, well above an <c>air 99</c> release.</para>
    /// <para>It is the same accumulation, over the same segments, with the same medium-aware rates, so the number a surfacing aims at and the number the validator judges the continuation by cannot drift apart. Restarting the walk mid-route is sound because the deficit is reset at every breathing arrival anyway: the tail's peak never depends on what happened before <paramref name="firstSegment"/>, only on the legs after it.</para>
    /// </remarks>
    /// <param name="segments">The planned segments, carrying their own <c>PlannedTickCost</c>.</param>
    /// <param name="firstSegment">The first segment still ahead. Values below zero read as zero.</param>
    /// <param name="world">The world the segments were planned against.</param>
    /// <param name="profile">The physics profile, which decides the water-travel era.</param>
    /// <param name="allowSprint">Whether the executor may hold <c>Sprint</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static double PeakDeficit(
        IReadOnlyList<PathSegment> segments,
        int firstSegment,
        IPhysicsWorldView world,
        PhysicsProfile profile,
        bool allowSprint)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        return Accumulate(
            segments, Math.Max(0, firstSegment), world, profile, allowSprint, double.PositiveInfinity).Peak;
    }

    /// <summary>One pass of the deficit walk: what it peaked at, where it ended, and what it found.</summary>
    private readonly record struct Accumulation(double Peak, double Deficit, int Violation, int BreathingStops);

    /// <summary>The deficit walk itself, shared by <see cref="Validate"/> and <see cref="PeakDeficit"/> so the planner's refusal and the supervisor's refill target are computed by one piece of code.</summary>
    private static Accumulation Accumulate(
        IReadOnlyList<PathSegment> segments,
        int firstSegment,
        IPhysicsWorldView world,
        PhysicsProfile profile,
        bool allowSprint,
        double maxDeficit)
    {
        double swimTicksPerBlock = SwimTicksPerBlock(profile, allowSprint);
        double walkTicksPerBlock = WadeTicksPerBlock(profile, allowSprint);

        double deficit = 0;
        double peak = 0;
        int violation = -1;
        int breathingStops = 0;
        bool wetSinceTheLastStop = false;

        for (int i = firstSegment; i < segments.Count; i++)
        {
            PathSegment segment = segments[i];
            bool submerged = IsSubmerged(world, segment.Start, segment.StartFeetY)
                || IsSubmerged(world, segment.End, segment.EndFeetY);
            double real = RealTicks(
                segment,
                submerged,
                swimTicksPerBlock,
                walkTicksPerBlock * WadeCurrentPenalty(world, segment));

            if (submerged)
            {
                wetSinceTheLastStop = true;
                deficit += real;
                if (deficit > peak)
                    peak = deficit;

                if (deficit > maxDeficit && violation < 0)
                    violation = i;

            }
            else
            {
                // Out of the water the lung refills four ticks a tick. The charged cost stands in for the real duration here, and on land the real duration is never SHORTER than the charge, so this under-states the refill: the safe direction.
                deficit = Math.Max(0.0, deficit - (RefillPerTick * real));
            }

            // The breathing stop. A segment that ENDS with the head out of the water ends somewhere the player can simply stay until the lung is full. Air refills four ticks per tick while the eye is outside water, with no bound on how long that is, so the deficit at such an arrival is a wait, not a debt. Without this the only refill was the decay term above, four times a move's own cost, which banks air in proportion to how many moves can be made while breathing and banks NOTHING at a pocket one cell across. E4's sealed bells and E5's collared surface holes are exactly such pockets. The search charges the wait to the g-cost (AStarPathFinder.NextAirDeficit); here it costs no breath, which is the only currency this pass is counting.
            if (!IsSubmerged(world, segment.End, segment.EndFeetY))
            {
                // The same arrival is the route's air stop, counted for the surfacing budget. The flag rather than the deficit is what says whether this arrival is a BREATH: the decay term above may already have taken the deficit to zero on the way here, and a route that was never wet in the first place is not breathing, it is walking.
                if (wetSinceTheLastStop)
                {
                    breathingStops++;
                    wetSinceTheLastStop = false;
                }

                deficit = 0.0;
            }
        }

        return new Accumulation(peak, deficit, violation, breathingStops);
    }

    /// <summary>The whole route's duration in REAL ticks: <see cref="RealTicks(PathSegment, bool, PhysicsProfile, bool)"/> summed over the segments, with each one's medium decided by the same head-cell test the deficit walk uses.</summary>
    /// <remarks>This is not the plan's g-cost and the difference is the reason it exists. The planner charges a submerged bottom-walk <see cref="ActionCosts.SprintOneBlock"/> because the move it emitted is an ordinary <c>Traverse</c>; the water makes that walk run at <see cref="SprintWadeBlocksPerTick"/>, two to three times slower. Any consumer asking "how long will this route actually take" - <see cref="EffectCoverage"/> asks it of a potion's remaining duration - has to ask it here, so that it and <see cref="Validate"/> cannot disagree about the same route.</remarks>
    /// <param name="segments">The planned segments.</param>
    /// <param name="world">The world the segments were planned against.</param>
    /// <param name="profile">The physics profile, which decides the water-travel era.</param>
    /// <param name="allowSprint">Whether the executor may hold <c>Sprint</c>.</param>
    /// <returns>The route's duration in real ticks.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static double RouteRealTicks(
        IReadOnlyList<PathSegment> segments, IPhysicsWorldView world, PhysicsProfile profile, bool allowSprint)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        double swimTicksPerBlock = SwimTicksPerBlock(profile, allowSprint);
        double walkTicksPerBlock = WadeTicksPerBlock(profile, allowSprint);

        double total = 0.0;
        for (int i = 0; i < segments.Count; i++)
        {
            PathSegment segment = segments[i];
            bool submerged = IsSubmerged(world, segment.Start, segment.StartFeetY)
                || IsSubmerged(world, segment.End, segment.EndFeetY);
            total += RealTicks(
                segment,
                submerged,
                swimTicksPerBlock,
                walkTicksPerBlock * WadeCurrentPenalty(world, segment));
        }

        return total;
    }

    /// <summary>The real tick duration of one segment: the planner's own charge out of the water, and the medium's measured rate inside it. Public because the life-safety supervisor prices the remaining route with exactly this model, and the two must never disagree.</summary>
    /// <remarks>
    /// <para><b>Swim segments</b> are executed by <c>SwimTemplate</c>, which holds <c>Sprint</c> on every tick, so the era rate is exact and is used as-is rather than maxed against the charge: on a sprint-aware protocol the planner's <see cref="ActionCosts.SwimOneBlock"/> (9.09) is 1.78x pessimistic and believing it would refuse routes the player swims comfortably.</para>
    /// <para><b>Everything else submerged</b> is a bottom-walk: <c>MoveType.Traverse</c> and <c>MoveType.Diagonal</c> map to <c>WalkTemplate</c>, which chooses <c>Sprint</c> per tick from its transition state rather than holding it. That per-tick choice is what the old note here concluded made the rate unknowable ("anywhere between 5.1 and 10.2 ticks a block and not a function of anything the plan carries"), and it is not: measured over seven shapes of course row E15's lane the executor holds <c>Sprint</c> on 82% of its ticks and the walk lands at 5.9 to 6.3 ticks a block, against 10.8 with <c>AllowSprint</c> off. So the rate IS a function of the flag the plan carries, and <see cref="WadeTicksPerBlock"/> reads it on the same era axis the swim arm already reads. The charge is kept as a floor so an expensively-priced wet move (a jump penalty, a slow floor) is never discounted.</para>
    /// </remarks>
    public static double RealTicks(PathSegment segment, bool submerged, PhysicsProfile profile, bool allowSprint)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(profile);
        return RealTicks(
            segment, submerged, SwimTicksPerBlock(profile, allowSprint), WadeTicksPerBlock(profile, allowSprint));
    }

    private static double SwimTicksPerBlock(PhysicsProfile profile, bool allowSprint)
        => allowSprint && profile.WaterTravel == WaterTravelEra.SprintAware
            ? 1.0 / SprintSwimBlocksPerTick
            : 1.0 / SubmergedBlocksPerTick;

    /// <summary>The real ticks a submerged BOTTOM-WALK of <paramref name="blocks"/> blocks takes.</summary>
    /// <remarks>
    /// Public because the life-safety supervisor and the tests that measure this walk against the real executor need the same arithmetic the validator applies, rather than a second copy of it.
    /// <para>The SEARCH deliberately does not use it: <c>AStarPathFinder.NextAirDeficit</c> keeps the no-sprint terminal velocity, because its own refusal is against a FULL lung where this one is against the lung the player holds, and the gap between those two caps is what that extra 1.4x pays for. See the note there.</para>
    /// </remarks>
    /// <param name="blocks">The submerged distance, in blocks.</param>
    /// <param name="profile">The physics profile, which decides the water-travel era.</param>
    /// <param name="allowSprint">Whether the executor may hold <c>Sprint</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static double WadeTicks(double blocks, PhysicsProfile profile, bool allowSprint)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return blocks * WadeTicksPerBlock(profile, allowSprint);
    }

    /// <summary>The real ticks a submerged BOTTOM-WALK spends per block, keyed on the same <paramref name="allowSprint"/> flag and the same era axis the swim rate is keyed on.</summary>
    /// <remarks><see cref="WaterTravelEra.Legacy"/> (protocols 47-340) gets the no-sprint rate whatever the flag says, because water movement had no sprint term before 1.13 and used flat damping of 0.8. That is the same era axis <see cref="SwimTicksPerBlock"/> already reads, so the two arms cannot disagree about which era has a sprint in water.</remarks>
    private static double WadeTicksPerBlock(PhysicsProfile profile, bool allowSprint)
        => allowSprint && profile.WaterTravel == WaterTravelEra.SprintAware
            ? 1.0 / SprintWadeBlocksPerTick
            : 1.0 / WadeBlocksPerTick;

    private static double RealTicks(PathSegment segment, bool submerged, double swimTicksPerBlock, double walkTicksPerBlock)
    {
        double charged = segment.PlannedTickCost;
        if (!submerged)
            return charged;

        // The PLANNER's own distance, whose rise is the logical feet-cell delta rather than the resolved elevation change. This number is turned straight into ticks of lung, and a resolved endpoint shortens a one-block ascend onto a bottom slab from 1.414 blocks to 1.118 - a 21% discount on the breath the move is priced at, in the one direction that drowns the player. See PathSegment.PlannedBlocks.
        double blocks = segment.PlannedBlocks;
        return segment.MoveType == MoveType.Swim
            ? blocks * swimTicksPerBlock
            : Math.Max(charged, blocks * walkTicksPerBlock);
    }

    /// <summary>Whether the head cell above an endpoint's LOGICAL feet cell is water.</summary>
    /// <remarks>The Y is passed in rather than floored out of <paramref name="point"/>, and that is the whole point of the parameter. <see cref="BreathModel.IsSubmerged"/> is defined on the feet CELL - it reads <c>y + 1</c> - so a Y that is a POSITION rather than a cell index shifts the read by one whole cell the moment the position is not the cell's own integer. A bottom slab resolves to <c>End.Y = 60.5</c> under a node at 61, <c>Math.Floor</c> gives 60, and the head test then reads cell 61, which is where the player's FEET are. On a one-deep sheet with open air above, <c>False</c> becomes <c>True</c> and a player standing in a puddle is validated as drowning. See <see cref="PathSegment.StartFeetY"/>.</remarks>
    private static bool IsSubmerged(IPhysicsWorldView world, in Vec3d point, int feetY)
        => BreathModel.IsSubmerged(world, (int)Math.Floor(point.X), feetY, (int)Math.Floor(point.Z));

    /// <summary>How much longer this segment's bottom-walk really takes because of the current it is walked against: <c>ActionCosts.WadeCurrentCostMultiplier</c> over the flow at the segment's own start cell. Exactly 1.0 in still water and out of it.</summary>
    /// <remarks>
    /// <para><b>Why the validator needs this at all.</b> <see cref="SprintWadeBlocksPerTick"/> and <see cref="WadeBlocksPerTick"/> are STILL-WATER measurements - 7.299 and 11.173 ticks a block - and this class turns them straight into ticks of lung. Against a dead-upstream current the real rate is 35.71 ticks a block, which is the same number <c>SegmentBudgetPolicy.SubmergedWalkSlack</c> derives itself from, and 41 ticks a block was observed on a real segment into a source cell. The still-water rates therefore UNDER-STATE a dead-upstream submerged leg by 3.2x to 4.9x, and a fully submerged leg the validator approves at 300 ticks can really take 1470. <b>That is a drowning hazard, not a cost-honesty gap</b>, which is why it is here and not left to a later cost-honesty pass.</para>
    /// <para><b>The direction is refusal-conservative, twice over.</b> The multiplier is clamped at 1.0, so a DOWNSTREAM leg is never credited and the validator can only ever become more careful; and the level-0 arm is used deliberately, with no depth-strider term, so a booted body is faster than the validator believes rather than slower. Both errors point at refusing a route the player could have made, which loses a route, rather than approving one that drowns.</para>
    /// <para><b>Blast radius on today's course is ZERO</b>, and that is a property of <see cref="IsSubmerged"/> rather than luck: it is a HEAD-CELL test, and every wet row in the course is a one-layer wade whose head is in air, so none of them is submerged and none of them contributes to the deficit at all. What this changes is a route with a genuinely submerged upstream leg.</para>
    /// <para><b>The same still-water assumption applies to <see cref="SprintSwimBlocksPerTick"/> and <see cref="SubmergedBlocksPerTick"/> and is deliberately NOT addressed here.</b> The swim family already has <c>ActionCosts.CurrentCostMultiplier</c>, so the same treatment applies to it, but a swim's flow is often vertical and the vertical term is a different measured ratio.</para>
    /// </remarks>
    private static double WadeCurrentPenalty(IPhysicsWorldView world, PathSegment segment)
    {
        if (segment.MoveType == MoveType.Swim)
            return 1.0;

        int x = (int)Math.Floor(segment.Start.X);
        int z = (int)Math.Floor(segment.Start.Z);
        Vec3d flow = Umpk.Physics.PlayerPhysics.GetWaterFlow(world, new BlockPos(x, segment.StartFeetY, z));
        if (flow.X == 0.0 && flow.Z == 0.0)
            return 1.0;

        var direction = new Vec3d(segment.End.X - segment.Start.X, 0.0, segment.End.Z - segment.Start.Z);
        return ActionCosts.WadeCurrentCostMultiplier(flow, direction);
    }
}
