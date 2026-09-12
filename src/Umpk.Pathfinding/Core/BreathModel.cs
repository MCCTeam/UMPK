using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding.Core;

/// <summary>What <see cref="BreathModel.EscapeTicks"/> found: either a tick count, or nothing at all.</summary>
/// <remarks>
/// <para>The two values are NOT a number and a sentinel. There is no "infinite" escape cost, because the two consumers of this model read an unknown with OPPOSITE polarity and a shared sentinel would be wrong for one of them. The planner refuses what it cannot prove survivable, so an unknown is a refusal. The life-safety supervisor pre-empts navigation, so for it an unknown must be silent. Treating an unknown as an infinite escape cost would make <c>air &lt; Threshold</c> true even at a full lung and would make the bot abandon its route as soon as it swam under an overhang.</para>
/// </remarks>
public readonly record struct BreathEscape
{
    /// <summary>Whether a route to a breathing cell was found.</summary>
    public bool IsKnown { get; private init; }

    /// <summary>The ticks to reach air, meaningful only when <see cref="IsKnown"/>.</summary>
    public double Ticks { get; private init; }

    /// <summary>An escape of a known duration.</summary>
    public static BreathEscape Known(double ticks) => new() { IsKnown = true, Ticks = ticks };

    /// <summary>No escape this model can see: a motion-blocking ceiling over the column, or more water above than the scan looks at.</summary>
    public static BreathEscape Unknown => default;
}

/// <summary>The one breath model, shared by the planner's feasibility test and the executor-side life-safety supervisor. Pure and static over an <see cref="IPhysicsWorldView"/> and a <see cref="PhysicsProfile"/>, so both sides compute the same numbers from the same terrain: if the planner believes a node is safe at 120 ticks of deficit and the supervisor panics at 100, every breath-aware plan is pre-empted on its own route.</summary>
/// <remarks>
/// <para>Air is already in ticks (300 max, -1 per submerged tick, +4 per surfaced tick) and every planner cost is in ticks, so the resource arithmetic needs no unit conversion and no second cost model.</para>
/// </remarks>
public static class BreathModel
{
    /// <summary>A full lung is 300 ticks on every supported version and for every player.</summary>
    public const double FullLungTicks = 300.0;

    /// <summary>Ticks to rise one block through water holding <c>Sprint</c> and <c>Jump</c> with the pitch up the column, on <see cref="WaterTravelEra.SprintAware"/>.</summary>
    /// <remarks>The climb was measured at 2.5415 ticks a block by least squares over depths 2 to 160 (steady rate 0.393548 blocks a tick: the fluid impulse <c>+0.04</c>, then the steering blend <c>v += (1 - v) * 0.06</c>, then <c>WaterYDamping 0.8</c>, with gravity skipped because the swimmer is sprinting). 2.62 is the conservative published fit and is what the threshold table is calibrated on, so it is what is used: over-stating the climb makes the supervisor fire early and the planner refuse early, which is the safe direction for both.</remarks>
    public const double AscendTicksPerBlockSprintAware = 2.62;

    /// <summary>Ticks to rise one block through water on <see cref="WaterTravelEra.Legacy"/> (protocols 47-340), where the jump impulse is a flat <c>+0.1</c> blocks a tick and neither sprint nor pitch changes it. Measured 10.0 exactly.</summary>
    public const double AscendTicksPerBlockLegacy = 10.0;

    /// <summary>The fixed overhead of an ascent, in ticks: the impulse ramp before the climb reaches its steady rate. The intercept of the published fits (measured 6.30 sprint-aware, 2.00 legacy).</summary>
    public const double AscendFixedTicks = 6.0;

    /// <summary>The number of cells the escape scan looks up a water column before giving up.</summary>
    public const int EscapeScanCells = 48;

    /// <summary>The multiplier applied to an escape before it becomes a firing threshold: model error, a current opposing the climb, a detour round a ceiling.</summary>
    public const double SafetyFactor = 1.5;

    /// <summary>The ticks reserved for deciding and reorienting before the climb starts. Pitch is rate-limited to 25 degrees a tick, so a 90-degree swing alone is four ticks.</summary>
    public const int ReactionTicks = 10;

    /// <summary>The width of one air band in the search's node key, in ticks.</summary>
    /// <remarks>Eight bands over a 300-tick lung. The smallest decision the dimension has to be able to make is "is there room for one more block of swimming", which is 5.1 to 10.2 ticks, so a 40-tick band is coarse against a single move and fine against a route. Banding costs at most one band of conservatism, 12.5% of the lung, because a stored deficit is always rounded UP.</remarks>
    public const double BandTicks = 40.0;

    /// <summary>The band a deficit is stored in: the deficit rounded UP to a band ceiling.</summary>
    /// <remarks>Up, never down, and that direction is the whole soundness argument. Two routes that arrive at the same cell with 60 and 79 ticks spent share a key, so the search keeps only the cheaper of them and then believes the survivor has whatever the BAND says. Rounding up makes that belief pessimistic - it credits the survivor with 80 ticks spent - so the search can refuse a route that was survivable and can never approve one that was not.</remarks>
    public static int Band(double deficitTicks)
        => deficitTicks <= 0.0 ? 0 : (int)Math.Ceiling(deficitTicks / BandTicks);

    /// <summary>The largest air deficit a route may reach, in ticks, on a FULL lung.</summary>
    public static double MaxDeficit(PhysicsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return FullLungTicks;
    }

