namespace Umpk.Pathfinding.Core;

/// <summary>Whether a status effect the player holds lasts long enough to cover the route a plan wants to price against it.</summary>
/// <remarks>
/// <para><b>Why this is arithmetic and not a boolean.</b> The interesting failure is not "the bot had no potion", it is "the bot had thirty seconds of fire resistance and a forty-second lava route". Course row M3 is that row by construction: M1's fourteen-block fire corridor with a two-second grant.</para>
/// <para><b>Why REAL ticks.</b> The route's cost in planner ticks is not the time it takes. A submerged bottom-walk is charged <see cref="ActionCosts.SprintOneBlock"/> because the move emitted is an ordinary <c>Traverse</c>, and the water makes that walk run two to three times slower - exactly the divergence <see cref="BreathValidator"/> exists to close. So the comparison runs against <see cref="BreathValidator.RouteRealTicks"/>, the same model the breath validator and the life-safety supervisor already price a route with, rather than a second opinion about how long a walk takes.</para>
/// <para><b>The polarity, which is the whole safety argument.</b> Three different things produce an empty or unusable answer - the producer could not observe effects at all (<see cref="PathfinderCapabilities.EffectsKnown"/> false), the player does not have the effect, and the effect is present but its remaining duration cannot be derived (<see cref="CapabilityEffect.UnknownRemaining"/>). All three answer NOT COVERED. Reading any of them as "covered" clears a hazard on no evidence, and the hazards this gates are the ones that kill.</para>
/// </remarks>
public static class EffectCoverage
{
    /// <summary>The multiplier applied to the route's real-tick estimate before comparing it against the remainder.</summary>
    /// <remarks>
    /// <see cref="BreathModel.SafetyFactor"/>, reused rather than re-invented: it is the same statement about the same executor ("the run takes longer than the model says") measured on the same routes, and a second constant for it would be a second number to calibrate and a second one to get out of step.
    /// <para>Note what it is NOT applied to: the medium. A wet leg is already priced at the wade rate inside <see cref="BreathValidator.RouteRealTicks"/>, so a second wet-route factor here would charge the same slowdown twice.</para>
    /// </remarks>
    public const double SafetyFactor = BreathModel.SafetyFactor;

    /// <summary>The extra reserve charged when the remainder is DERIVED rather than read off a packet that landed on the capture tick.</summary>
    /// <remarks>The wire carries no remaining duration: vanilla sends <c>update_mob_effect</c> on add and on refresh and <c>remove_mob_effect</c> on expiry and nothing in between, so <see cref="CapabilityEffect.RemainingTicks"/> is the applied duration less the ticks since the apply. That derivation is exact when nothing was missed, and it is optimistic when something was - a dropped refresh, or a session tick counter that ran ahead of the server's. This is a conservative surcharge for that, not a measurement of it; it is one <see cref="BreathModel.ReactionTicks"/> because that is the unit this area already charges a "cannot react instantly" reserve in, and inventing a second one would be inventing a second number with no more evidence behind it.</remarks>
    public const int EstimateReserveTicks = BreathModel.ReactionTicks;

    /// <summary>The remainder a route of <paramref name="routeRealTicks"/> demands: the route at <see cref="SafetyFactor"/>, plus <see cref="BreathModel.ReactionTicks"/>, plus <see cref="EstimateReserveTicks"/> when the remainder is an estimate.</summary>
    /// <param name="routeRealTicks">The route's real-tick estimate.</param>
    /// <param name="durationIsEstimated">Whether the remainder was derived rather than read.</param>
    /// <returns>The required remaining ticks.</returns>
    public static double RequiredTicks(double routeRealTicks, bool durationIsEstimated)
        => (Math.Max(0.0, routeRealTicks) * SafetyFactor)
            + BreathModel.ReactionTicks
            + (durationIsEstimated ? EstimateReserveTicks : 0);

    /// <summary>Whether one captured effect covers a route of <paramref name="routeRealTicks"/>.</summary>
    /// <remarks>An infinite effect covers anything. An <see cref="CapabilityEffect.UnknownRemaining"/> one covers nothing, however short the route: "present, duration not derivable" is not a number and must not be treated as a large one.</remarks>
    /// <param name="effect">The captured effect.</param>
    /// <param name="routeRealTicks">The route's real-tick estimate.</param>
    /// <returns>True when the effect lasts the route out with the reserve intact.</returns>
    public static bool Covers(in CapabilityEffect effect, double routeRealTicks)
    {
        if (effect.IsInfinite || effect.RemainingTicks == CapabilityEffect.InfiniteRemaining)
            return true;

        if (effect.RemainingTicks == CapabilityEffect.UnknownRemaining)
            return false;

        return effect.RemainingTicks >= RequiredTicks(routeRealTicks, effect.DurationIsEstimated);
    }

    /// <summary>Whether the captured player holds an effect that covers a route of <paramref name="routeRealTicks"/>.</summary>
    /// <remarks>False when <see cref="PathfinderCapabilities.EffectsKnown"/> is false, and that arm is the point of the overload: a producer with no entity tracking reports an EMPTY effect list that is indistinguishable from "no potions", and a consumer that skipped the flag would clear a hazard because it could not see the potion that was not there.</remarks>
    /// <param name="capabilities">The captured capabilities.</param>
    /// <param name="effectId">The effect to look for, e.g. <c>minecraft:fire_resistance</c>.</param>
    /// <param name="routeRealTicks">The route's real-tick estimate.</param>
    /// <returns>True when the effect is observed, present, and long enough.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    public static bool Covers(PathfinderCapabilities capabilities, Identifier effectId, double routeRealTicks)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.EffectsKnown || !capabilities.TryGetEffect(effectId, out CapabilityEffect effect))
            return false;

        return Covers(effect, routeRealTicks);
    }
}
