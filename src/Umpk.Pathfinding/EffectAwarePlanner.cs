using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding;

/// <summary>One finished plan, together with the status-effect arms it was planned under and the effects it therefore depends on staying alive.</summary>
/// <remarks>The two arm flags are answers about THIS route, not about the capture: a fire-resistance potion the route never walks a hazard for leaves <see cref="FireHazardsCleared"/> false, and water breathing on a route with no water on it leaves <see cref="BreathSuspended"/> false. That is what makes <see cref="DependsOnEffects"/> a usable replan trigger instead of a list of everything the player happened to be holding.</remarks>
public sealed record EffectAwarePlan
{
    /// <summary>The search result the plan ended on.</summary>
    public required PathResult Result { get; init; }

    /// <summary>The executable segments built from <see cref="Result"/>, empty when it found nothing.</summary>
    public required IReadOnlyList<PathSegment> Segments { get; init; }

    /// <summary>Whether this route was priced with vanilla's fire hazards cleared, i.e. whether it walks through something only a fire-resistance potion makes survivable.</summary>
    public required bool FireHazardsCleared { get; init; }

    /// <summary>Whether this route was planned with the search's breath dimension suspended, i.e. whether it crosses water only water breathing (or conduit power) makes survivable.</summary>
    public required bool BreathSuspended { get; init; }

    /// <summary>The effects this route LEANS ON. Empty for every plan that would be identical without them, which is every dry plan and every plan that routes around its hazards anyway.</summary>
    public required IReadOnlyList<Identifier> DependsOnEffects { get; init; }

    /// <summary>How many A* searches the plan cost. One for every plan whose arms held (and for every plan with no arms at all); two or three when an arm was taken, priced against the finished route, and found too short.</summary>
    public required int Searches { get; init; }
}

/// <summary>The two-pass plan: run the search under the status-effect arms the capture might support, price the finished route in REAL ticks, and re-plan without any arm the effect cannot pay for.</summary>
/// <remarks>
/// <para><b>The ordering problem this exists to break.</b> The duration gate needs the route's real-tick cost (<see cref="BreathValidator.RouteRealTicks"/>), and the route depends on whether the hazards were cleared and whether the breath dimension was suspended. That is circular. Two passes break it: plan once with the arms on, measure, and if an arm is not covered, drop it and plan again. <c>FireResistancePlanningTests.TheDurationGateIsArithmetic</c> ran exactly that round trip by hand; this is the planner running it for itself.</para>
/// <para><b>Which stage may GRANT, which is the whole safety argument.</b> Only the second one. The pre-search filter is REFUSAL-ONLY: it answers the three cases in which no arithmetic is possible at all - the producer could not observe effects (<see cref="PathfinderCapabilities.EffectsKnown"/> false), the player does not hold the effect, and the effect is present but its remainder is not derivable (<see cref="CapabilityEffect.UnknownRemaining"/>) - and everything it lets through is PROVISIONAL. No suspension and no clearance is ever granted against a guess at a route that does not exist yet. The grant is the post-search comparison against the route's own real-tick cost at <see cref="EffectCoverage.SafetyFactor"/> with the reaction and estimate reserves on top, and <see cref="BreathValidator.RouteRealTicks"/> is the pessimistic end of that model: it prices a submerged bottom-walk at the measured wade rate rather than at the planner's charge.</para>
/// <para><b>Why a provisional arm is safe to search under.</b> Both arms are RELAXATIONS - clearing a hazard only adds passable and standable cells, and <see cref="PathfinderOptions.BreathAware"/> false only removes refusals - so the reachable set under an arm is a superset of the reachable set without it. Three consequences, all load-bearing here. A search that FAILS under an arm would also have failed without it, so a failure needs no second pass. A route that comes back touching none of the cells the arm unlocked is optimal in the strict world too, because it is a strict-world route that was optimal over a superset. And a provisional arm that turns out uncovered costs one wasted search and never reaches an executor.</para>
/// <para><b>Termination.</b> Each re-plan drops at least one arm and no arm is ever put back, so with two arms the loop runs at most three searches, and the common case - no relevant effect, or an arm the route does not use - runs exactly one.</para>
/// </remarks>
public static class EffectAwarePlanner
{
    /// <summary>The effect that clears vanilla's <c>IS_FIRE</c> hazards.</summary>
    public static Identifier FireResistance { get; } = Identifier.Minecraft("fire_resistance");

    /// <summary>The water-breathing status effect identifier.</summary>
    public static Identifier WaterBreathing { get; } = Identifier.Minecraft("water_breathing");

    /// <summary>Conduit power grants the same water-breathing immunity and is checked in the same disjunction (and <c>PhysicsEngineHolder.HasWaterBreathing</c> already reads both).</summary>
    public static Identifier ConduitPower { get; } = Identifier.Minecraft("conduit_power");

    /// <summary>Plans from <paramref name="start"/> to <paramref name="goal"/>, taking the status-effect arms the capture supports and dropping any the finished route outlives.</summary>
    /// <param name="world">The frozen planning region.</param>
    /// <param name="options">The caller's options.</param>
    /// <param name="start">The start block.</param>
    /// <param name="goal">The goal.</param>
    /// <param name="capabilities">The player's capabilities at capture time, or null for <see cref="PathfinderCapabilities.None"/>, which takes no arm and performs a single search.</param>
    /// <param name="profile">The physics profile the route's real-tick cost is priced against.</param>
    /// <param name="timeProvider">The clock the search timeout is measured against.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The finished plan and the arms it stands on.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static EffectAwarePlan Plan(
        PlanningWorldView world,
        PathfinderOptions options,
        BlockPos start,
        IGoal goal,
        PathfinderCapabilities? capabilities,
        PhysicsProfile profile,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(profile);

