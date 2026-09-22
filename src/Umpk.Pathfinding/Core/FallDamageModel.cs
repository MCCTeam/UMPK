using Umpk.Game.Blocks;

namespace Umpk.Pathfinding.Core;

/// <summary>What a landing costs in hearts, and which floors soften it.</summary>
/// <remarks>
/// <para><b>The arithmetic.</b> Subtract the safe fall distance of 3.0, scale by the block multiplier and fall-damage attribute, then round. Ordinary blocks use 1.0, hay uses <b>0.2</b>, and slime uses <b>0.0</b>. Sneaking on slime also yields zero.</para>
/// <para><b>The rounding is an era boundary, and this takes the dearer side of it deliberately.</b> Up to 1.21.1 the round is <c>ceil((dist - safe) * multiplier * attr)</c> (same in 1.20.6); from 1.21.5 it is <c>floor((dist + 1.0E-6 - safe) * multiplier * attr)</c>. For every input <c>ceil(x) &gt;= floor(x + 1e-6)</c>, so the older form is an upper bound on both eras: it is exact on 47-767 and over-states 1.21.5+ by at most one point. Over-stating damage refuses a route that was survivable; under-stating it plans a landing that kills. No era axis is added for a difference whose safe side is free - and a dataset axis would move behaviour on 50 protocols to buy one point of optimism.</para>
/// <para><b>The distance is the planner's block count</b>, which is the number of whole cells the body falls, not the fractional <c>fallDistance</c> the engine accumulates. They agree to within the partial support the body starts and ends on, and the difference is under one block, so it can move the answer by at most one point on a 1.0 multiplier and by nothing at all on hay's 0.2 below a five-block error.</para>
/// </remarks>
public static class FallDamageModel
{
    /// <summary>The default safe fall distance: three blocks fall free.</summary>
    public const double SafeFallDistance = 3.0;

    /// <summary>The fall-damage multiplier for hay.</summary>
    public const double HayMultiplier = 0.2;

    /// <summary>The distance a stalagmite tip adds to the effective fall.</summary>
    public const double StalagmiteDistanceBonus = 2.5;

    /// <summary>The multiplier the same call passes, <c>2.0F</c>: a stalagmite landing hurts twice.</summary>
    public const double StalagmiteMultiplier = 2.0;

    /// <summary>The health a landing policy refuses to spend, i.e. the floor it will not plan a body down to. Six, the same number <c>Umpk.Client</c>'s life-safety supervisor treats as its health floor.</summary>
    /// <remarks>Shared as a number rather than as a reference, because <c>Umpk.Pathfinding</c> cannot depend on <c>Umpk.Client</c>. Three hearts is what vanilla leaves a player after a fully-charged creeper at close range, so it is a reserve with a reason rather than a round number.</remarks>
    public const double HealthReserve = 6.0;

    /// <summary>What a landing block does to a fall: a distance the block ADDS before the safe-fall subtraction, and the multiplier it scales the result by.</summary>
    /// <remarks>Two numbers rather than one because landing damage uses both a distance and a multiplier, and the softening blocks only ever move the second. The stalagmite is the first block in the game to move the first, and a model that could express only the multiplier could not price it at all: doubling 0.3125 is 0.625 and vanilla charged five.</remarks>
    /// <param name="DistanceBonus">Blocks added to the fall distance before <see cref="SafeFallDistance"/> is subtracted.</param>
    /// <param name="Multiplier">The block's own damage multiplier.</param>
    public readonly record struct LandingImpact(double DistanceBonus, double Multiplier);

    /// <summary>The distance bonus and multiplier a landing block imposes.</summary>
    /// <remarks>
    /// <para><b>The stalagmite.</b> An upward pointed-dripstone tip adds 2.5 to distance and uses a 2.0 multiplier. Every other state of the block is an ordinary landing. Standing on a stalagmite is free and walking past one is free; the danger is entirely the arrival velocity. That is why the block is priced here and is deliberately NOT in <c>MoveHelper.HazardBlocks</c>: a blanket ban would turn every dripstone-cave ceiling into a wall, because <c>CanWalkThrough</c> asks <c>IsHazard</c> before the <c>BlocksMotion</c> arm.</para>
    /// <para><b>Era, and the 26.x rename.</b> Matched on the registry id plus two property reads, so a pre-flattening source falls straight through to the default, which is correct: no protocol below 755 has the block. In 26.2 <c>minecraft:sulfur_spike</c> joined the same collision-offset family but remained an ordinary landing, so it must not be added here. The two blocks DO share the collision-offset family; the two families are not the same set.</para>
    /// </remarks>
    /// <param name="landing">The block the body comes to rest on.</param>
    /// <returns>The bonus and multiplier; <c>(0.0, 1.0)</c> for an ordinary floor.</returns>
    public static LandingImpact ImpactFor(BlockState landing)
    {
        if (landing.IsDefault)
            return Ordinary;

        Identifier id = landing.Block.Id;
        if (id == SlimeBlockId || id == LegacySlimeBlockId)
            return new LandingImpact(0.0, 0.0);

        if (id == HayBlockId)
            return new LandingImpact(0.0, HayMultiplier);

        if (id == PointedDripstoneId
            && landing.TryGetProperty("vertical_direction", out string direction)
            && direction == "up"
            && landing.TryGetProperty("thickness", out string thickness)
            && thickness == "tip")
            return new LandingImpact(StalagmiteDistanceBonus, StalagmiteMultiplier);

        return Ordinary;
    }