    /// <summary>The largest air deficit a route may reach for a player holding <paramref name="airTicks"/> of breath right now: the lung it actually has, less the same reaction reserve the supervisor's own route threshold charges.</summary>
    /// <remarks>
    /// <para><b>Why the current lung and not a full one.</b> The validator and supervisor must use the same available-air basis. A route of D ticks is acceptable only when <c>air &gt;= D + ReactionTicks</c>; otherwise approval would be followed by immediate pre-emption. Course row E19 exercises this condition.</para>
    /// <para><b>Why the reserve.</b> <see cref="RouteThreshold"/> adds <see cref="ReactionTicks"/> because reorienting and starting to rise really does cost ticks. Charging the planner the same ten is what makes the implication <c>validator approved =&gt; supervisor silent</c> true rather than nearly true: without it the two bars sit exactly ten ticks apart and the invariant fails on every route within ten ticks of the budget, at a full lung as readily as at a drawn-down one.</para>
    /// <para>The result may be negative for a player that is already almost out of air, and that is the correct answer: no submerged segment at all is affordable.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static double RouteBudget(PhysicsProfile profile, int airTicks)
        => Math.Min(MaxDeficit(profile), airTicks) - ReactionTicks;

    /// <summary>Ticks to climb <paramref name="depthBlocks"/> blocks of water to the surface.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static double AscendTicks(int depthBlocks, PhysicsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double perBlock = profile.WaterTravel == WaterTravelEra.SprintAware
            ? AscendTicksPerBlockSprintAware
            : AscendTicksPerBlockLegacy;
        return (perBlock * depthBlocks) + AscendFixedTicks;
    }

    /// <summary>Whether a player standing with its feet in this cell has its eyes in water.</summary>
    /// <remarks>
    /// A one-cell test on the HEAD cell. The eye sits 1.62 above the feet in the standing pose and 0.4 in the swimming pose, so a water head cell puts the eye inside water in either pose, and an air head cell caps the water surface at <c>y + 1.0</c>, which a standing eye at <c>y + 1.62</c> clears. This is the eye-in-water predicate that drives air supply, expressed with the planner's own <see cref="MoveHelper.IsWater"/> so the model and the move set can never disagree about what water is. The known edge is a fractional-height film (a level-7 cell is 1/9 deep), which <c>MoveHelper.CanTraverseWater</c> already treats as a full column.
    /// <para><b>A bubble column is water and does not drain air.</b> The exemption is a second, independent condition rather than a consequence of the first, and it has to be spelled out here. Getting it wrong is not a rounding error: an elevator is fifteen cells deep, the escape scan would price a climb out of every one of them, and the supervisor would surface a bot that vanilla lets ride all day. The exemption is on the EYE cell specifically, which is the cell this predicate already reads.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public static bool IsSubmerged(IPhysicsWorldView view, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(view);
        BlockState eye = view.GetBlock(new BlockPos(x, y + 1, z));
        return MoveHelper.IsWater(eye) && !MoveHelper.IsBubbleColumn(eye);
    }

    /// <summary>Ticks to reach a breathing cell straight up from a node, or <see cref="BreathEscape.Unknown"/> when this column cannot surface.</summary>
    /// <remarks>A bounded vertical scan rather than a breadth-first search over the water sub-graph. It is exact for open water above and wrong for a column whose only air is reachable sideways: it under-reports reachability, so it never approves an escape that does not exist. The escalation, if the banded-air search shows it refuses real routes, is a multi-source BFS from every breathing cell computed once per capture; that costs a pass over every water cell in the region, which for a 49x49x49 ocean capture is up to 117k cells on the session loop.</remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static BreathEscape EscapeTicks(IPhysicsWorldView view, int x, int y, int z, PhysicsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(profile);

        if (!IsSubmerged(view, x, y, z))
            return BreathEscape.Known(0.0);

        double perBlock = profile.WaterTravel == WaterTravelEra.SprintAware
            ? AscendTicksPerBlockSprintAware
            : AscendTicksPerBlockLegacy;

        for (int h = 2; h <= EscapeScanCells; h++)
        {
            BlockState cell = view.GetBlock(new BlockPos(x, y + h, z));
            if (MoveHelper.IsWater(cell))
                continue;

            return cell.BlocksMotion
                ? BreathEscape.Unknown
                : BreathEscape.Known((perBlock * (h - 1)) + AscendFixedTicks);
        }

        return BreathEscape.Unknown;
    }

    /// <summary>The air level at or below which a player at a given escape distance must stop what it is doing and surface.</summary>
    public static double Threshold(double escapeTicks)
        => Math.Ceiling(SafetyFactor * escapeTicks) + ReactionTicks;

    /// <summary>The air level at or below which a player must abandon a PLANNED route whose next breathing node is <paramref name="ticksToNextBreath"/> real ticks ahead.</summary>
    /// <remarks>No <see cref="SafetyFactor"/> here, and that is deliberate rather than an omission. The factor exists to cover the error in a MODEL of a climb nobody planned. A route's ticks-to-air is not a model: it is the same medium-aware real-tick sum, over the same segments, that <see cref="BreathValidator"/> already ran before the plan was allowed to exist, and that sum is itself priced at the unsafe bound (a submerged bottom-walk at 10.2 ticks a block, roughly twice the sprint rate). Multiplying it again would put the supervisor's bar ABOVE the validator's, so every route the planner approved would be pre-empted on its own first tick - which is exactly the failure the one-model-two-consumers rule exists to prevent. The reaction reserve stays, because reorienting and starting to rise really does cost ticks.</remarks>
    public static double RouteThreshold(double ticksToNextBreath)
        => Math.Ceiling(ticksToNextBreath) + ReactionTicks;

    /// <summary>Whether a player with <paramref name="airSupply"/> ticks of breath left must surface now. An unknown escape NEVER fires: see <see cref="BreathEscape"/>.</summary>
    public static bool ShouldSurface(BreathEscape escape, int airSupply)
        => escape.IsKnown && airSupply < Threshold(escape.Ticks);
}