        PathfinderCapabilities caps = capabilities ?? PathfinderCapabilities.None;

        // Stage one, refusal-only. Nothing here grants an arm; it only rules out the cases in which the post-search arithmetic could not run.
        bool hazardArm = MayBeTimed(caps, FireResistance);
        Identifier? breathArm = options.BreathAware && world.MayContainWater
            ? FirstTimed(caps, WaterBreathing, ConduitPower)
            : null;

        int searches = 0;
        while (true)
        {
            searches++;
            PathfinderOptions searchOptions = breathArm is null ? options : options with { BreathAware = false };
            PathResult result = PathPlanner.FindPath(
                world, searchOptions, start, goal, timeProvider, ct, caps, hazardArm);

            if (result.Status == PathStatus.Failed || result.Path.Count == 0)
            {
                // A relaxed search that found nothing means the strict one would find nothing either, so there is no second pass to run and no arm to report: a failure depends on no potion.
                return Refused(result, searches);
            }

            IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, world, searchOptions);
            if (segments.Count == 0)
                return Refused(result, searches);

            bool hazardUsed = hazardArm && RouteEntersAClearedHazard(segments, world);
            bool breathUsed = breathArm is not null && RouteGoesUnderWater(segments, world);

            if (!hazardUsed && !breathUsed)
            {
                // Neither arm changed this route, so the default planner produces the same plan and it depends on nothing. This is the arm that keeps a potion in the hotbar from charging every dry plan a second search.
                return new EffectAwarePlan
                {
                    Result = result,
                    Segments = segments,
                    FireHazardsCleared = false,
                    BreathSuspended = false,
                    DependsOnEffects = [],
                    Searches = searches,
                };
            }

            double routeRealTicks = BreathValidator.RouteRealTicks(segments, world, profile, options.AllowSprint);
            bool hazardCovered = !hazardUsed || EffectCoverage.Covers(caps, FireResistance, routeRealTicks);
            bool breathCovered = !breathUsed || EffectCoverage.Covers(caps, breathArm!.Value, routeRealTicks);

            if (hazardCovered && breathCovered)
            {
                var depends = new List<Identifier>(2);
                if (hazardUsed)
                    depends.Add(FireResistance);

                if (breathUsed)
                    depends.Add(breathArm!.Value);

                return new EffectAwarePlan
                {
                    Result = result,
                    Segments = segments,
                    FireHazardsCleared = hazardUsed,
                    BreathSuspended = breathUsed,
                    DependsOnEffects = depends,
                    Searches = searches,
                };
            }

            // At least one arm was used and is not paid for. Drop every uncovered one and plan again;
            // nothing is ever put back, so this terminates.
            hazardArm &= hazardCovered;
            if (!breathCovered)
                breathArm = null;

        }
    }

    private static EffectAwarePlan Refused(PathResult result, int searches) => new()
    {
        Result = result,
        Segments = [],
        FireHazardsCleared = false,
        BreathSuspended = false,
        DependsOnEffects = [],
        Searches = searches,
    };

    /// <summary>Whether an effect is present AND carries a remainder the coverage arithmetic can run on. This is the whole pre-search filter, and every one of its three false arms is a refusal on doubt: unobservable, absent, and present-but-undatable all answer no.</summary>
    private static bool MayBeTimed(PathfinderCapabilities capabilities, Identifier id)
        => capabilities.EffectsKnown
            && capabilities.TryGetEffect(id, out CapabilityEffect effect)
            && effect.RemainingTicks != CapabilityEffect.UnknownRemaining;

    private static Identifier? FirstTimed(PathfinderCapabilities capabilities, Identifier a, Identifier b)
    {
        if (MayBeTimed(capabilities, a))
            return a;

        return MayBeTimed(capabilities, b) ? b : null;
    }

    /// <summary>Whether the finished route actually stands in, on, or under a cell only fire resistance makes survivable. Three cells per endpoint - the feet cell, the head cell, and the support under the feet - which is exactly where <c>MoveHelper.IsHazard</c> is consulted for a standing body.</summary>
    private static bool RouteEntersAClearedHazard(IReadOnlyList<PathSegment> segments, IPhysicsWorldView world)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            PathSegment segment = segments[i];
            if (TouchesFire(world, segment.Start, segment.StartFeetY)
                || TouchesFire(world, segment.End, segment.EndFeetY))
                return true;

        }

        return false;

        static bool TouchesFire(IPhysicsWorldView world, in Vec3d point, int feetY)
        {
            int x = (int)Math.Floor(point.X);
            int z = (int)Math.Floor(point.Z);
            return MoveHelper.IsClearedByFireResistance(world.GetBlock(new BlockPos(x, feetY - 1, z)))
                || MoveHelper.IsClearedByFireResistance(world.GetBlock(new BlockPos(x, feetY, z)))
                || MoveHelper.IsClearedByFireResistance(world.GetBlock(new BlockPos(x, feetY + 1, z)));
        }
    }

    /// <summary>Whether any endpoint of the route puts the player's EYES under water, which is the only condition under which the breath dimension has anything to say. It is <c>BreathModel.IsSubmerged</c>, the same test the search's own deficit walk and the validator use, so the three cannot disagree about which route is a wet one.</summary>
    private static bool RouteGoesUnderWater(IReadOnlyList<PathSegment> segments, IPhysicsWorldView world)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            PathSegment segment = segments[i];
            if (Submerged(world, segment.Start, segment.StartFeetY)
                || Submerged(world, segment.End, segment.EndFeetY))
                return true;

        }

        return false;

        static bool Submerged(IPhysicsWorldView world, in Vec3d point, int feetY)
            => BreathModel.IsSubmerged(world, (int)Math.Floor(point.X), feetY, (int)Math.Floor(point.Z));
    }
}
