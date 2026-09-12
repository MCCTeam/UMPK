using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>How long one segment is allowed to take, and how slowly its executor is allowed to move before it counts as stuck. Derived from what the PLANNER charged the segment (<see cref="PathSegment.PlannedTickCost"/>) and from the medium the body is actually in, rather than from template-specific flat integer literals.</summary>
/// <remarks>
/// <para>Every number here is a RATIO between two measured rates, not a tuning knob. A slack is "the worst speed the executor can legitimately achieve in this medium, divided by the speed the planner charged"; a stuck speed is a quarter of the medium's nominal speed. That is what makes them track changes in the physics model.</para>
/// <para><b>Both bounds are real.</b> The floor exists because a one-block segment's charge is smaller than the approach-and-settle overhead around it, and the ceiling exists because it is the compatibility ceiling, so this policy cannot exceed that bound.</para>
/// </remarks>
public sealed class SegmentBudgetPolicy
{
    /// <summary>The slack on dry land: the worst legitimate real duration over the planner's charge.</summary>
    /// <remarks>
    /// <para><b>The worst legitimate land case is a slow floor, measured directly.</b> Soul sand and honey are speed factor 0.4, the smallest in the game (<see cref="ActionCosts.MeasuredSlowFloorSpeedFactor"/>, swept exhaustively over all 49 protocols), and a sprint over one measures 6.13501 ticks a block against stone's 3.62765. That is <b>1.691</b>, and it is what this constant is, tied to <see cref="ActionCosts.MeasuredSlowFloorCostMultiplier"/> rather than restated so the budget and the CHARGE can never drift apart: they are two consequences of one measurement.</para>
    /// <para><b>Why the multiplier is 1.691 rather than 2.50.</b> The tempting <c>1 / 0.4</c> estimate assumes a 0.4 speed factor makes traversal 2.5 times slower. It does not. <c>floor speed-factor selection</c>'s result multiplies the VELOCITY inside the friction loop rather than scaling a terminal speed, so the steady state settles well above <c>factor x</c> free speed and the first-principles figure over-charged by 48 percent. The value was safe; its stated reason was not true, which is the thing this repository does not let stand.</para>
    /// <para><b>What remains for slack to cover.</b> A ground move is priced at its DESTINATION floor's factor, so a move onto soul sand is charged 1.691x and executed 1.691x and the ratio is one. What the charge cannot see is the cell the body LEAVES: <c>ApplyBlockSpeedFactor</c> scales the velocity by whichever cell the body is in on the tick, so a move OFF a slow floor onto a fast one is charged the open-ground rate and spends its first ticks at the slow one. Its bound is the whole-move 1.691 - the same number, now standing for the source floor instead of the destination. The same bound also covers a hand-built segment, whose <see cref="ExpectedTicks"/> fallback is the medium's nominal rate with no floor term at all.</para>
    /// <para><b>The approach-and-settle allowance is not in here.</b> It is additive (<see cref="FixedOverheadTicks"/>), and <see cref="MinBudgetTicks"/> floors every short segment at 40 regardless, so this ratio only reaches the answer past <c>(40 - 20) / 1.691 = 11.83</c> charged ticks - 3.32 blocks of sprint. One- and two-block land segments are bit-identical to what they were at 2.50.</para>
    /// <para>The charge and budget deliberately differ for soul-speed equipment. <c>ActionCosts.SpeedFactorCostMultiplier(factor, bypassed)</c> drops a soul-speed wearer's slow-floor charge to 1.0. A 20-block soul-sand lane measures 6.014 s bare and 3.009 s booted, against a 4.013 s stone control. This constant does NOT follow it and must not: the sentence above says the two "can never drift apart: they are two consequences of one measurement", and that stays true of the MEASUREMENT while the charge gains a second, capability-dependent arm. A segment planned at the fast rate keeps a budget sized at the slow one, which is asymmetric in the SAFE direction and is exactly the headroom the next paragraph needs.</para>
    /// <para><b>The staleness that headroom is for, stated correctly.</b> A plan captured with boots on and executed after they break walks a lane priced at 1.0 at the real 1.691, and the slack here is what absorbs it: <b>1.691x plus the flat <see cref="FixedOverheadTicks"/> 20</b>. It is not "3x headroom" - nothing on land has 3x; the only slacks at or above three are water's (<see cref="WaterSlackWithoutTheSwimPose"/> 4.50 and <see cref="SubmergedWalkSlack"/> 10.0). A broken boot consumes a land segment's entire slack exactly, leaving only the flat 20.</para>
    /// <para>Every other capability the planner prices either expires on a timer known at capture (a potion) or is consumed by an explicit action (a placed block). Soul-speed boots break STOCHASTICALLY, at 4% per tick, BECAUSE the bot walked the lane the plan chose on the strength of them. That is the first self-destroying capability in the system, nothing preempts on it, and <c>PhysicsEngineHolder.LostALoadBearingEffect</c> - the only mid-route capability watchdog - probes EFFECTS only, so no replan is triggered by a boot breaking. The executor picks up the slower engine value at its next <c>PushConditions</c>; the plan stays priced at capture.</para>
    /// </remarks>
    public const double LandSlack = ActionCosts.MeasuredSlowFloorCostMultiplier;