    /// <summary>Hearts a body loses landing after falling <paramref name="blocks"/> whole blocks onto a floor that adds <paramref name="bonus"/> to the distance and scales by <paramref name="multiplier"/>.</summary>
    /// <remarks>The bonus is added INSIDE the safe-fall subtraction, exactly as vanilla adds it at the damage calculation, so it can turn a fall that was free into one that is charged: a one-block drop onto a stalagmite costs a heart.</remarks>
    /// <param name="blocks">The unprotected fall height, in blocks.</param>
    /// <param name="bonus">The landing block's distance bonus, from <see cref="ImpactFor"/>.</param>
    /// <param name="multiplier">The landing block's own multiplier, from <see cref="ImpactFor"/>.</param>
    /// <returns>The damage in hearts, never negative.</returns>
    public static int Damage(double blocks, double bonus, double multiplier)
    {
        if (multiplier <= 0.0)
            return 0;

        double raw = (blocks + bonus - SafeFallDistance) * multiplier;
        return raw <= 0.0 ? 0 : (int)Math.Ceiling(raw);
    }

    /// <summary>Hearts a body loses landing after falling <paramref name="blocks"/> whole blocks onto a floor whose multiplier is <paramref name="multiplier"/>.</summary>
    /// <param name="blocks">The unprotected fall height, in blocks.</param>
    /// <param name="multiplier">The landing block's own multiplier, from <see cref="MultiplierFor"/>.</param>
    /// <returns>The damage in hearts, never negative.</returns>
    public static int Damage(double blocks, double multiplier) => Damage(blocks, 0.0, multiplier);

    /// <summary>The fall-damage multiplier a landing block imposes: 0.0 for a full absorber, 0.2 for hay, 1.0 for everything else.</summary>
    /// <remarks>
    /// <para>Water is 0.0 here for completeness, but no fall arm reaches it through this predicate: a water landing is a different move shape entirely (the body enters the fluid rather than resting on it) and both <c>MoveFall</c> and <c>MoveDescend</c> take it before the solid arm, under <c>MaxFallHeightIntoWater</c>.</para>
    /// <para><b>Powder snow is deliberately not here.</b> It absorbs a fall with a zero damage multiplier, and it is also a curated hazard that freezes a body standing in it, so <c>MoveHelper.IsHazard</c> refuses the cell before any cost question is asked. Listing it would be a multiplier nothing can read.</para>
    /// </remarks>
    /// <param name="landing">The block the body comes to rest on.</param>
    /// <returns>The multiplier, 0.0, 0.2 or 1.0.</returns>
    public static double MultiplierFor(BlockState landing) => ImpactFor(landing).Multiplier;

    /// <summary>The hearts a plan may spend on landings, given what it knows about the player.</summary>
    /// <remarks>
    /// <para><b>Zero when the vitals are unknown</b>, and that costs nothing that was working before: the height gate every fall arm already applies is untouched, so an unobserved session plans exactly the falls it planned yesterday, plus the damage-FREE ones (slime, at any height, because a zero multiplier is zero at any height). Only a partly-absorbing floor - hay, and hay alone - needs a budget, and spending health a session cannot see is how a bot dies of a plan.</para>
    /// <para>The reserve is subtracted rather than compared against, so a bot at 7 health may spend 1 and a bot at 6 may spend nothing.</para>
    /// </remarks>
    /// <param name="capabilities">The plan's capability snapshot.</param>
    /// <returns>The spendable hearts, never negative.</returns>
    public static double Budget(PathfinderCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.VitalsKnown)
            return 0.0;

        double spendable = capabilities.Health - HealthReserve;
        return spendable > 0.0 ? spendable : 0.0;
    }

    /// <summary>An ordinary landing: no bonus, no softening, no hardening.</summary>
    private static readonly LandingImpact Ordinary = new(0.0, 1.0);

    private static readonly Identifier SlimeBlockId = Identifier.Minecraft("slime_block");

    // The flattening renamed minecraft:slime to minecraft:slime_block at 1.13; 1.8-1.12.2 registries (legacy block registration, id 165) spell it the short way.
    private static readonly Identifier LegacySlimeBlockId = Identifier.Minecraft("slime");

    private static readonly Identifier HayBlockId = Identifier.Minecraft("hay_block");

    private static readonly Identifier PointedDripstoneId = Identifier.Minecraft("pointed_dripstone");
}
