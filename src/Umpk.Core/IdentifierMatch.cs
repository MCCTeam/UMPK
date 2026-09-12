namespace Umpk;

/// <summary>Matching a user-typed id against a registry <see cref="Identifier"/>. "chest" and "minecraft:chest" both name the vanilla chest; "mod:chest" names something else and a bare "chest" must not reach it.</summary>
public static class IdentifierMatch
{
    /// <summary>Whether <paramref name="id"/> is named by <paramref name="needle"/>, which may be namespaced or bare. A bare needle matches ONLY the minecraft namespace: a bare path that also matched a modded namespace would make "drop chest" empty a modded chest by accident.</summary>
    public static bool Matches(Identifier id, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return false;

        string trimmed = needle.Trim();
        return Matches(id, trimmed, BarePath(trimmed));
    }

    /// <summary>The pre-split form, for a loop testing thousands of ids against one needle.</summary>
    public static bool Matches(Identifier id, string needle, string barePath)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return false;

        return string.Equals(id.ToString(), needle, StringComparison.OrdinalIgnoreCase)
            || (id.IsMinecraft && string.Equals(id.Path, barePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Splits a needle once: trims, and takes everything after the first colon as the bare path.</summary>
    public static string BarePath(string needle)
    {
        ArgumentNullException.ThrowIfNull(needle);
        string trimmed = needle.Trim();
        int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? trimmed : trimmed[(colon + 1)..];
    }
}