    /// <summary>The slack in water for a swimmer that has the swim pose.</summary>
    /// <remarks>
    /// <para>Measured as the worst ratio of executed ticks to charged ticks over the shapes the swim move set emits, driving the real engine through the real executor on protocol 772 with <c>Sprint</c> allowed: horizontal 0.674, ascend 0.330, dive 0.399. Every one is FASTER than its charge, because <see cref="ActionCosts.SwimOneBlock"/> (9.0909) is 1.78x pessimistic against a 5.106-ticks-a-block sprint swim. Current does not move the ratio because the charge scales by the same current the executor fights.</para>
    /// <para>1.25 is therefore a settle allowance rather than a speed slack.</para>
    /// </remarks>
    public const double WaterSlack = 1.25;

    /// <summary>The slack in water for a swimmer that does NOT have the swim pose.</summary>
    /// <remarks>
    /// The pose needs <c>Sprint</c> and an era that has one, and without it there is no downward authority at all: the look-angle blend never runs, downward swimming is unimplemented, and a dive becomes the passive sink. Measured on protocol 772 with <c>AllowSprint</c> false: a descending swim segment executes at 37.625 ticks a block against a charge of 9.0909, a ratio of 4.139. The horizontal case is 1.128 and the ascent 0.660, so the dive is what sets this row.
    /// <para><c>Sneak</c> gives zero downward impulse, so the dive remains a passive sink. A direction-aware, pose-aware swim charge belongs in the planner; until then the budget carries this measured case.</para>
    /// <para>Legacy protocols are covered by the same row and get more than they need: their passive sink is 10.256 ticks a block (measured, protocol 340), a ratio of 1.13, because <c>LegacyWaterSink</c> is four times the modern rate.</para>
    /// </remarks>
    public const double WaterSlackWithoutTheSwimPose = 4.50;

    /// <summary>The slack on a fall that lands in, and continues through, water.</summary>
    /// <remarks>Measured 1.026 and kept at 1.25 for the same settle allowance. The planner charges <see cref="ActionCosts.WaterSinkOneBlock"/> a block for a submerged sink and the passive sink measures 41.026 ticks a block on <see cref="WaterTravelEra.SprintAware"/> (protocol 772, 200 ticks of <c>MovementInput.None</c>), so the two agree - which is the whole point of that re-pricing. Legacy is 10.256 and therefore 0.256 of its charge, deliberately over-charged.</remarks>
    public const double FallIntoWaterSlack = 1.25;

    /// <summary>The slack on a ladder or vine.</summary>
    public const double ClimbSlack = 1.50;

