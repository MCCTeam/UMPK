namespace Umpk.Game.Entities;

/// <summary>Canonicalises an attribute identifier across vanilla's 1.21.2 attribute rename, so one id names the same attribute on every protocol.</summary>
/// <remarks>
/// <para>Vanilla registered its attributes under a category-prefixed path until 1.21.1 and unprefixed from 1.21.2, with no semantic change to any of them: <c>"generic.movement_speed"</c> became <c>"movement_speed"</c>, and the same rename hit <c>player.</c>, <c>zombie.</c> and <c>horse.</c>. The dataset records exactly that: protocols 735-767 are 100% prefixed, 768-776 are 0%.</para>
/// <para>A consumer asking for <c>minecraft:movement_speed</c> misses on 766 and 767 unless something strips the segment. This is the only place that strip occurs. The data layer keeps the raw names: <c>minecraft:horse.jump_strength</c> (0.7 in [0,2], and <c>minecraft:generic.jump_strength</c> (0.42F in [0,32]) canonicalise to the same string with different defaults AND different ranges, and <c>AttributeInstance</c> consumes both numbers, so collapsing them in the registry would silently use one era's definition for the other. Collapsing them in a per-session map is safe, because a session only ever has one of the two.</para>
/// <para>The strip set is exactly four segments. Across all 27 protocols that carry a <c>minecraft:attribute</c> registry, there are 72 distinct raw names and 40 distinct canonical ones, and ZERO within-protocol collisions. Prefix occurrences across the dataset are <c>generic.</c> 23 distinct names, <c>player.</c> 7, <c>zombie.</c> 1, <c>horse.</c> 1.</para>
/// </remarks>
public static class AttributeIds
{
    /// <summary>The four category segments vanilla stripped at 1.21.2. Order does not matter: no attribute name begins with two of them.</summary>
    private static readonly string[] CategoryPrefixes = ["generic.", "player.", "zombie.", "horse."];

    /// <summary>The canonical form of an attribute identifier: the same identifier with a leading <c>generic.</c>, <c>player.</c>, <c>zombie.</c> or <c>horse.</c> path segment removed. An identifier that carries none of them is returned unchanged, so this is safe to apply to an already-canonical id and to a non-attribute id alike.</summary>
    public static Identifier Canonical(Identifier id)
    {
        string path = id.Path;
        foreach (string prefix in CategoryPrefixes)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return new Identifier(id.Namespace, path[prefix.Length..]);

        return id;
    }
}