    /// <summary>The slack on a WALK the planner priced as dry land and the world put under water.</summary>
    /// <remarks>
    /// <para>The planner emits a submerged bottom-walk as an ordinary <c>Traverse</c> or <c>Diagonal</c> - the jump family produces those, <c>ActionTemplateFactory</c> maps both to <c>WalkTemplate</c>, and <c>MoveHelper.CanWalkThrough</c> admits water whenever <c>AllowSwim</c> is set - so it charges <see cref="ActionCosts.SprintOneBlock"/>, 3.5638 ticks a block, for a move the body makes at 0.098 blocks a tick at best and 0.028 dead upstream. Nothing in the segment says the body is in water, so nothing but the world can say it.</para>
    /// <para>10.0 is that worst case over that charge: <c>(1 / 0.028) / 3.5638 = 10.02</c>, where 0.028 is <c>(0.0196 - 0.014) / (1 - 0.8)</c>, the thrust net of a dead-upstream current at the no-sprint damping. Course rows E6 and E9 measure 22.2 and 33.6 ticks per upstream block, respectively, both beyond a land-calibrated budget.</para>
    /// <para><b>The charge includes current; this multiplier deliberately does not.</b> <see cref="ActionCosts.WadeCurrentCostMultiplier(Umpk.Geometry.Vec3d, Umpk.Geometry.Vec3d, int)"/> prices an upstream wade up to 3.7371x, which is the same physics 10.0 was derived from, so this doc reads as double-counting unless the asymmetry is written down. It is the same asymmetry <see cref="LandSlack"/> carries for stale capabilities, and it is deliberate in the same way.</para>
    /// <para><b>This is a proof, not a margin estimate, and it is the reason the slack neither needs to move nor may be tightened.</b> Because the wade multiplier is clamped to <c>[1.0, ActionCosts.WadeMaxCurrentCostMultiplier]</c>, every wade budget is at least <c>ceil(SubmergedWalkSlack * SprintOneBlock * 1.0) + FixedOverheadTicks = 56</c>. <b>No wade segment can receive less than this baseline under any staleness.</b> Tightening the slack would remove headroom the clamp has already proved is never removed.</para>
    /// <para>A dead-upstream cell is budgeted <c>SubmergedWalkSlack * 3.7371 = 37.4x</c> its still-water charge. That is intentional and it does not hide a stall, because the budget is neither the only stall detector nor the fast one: <see cref="StuckTicksFor"/> and <see cref="StuckMovedSquared"/> run every tick against the LIVE <c>InWater</c> flag and are untouched by any of this. What a budget kills is a body that is MOVING but too slowly, and pricing the current is precisely what makes "too slowly" an honest threshold rather than a land-calibrated one.</para>
    /// <para><b>Staleness.</b> <c>PathfinderCapabilities</c> is frozen at capture, so a plan priced with depth strider III and then executed bare is charged 1.0870x for work that costs 3.7371x. The clamp argument above covers it: the budget that plan received is still at least 56, which is still at least the budget a bare plan would have received, and the worst bare execution actually observed for one block is 41 ticks. There is no watchdog and no preemption for this, and there should not be: depth-strider boots do not self-destroy the way soul-speed boots do, so nothing about EXECUTING the plan destroys the capability that priced it.</para>
    /// </remarks>
    public const double SubmergedWalkSlack = 10.0;

    /// <summary>The approach-and-settle overhead added to every budget, in ticks.</summary>
    public const int FixedOverheadTicks = 20;

    /// <summary>The same overhead for a swim, which has no braking or grounded arrival to settle.</summary>
    public const int SwimFixedOverheadTicks = 8;

    /// <summary>The overhead for a climb.</summary>
    public const int ClimbFixedOverheadTicks = 12;

    /// <summary>No segment gets less than this, whatever its charge.</summary>
    public const int MinBudgetTicks = 40;

    /// <summary>No segment gets more than this: <b>400</b> ticks.</summary>
    /// <remarks>
    /// <para><b>What 200 could not fit.</b> <c>PathSegmentBuilder.Build</c> emits one segment per node pair with no coalescing, so a fall or descend through N blocks of still water is ONE segment charged <c>N * ActionCosts.WaterSinkOneBlock</c>. The engine's measured passive sink is 41.026 ticks a block on <see cref="WaterTravelEra.SprintAware"/>, so:</para>
    /// <code>
    /// blocks  charge  raw budget  clamp@200  clamp@400  measured @ 41.026 t/blk  verdict at 200
    ///    4     160.0     220.0       200        220           164.10             fitted anyway
    ///    5     200.0     270.0       200        270           205.13             BLEW THE BUDGET
    ///    9     360.0     470.0       200        400           369.23             BLEW THE BUDGET
    ///   10     400.0     520.0       200        400           410.26             still blows it
    /// </code>
    /// <para>This bites exactly one shape. Every DRY segment is at or under 70 - <c>LandSlack 1.691 * SprintOneBlock 3.5638 + 20 = 27</c> for a one-block move, floored at <see cref="MinBudgetTicks"/> 40 - so nothing dry comes within a factor of three of either ceiling and the whole dry course is bit-identical across this change. A one-block wade is <c>ceil(10 * 3.5638) + 20 = 56</c>, nowhere near it. A one-block swim is <c>ceil(1.25 * 8.1679) + 8 = 19</c>, floored at 40.</para>
    /// <para>A dead-upstream wade measures 3.7371x its still-water rate. With <c>ActionCosts.WadeCurrentCostMultiplier</c> in play, its budget grows with its charge, and the arithmetic is worth writing out because <see cref="FixedOverheadTicks"/> is <b>additive and is NOT scaled</b>:</para>
    /// <code>
    /// cardinal 1-block upstream wade: ceil(10 * 3.5638 * 3.7371) + 20 = ceil(133.17) + 20 = 154 diagonal 1-block upstream wade: ceil(10 * 3.5638 * 1.41421 * 3.7371) + 20 = 209 -> clamped
    /// </code>
    /// <para>A cardinal upstream wade stays below the 200 ceiling; a diagonal one reaches 209 and is clamped. Neither is a practical hazard because a diagonal wade measures around 50 ticks - but the two must not be conflated, and in particular <c>56 * 3.7371 = 211</c> is NOT the answer for either: that scales the flat overhead by mistake. <b>None of this is why the ceiling moved.</b> The ceiling moved for the still-water sink alone, which is what <c>SegmentBudgetPolicyTests</c> measures directly.</para>
    /// <para><b>The ceiling stays FINITE on purpose, and that is the whole of the safety argument.</b> 400 is not "large enough for anything"; it is chosen so a nine-block still-water sink (369 ticks) fits and a ten-block one does not. A still-water descent deeper than nine blocks SHOULD fail its segment and replan: a fall through ten blocks of still water at 41 ticks a block is twenty seconds of a bot doing nothing while its breath runs down, and <c>BreathValidator</c> prices the same descent at 10.204 ticks a block and would already have refused it if it were a swim. The budget is the backstop for the case where the two models disagree.</para>
    /// <para><b>Why a looser budget does not hide a stall.</b> The budget is neither the only stall detector nor the fast one. <see cref="StuckTicksFor"/> and <see cref="StuckMovedSquared"/> run every tick against the LIVE <c>InWater</c> flag, with a water stuck speed of <c>0.25 * 0.098 * (1 - 0.7143) = 0.0070</c> blocks a tick, and a genuinely stalled body is caught by consecutive stuck ticks long before a 400-tick budget expires. Both are untouched here. What a budget kills is a body that is MOVING but too slowly, and pricing the current is what makes "too slowly" an honest threshold rather than a land-calibrated one.</para>
    /// </remarks>
    public const int MaxBudgetTicks = 400;

    /// <summary>The fraction of a medium's nominal speed below which a tick counts as stuck.</summary>
    public const double StuckSpeedFraction = 0.25;

    private readonly IPhysicsWorldView _world;
    private readonly PhysicsProfile _profile;
    private readonly bool _allowSprint;
    private readonly bool _hasSwimPose;
    private readonly double _landStuckSpeed;
    private readonly double _waterStuckSpeed;

    /// <summary>Creates a policy for one execution context.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    internal SegmentBudgetPolicy(IPhysicsWorldView world, PhysicsProfile profile, bool allowSprint)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        _world = world;
        _profile = profile;
        _allowSprint = allowSprint;
        _hasSwimPose = allowSprint && profile.WaterTravel == WaterTravelEra.SprintAware;

        double landNominal = allowSprint ? LandSprintBlocksPerTick : LandWalkBlocksPerTick;
        _landStuckSpeed = StuckSpeedFraction * landNominal;

        // The water threshold is deliberately NOT keyed on the swim pose, and takes the SLOWEST legitimate water rate whatever the profile says. Two reasons, both structural. It is the threshold WalkTemplate uses for a submerged bottom-walk, and a bottom-walking body has no swim pose whatever AllowSprint says, so a pose-keyed number would be twice too strict for exactly the case the whole medium-awareness exists for. And a current takes a further 1 - CurrentToThrustRatio off the top - 0.286 of nominal, dead upstream - which for a Traverse is invisible in the segment's own charge, because the planner priced it as land. 0.098 * 0.286 = 0.028 blocks a tick, as measured by course rows E6 and E9.
        _waterStuckSpeed = StuckSpeedFraction * SwimBlocksPerTick * (1.0 - ActionCosts.CurrentToThrustRatio);
    }

    /// <summary>Blocks a tick for a sprint on open ground, post-sprint-fix.</summary>
    private const double LandSprintBlocksPerTick = 0.28062;

    /// <summary>Blocks a tick for a walk on open ground.</summary>
    private const double LandWalkBlocksPerTick = 0.21586;

    /// <summary>Blocks a tick for a swim with no sprint, on every era.</summary>
    private const double SwimBlocksPerTick = 0.098;

    /// <summary>How many ticks a segment gets before it is declared failed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="segment"/> is null.</exception>
    public int BudgetFor(PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        double expected = ExpectedTicks(segment);
        double slack = SlackFor(segment);
        int fixedOverhead = segment.MoveType switch
        {
            MoveType.Swim => SwimFixedOverheadTicks,
            MoveType.Climb => ClimbFixedOverheadTicks,
            _ => FixedOverheadTicks,
        };

        double budget = Math.Ceiling(slack * expected) + fixedOverhead;
        return (int)Math.Clamp(budget, MinBudgetTicks, MaxBudgetTicks);
    }

    /// <summary>How many consecutive stuck ticks a segment tolerates before it is declared failed.</summary>
    public int StuckTicksFor(int budget) => Math.Max(MinStuckTicks, budget / 2);

    /// <summary>The floor stuck-tick count, matching the tightest literal any template carried.</summary>
    public const int MinStuckTicks = 40;

    /// <summary>The squared per-tick horizontal displacement below which a tick counts as no progress, in the medium the body is ACTUALLY in.</summary>
    /// <remarks>Keyed on the live <c>InWater</c> flag rather than on the segment's move type, because the two disagree exactly where it matters: <c>MoveType.Traverse</c> and <c>MoveType.Diagonal</c> are what the planner emits for a submerged bottom-walk, they are executed by <c>WalkTemplate</c>, and a land threshold applied to a body wading at 0.098 blocks a tick - or at 0.028 against a current - fires on a bot that is making perfectly good progress.</remarks>
    public double StuckMovedSquared(bool inWater)
    {
        double speed = inWater ? _waterStuckSpeed : _landStuckSpeed;
        return speed * speed;
    }

    /// <summary>The stuck speed itself, in blocks a tick, for reporting and for tests.</summary>
    public double StuckSpeed(bool inWater) => inWater ? _waterStuckSpeed : _landStuckSpeed;

    private double SlackFor(PathSegment segment) => segment.MoveType switch
    {
        MoveType.Swim => _hasSwimPose ? WaterSlack : WaterSlackWithoutTheSwimPose,
        MoveType.Climb => ClimbSlack,
        // A fall or a descend whose charge is at least the water sink rate went through water, and the sink is what it will really do. Anything cheaper is an air fall and the fall table already integrates gravity against drag, so the land slack covers it.
        MoveType.Fall or MoveType.Descend when IsPricedAsAWaterSink(segment) => FallIntoWaterSlack,
        _ => IsInWater(segment) ? SubmergedWalkSlack : LandSlack,
    };

    /// <summary>Whether either endpoint of a segment puts the BODY in water: the feet cell or the head cell.</summary>
    /// <remarks>
    /// <para>The head-cell test alone is the wrong question here, and it is a different question from the one <c>BreathValidator</c> asks. Breath depends on whether the eye is in fluid, so a head cell is exactly right for it. SPEED is <c>body-water overlap</c>, which is true the moment the body's box overlaps any water cell, and vanilla's <c>movement update</c> takes its water arm on that flag: a body wading a one-deep sheet with its head in air moves at <see cref="SwimBlocksPerTick"/>, not at the sprint rate the planner charged it.</para>
    /// <para><see cref="StuckMovedSquared"/> is keyed on the live <c>InWater</c> flag because the move type and the medium can disagree, while the budget is keyed on the head cell, so the same wade got a water stuck threshold and a land tick budget.</para>
    /// <para>Course row E10 waterfall is what that costs. The source arm's sheet covers the shell top, and the live telemetry prices it: <c>Segment 2/10 completed (Traverse) in 22 ticks</c> - 22 of its 40 for ONE block, where an open-ground sprint takes five - and then <c>Segment 3/10 failed (Diagonal) after 41 ticks</c>, which is <see cref="MinBudgetTicks"/> + 1. Both segments have their head cells in air, so both were priced as dry land and floored at 40, and 1.414 blocks of diagonal wade does not fit in 40 ticks.</para>
    /// <para>Dry terrain cannot move: with no water at either cell of either endpoint the answer is the same <see cref="LandSlack"/> it always was.</para>
    /// </remarks>
    private bool IsInWater(PathSegment segment)
        => IsInWater(segment.Start, segment.StartFeetY) || IsInWater(segment.End, segment.EndFeetY);

    /// <summary>Whether the body standing at an endpoint's LOGICAL feet cell is in water: that cell, or the head cell above it.</summary>
    /// <remarks>The Y arrives as a cell index rather than being floored out of <paramref name="point"/> for the reason spelled out on <see cref="PathSegment.StartFeetY"/>: once a partial support's elevation is resolved into the endpoint, <c>Math.Floor(End.Y)</c> is the SUPPORT cell, not the feet cell, and both reads here move down one with it.</remarks>
    private bool IsInWater(in Vec3d point, int feetY)
    {
        int x = (int)Math.Floor(point.X);
        int z = (int)Math.Floor(point.Z);
        return MoveHelper.IsWater(_world.GetBlock(new BlockPos(x, feetY, z)))
            || BreathModel.IsSubmerged(_world, x, feetY, z);
    }

    /// <summary>Whether a fall or descend was charged at least the water-sink rate for its own drop, which is what says it went through water rather than through air.</summary>
    /// <remarks>The drop is the LOGICAL one, feet cell to feet cell, because the charge it is compared against is the planner's and the planner counted whole cells. A resolved endpoint shrinks the geometric drop by up to a block, which would lower the threshold and start calling air falls water sinks.</remarks>
    private static bool IsPricedAsAWaterSink(PathSegment segment)
    {
        double blocks = Math.Max(1.0, Math.Abs(segment.StartFeetY - segment.EndFeetY));
        return segment.PlannedTickCost >= ActionCosts.WaterSinkOneBlock * blocks * 0.999;
    }

    private double ExpectedTicks(PathSegment segment)
    {
        if (segment.PlannedTickCost > 0)
            return segment.PlannedTickCost;

        // A hand-built segment carries no charge. Fall back to its own PLANNER geometry (the logical feet-cell rise, see PathSegment.PlannedBlocks) at the medium's nominal rate; believing a free move would give every such segment the floor.
        double length = segment.PlannedBlocks;
        double perBlock = segment.MoveType switch
        {
            MoveType.Swim => ActionCosts.SwimOneBlock,
            MoveType.Climb => ActionCosts.LadderUpOne,
            MoveType.Fall => ActionCosts.WaterSinkOneBlock,
            _ => _allowSprint ? ActionCosts.SprintOneBlock : ActionCosts.WalkOneBlock,
        };

        return Math.Max(1.0, length) * perBlock;
    }

    /// <summary>The water-travel era this policy was built for, for reporting.</summary>
    public WaterTravelEra WaterTravel => _profile.WaterTravel;
}
